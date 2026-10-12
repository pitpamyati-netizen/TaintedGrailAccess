#!/usr/bin/env python3
"""Compile and run portable suites with an installed .NET 8 SDK, without NuGet.

This uses SDK-owned Roslyn and reference packs directly. It does not install an
SDK, modify a game, generate loader stubs, or claim to validate native speech.
Use --netstandard-ref to additionally validate production netstandard2.0 APIs.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path


def version_key(path: Path) -> tuple[int, ...]:
    return tuple(int(n) for n in re.findall(r"\d+", path.name))


def latest(parent: Path, pattern: str) -> Path:
    options = sorted(parent.glob(pattern), key=version_key)
    if not options:
        raise RuntimeError(f"Missing .NET SDK component: {parent.name}/{pattern}")
    return options[-1]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk-root", type=Path, required=True, help="Directory containing dotnet executable, sdk and packs")
    parser.add_argument("--netstandard-ref", type=Path, help="NETStandard.Library 2.0.3 build/netstandard2.0/ref directory")
    parser.add_argument("--output", type=Path, default=None)
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[1]
    output = (args.output or project / "artifacts" / "verification").resolve()
    output.mkdir(parents=True, exist_ok=True)
    sdk_root = args.sdk_root.resolve()
    dotnet = sdk_root / ("dotnet.exe" if sys.platform == "win32" else "dotnet")
    sdk = latest(sdk_root / "sdk", "8.*")
    csc = sdk / "Roslyn" / "bincore" / "csc.dll"
    ref_pack = latest(sdk_root / "packs" / "Microsoft.NETCore.App.Ref", "8.*")
    net8_refs = sorted((ref_pack / "ref" / "net8.0").glob("*.dll"))
    library_refs = sorted(args.netstandard_ref.resolve().glob("*.dll")) if args.netstandard_ref else net8_refs
    if not dotnet.is_file() or not csc.is_file() or not net8_refs or not library_refs:
        raise RuntimeError("Incomplete SDK or reference directory.")
    if args.netstandard_ref and not (args.netstandard_ref / "netstandard.dll").is_file():
        raise RuntimeError("The netstandard reference directory has no netstandard.dll.")
    results: list[dict] = []
    built: dict[str, Path] = {}

    def compile_project(name: str, test: bool, dependencies: list[str]) -> Path:
        folder = project / ("tests" if test else "src") / name
        sources = sorted(p for p in folder.rglob("*.cs") if "obj" not in p.parts and "bin" not in p.parts)
        if not sources:
            raise RuntimeError(f"No sources for {name}")
        destination = output / (name + ".dll")
        flags = ["-nologo", "-nostdlib+", "-langversion:7.3", "-warnaserror+", "-deterministic+", "-parallel-",
                 "-target:" + ("exe" if test else "library"), '-out:"' + str(destination) + '"']
        flags += ['-r:"' + str(p) + '"' for p in (net8_refs if test else library_refs)]
        flags += ['-r:"' + str(built[dep]) + '"' for dep in dependencies]
        flags += ['"' + str(p) + '"' for p in sources]
        response = output / (name + ".rsp")
        response.write_text("\n".join(flags) + "\n", encoding="utf-8")
        process = subprocess.run([str(dotnet), str(csc), "@" + str(response)], cwd=project,
                                 text=True, capture_output=True, encoding="utf-8")
        text = process.stdout + process.stderr
        (output / (name + ".compile.log")).write_text(text, encoding="utf-8")
        results.append({"project": name, "phase": "compile", "exit_code": process.returncode,
                        "reference_target": "net8.0" if test or not args.netstandard_ref else "netstandard2.0"})
        if process.returncode:
            raise RuntimeError(f"{name}: compilation failed\n{text}")
        built[name] = destination
        if test:
            (output / (name + ".runtimeconfig.json")).write_text(json.dumps({"runtimeOptions": {
                "tfm": "net8.0", "framework": {"name": "Microsoft.NETCore.App", "version": "8.0.0"},
                "rollForward": "LatestPatch"}}, indent=2), encoding="utf-8")
            process = subprocess.run([str(dotnet), str(destination)], cwd=project, text=True,
                                     capture_output=True, encoding="utf-8")
            text = process.stdout + process.stderr
            (output / (name + ".run.log")).write_text(text, encoding="utf-8")
            results.append({"project": name, "phase": "run", "exit_code": process.returncode, "output": text.strip()})
            if process.returncode:
                raise RuntimeError(f"{name}: tests failed\n{text}")
            print(text.strip())
        return destination

    succeeded = False
    try:
        compile_project("TaintedGrailAccess.Graph", False, [])
        compile_project("TaintedGrailAccess.Runtime", False, ["TaintedGrailAccess.Graph"])
        compile_project("TaintedGrailAccess.Windows", False, ["TaintedGrailAccess.Runtime"])
        compile_project("TaintedGrailAccess.Graph.Tests", True, ["TaintedGrailAccess.Graph"])
        compile_project("TaintedGrailAccess.Runtime.Tests", True, ["TaintedGrailAccess.Graph", "TaintedGrailAccess.Runtime"])
        windows_tests = project / "tests" / "TaintedGrailAccess.Windows.Tests"
        if windows_tests.is_dir():
            compile_project("TaintedGrailAccess.Windows.Tests", True,
                            ["TaintedGrailAccess.Graph", "TaintedGrailAccess.Runtime", "TaintedGrailAccess.Windows"])
        python_tests = subprocess.run([sys.executable, "-m", "unittest", "discover", "-s", "tests", "-p", "test_*.py"],
                                      cwd=project, text=True, capture_output=True, encoding="utf-8")
        python_output = python_tests.stdout + python_tests.stderr
        (output / "python-tests.log").write_text(python_output, encoding="utf-8")
        results.append({"project": "python", "phase": "run", "exit_code": python_tests.returncode, "output": python_output.strip()})
        if python_tests.returncode:
            raise RuntimeError("Python tests failed\n" + python_output)
        print("Python: " + next((line for line in python_output.splitlines() if line.startswith("Ran ")), "passed"))
        succeeded = True
        return 0
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        return 1
    finally:
        report = {"success": succeeded, "sdk": sdk.name, "method": "Direct SDK Roslyn compiler, separate assemblies",
                  "production_reference_check": bool(args.netstandard_ref), "game_tested": False, "native_speech_tested": False,
                  "results": results}
        (output / "verification.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print("Report: " + str(output / "verification.json"))


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, RuntimeError) as error:
        print("Verification unavailable: " + str(error), file=sys.stderr)
        raise SystemExit(2)
