#!/usr/bin/env python3
"""Download and verify the exact upstream archives in source_manifest.json."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]


def file_hash(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    manifest = json.loads((ROOT / "tools" / "source_manifest.json").read_text(encoding="utf-8"))
    destination = ROOT / "upstream"
    destination.mkdir(parents=True, exist_ok=True)
    for item in manifest["archives"]:
        target = destination / item["file"]
        if target.exists() and file_hash(target) == item["sha256"]:
            print(f"Verified existing {target.name}")
            continue
        temporary = target.with_suffix(target.suffix + ".part")
        request = urllib.request.Request(item["url"], headers={"User-Agent": "PortableDictionary-Build/1.0"})
        print(f"Downloading {item['url']}")
        with urllib.request.urlopen(request, timeout=180) as response, temporary.open("wb") as output:
            while chunk := response.read(1024 * 1024):
                output.write(chunk)
        actual = file_hash(temporary)
        if actual != item["sha256"]:
            temporary.unlink(missing_ok=True)
            raise RuntimeError(f"SHA-256 mismatch for {item['file']}: expected {item['sha256']}, got {actual}")
        temporary.replace(target)
        print(f"Verified {target.name}: {actual}")


if __name__ == "__main__":
    main()
