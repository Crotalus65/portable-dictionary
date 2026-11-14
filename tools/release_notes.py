#!/usr/bin/env python3
"""Print the GitHub release notes for the current application version.

The notes combine the download and run instructions, the matching
"## Version X" section of README.md, and the SHA-256 checksums written by
tools/build.py when release/ already holds them. The build workflow runs
this when it publishes a release for a version tag.

    python tools/release_notes.py            # print the notes as Markdown
    python tools/release_notes.py --version  # print only the version number
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from build import APP_VERSION, CHECKSUMS, PORTABLE_ZIP, ROOT, SOURCE_ZIP  # noqa: E402


def version_section(readme: str, version: str) -> str:
    """The body of the README heading '## Version <version>', or '' when absent."""
    heading = re.compile(rf"^## Version {re.escape(version)}\s*$", re.MULTILINE)
    match = heading.search(readme)
    if not match:
        return ""
    rest = readme[match.end():]
    following = re.search(r"^## ", rest, re.MULTILINE)
    return (rest[: following.start()] if following else rest).strip()


def release_notes() -> str:
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    lines = [
        f"Portable Dictionary and Thesaurus {APP_VERSION}: a compact, offline Windows 10/11 x64 "
        "dictionary and thesaurus with a global lookup shortcut.",
        "",
        "## Download and run",
        "",
        f"1. Download `{PORTABLE_ZIP.name}` below and extract the whole archive to a writable folder or USB drive.",
        f"2. Open the extracted `Dictionary-{APP_VERSION}` folder and run `Dictionary.exe`. "
        "No installer, separate .NET installation, or network connection is needed.",
        "3. If an earlier version is running, choose **Exit** from its tray menu first.",
        "4. The executable is not code-signed. If Windows SmartScreen shows \"Windows protected your PC\", "
        "choose **More info**, then **Run anyway**.",
        "",
    ]
    whats_new = version_section(readme, APP_VERSION)
    if whats_new:
        lines += [f"## What's new in {APP_VERSION}", "", whats_new, ""]
    if CHECKSUMS.exists():
        lines += ["## SHA-256 checksums", "", "```", CHECKSUMS.read_text(encoding="utf-8").strip(), "```", ""]
    lines += [
        f"`{SOURCE_ZIP.name}` is the matching source. Both archives were built by GitHub Actions on a "
        "Windows runner from the tagged commit; see docs/BUILD.md to reproduce them."
    ]
    return "\n".join(lines) + "\n"


def main() -> None:
    if "--version" in sys.argv[1:]:
        print(APP_VERSION)
        return
    sys.stdout.write(release_notes())


if __name__ == "__main__":
    main()
