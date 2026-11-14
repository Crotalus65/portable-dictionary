# Portable Dictionary and Thesaurus 1.0.5

[![Build](https://github.com/Crotalus65/portable-dictionary/actions/workflows/build.yml/badge.svg)](https://github.com/Crotalus65/portable-dictionary/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/Crotalus65/portable-dictionary?label=download)](https://github.com/Crotalus65/portable-dictionary/releases/latest)

A compact, offline Windows 10/11 x64 dictionary and thesaurus built with C#, Windows Forms, and .NET 10. It combines Open English WordNet definitions, source examples, meaning-specific synonyms, WordNet-style morphology, a global selected-text shortcut, tray operation, and portable settings.

## Download

**[Download the latest release](https://github.com/Crotalus65/portable-dictionary/releases/latest)** and pick `Portable-Dictionary-<version>-Windows-x64.zip`. The archive is about 85 MB because it bundles the .NET runtime and the complete dictionary database; nothing else needs to be installed.

1. Extract the whole archive to a writable folder or USB drive.
2. Open the extracted `Dictionary-<version>` folder and run `Dictionary.exe`.
3. The executable is not code-signed, so Windows SmartScreen may show "Windows protected your PC" the first time. Choose **More info**, then **Run anyway**.

Each release page also lists SHA-256 checksums for the archives and offers the matching source archive. Usage instructions appear in the User Guide shown when the program starts and in [docs/USER_GUIDE.md](docs/USER_GUIDE.md).

## Version 1.0.5

- Resets the startup and reopened guide to the top, showing **Dictionary User Guide** after layout and initial focus settle.
- Applies the revised guide wording, moves **Reopen this guide** immediately after **Change the lookup shortcut**, and adds guidance for word processors and PDF readers that require highlighting.
- Lookup behavior and dictionary data remain unchanged from 1.0.4.
- A `dictionary.db` that fails schema or dataset validation is closed before the error is reported, so Windows can replace or delete the file afterwards. This also lets the two schema-rejection tests pass on Windows.

## Version 1.0.4

- The complete revised User Guide appears in the main pane on startup. It wraps and scrolls, follows the selected text size, and includes lookup shortcut instructions.
- **User Guide** replaces **About** in both menus. Opening it retains the current lookup and history; **Back** or **Alt+Left** returns to that lookup. The tray menu also restores the window when opening the guide.
- About information and the three license-notice links are at the bottom of the guide.
- Notice windows open at the beginning with no text selected and keyboard focus on **Close**. Text can still be selected and copied deliberately.
- The Princeton notice viewer omits the sequential line-number column. The bundled license files and their wording are preserved unchanged.
- The 1.0.3 capture behavior, session window positioning, shortcut-dialog wrapping, dictionary data, and dependency versions are retained.

## Version 1.0.3

- Restores version 1.0.1's lookup behavior and session window positioning. The cursor-capture changes introduced in 1.0.2 have been removed.
- Retains the 1.0.2 **Menu > Lookup shortcut** dialog fix: instructions and warning messages wrap within the window, and the dialog can grow vertically to fit them.
- Documents the applications that require a highlighted word before using the lookup shortcut.

## Version 1.0.1 fixes retained

- The original centered 720 × 580 default applies only when the program starts. Lookups and tray restores preserve the position, size, and maximized state chosen during the session. A full Exit followed by a new launch returns to the default.
- Clipboard capture reads Unicode text while holding the native clipboard lock. Delayed rendering may legitimately advance the clipboard sequence while the owner supplies text; the rendered sequence is now used for validation and restoration. A newer copy before the lock or a different owner is rejected.
- Added read-only Word document-range capture through the focused document window's native object model, and MSAA/IAccessible2 selection/caret capture for document applications such as LibreOffice. Slow native providers cannot block the separate Copy fallback. No document or selection editing commands are issued.
- Custom word processor controls use Ctrl+C after the shortcut is released, with more time for clipboard publication. Direct WM_COPY is reserved for standard Edit/RichEdit controls.
- Version 1.0.1 included 71 .NET cases, including 11 clipboard regression cases.

Version 1.0.1 retains the first-lookup initialization retry from 1.0 and the earlier selection/caret, display, and navigation improvements.

## Run the portable release

1. Extract the entire `Portable-Dictionary-1.0.5-Windows-x64.zip` archive to a writable local folder or USB drive.
2. Open the extracted `Dictionary-1.0.5` folder and run `Dictionary.exe`.
3. No separate .NET installation, first-run download, database conversion, account, or network connection is required.

If an earlier version is still running, choose **Exit** from its tray menu before launching 1.0.5. All releases intentionally use the same single-instance and global-shortcut identity.

Keep `data/dictionary.db`, `Assets`, and `licenses` beside the executable. Settings are written to `settings.json` beside `Dictionary.exe`; no registry or AppData settings are used. If that file cannot be written, the app continues with in-memory settings and shows a brief nonblocking warning.

## Use

- **Ctrl+Alt+D:** capture a usable single-line selection, or the word at the text insertion cursor where supported, restore Dictionary, and look it up. When neither is readable, the existing query is selected for replacement without an error message.
- **Enter:** look up the word field. There is intentionally no search-as-you-type, autocomplete, spellcheck, online lookup, or AI feature.
- **Click a synonym:** look up that synonym in its own meaning context.
- **Back / Alt+Left:** return to the preceding lookup without adding another history entry. When viewing the guide, return to the current lookup first.
- **Ctrl+L:** focus the word field and select its contents.
- **Escape or X:** hide to the tray; the global shortcut remains active.
- **Tray icon:** restore the window. The tray and main menus provide text sizing, shortcut reassignment, User Guide, and genuine Exit.
- **Menu > User Guide:** reopen the complete instructions. About information and the OEWN, CC BY 4.0, and Princeton WordNet notice links are at the bottom.
- **Text size:** 9–20 point system UI font; the layout reflows with window size and Windows display scaling.

Selections are limited to 160 characters and one line. Surrounding punctuation is handled conservatively; internal apostrophes, hyphens, and periods are preserved for exact lookup before alternate variants are considered.

### Applications that require highlighting

In some applications, **the word must be highlighted (selected) before using the keyboard lookup shortcut**. This includes **LibreOffice Writer**, **TextMaker from SoftMaker Office**, and **ONLYOFFICE**. Highlight the word, then press **Ctrl+Alt+D** or your configured shortcut. Placing the text cursor inside the word is not sufficient in these applications; Dictionary may open without changing the previous lookup.

## Dictionary data

The bundled prepared database uses **Open English WordNet 2025 Edition**, dataset identifier **`oewn:2025`**, released 2025-12-31. Structured WN-LMF 1.3 XML supplies lexical entries, senses, synsets, definitions, and examples. The companion Princeton-compatible WNDB archive supplies `noun.exc`, `verb.exc`, `adj.exc`, and `adv.exc` morphology mappings.

| Upstream archive | Source URL | SHA-256 |
|---|---|---|
| `english-wordnet-2025.xml.gz` | https://en-word.net/static/english-wordnet-2025.xml.gz | `9ca6d1dcb75f822fdd66617f7d9da48142ace38dd544d6ad5e2feca1674ad3fe` |
| `english-wordnet-2025.zip` | https://en-word.net/static/english-wordnet-2025.zip | `38b16326159f51853626b7d24a44c453fa88ab33f06fce5ec8fc5996d1c2be93` |

The import runs only while preparing a release. The bundled database contains **135,969 lexical entries, 185,129 senses, 107,519 synsets, and 49,596 source examples**.

### Prepared-data contract and ordering

- Prepared schema: **1**, stored both in `PRAGMA user_version` and `metadata.schema_version`.
- Dataset identity: **`oewn:2025`**, also validated on startup. Unsupported or corrupt data produces a clear error instead of partial results.
- Validated base-form headwords appear before exact-spelling headwords. Thus `occasions` shows `occasion` then `occasions`; `better` shows `good`, `well`, then `better`.
- Roots are discovered in noun, verb, adjective, adverb order; exception mappings precede regular suffix rules, with source order retained. Every candidate must exist for that part of speech. This protects legitimate words such as `news`.
- Exact and base records are deduplicated by resolved headword; definitions remain distinct by sense and source definition. Distinct headword sections are never merged merely because their text matches.

## Build and reproduce

Build prerequisites are Python 3 and .NET SDK **10.0.402** (or a later compatible 10.0 SDK). They are not runtime prerequisites.

From the source root:

```powershell
# Windows: use the prepared database bundled in the source ZIP
py -3 tools/build.py

# Also re-download, hash-verify, and regenerate the database from upstream
py -3 tools/build.py --reimport
```

```bash
# Linux cross-build; EnableWindowsTargeting is already set in the project
python3 tools/build.py
```

The script runs all tests, publishes a self-contained `win-x64` Release build, and creates both ZIPs under `release/`. NuGet access is needed to restore build dependencies unless they are already cached. See `docs/BUILD.md` for individual commands and reproducibility details.

## Capture limitations

- UI Automation is preferred. Word native ranges and MSAA/IAccessible2 text are attempted when necessary; standard edit and rich-edit controls then receive a direct `WM_COPY` message, and other applications use the guarded Ctrl+C fallback. Copy behavior can still vary by application, custom control, focus change, or Windows integrity-level boundary. Capture into an elevated source application may fail when Dictionary is not elevated.
- The first shortcut invocation in a process has an eight-second total initialization budget when its normal capture paths fail. This accommodates an accessibility provider that is starting for the first time. Later failed lookups use the normal short timeouts. A source-window change stops further capture commands and retries.
- Word-at-cursor capture requires a supported text/caret API (UI Automation, the Word object model, or IAccessible2). In LibreOffice Writer, TextMaker from SoftMaker Office, ONLYOFFICE, and other controls without a readable caret, highlight the word before pressing the lookup shortcut. In an ordinary browser page without a text insertion cursor, highlight the word first (or enable the browser's caret browsing). Hovering the mouse pointer alone does not establish a text insertion point. Controls that expose neither a caret nor a usable selection still require manual entry.
- Clipboard sequence numbers prevent unchanged clipboard text from being mistaken for a new selection. Prior clipboard content is restored only if no newer clipboard change is detected. Snapshotting and restoration are best-effort for delayed-rendered or application-specific clipboard formats.
- An application that copies the current line without a selection may still supply text; multiline results are rejected, but source-application behavior can limit accuracy.
- A failed shortcut registration leaves tray and manual lookup available. Use **Lookup shortcut…** to choose another combination.

## Verification status

For this revision, **82 .NET tests** pass, including guide navigation/history, current-shortcut expansion, notice links, conservative notice formatting, delayed clipboard rendering, newer-copy rejection, first-capture retries, focus changes, bounded waits, and older-settings migration; the Windows application compiles and publishes for x64. The Python importer/data suite has **3 passes and 1 skip** because upstream archives are not included in the source ZIP. The Windows executable was **not run in this Linux environment**, so guide layout, initial notice focus, real-editor/browser capture, and other Windows GUI behaviors remain manual verification items. See `docs/VERIFICATION.md` for the current record and checklist.

## Repository

- `src/` holds the Windows Forms application and the core lookup library, `tests/` the xUnit suite, `tools/` the data import, build and release-notes scripts, and `docs/` the build, verification and user-guide documents.
- Every push is tested, published and packaged on a Windows runner by the [Build workflow](.github/workflows/build.yml); the archives from each run are kept for 30 days as a workflow artifact.
- A release is cut by pushing a version tag that matches `APP_VERSION` in `tools/build.py`, for example `v1.0.5`, or by running the Build workflow manually on `main` with **Publish a release** enabled. The workflow then publishes the portable archive, the source archive and their SHA-256 checksums on the [Releases](https://github.com/Crotalus65/portable-dictionary/releases) page, with notes drawn from this README's section for that version.

## Licenses and attribution

Application source is MIT-licensed; see `LICENSE`. OEWN is CC BY 4.0 and incorporates Princeton WordNet under the WordNet License. These datasets are not described as public domain. The About section at the bottom of the User Guide and the `licenses/` folder provide the full OEWN, Creative Commons, Princeton, runtime, and library notices. See `THIRD_PARTY_NOTICES.md`.
