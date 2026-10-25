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

- `release/Dictionary-0.3/` — runnable portable folder, with `Dictionary.exe` at its root.
- `release/Portable-Dictionary-0.3-Windows-x64.zip` — self-contained portable release.
- `release/Portable-Dictionary-0.3-Source.zip` — complete source, tests, prepared data, scripts, and notices.
- `release/Portable-Dictionary-0.3-SHA256SUMS.txt` — hashes of both ZIP deliverables.

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
  -o release/Dictionary-0.3
```

Copy `data/dictionary.db` to `release/Dictionary-0.3/data/`, and copy `Assets`, `licenses`, `README.md`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `docs/VERIFICATION.md` into the portable folder. `tools/build.py` performs these steps and creates the ZIP automatically.

## Source layout

- `src/Dictionary.Core/` — SQLite validation/access, lookup, morphology, normalization, synonym filtering, history, and portable settings.
- `src/Dictionary.App/` — Windows Forms UI, global shortcut, UI Automation and clipboard capture, single instance, tray, dialogs, and Windows integration.
- `tests/Dictionary.Tests/` — 30 cross-platform automated tests against controlled cases and the real database.
- `tools/` — manifest, downloader, importer, importer/data tests, build and packaging automation.
- `data/dictionary.db` — prepared schema 1 database; no first-run conversion.
- `licenses/` — data, runtime, and library notices.

## Change the dataset or schema

A different dataset requires updating the manifest, `DataContract`, tests, and documentation together. Any incompatible prepared representation change requires incrementing schema 1 in both the importer and `DataContract.SchemaVersion`; the application deliberately rejects a mismatch rather than silently returning incomplete results.
