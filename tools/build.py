#!/usr/bin/env python3
"""Test, publish, and package Portable Dictionary for Windows x64."""
from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RELEASE = ROOT / "release"
APP_VERSION = "0.3"
PORTABLE_FOLDER = RELEASE / f"Dictionary-{APP_VERSION}"
PORTABLE_ZIP = RELEASE / f"Portable-Dictionary-{APP_VERSION}-Windows-x64.zip"
SOURCE_ZIP = RELEASE / f"Portable-Dictionary-{APP_VERSION}-Source.zip"
CHECKSUMS = RELEASE / f"Portable-Dictionary-{APP_VERSION}-SHA256SUMS.txt"
DOTNET = os.environ.get("DOTNET", "dotnet")
FIXED_ZIP_TIME = (2025, 12, 31, 0, 0, 0)


def run(arguments: list[str]) -> None:
    print("\n>", " ".join(arguments), flush=True)
    environment = os.environ.copy()
    environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    subprocess.run(arguments, cwd=ROOT, env=environment, check=True)


def file_hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def zip_tree(archive_path: Path, source: Path, prefix: str, include=None) -> None:
    archive_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = archive_path.with_suffix(archive_path.suffix + ".tmp")
    temporary.unlink(missing_ok=True)
    files = []
    for path in source.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if include is not None and not include(relative):
            continue
        files.append((relative, path))
    with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for relative, path in sorted(files, key=lambda item: item[0].as_posix()):
            info = zipfile.ZipInfo(prefix + relative.as_posix(), FIXED_ZIP_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            with path.open("rb") as input_stream, archive.open(info, "w") as output_stream:
                shutil.copyfileobj(input_stream, output_stream, 1024 * 1024)
    os.replace(temporary, archive_path)


def source_file_included(relative: Path) -> bool:
    excluded_directories = {"bin", "obj", "release", "upstream", "__pycache__", ".git", ".vs"}
    if any(part in excluded_directories for part in relative.parts):
        return False
    if relative.name.endswith((".tmp", ".user", ".pyc")):
        return False
    return True


def publish() -> None:
    if PORTABLE_FOLDER.exists():
        shutil.rmtree(PORTABLE_FOLDER)
    PORTABLE_FOLDER.parent.mkdir(parents=True, exist_ok=True)
    run([
        DOTNET, "publish", "src/Dictionary.App/Dictionary.App.csproj",
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=false", "-p:DebugType=None", "-p:DebugSymbols=false",
        "-o", str(PORTABLE_FOLDER),
    ])

    for pattern in ("*.pdb", "*.xml"):
        for path in PORTABLE_FOLDER.rglob(pattern):
            path.unlink()

    data_target = PORTABLE_FOLDER / "data"
    data_target.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / "data" / "dictionary.db", data_target / "dictionary.db")
    shutil.copytree(ROOT / "licenses", PORTABLE_FOLDER / "licenses", dirs_exist_ok=True)
    shutil.copytree(ROOT / "src" / "Dictionary.App" / "Assets", PORTABLE_FOLDER / "Assets", dirs_exist_ok=True)
    for filename in ("README.md", "LICENSE", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(ROOT / filename, PORTABLE_FOLDER / filename)
    docs_target = PORTABLE_FOLDER / "docs"
    docs_target.mkdir(exist_ok=True)
    shutil.copy2(ROOT / "docs" / "VERIFICATION.md", docs_target / "VERIFICATION.md")

    required = [
        PORTABLE_FOLDER / "Dictionary.exe",
        PORTABLE_FOLDER / "Dictionary.dll",
        PORTABLE_FOLDER / "Dictionary.deps.json",
        PORTABLE_FOLDER / "Dictionary.runtimeconfig.json",
        PORTABLE_FOLDER / "coreclr.dll",
        PORTABLE_FOLDER / "hostfxr.dll",
        data_target / "dictionary.db",
        PORTABLE_FOLDER / "Assets" / "Dictionary.ico",
    ]
    missing = [str(path) for path in required if not path.exists()]
    if not list(PORTABLE_FOLDER.rglob("e_sqlite3.dll")):
        missing.append("e_sqlite3.dll")
    if missing:
        raise RuntimeError("Publish output is incomplete: " + ", ".join(missing))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reimport", action="store_true", help="Download, hash-verify, and regenerate OEWN data first")
    parser.add_argument("--skip-tests", action="store_true", help="Publish without running tests (not recommended)")
    parser.add_argument("--skip-source", action="store_true", help="Do not create the complete source ZIP")
    args = parser.parse_args()

    if args.reimport or not (ROOT / "data" / "dictionary.db").exists():
        run([sys.executable, "tools/fetch_data.py"])
        run([sys.executable, "tools/prepare_data.py"])
    if not args.skip_tests:
        run([sys.executable, "tools/test_importer.py"])
        run([DOTNET, "test", "tests/Dictionary.Tests/Dictionary.Tests.csproj", "-c", "Release"])

    publish()
    zip_tree(PORTABLE_ZIP, PORTABLE_FOLDER, f"Dictionary-{APP_VERSION}/")
    if not args.skip_source:
        zip_tree(SOURCE_ZIP, ROOT, f"Portable-Dictionary-{APP_VERSION}-Source/", source_file_included)

    archives = [PORTABLE_ZIP] + ([] if args.skip_source else [SOURCE_ZIP])
    checksums = "".join(f"{file_hash(path)}  {path.name}\n" for path in archives)
    CHECKSUMS.write_text(checksums, encoding="utf-8")
    print("\nBuild completed:")
    for path in archives:
        print(f"  {path.relative_to(ROOT)} ({path.stat().st_size / 1024 / 1024:.1f} MiB)")
    print(f"  {PORTABLE_FOLDER.relative_to(ROOT)}/Dictionary.exe")


if __name__ == "__main__":
    main()
