# Verification record and Windows checklist

## Completed in the build environment

- .NET application Release build completed for Windows x64 with zero compiler warnings and zero errors.
- Self-contained `win-x64` publication completed; the output contains `Dictionary.exe` and the .NET/Windows Desktop runtime files.
- 82 xUnit checks passed: all 71 earlier cases and 11 guide/notice cases. New checks cover startup without lookup history, returning from the guide to the current lookup, older history preservation, repeated guide opening, new lookups from the guide, the embedded guide's current shortcut/version/dataset, the three notice links, and conservative line-number display formatting using the real bundled notice. The original clipboard/capture/core tests are retained.
- The database, original license files, capture services, results renderer, window placement, and shortcut dialog match 1.0.3 byte for byte. The 1.0.2 nested-caret changes remain absent.
- Python checks: 3 passed (synthetic importer fixture, real database metadata/counts, and example/synset mapping); 1 skipped (upstream archive hashes, because the source ZIP does not include those archives). The synthetic importer fixture passed SQLite integrity and foreign-key checks. This revision does not reimport or modify the bundled database.
- NuGet restore reports an existing advisory for the bundled SQLitePCLRaw.lib.e_sqlite3 2.1.11 dependency (NU1903). This guide revision leaves package versions unchanged.
- Automated checks explicitly cover `occasions`, `running`, `ran`, `mice`, `went`, `news`, `axes`, and `better`.

The restored 1.0.1 Word object-model and MSAA/IAccessible2 adapters were compiled but not executed in real Word, LibreOffice, or TextMaker. User feedback confirmed capture in Chrome, UpNote, Joplin, Typora, and the custom Markdown editor, and that LibreOffice Writer, TextMaker from SoftMaker Office, and ONLYOFFICE require the word to be highlighted before using the shortcut. This requirement is documented in the README. The current build still needs Windows regression checks in those applications.

These checks ran on Linux. They validate the cross-platform core and the generated Windows build, not actual Windows GUI behavior.

The GitHub Actions **Build** workflow repeats the Python checks, the xUnit suite and the self-contained publish on a `windows-latest` runner for every push and for each release tag; the run for a given commit is the Windows record of its automated checks. GUI behavior still requires the manual procedure below.

## Not executed in this environment

The following require a real Windows 10 or 11 x64 machine. Do not treat them as already tested:

- Selected-text capture in Notepad and the intended everyday editor, including the first lookup with a cold accessibility provider, UI Automation fallback behavior, and clipboard preservation.
- No-selection, unreadable-selection, oversized-selection, focus-change, and elevated-source-application cases.
- Shortcut conflict with WordWeb or another registered global hotkey; reassignment and subsequent registration.
- Startup/User Guide layout and scrolling at each supported text size and display scaling; return-to-lookup behavior through both menus and Back/Alt+Left; current shortcut after reassignment; notice keyboard links and initially unselected notice text. These Windows controls were compiled but not executed here.
- Menu > Lookup shortcut instructions and warning messages wrapping within the dialog, with buttons visible at supported display scaling. The retained 1.0.2 layout was compiled but not visually inspected here.
- Tray restore, close-to-tray, genuine Exit, second-instance restore, and shortcut operation while hidden.
- Back and keyboard navigation in the actual window; clickable synonym hit regions at all supported text sizes.
- Default geometry on a fresh process launch and session geometry retained across shortcut/tray restores, and window layout at 100%, 125%, 150%, and 200% scaling and across multiple monitors.
- Unwritable settings behavior on an ACL-restricted folder, Windows shutdown, and moved-folder/offline operation without a separate .NET installation.

## Manual verification procedure

Use a Windows 10 or 11 x64 test account with networking disabled for the offline portion.

1. Exit any running earlier copy first. Extract the 1.0.5 portable ZIP to a writable folder. Confirm that `Dictionary.exe`, `data/dictionary.db`, `Assets`, and `licenses` are present. Run without installing .NET and confirm **1.0.5** in the title bar and the About Dictionary section at the bottom of the startup guide.
2. Look up `feign` and confirm the word appears only once above its definitions, without a separate overall query heading. Then look up `occasions`, `better`, `axes`, `running`, `ran`, `mice`, `went`, and `news`. Confirm base-before-exact ordering, all ambiguous roots, faithful examples, per-meaning synonyms, omission of empty synonym lines, and plain headword headings without “Exact-spelling headword” or “Base-form headword” prefixes.
3. Try `social_gathering`, `mother-in-law's`, `a.m.`, and a word surrounded by quotes and a comma. Confirm readable text and conservative punctuation behavior.
4. Open a result with many meanings, scroll well down the page, and click a synonym. Confirm the new result starts at its first line with no blank region above it. Repeat at several scroll offsets. Then use Back and Alt+Left, verify that Back does not create another entry, and test Ctrl+L and no-entry-found recovery.
5. After a fresh Windows login or with a newly started source application whose accessibility provider has not been used, start Dictionary, return to the source editor/browser, select `feign`, and press Ctrl+Alt+D once. Confirm this very first shortcut looks up **feign** without needing a second press; a cold provider may use the initialization grace period before Dictionary takes focus. Restarting Dictionary alone may not reproduce cold provider initialization. Repeat after restarting Dictionary, after an earlier lookup, and in the intended everyday editor and a browser. Test both quick and slow release of Ctrl, Alt, and D, including releasing the modifiers before D. Confirm capture occurs before Dictionary takes focus and that the source document is unchanged. Repeat with a reassigned shortcut.
6. In Notepad and the everyday editor, remove the selection and place the text insertion cursor inside `running`. Press Ctrl+Alt+D after previously looking up `feign`; confirm the query changes to `running` and the first base form is `run`. Check that the original cursor position and selection have not moved. Repeat in a browser text field or contenteditable area; for ordinary page text, explicitly select the word or enable caret browsing. Also test blank controls, whitespace/punctuation, a multiline selection, text longer than 160 characters, a source-window change during capture, and an editor that does not expose selection through UI Automation. Confirm graceful fallback and clipboard preservation where supported. No readable selection or caret should open Dictionary for manual replacement; an oversized selection may still show its brief limit warning.
7. Register the same shortcut in another program, restart Dictionary, and confirm the conflict warning. Choose another combination through Lookup shortcut and verify that tray/manual lookup still work when registration fails. In Menu > Lookup shortcut, check the initial instructions, an invalid combination, and a registration-conflict warning at 100%, 125%, 150%, and 200% scaling. Text must wrap within the right margin and the buttons must remain visible.
8. Hide with Escape and X; use the shortcut and tray icon to restore. Launch a second copy and confirm restoration of the first. Confirm Exit actually terminates and removes the tray icon.
9. With results displayed, test Smaller, Larger, and Reset repeatedly; no font exception should occur. Confirm the full Menu label remains visible and dialogs fit at 100%, 125%, 150%, and 200% scaling. Move/resize/maximize Dictionary; perform repeated lookups from another program and hide/reopen through the tray. It must keep the chosen session geometry. Fully Exit through the tray menu and relaunch: it must use the original centered 720 × 580 default. Older settings containing window coordinates must not override this startup default; text size and shortcut preferences should persist. Test smaller working areas and multiple monitors.
10. Deny write permission to the portable folder in a controlled test location. Verify continued operation, in-memory preferences, and the brief warning; no settings should appear in AppData or the registry. Restore permissions afterward.
11. Move the complete folder to another writable path or USB drive, disable networking, and repeat representative lookups. Confirm no first-run import or download.
12. For schema rejection, back up `data/dictionary.db`, run the following against a copy with Python, replace the bundled file with that copy, and launch. A clear schema error should appear. Restore the good database afterward.

```python
import sqlite3
connection = sqlite3.connect("dictionary-copy.db")
connection.execute("PRAGMA user_version = 99")
connection.commit()
connection.close()
```

In addition, test TextMaker from SoftMaker Office, ONLYOFFICE, Microsoft Word, and LibreOffice Writer individually. Highlight `feign`, press the shortcut, and confirm a new lookup. LibreOffice Writer, TextMaker, and ONLYOFFICE require the word to be highlighted; cursor-only lookup is not promised for those applications. In Microsoft Word, also test the insertion cursor inside `running` without highlighting; native caret support depends on the application's exposed interface. Try a new, saved, and read-only document; Word table cells; multiple Word windows/documents (the focused one must be read); and clipboard content copied from both plain text and rich documents. Confirm no text edits or selection movement. Copy unrelated text while capture is pending and ensure it is not looked up or overwritten. Compare clipboard text and rich formats before and after fallback. Test the native provider timeout and rerun the working-app regression set.

Record Windows version, editor names, results, and any application-specific capture limitations before considering the Windows checklist complete.

## Version 1.0.5 guide and notice checks on Windows

1. Fully Exit, then launch. The entire revised guide must appear in the main pane, beginning at its title. Back is disabled before any lookup. Verify wrapping, vertical scrolling, and all sections through the About information and three notice links at 9, 10, and 20 point text, and 100%, 125%, 150%, and 200% display scaling. Resize the window while reading; text must remain inside the right margin.
2. Look up `feign`, then `running`; open Menu > User Guide. Back returns to `running`, then to `feign`. Repeat using Alt+Left and repeated guide openings. From the guide, enter another query and verify ordinary lookup history. The guide must not add a lookup-history entry or change session window geometry.
3. Hide to the tray, choose User Guide from the tray menu, and verify that the main window is restored with the guide. Back returns to the prior lookup.
4. Reassign the shortcut, open the guide, and confirm the configured combination is displayed alongside the Ctrl+Alt+D default. Change it while viewing the guide, then Exit and relaunch; the displayed preference must remain correct. Check any registration-conflict warning.
5. Open each of the three notice links by mouse and by Tab/Enter. Each opens at the beginning with no text highlighted and focus on Close. Text must still be selectable and copyable by deliberate action. Close, Escape, and Enter return to the guide at its reading position.
6. The Princeton notice must show its wording without the sequential line-number column; CC BY section and clause numbering must remain. The original notice files in `licenses/` are unchanged. Move or resize notice windows and verify readable text and accessible scrolling.

These are pending Windows checks, not claims of completed GUI testing. The build environment is Linux.

## Version 1.0.5 guide checks (Windows pending)

1. Start a fresh session: the heading Dictionary User Guide must be visible at the top, without scrolling upward. Repeat with saved larger text sizes and different display scaling.
2. Scroll to the notice links, perform a lookup, and reopen the guide from both window and tray menus. The heading must be visible again.
3. Confirm the revised paragraphs and section order against docs/USER_GUIDE.md. The lookup instructions intentionally show the requested default Ctrl+Alt+D. Confirm custom shortcuts still work and are shown in Menu > Lookup shortcut.
