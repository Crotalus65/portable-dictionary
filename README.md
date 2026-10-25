# Portable Dictionary and Thesaurus 0.3

A compact, offline Windows 10/11 x64 dictionary and thesaurus built with C#, Windows Forms, and .NET 10. It combines Open English WordNet definitions, source examples, meaning-specific synonyms, WordNet-style morphology, a global selected-text shortcut, tray operation, and portable settings.

## Changes since the 0.3 archive

- Fixed Ctrl+Alt+D opening Dictionary without looking anything up. The shortcut previously acted only on a highlighted selection; with the cursor merely on a word, every capture step found nothing and the window opened on the previous query. It now looks up, in order, the highlighted selection, the word under the mouse pointer when the pointer rests on text of the active window, and the word at the text cursor of the focused control.
- Browsers build their accessibility tree only when first asked, so a capture that finds no text at all is retried briefly instead of giving up.
- Pressing the shortcut while Dictionary itself is active no longer re-looks-up the word already in its own word field; it just focuses the field.
- The guarded Ctrl+C fallback is skipped when UI Automation has already shown that the focused control exposes its text and has no selection. In a console window, for example, a simulated Ctrl+C would interrupt the running program rather than copy anything.
- Clipboard restoration after the Copy fallback now works: the previous code kept a live reference to the clipboard, which already described the captured text by the time it was "restored". Common formats (text, RTF, HTML, file lists, bitmaps) are copied by value and put back when no newer clipboard change is detected.

## Version 0.3 fixes

- Strengthened Ctrl+Alt+D selected-text lookup: after UI Automation, standard edit and rich-edit controls receive a direct Copy message before the guarded Ctrl+C fallback is attempted.
- Removed the duplicate overall heading from successful results. The submitted query remains in the input field and each resolved headword appears once above its definitions.
- About now opens without highlighting its text; focus starts on Close instead.

Version 0.3 retains the 0.2 fixes for headword prefixes, synonym scrolling, menu and dialog sizing, text-size changes, and shortcut warnings. The portable and source archives, extracted folders, window title, welcome screen, and About dialog all carry the **0.3** identity.

## Run the portable release

1. Extract the entire `Portable-Dictionary-0.3-Windows-x64.zip` archive to a writable local folder or USB drive.
2. Open the extracted `Dictionary-0.3` folder and run `Dictionary.exe`.
3. No separate .NET installation, first-run download, database conversion, account, or network connection is required.

If an earlier version is still running, choose **Exit** from its tray menu before launching 0.3. All releases intentionally use the same single-instance and global-shortcut identity.

Keep `data/dictionary.db`, `Assets`, and `licenses` beside the executable. Settings are written to `settings.json` beside `Dictionary.exe`; no registry or AppData settings are used. If that file cannot be written, the app continues with in-memory settings and shows a brief nonblocking warning.

## Use

- **Ctrl+Alt+D:** look up a word from the active application and restore Dictionary. A highlighted single-line selection is used first; otherwise the word under the mouse pointer, when the pointer rests on text of the active window; otherwise the word at the text cursor of the focused control. With nothing readable, the existing query is selected for replacement without an error message.
- **Enter:** look up the word field. There is intentionally no search-as-you-type, autocomplete, spellcheck, online lookup, or AI feature.
- **Click a synonym:** look up that synonym in its own meaning context.
- **Back / Alt+Left:** return to the preceding lookup without adding another history entry.
- **Ctrl+L:** focus the word field and select its contents.
- **Escape or X:** hide to the tray; the global shortcut remains active.
- **Tray icon:** restore the window. The tray and main menus provide text sizing, shortcut reassignment, About, and genuine Exit.
- **Text size:** 9–20 point system UI font; the layout reflows with window size and Windows display scaling.

Selections are limited to 160 characters and one line. Surrounding punctuation is handled conservatively; internal apostrophes, hyphens, and periods are preserved for exact lookup before alternate variants are considered.

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

- UI Automation is preferred, and it is the only way the word under the pointer or at the text cursor can be read: the application must expose its text through the UI Automation Text pattern. Notepad, WordPad, rich-edit and standard edit controls, Microsoft Word, Chromium browsers (Chrome, Edge) and the Windows consoles do; some PDF readers, older or custom-drawn editors, and games do not. Those applications still work with a highlighted selection: standard edit and rich-edit controls receive a direct `WM_COPY` message, and other applications use the guarded Ctrl+C fallback. Copy behavior can still vary by application, custom control, focus change, or Windows integrity-level boundary. Capture into an elevated source application may fail when Dictionary is not elevated.
- The pointer is only consulted while it rests on text of the active window (or another window of the same application). Pointing at text in a background window does nothing; click that window first.
- Browsers enable their accessibility tree when a UI Automation client first asks for it. The very first shortcut after a browser starts can therefore be slower or, rarely, find nothing; the next press works.
- Clipboard sequence numbers prevent unchanged clipboard text from being mistaken for a new selection. Prior clipboard content is restored only if no newer clipboard change is detected. Snapshotting and restoration are best-effort for delayed-rendered or application-specific clipboard formats.
- An application that copies the current line without a selection may still supply text; multiline results are rejected, but source-application behavior can limit accuracy.
- A failed shortcut registration leaves tray and manual lookup available. Use **Lookup shortcut…** to choose another combination.

## Verification status

Completed on Linux: clean solution build, self-contained Windows x64 publication, **30 .NET tests**, **4 Python importer/data tests**, source-hash verification, and importer integrity/foreign-key checks. The Windows executable was **not run in this Linux environment**, so GUI, tray, global shortcut, real-editor capture, display scaling, shutdown, and other Windows-specific behaviors remain manual verification items. The exact checklist is in `docs/VERIFICATION.md`.

## Licenses and attribution

Application source is MIT-licensed; see `LICENSE`. OEWN is CC BY 4.0 and incorporates Princeton WordNet under the WordNet License. These datasets are not described as public domain. The About dialog and `licenses/` folder provide the full OEWN, Creative Commons, Princeton, runtime, and library notices. See `THIRD_PARTY_NOTICES.md`.
