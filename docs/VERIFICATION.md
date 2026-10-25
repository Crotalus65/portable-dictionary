# Verification record and Windows checklist

## Completed in the build environment

- .NET solution Release build completed with zero warnings and zero errors.
- Self-contained `win-x64` publication completed; the output contains `Dictionary.exe` and the .NET/Windows Desktop runtime files.
- 30 xUnit checks passed for exact and inflected forms, ambiguous roots, deterministic ordering, source examples, meaning-specific synonyms, punctuation, multiword formatting, history, portable settings, and schema/dataset rejection.
- 4 Python checks passed for a synthetic import fixture, real database metadata/counts, structured example/synset mapping, and pinned upstream SHA-256 hashes.
- The importer completed SQLite `integrity_check` and `foreign_key_check` before atomically installing the prepared database.
- Automated checks explicitly cover `occasions`, `running`, `ran`, `mice`, `went`, `news`, `axes`, and `better`.

These checks ran on Linux. They validate the cross-platform core and the generated Windows build, not actual Windows GUI behavior.

## Not executed in this environment

The following require a real Windows 10 or 11 x64 machine. Do not treat them as already tested:

- Selected-text capture in Notepad and the intended everyday editor, including UI Automation fallback behavior and clipboard preservation.
- No-selection, unreadable-selection, oversized-selection, focus-change, and elevated-source-application cases.
- Shortcut conflict with WordWeb or another registered global hotkey; reassignment and subsequent registration.
- Tray restore, close-to-tray, genuine Exit, second-instance restore, and shortcut operation while hidden.
- Back and keyboard navigation in the actual window; clickable synonym hit regions at all supported text sizes.
- Window layout at 100%, 125%, 150%, and 200% scaling and across multiple monitors.
- Unwritable settings behavior on an ACL-restricted folder, Windows shutdown, and moved-folder/offline operation without a separate .NET installation.

## Manual verification procedure

Use a Windows 10 or 11 x64 test account with networking disabled for the offline portion.

1. Exit any running earlier copy first. Extract the 0.3 portable ZIP to a writable folder. Confirm that `Dictionary.exe`, `data/dictionary.db`, `Assets`, and `licenses` are present. Run without installing .NET and confirm **0.3** in the title bar and About dialog.
2. Look up `feign` and confirm the word appears only once above its definitions, without a separate overall query heading. Then look up `occasions`, `better`, `axes`, `running`, `ran`, `mice`, `went`, and `news`. Confirm base-before-exact ordering, all ambiguous roots, faithful examples, per-meaning synonyms, omission of empty synonym lines, and plain headword headings without “Exact-spelling headword” or “Base-form headword” prefixes.
3. Try `social_gathering`, `mother-in-law's`, `a.m.`, and a word surrounded by quotes and a comma. Confirm readable text and conservative punctuation behavior.
4. Open a result with many meanings, scroll well down the page, and click a synonym. Confirm the new result starts at its first line with no blank region above it. Repeat at several scroll offsets. Then use Back and Alt+Left, verify that Back does not create another entry, and test Ctrl+L and no-entry-found recovery.
5. Select `feign` in Notepad and press Ctrl+Alt+D. Confirm Dictionary opens and immediately looks up **feign**, rather than merely highlighting a blank or previous query. Repeat after restarting Dictionary, after an earlier lookup, and in the intended everyday editor. Test both quick and slow release of Ctrl, Alt, and D. Confirm capture occurs before Dictionary takes focus and that the source document is unchanged.
6. Without selecting anything, rest the mouse pointer on a word in Notepad and press the shortcut; confirm that word is looked up. Move the pointer off the text (title bar, blank area below the last line, another application) and click into the middle of a word so only the text cursor marks it; confirm the word at the text cursor is looked up, including with the cursor placed just after the last letter at the end of a line. Repeat the pointer test in Chrome or Edge on an ordinary web page, in Microsoft Word, and with a word followed by a comma or enclosed in quotes (the punctuation should be dropped). Press the shortcut once with the pointer on a word of a background window while another application is active, and confirm nothing from the background window is used. Press it while Dictionary itself is active and confirm the word field is simply focused.
7. Repeat with no selection and the pointer away from text, a multiline selection, text longer than 160 characters, and an editor that does not expose its text through UI Automation. Confirm graceful fallback and clipboard preservation where supported: copy a line of text, an image, and a file in Explorer before the shortcut, and verify each is still on the clipboard afterwards. No selection or an unreadable selection should open Dictionary without the former “selection could not be read” error; an oversized selection may still show its brief limit warning. In a Windows console or Windows Terminal running a long command, press the shortcut with no selection and confirm the command is not interrupted.
8. Register the same shortcut in another program, restart Dictionary, and confirm the conflict warning. Choose another combination through Lookup shortcut and verify that tray/manual lookup still work when registration fails.
9. Hide with Escape and X; use the shortcut and tray icon to restore. Launch a second copy and confirm restoration of the first. Confirm Exit actually terminates and removes the tray icon.
10. With results displayed, test Smaller, Larger, and Reset repeatedly; no font exception should occur. Confirm the full **Menu** label remains visible. Open Lookup shortcut and About at 100%, 125%, 150%, and 200% scaling and confirm that OK, Cancel, and Close are fully visible. About should open with no highlighted text and with focus on Close. Test resizing and multiple monitors, then move the saved position off-screen or disconnect a monitor and verify restored placement is visible.
11. Deny write permission to the portable folder in a controlled test location. Verify continued operation, in-memory preferences, and the brief warning; no settings should appear in AppData or the registry. Restore permissions afterward.
12. Move the complete folder to another writable path or USB drive, disable networking, and repeat representative lookups. Confirm no first-run import or download.
13. For schema rejection, back up `data/dictionary.db`, run the following against a copy with Python, replace the bundled file with that copy, and launch. A clear schema error should appear. Restore the good database afterward.

```python
import sqlite3
connection = sqlite3.connect("dictionary-copy.db")
connection.execute("PRAGMA user_version = 99")
connection.commit()
connection.close()
```

Record Windows version, editor names, results, and any application-specific capture limitations before considering the Windows checklist complete.
