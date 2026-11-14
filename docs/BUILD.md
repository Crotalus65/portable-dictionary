# Reproducible build

## Prerequisites

- Windows 10/11 x64 for running or manually testing the application; Linux can cross-build it.
- .NET SDK **10.0.402**, selected by `global.json` with `latestFeature` roll-forward.
- Python 3.10 or later. Data scripts use only the Python standard library.
- Network access for NuGet restore, unless the required packages are cached. Runtime lookup is entirely offline.

The source ZIP includes `data/dictionary.db`, so a normal application build does not download or reimport OEWN. Upstream archives are deliberately not duplicated in the source ZIP; `tools/fetch_data.py` retrieves the exact pinned files when needed.

## One-command build

```powershell
py -3 tools/build.py
```

```bash
python3 tools/build.py
```

To regenerate the prepared data first:

```bash
python3 tools/build.py --reimport
```

Outputs:

- `release/Dictionary-1.0.5/` — runnable portable folder, with `Dictionary.exe` at its root.
- `release/Portable-Dictionary-1.0.5-Windows-x64.zip` — self-contained portable release.
- `release/Portable-Dictionary-1.0.5-Source.zip` — complete source, tests, prepared data, scripts, and notices.
- `release/Portable-Dictionary-1.0.5-SHA256SUMS.txt` — hashes of both ZIP deliverables.

The packager writes stable ZIP entry order and timestamps. .NET uses deterministic compilation, but byte-for-byte output can still vary with the SDK patch, NuGet dependency versions, or runtime pack. Use the pinned SDK and included lock files for the closest reproduction.

## Individual steps

### 1. Fetch and verify upstream archives

```bash
python3 tools/fetch_data.py
```

`tools/source_manifest.json` is authoritative for dataset identity, URLs, and hashes. Any hash mismatch stops the build.

### 2. Prepare the offline database

```bash
python3 tools/prepare_data.py
```

The importer streams WN-LMF XML, reads all four WNDB exception files, writes a temporary SQLite database, validates source identity and foreign keys, runs `PRAGMA integrity_check`, creates indexes, and atomically replaces `data/dictionary.db`. It does not run on an end user's machine.

Prepared schema 1 contains metadata, lexical entries, senses, synsets, definitions, examples, exception mappings, and an indexed `synset_members` representation derived directly from sense-to-synset membership. The latter avoids expensive runtime joins from slow removable media without changing which words are synonyms for a meaning.

### 3. Run automated checks

```bash
python3 tools/test_importer.py
dotnet test tests/Dictionary.Tests/Dictionary.Tests.csproj -c Release
dotnet build PortableDictionary.slnx -c Release
```

### 4. Publish Windows x64 self-contained

```bash
dotnet publish src/Dictionary.App/Dictionary.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false \
  -o release/Dictionary-1.0.5
```

Copy `data/dictionary.db` to `release/Dictionary-1.0.5/data/`, and copy `Assets`, `licenses`, `README.md`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `docs/VERIFICATION.md` into the portable folder. `tools/build.py` performs these steps and creates the ZIP automatically.

## Source layout

- `src/Dictionary.Core/` — SQLite validation/access, lookup, morphology, normalization, synonym filtering, history, and portable settings.
- `src/Dictionary.App/` — Windows Forms UI, global shortcut, UI Automation and clipboard capture, single instance, tray, dialogs, and Windows integration.
- `tests/Dictionary.Tests/` — 82 cross-platform automated tests against capture cases, controlled core cases, and the real database. The selection reader, clipboard reader, capture coordinator, guide navigation, guide document, and license display formatter are linked from the application so the tests exercise production policies. The tests embed the same guide template and use the bundled Princeton notice.
- `tools/` — manifest, downloader, importer, importer/data tests, build and packaging automation.
- `data/dictionary.db` — prepared schema 1 database; no first-run conversion.
- `licenses/` — data, runtime, and library notices.
- `docs/USER_GUIDE.md` — the guide template embedded in the application; its version, current shortcut, and dataset tokens are expanded at runtime. Native labels provide wrapping and keyboard-accessible notice links.

## Release a version

Releases are published by the GitHub Actions workflow in `.github/workflows/build.yml`, which runs the same `tools/build.py` steps on a `windows-latest` runner with the SDK pinned by `global.json`.

1. Set the new version in `tools/build.py` (`APP_VERSION`), `src/Dictionary.App/Dictionary.App.csproj` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`), the README title and the file names mentioned in this document, and add a `## Version X.Y.Z` section to the README; that section becomes the release notes (`tools/release_notes.py` assembles them).
2. Push to `main` and wait for the **Build** workflow to pass. Every push produces the archives as a 30-day workflow artifact without publishing anything.
3. Either tag the commit and push the tag (`git tag v1.0.5 && git push origin v1.0.5`; the tag must equal `v` plus `APP_VERSION`, and the workflow refuses a mismatch), or open **Actions → Build → Run workflow** on `main` and enable **Publish a release**, which makes the runner create that tag itself.
4. The tag run publishes the GitHub release with `Portable-Dictionary-X.Y.Z-Windows-x64.zip`, `Portable-Dictionary-X.Y.Z-Source.zip` and `Portable-Dictionary-X.Y.Z-SHA256SUMS.txt`. The README's download link always points at the latest release.

## Change the dataset or schema

A different dataset requires updating the manifest, `DataContract`, tests, and documentation together. Any incompatible prepared representation change requires incrementing schema 1 in both the importer and `DataContract.SchemaVersion`; the application deliberately rejects a mismatch rather than silently returning incomplete results.

## Native capture references

- Word window object model: https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-accessibleobjectfromwindow
- IAccessible2 text interface and method ordering: https://github.com/LinuxA11y/IAccessible2/blob/master/api/AccessibleText.idl
- Clipboard ownership and delayed rendering: https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-operations

## Notice display formatting

The Princeton license page (https://wordnet.princeton.edu/license-and-commercial-use) presents unnumbered prose. The bundled OEWN WNDB notice has a sequential 1..30 line-number column. Version 1.0.5 omits that column only in the viewer, and only when the complete sequence is recognized. License files remain byte-for-byte unchanged; clause numbers in other notices are retained. Notice dialogs focus their Close button and explicitly clear the initial text selection on Shown.
