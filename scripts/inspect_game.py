#!/usr/bin/env python3
"""Read-only inventory for Tainted Grail: Conquest; Python 3.9+ stdlib only.

Static evidence can establish a PE/CLI file format, not the backend loaded by a
running game or compatibility with a particular loader. No assembly is loaded,
no executable is launched, and no game file is changed.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import sys
import tempfile
from typing import Any, Dict, List, Optional


EXPECTED_APP_ID = "1199030"
EXPECTED_EXE = "Tainted Grail.exe"
EXPECTED_DATA = "Tainted Grail_Data"
SCHEMA_VERSION = 1
MANAGED_FILES = {
    "tg.main.dll": "game_assembly",
    "assembly-csharp.dll": "game_assembly",
    "assembly-csharp-firstpass.dll": "game_assembly",
    "mscorlib.dll": "framework_assembly",
    "netstandard.dll": "framework_assembly",
    "unityengine.coremodule.dll": "unity_assembly",
    "unityengine.ui.dll": "ui_dependency",
    "unity.textmeshpro.dll": "ui_dependency",
    "astarpathfindingproject.dll": "navigation_dependency",
    "navmeshcomponents.dll": "navigation_dependency",
    "rewired.dll": "input_dependency",
    "rewired_core.dll": "input_dependency",
    "rewired_windows.dll": "input_dependency",
    "i2.dll": "localization_dependency",
}
MACHINES = {0x014C: "x86", 0x8664: "x64", 0xAA64: "arm64", 0x01C4: "arm32"}


def normalize_game_path(value: str, windows_host: Optional[bool] = None) -> Path:
    """Translate only the standard drive mapping; never search for a game."""
    if windows_host is None:
        windows_host = os.name == "nt"
    drive_path = re.match(r"^([A-Za-z]):[\\/](.*)$", value)
    mount_path = re.match(r"^/mnt/([A-Za-z])(?:/(.*))?$", value)
    if drive_path and not windows_host:
        value = "/mnt/" + drive_path.group(1).lower() + "/" + drive_path.group(2).replace("\\", "/")
    elif mount_path and windows_host:
        value = mount_path.group(1).upper() + ":\\" + (mount_path.group(2) or "").replace("/", "\\")
    return Path(value).expanduser()


def within(path: Path, directory: Path) -> bool:
    try:
        path.relative_to(directory)
        return True
    except ValueError:
        return False


def streaming_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def inspect_pe(path: Path) -> Dict[str, Any]:
    """Validate PE bounds and the CLI header/metadata; do not execute the file."""
    result: Dict[str, Any] = {
        "format": "unknown", "architecture": None, "has_cli_header": False,
        "managed_metadata_valid": False, "metadata_runtime_string": None,
        "confirmed_types": [],
    }
    try:
        size = path.stat().st_size
        with path.open("rb") as stream:
            def read_at(offset: int, length: int) -> bytes:
                if offset < 0 or length < 0 or offset + length > size:
                    raise ValueError("truncated_binary")
                stream.seek(offset)
                data = stream.read(length)
                if len(data) != length:
                    raise ValueError("truncated_binary")
                return data

            if size < 64 or read_at(0, 2) != b"MZ":
                return result
            pe_offset = struct.unpack("<I", read_at(0x3C, 4))[0]
            header = read_at(pe_offset, 24)
            if header[:4] != b"PE\x00\x00":
                return result
            machine, section_count = struct.unpack_from("<HH", header, 4)
            optional_size = struct.unpack_from("<H", header, 20)[0]
            if not 1 <= section_count <= 512 or optional_size > 4096:
                raise ValueError("invalid_pe_header")
            optional_offset = pe_offset + 24
            optional = read_at(optional_offset, optional_size)
            if len(optional) < 2:
                raise ValueError("invalid_optional_header")
            magic = struct.unpack_from("<H", optional)[0]
            if magic == 0x10B:
                directory_offset, count_offset, pe_format = 96, 92, "PE32"
            elif magic == 0x20B:
                directory_offset, count_offset, pe_format = 112, 108, "PE32+"
            else:
                raise ValueError("unknown_optional_header")
            if len(optional) < directory_offset:
                raise ValueError("truncated_optional_header")
            table = read_at(optional_offset + optional_size, 40 * section_count)
            sections = []
            for index in range(section_count):
                virtual_size, rva, raw_size, raw_offset = struct.unpack_from("<IIII", table, index * 40 + 8)
                if raw_size and raw_offset + raw_size > size:
                    raise ValueError("truncated_section")
                sections.append((rva, virtual_size, raw_offset, raw_size))
            result.update({"format": pe_format, "architecture": MACHINES.get(machine, "unknown"),
                           "machine_code": hex(machine), "confirmed_types": ["portable_executable"]})
            directory_count = struct.unpack_from("<I", optional, count_offset)[0]
            cli_entry_offset = directory_offset + 14 * 8
            if directory_count <= 14 or cli_entry_offset + 8 > len(optional):
                return result
            cli_rva, cli_size = struct.unpack_from("<II", optional, cli_entry_offset)
            if not cli_rva and not cli_size:
                return result
            size_of_headers = struct.unpack_from("<I", optional, 60)[0]

            def rva_to_offset(rva: int, length: int) -> int:
                if rva < size_of_headers and rva + length <= min(size_of_headers, size):
                    return rva
                for section_rva, virtual_size, raw_offset, raw_size in sections:
                    delta = rva - section_rva
                    if 0 <= delta and delta + length <= raw_size and delta < max(virtual_size, raw_size):
                        return raw_offset + delta
                raise ValueError("invalid_cli_rva")

            if cli_size < 72:
                raise ValueError("invalid_cli_header_size")
            cli = read_at(rva_to_offset(cli_rva, 72), 72)
            if struct.unpack_from("<I", cli)[0] < 72:
                raise ValueError("invalid_cli_header_size")
            result["has_cli_header"] = True
            result["confirmed_types"].append("cli_header_present")
            metadata_rva, metadata_size = struct.unpack_from("<II", cli, 8)
            if metadata_size < 16:
                raise ValueError("invalid_cli_metadata_size")
            metadata_offset = rva_to_offset(metadata_rva, metadata_size)
            metadata_header = read_at(metadata_offset, 16)
            if metadata_header[:4] != b"BSJB":
                raise ValueError("invalid_cli_metadata_signature")
            version_length = struct.unpack_from("<I", metadata_header, 12)[0]
            if not 0 < version_length <= min(4096, metadata_size - 16):
                raise ValueError("invalid_cli_metadata_version")
            runtime = read_at(metadata_offset + 16, version_length).rstrip(b"\x00").decode("ascii", "replace")
            # This is CLR metadata, NOT the game's version or Unity's version.
            result.update({"managed_metadata_valid": True, "metadata_runtime_string": runtime})
            result["confirmed_types"].append("cli_metadata_signature")
    except (OSError, ValueError, struct.error) as error:
        result["validation_issue"] = str(error) if isinstance(error, ValueError) else type(error).__name__
    return result


def safe_game_file(path: Path, root: Path) -> bool:
    if not within(path, root):
        return False
    current = path
    while current != root:
        if current.is_symlink():
            return False
        current = current.parent
    return path.is_file() and within(path.resolve(), root)


def inventory_file(path: Path, root: Path, role: str) -> Dict[str, Any]:
    item: Dict[str, Any] = {"path": path.relative_to(root).as_posix(), "role": role}
    if not safe_game_file(path, root):
        item["read_status"] = "skipped_symlink_or_not_regular_file"
        return item
    try:
        before = path.stat()
        item.update({"size_bytes": before.st_size, "sha256": streaming_sha256(path), "read_status": "ok"})
        if path.suffix.lower() in (".exe", ".dll"):
            item["binary"] = inspect_pe(path)
        elif path.name.lower() == "global-metadata.dat":
            with path.open("rb") as stream:
                header = stream.read(8)
            magic_ok = len(header) == 8 and struct.unpack_from("<I", header)[0] == 0xFAB11BAF
            item["metadata"] = {
                "il2cpp_magic_valid": magic_ok,
                "format_version": struct.unpack_from("<I", header, 4)[0] if magic_ok else None,
                "confirmed_types": ["il2cpp_metadata_signature"] if magic_ok else [],
                "note": "Signature only; full metadata structure and runtime use are not verified.",
            }
        after = path.stat()
        if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
            item["read_status"] = "changed_while_reading"
            item.pop("sha256", None)
    except OSError as error:
        item["read_status"] = "read_error"
        item["error_type"] = type(error).__name__
    return item


def read_manifest(path: Optional[Path]) -> Dict[str, Any]:
    result: Dict[str, Any] = {"provided": path is not None, "appid": None, "build_id": None}
    if path is None:
        return result
    try:
        if not path.is_file() or path.stat().st_size > 1024 * 1024:
            raise ValueError("manifest_not_regular_or_too_large")
        text = path.read_text(encoding="utf-8-sig")
        # Extract values only inside top-level AppState. Nested depot/build data
        # must not accidentally become the game AppID or build number.
        tokens = re.findall(r'"((?:\\.|[^"\\])*)"|([{}])|(?:\A|\s)([^\s{}"]+)', re.sub(r"(?m)^\s*//.*$", "", text))
        values = [quoted if quoted != "" else brace if brace else bare for quoted, brace, bare in tokens]
        if len(values) < 3 or values[0].lower() != "appstate" or values[1] != "{":
            raise ValueError("invalid_appstate")
        depth = 1
        index = 2
        extracted = {}
        while index < len(values) and depth:
            token = values[index]
            if token == "{":
                depth += 1
                index += 1
            elif token == "}":
                depth -= 1
                index += 1
            elif depth == 1 and index + 1 < len(values) and values[index + 1] not in ("{", "}"):
                key = token.lower()
                if key in ("appid", "buildid"):
                    if key in extracted:
                        raise ValueError("duplicate_manifest_identity_key")
                    extracted[key] = values[index + 1]
                index += 2
            else:
                index += 1
        if depth or not extracted.get("appid", "").isdigit():
            raise ValueError("invalid_manifest_appid")
        build = extracted.get("buildid")
        if build is not None and not build.isdigit():
            raise ValueError("invalid_manifest_buildid")
        result.update({"read_status": "ok", "appid": extracted["appid"], "build_id": build,
                       "expected_appid_matches": extracted["appid"] == EXPECTED_APP_ID,
                       "sha256": streaming_sha256(path)})
    except (OSError, ValueError, UnicodeError) as error:
        result["read_status"] = "invalid_or_unreadable"
        result["error_type"] = type(error).__name__
    return result


def classify_runtime(files: List[Dict[str, Any]], layout_status: str) -> Dict[str, Any]:
    usable = [item for item in files if item.get("read_status") == "ok"]
    game_cli = [item for item in usable if item["role"] == "game_assembly" and Path(item["path"]).name.lower() in ("tg.main.dll", "assembly-csharp.dll") and item.get("binary", {}).get("managed_metadata_valid")]
    mono_files = [item for item in files if item["role"] == "mono_runtime"]
    mono_pe = [item for item in usable if item["role"] == "mono_runtime" and item.get("binary", {}).get("format") in ("PE32", "PE32+") and not item["binary"].get("has_cli_header")]
    assembly_files = [item for item in files if item["role"] == "il2cpp_native_candidate"]
    native_pe = [item for item in usable if item["role"] == "il2cpp_native_candidate" and item.get("binary", {}).get("format") in ("PE32", "PE32+") and not item["binary"].get("has_cli_header")]
    metadata_files = [item for item in files if item["role"] == "il2cpp_metadata_candidate"]
    metadata_valid = [item for item in usable if item.get("metadata", {}).get("il2cpp_magic_valid")]
    mono_score = (3 if game_cli else 0) + (2 if mono_pe else 0)
    il2cpp_score = (2 if native_pe else 0) + (3 if metadata_valid else 0)
    reasons = []
    classification = "unknown"
    if layout_status in ("ambiguous", "unexpected"):
        classification = "ambiguous"
        reasons.append("data_or_executable_layout_not_unique_and_expected")
    elif (game_cli or mono_files) and (assembly_files or metadata_files):
        classification = "ambiguous"
        reasons.append("mono_and_il2cpp_indicators_coexist")
    elif layout_status == "incomplete":
        reasons.append("required_game_layout_incomplete")
    elif game_cli and mono_pe:
        classification = "mono_likely"
        reasons.append("managed_game_metadata_and_native_mono_runtime_observed")
    elif native_pe and metadata_valid:
        classification = "il2cpp_likely"
        reasons.append("native_gameassembly_and_il2cpp_metadata_signature_observed")
    else:
        reasons.append("insufficient_validated_static_evidence")
    exe_architectures = {item.get("binary", {}).get("architecture") for item in usable if item["role"] == "game_executable"}
    runtime_architectures = {item.get("binary", {}).get("architecture") for item in mono_pe + native_pe}
    meaningful_exe = exe_architectures - {None, "unknown"}
    meaningful_runtime = runtime_architectures - {None, "unknown"}
    if meaningful_exe and meaningful_runtime and meaningful_exe != meaningful_runtime:
        classification = "ambiguous"
        reasons.append("executable_and_native_runtime_architecture_conflict")
    if any(item.get("read_status") != "ok" for item in files if item["role"] in ("game_executable", "game_assembly", "mono_runtime", "il2cpp_native_candidate", "il2cpp_metadata_candidate")):
        classification = "ambiguous"
        reasons.append("critical_file_unreadable_or_changed")
    return {
        "classification": classification, "confirmed": False, "reason_codes": reasons,
        "evidence_score": {"mono": mono_score, "il2cpp": il2cpp_score, "maximum_each": 5,
                           "meaning": "Heuristic evidence weights, not confidence percentages; filenames alone never confirm the active runtime."},
        "requires_in_game_probe": True,
    }


def inspect_game(game_dir: Path, appmanifest: Optional[Path] = None) -> Dict[str, Any]:
    root = game_dir.resolve(strict=True)
    if not root.is_dir():
        raise ValueError("game_directory_required")
    children = sorted(root.iterdir(), key=lambda path: path.name.lower())
    exe_candidates = [path for path in children if path.name.lower() == EXPECTED_EXE.lower()]
    data_candidates = [path for path in children if path.name.lower().endswith("_data") and (path.is_dir() or path.is_symlink())]
    if len(exe_candidates) > 1 or len(data_candidates) > 1 or any(path.is_symlink() for path in exe_candidates + data_candidates):
        layout_status = "ambiguous"
    elif not exe_candidates or not data_candidates:
        layout_status = "incomplete"
    elif data_candidates[0].name.lower() != EXPECTED_DATA.lower():
        layout_status = "unexpected"
    else:
        layout_status = "expected_unique"
    files: List[Dict[str, Any]] = []
    for path in exe_candidates:
        files.append(inventory_file(path, root, "game_executable"))
    for path in children:
        role = {"unityplayer.dll": "unity_player", "gameassembly.dll": "il2cpp_native_candidate"}.get(path.name.lower())
        if role:
            files.append(inventory_file(path, root, role))
    for data in data_candidates:
        if data.is_symlink() or not data.is_dir():
            continue
        managed = data / "Managed"
        if managed.is_dir() and not managed.is_symlink():
            for path in sorted(managed.iterdir(), key=lambda candidate: candidate.name.lower()):
                role = MANAGED_FILES.get(path.name.lower())
                if role:
                    files.append(inventory_file(path, root, role))
        for relative in ("il2cpp_data/Metadata/global-metadata.dat", "Metadata/global-metadata.dat"):
            path = data / relative
            if path.exists() or path.is_symlink():
                files.append(inventory_file(path, root, "il2cpp_metadata_candidate"))
    for relative in ("MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll", "Mono/EmbedRuntime/mono.dll", "Mono/mono.dll"):
        path = root / relative
        if path.exists() or path.is_symlink():
            files.append(inventory_file(path, root, "mono_runtime"))
    manifest = read_manifest(appmanifest)
    manifest_matches = manifest.get("expected_appid_matches")
    identity = "manifest_appid_matches" if manifest_matches is True else "conflicting_manifest" if manifest_matches is False else "filename_candidate_only" if exe_candidates else "unidentified"
    runtime = classify_runtime(files, layout_status)
    return {
        "schema_version": SCHEMA_VERSION,
        "scope": "Explicit game directory and optional explicit Steam manifest; no system scan.",
        "privacy": "Game file paths are relative; absolute installation paths and account data are omitted.",
        "identity": {"expected_title": "Tainted Grail: Conquest", "expected_steam_appid": EXPECTED_APP_ID,
                     "status": identity, "manifest": manifest,
                     "note": "A supplied manifest identifies itself, not ownership or integrity of the inspected binaries."},
        "layout": {"status": layout_status, "executable_candidates": [path.name for path in exe_candidates],
                   "data_directory_candidates": [path.name for path in data_candidates],
                   "selected_executable": exe_candidates[0].name if layout_status == "expected_unique" else None,
                   "selected_data_directory": data_candidates[0].name if layout_status == "expected_unique" else None},
        "runtime": runtime,
        "versions": {"game_version": None, "unity_version": None, "steam_build_id": manifest.get("build_id") if manifest_matches else None,
                     "note": "Exact game/Unity versions require local game logs or validated binary metadata. CLR strings are not game versions."},
        "files": files,
        "next_step": "Inspect local assembly metadata and verify a minimal in-game loader/speech probe. Do not enable gameplay actions from this report alone.",
        "compatibility_confirmed": False,
    }


def write_report(report: Dict[str, Any], output: Path, game_root: Path) -> Path:
    target = output.resolve()
    root = game_root.resolve()
    if within(target, root):
        raise ValueError("output_must_be_outside_game_directory")
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary_name = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", newline="\n", dir=target.parent, prefix=".inventory-", suffix=".tmp", delete=False) as stream:
            temporary_name = stream.name
            json.dump(report, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        os.replace(temporary_name, target)
    finally:
        if temporary_name and os.path.exists(temporary_name):
            os.unlink(temporary_name)
    return target


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Безопасная инвентаризация Tainted Grail: Conquest без запуска и изменения игры.")
    parser.add_argument("--game-dir", required=True, help="Явный путь к игре, например C:\\Games\\Tainted Grail или /mnt/c/Games/Tainted Grail.")
    parser.add_argument("--appmanifest", help="Необязательный явный путь к appmanifest_1199030.acf. Поиск на дисках не выполняется.")
    parser.add_argument("--output", default="artifacts/discovery/game-inventory.json", help="JSON вне каталога игры.")
    parser.add_argument("--verbose", action="store_true", help="Вывести полный JSON после краткого отчёта.")
    args = parser.parse_args(argv)
    try:
        root = normalize_game_path(args.game_dir).resolve(strict=True)
        output = normalize_game_path(args.output)
        if within(output.resolve(), root):
            raise ValueError("output_must_be_outside_game_directory")
        manifest = normalize_game_path(args.appmanifest) if args.appmanifest else None
        report = inspect_game(root, manifest)
        target = write_report(report, output, root)
    except (OSError, ValueError) as error:
        detail = "Выберите файл отчёта вне каталога игры." if isinstance(error, ValueError) and str(error) == "output_must_be_outside_game_directory" else "Проверьте указанный путь и права чтения; полный путь не выводится."
        print("Инвентаризация не завершена. " + detail, file=sys.stderr)
        print("Тип ошибки: " + type(error).__name__, file=sys.stderr)
        return 2
    runtime_labels = {"mono_likely": "вероятно Mono", "il2cpp_likely": "вероятно IL2CPP", "ambiguous": "неоднозначна", "unknown": "не определена"}
    print("Инвентаризация завершена; файлы игры не изменены.")
    print("AppID 1199030: " + ("совпадает с указанным манифестом." if report["identity"]["manifest"].get("expected_appid_matches") else "не подтверждён манифестом."))
    print("Структура каталогов: " + ("ожидаемая, единственный кандидат." if report["layout"]["status"] == "expected_unique" else "требуется проверка; автоматический выбор не выполнен."))
    print("Среда " + runtime_labels[report["runtime"]["classification"]] + "; совместимость требует запуска проверочного модуля.")
    print("Отчёт: " + target.name.replace("\r", "").replace("\n", "") + ".")
    if args.verbose:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
