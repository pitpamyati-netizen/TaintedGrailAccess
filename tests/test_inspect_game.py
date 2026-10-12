"""Synthetic binary fixtures: no game files or proprietary assemblies needed."""

import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest


MODULE_PATH = Path(__file__).resolve().parents[1] / "scripts" / "inspect_game.py"
SPEC = importlib.util.spec_from_file_location("inspect_game", MODULE_PATH)
inspector = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(inspector)


def pe_fixture(architecture="x64", managed=False, metadata_valid=True):
    """Minimal bounded PE headers and a real CLI metadata signature if requested."""
    image = bytearray(0x600)
    image[:2] = b"MZ"
    struct.pack_into("<I", image, 0x3C, 0x80)
    image[0x80:0x84] = b"PE\x00\x00"
    machine, optional_size, magic = (0x8664, 240, 0x20B) if architecture == "x64" else (0x014C, 224, 0x10B)
    struct.pack_into("<HH", image, 0x84, machine, 1)
    struct.pack_into("<H", image, 0x94, optional_size)
    optional = 0x98
    struct.pack_into("<H", image, optional, magic)
    struct.pack_into("<I", image, optional + 60, 0x200)
    directory_offset = 112 if architecture == "x64" else 96
    struct.pack_into("<I", image, optional + directory_offset - 4, 16)
    section = optional + optional_size
    image[section:section + 8] = b".text\x00\x00\x00"
    struct.pack_into("<IIII", image, section + 8, 0x400, 0x1000, 0x400, 0x200)
    if managed:
        struct.pack_into("<II", image, optional + directory_offset + 14 * 8, 0x1000, 72)
        struct.pack_into("<IHHIII", image, 0x200, 72, 2, 5, 0x1080, 64, 1)
        image[0x280:0x284] = b"BSJB" if metadata_valid else b"NOPE"
        struct.pack_into("<HHII", image, 0x284, 1, 1, 0, 12)
        image[0x290:0x29C] = b"v4.0.30319\x00".ljust(12, b"\x00")
    return bytes(image)


class InspectGameTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="private-user-inspector-")
        self.base = Path(self.temporary.name)
        self.game = self.base / "game"
        self.game.mkdir()
        self.data = self.game / "Tainted Grail_Data"
        self.data.mkdir()
        self.write("Tainted Grail.exe", pe_fixture())

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, relative, contents):
        path = self.game / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(contents)
        return path

    def add_mono(self, architecture="x64"):
        self.write("Tainted Grail_Data/Managed/TG.Main.dll", pe_fixture(managed=True))
        self.write("Tainted Grail_Data/Managed/Assembly-CSharp.dll", pe_fixture(managed=True))
        self.write("MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll", pe_fixture(architecture))

    def add_il2cpp(self, magic=True):
        self.write("GameAssembly.dll", pe_fixture())
        self.write("Tainted Grail_Data/il2cpp_data/Metadata/global-metadata.dat",
                   struct.pack("<II", 0xFAB11BAF if magic else 0, 29) + bytes(64))

    def test_windows_wsl_paths_are_translated_without_search(self):
        path = inspector.normalize_game_path(r"D:\Games\Tainted Grail", windows_host=False)
        self.assertEqual(str(path), "/mnt/d/Games/Tainted Grail")
        path = inspector.normalize_game_path("/mnt/d/Games/Tainted Grail", windows_host=True)
        self.assertEqual(str(path), "D:\\Games\\Tainted Grail")
        self.assertEqual(str(inspector.normalize_game_path("/games/test", windows_host=False)), "/games/test")

    def test_pe_architecture_and_cli_metadata_are_positive_evidence(self):
        for architecture in ("x86", "x64"):
            with self.subTest(architecture=architecture):
                path = self.write("fixture.dll", pe_fixture(architecture, managed=True))
                result = inspector.inspect_pe(path)
                self.assertEqual(result["architecture"], architecture)
                self.assertTrue(result["has_cli_header"])
                self.assertTrue(result["managed_metadata_valid"])
                self.assertIn("cli_metadata_signature", result["confirmed_types"])
                self.assertEqual(result["metadata_runtime_string"], "v4.0.30319")

    def test_filename_dll_and_mz_alone_are_not_managed_evidence(self):
        path = self.write("fake.dll", b"MZ" + bytes(20))
        result = inspector.inspect_pe(path)
        self.assertEqual(result["format"], "unknown")
        self.assertFalse(result["managed_metadata_valid"])

    def test_cli_header_with_bad_metadata_is_not_valid_managed_assembly(self):
        result = inspector.inspect_pe(self.write("fake.dll", pe_fixture(managed=True, metadata_valid=False)))
        self.assertTrue(result["has_cli_header"])
        self.assertFalse(result["managed_metadata_valid"])
        self.assertEqual(result["validation_issue"], "invalid_cli_metadata_signature")

    def test_out_of_bounds_cli_rva_does_not_crash_or_confirm_cli(self):
        image = bytearray(pe_fixture(managed=True))
        struct.pack_into("<I", image, 0x98 + 112 + 14 * 8, 0xFFFFF000)
        result = inspector.inspect_pe(self.write("bad.dll", bytes(image)))
        self.assertFalse(result["has_cli_header"])
        self.assertFalse(result["managed_metadata_valid"])
        self.assertEqual(result["validation_issue"], "invalid_cli_rva")

    def test_valid_mono_evidence_remains_likely_not_runtime_confirmed(self):
        self.add_mono()
        report = inspector.inspect_game(self.game)
        self.assertEqual(report["runtime"]["classification"], "mono_likely")
        self.assertEqual(report["runtime"]["evidence_score"]["mono"], 5)
        self.assertFalse(report["runtime"]["confirmed"])
        self.assertFalse(report["compatibility_confirmed"])
        self.assertIsNone(report["versions"]["game_version"])
        self.assertIsNone(report["versions"]["unity_version"])

    def test_native_il2cpp_and_positive_metadata_signature_are_likely(self):
        self.add_il2cpp()
        report = inspector.inspect_game(self.game)
        self.assertEqual(report["runtime"]["classification"], "il2cpp_likely")
        self.assertFalse(report["runtime"]["confirmed"])

    def test_il2cpp_filenames_without_metadata_magic_are_unknown(self):
        self.add_il2cpp(magic=False)
        self.assertEqual(inspector.inspect_game(self.game)["runtime"]["classification"], "unknown")

    def test_both_runtime_families_are_ambiguous(self):
        self.add_mono()
        self.add_il2cpp()
        report = inspector.inspect_game(self.game)
        self.assertEqual(report["runtime"]["classification"], "ambiguous")
        self.assertIn("mono_and_il2cpp_indicators_coexist", report["runtime"]["reason_codes"])

    def test_multiple_data_directories_are_never_guessed(self):
        self.add_mono()
        (self.game / "OldBuild_Data").mkdir()
        report = inspector.inspect_game(self.game)
        self.assertEqual(report["layout"]["status"], "ambiguous")
        self.assertIsNone(report["layout"]["selected_data_directory"])
        self.assertIsNone(report["layout"]["selected_executable"])
        self.assertEqual(report["runtime"]["classification"], "ambiguous")

    def test_other_single_data_directory_is_unexpected(self):
        self.data.rename(self.game / "AnotherGame_Data")
        report = inspector.inspect_game(self.game)
        self.assertEqual(report["layout"]["status"], "unexpected")
        self.assertIsNone(report["layout"]["selected_data_directory"])

    def test_native_runtime_architecture_conflict_is_ambiguous(self):
        self.add_mono(architecture="x86")
        report = inspector.inspect_game(self.game)
        self.assertEqual(report["runtime"]["classification"], "ambiguous")
        self.assertIn("executable_and_native_runtime_architecture_conflict", report["runtime"]["reason_codes"])

    def test_manifest_appid_and_buildid_do_not_use_nested_keys(self):
        manifest = self.base / "appmanifest_1199030.acf"
        manifest.write_text('"AppState" { "appid" "1199030" "buildid" "20188111" "Depots" { "appid" "1466060" "buildid" "9" } }', encoding="utf-8")
        report = inspector.inspect_game(self.game, manifest)
        self.assertEqual(report["identity"]["status"], "manifest_appid_matches")
        self.assertEqual(report["versions"]["steam_build_id"], "20188111")
        self.assertNotIn(str(self.base), json.dumps(report))

    def test_wrong_manifest_filename_does_not_override_wrong_appid(self):
        manifest = self.base / "appmanifest_1199030.acf"
        manifest.write_text('"AppState" { "appid" "1466060" "buildid" "42" }', encoding="utf-8")
        report = inspector.inspect_game(self.game, manifest)
        self.assertEqual(report["identity"]["status"], "conflicting_manifest")
        self.assertIsNone(report["versions"]["steam_build_id"])

    def test_duplicate_identity_keys_make_manifest_invalid(self):
        manifest = self.base / "appmanifest_1199030.acf"
        manifest.write_text('"AppState" { "appid" "1199030" "appid" "1466060" }', encoding="utf-8")
        self.assertEqual(inspector.read_manifest(manifest)["read_status"], "invalid_or_unreadable")

    def test_manifest_is_not_implicitly_read_from_parent(self):
        (self.base / "appmanifest_1199030.acf").write_text('"AppState" { "appid" "1199030" }', encoding="utf-8")
        self.assertFalse(inspector.inspect_game(self.game)["identity"]["manifest"]["provided"])

    def test_hashes_are_streamed_and_all_report_paths_are_relative(self):
        self.add_mono()
        before = {path: path.read_bytes() for path in self.game.rglob("*") if path.is_file()}
        report = inspector.inspect_game(self.game)
        for item in report["files"]:
            self.assertFalse(Path(item["path"]).is_absolute())
            self.assertEqual(item["sha256"], hashlib.sha256(before[self.game / item["path"]]).hexdigest())
        self.assertNotIn(str(self.base), json.dumps(report))
        self.assertEqual(before, {path: path.read_bytes() for path in self.game.rglob("*") if path.is_file()})

    def test_output_within_game_is_refused_before_directory_creation(self):
        target = self.game / "new-folder" / "inventory.json"
        with self.assertRaisesRegex(ValueError, "outside_game_directory"):
            inspector.write_report({}, target, self.game)
        self.assertFalse(target.parent.exists())

    def test_output_symlink_into_game_is_refused(self):
        outside_link = self.base / "report-link"
        try:
            outside_link.symlink_to(self.game, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("Symlink creation is not available.")
        with self.assertRaisesRegex(ValueError, "outside_game_directory"):
            inspector.write_report({}, outside_link / "report.json", self.game)
        self.assertFalse((self.game / "report.json").exists())

    def test_game_symlink_to_external_assembly_is_not_read(self):
        external = self.base / "private.dll"
        external.write_bytes(pe_fixture(managed=True))
        managed = self.data / "Managed"
        managed.mkdir()
        try:
            (managed / "TG.Main.dll").symlink_to(external)
        except (OSError, NotImplementedError):
            self.skipTest("Symlink creation is not available.")
        report = inspector.inspect_game(self.game)
        entry = next(item for item in report["files"] if item["role"] == "game_assembly")
        self.assertEqual(entry["read_status"], "skipped_symlink_or_not_regular_file")
        self.assertNotIn("sha256", entry)
        self.assertEqual(report["runtime"]["classification"], "ambiguous")

    def test_cli_has_five_summary_lines_and_writes_only_external_json(self):
        self.add_mono()
        output = self.base / "reports" / "inventory.json"
        capture = io.StringIO()
        with contextlib.redirect_stdout(capture):
            code = inspector.main(["--game-dir", str(self.game), "--output", str(output)])
        self.assertEqual(code, 0)
        self.assertEqual(len(capture.getvalue().splitlines()), 5)
        self.assertTrue(output.is_file())
        self.assertNotIn(str(self.base), capture.getvalue())
        self.assertEqual(json.loads(output.read_text(encoding="utf-8"))["runtime"]["classification"], "mono_likely")


if __name__ == "__main__":
    unittest.main()
