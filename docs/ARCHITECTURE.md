# Architecture

## WPF host application

`src/MDRedactor.App` is the native Windows shell built with WPF and .NET 10. The main window contains a compact top bar, theme and language selectors, Open and Save commands, a status line, and WebView2.

The app loads the built web editor from `web/editor/dist/index.html`. WebView2 uses a virtual host mapping for the `dist` folder, so Vite assets are loaded from stable relative paths. If the web editor has not been built, the app shows a localized startup error instead of crashing.

The window keeps the standard Windows frame. Minimize, maximize, close, and resize are handled by the operating system.

Theme preferences are `System`, `Light`, and `Dark`. Language preferences are `System`, `Russian`, and `English`. Both settings are stored in:

```text
%LOCALAPPDATA%\MDRedactor\settings.json
```

This file stores application preferences only. It is not used to store edits. WPF resolves system theme and system language to effective values, then sends them to the web editor through `host.setTheme` and `host.setLanguage`.

Files can be opened with the Open button, `Ctrl+O`, drag-and-drop, command-line arguments, or Windows "Open with...". Startup arguments are treated as pending file paths. The app waits for `editor.ready`, then opens the document through `MarkdownFileService`.

Before saving, WPF asks the web editor for the current Markdown, validates edit tags with `EditTagValidator`, and writes the file only when there are no `Error` diagnostics. Diagnostics have stable codes and Russian fallback messages. The WPF localizer uses those codes to show errors in the selected interface language.

Saving uses `MarkdownFileService.SaveAtomicAsync`:

1. The Markdown is encoded with the original file encoding when possible.
2. A temporary `filename.md.tmp` file is written next to the source file.
3. A `filename.md.bak` backup is created before the first save in the current WPF session.
4. The source file is replaced by the temporary file.
5. The temporary file is removed after success and `LastWriteTimeUtc` is updated.

If temporary write, backup creation, or replacement fails, the original file is left untouched where possible. The app logs read, save, and WebView2 protocol errors to:

```text
%LOCALAPPDATA%\MDRedactor\logs
```

The document text is not logged.

## Core library

`src/MDRedactor.Core` contains Markdown file I/O and edit tag services.

`MarkdownFileService` reads:

- UTF-8 with BOM;
- UTF-8 without BOM;
- UTF-16 LE/BE by BOM;
- Windows-1251 fallback when the file is not valid UTF-8.

When saving an opened document, the service keeps the detected encoding where possible. New documents use UTF-8 without BOM.

`MarkdownDocument` stores Markdown text, file path, encoding name, BOM flag, newline kind, encoding diagnostics, backup state, and the last write time observed on disk.

`EditTagParser`, `EditTagValidator`, and `EditTagSerializer` implement the short edit format.

Block edit:

```markdown
<!-- ed-start id="1" -->
Fragment text.
<!-- ed-comm id="1"
Reviewer comment.
-->
<!-- ed-end id="1" -->
```

Inline edit:

```markdown
Text before <!-- ed-start id="2" -->fragment<!-- ed-comm id="2"
Reviewer comment.
--><!-- ed-end id="2" --> text after.
```

`ed-start` opens an edit, `ed-comm` separates the fragment from the comment, and `ed-end` closes the edit. The comment is stored inside the `ed-comm` HTML comment block, after the first line and before the closing `-->`.

All three markers of one edit must use the same positive `id`. Ids do not need to be continuous. Deleting an edit never renumbers other ids. A new edit receives `max(existing id) + 1`.

Nested, overlapping, and duplicate edits are invalid. Edits have no status. Attributes such as `status`, `resolved`, and `open` are not part of the format.

## WebView2 protocol

WebView2 is the boundary between the native host and the web editor. Messages are passed with `postMessage`.

Messages from web to host:

- `editor.ready`
- `editor.dirtyChanged`
- `editor.saveRequested`
- `editor.error`

Messages from host to web:

- `host.loadDocument`
- `host.requestMarkdown`
- `host.setTheme` with `light` or `dark`
- `host.setLanguage` with `ru` or `en`

## Web editor

`web/editor` is a Vite + TypeScript project built on ProseMirror. It receives Markdown from WPF, parses short edit tags, hides service markup from the main document, and renders the clean Markdown as formatted text.

The editor supports paragraphs, headings, bold and italic text, ordered and unordered lists, blockquotes, hard breaks, and horizontal rules. Markdown markers such as `#`, `**`, and `_` are not shown in the main editing view.

The web-side parser mirrors the Core short tag format for display:

- removes `ed-start`, `ed-comm`, and `ed-end` from visible Markdown;
- removes comments from the main text;
- builds the review panel model;
- maps edit fragments to ProseMirror document positions by visible text;
- produces localized diagnostics for damaged markup or unmapped ranges.

Core remains the strict source of validation before save. The web parser exists to keep the editor responsive and to preserve edit data while the user works.

When saving, the web editor serializes the ProseMirror document to canonical Markdown and inserts short edit tags around mapped ranges. Existing ids are not changed. If an edit cannot be mapped reliably, the original tagged fragment is preserved separately during serialization so its id and comment are not lost.

New edits are created by pressing `Enter` with a non-empty ProseMirror selection. The command rejects nested and overlapping edits. Inline edits are created for selections within one text block. Block edits are created when the selection covers one or more top-level blocks completely. Partial selections across multiple paragraphs are rejected with a localized message.

The review panel has no separate data source. It is built from Markdown edit tags and current ProseMirror ranges. Cards are sorted by document position, not by id. Sorting never renumbers ids.

Deleting an edit removes only `ed-start`, `ed-comm`, `ed-end`, and the comment. The selected text remains in the document. Other ids are not changed.

HTML comments cannot contain `--` inside comment bodies. The web editor replaces `--` with `- -`; `-->` becomes `- ->`. The same normalization is applied before serialization.

The web UI uses CSS variables for light and dark themes. Language switching uses a small TypeScript dictionary and does not affect Markdown content.

## Build, package, and installer

`scripts/build.ps1` checks the environment, builds `web/editor`, runs web tests, then restores, builds, and tests the .NET solution.

`scripts/package.ps1` runs the release pipeline for Windows x64: bootstrap, web build, web tests, .NET tests, self-contained `dotnet publish`, and copying `web/editor/dist` to:

```text
artifacts\publish\win-x64\web\editor\dist
```

Folder publish is used because WebView2 assets must stay next to the executable. The .NET runtime is included in publish output. WebView2 Runtime remains an external system component.

`scripts/installer.ps1` builds the EXE installer on top of publish output:

1. Run `scripts/bootstrap.ps1`.
2. Check Inno Setup compiler `ISCC.exe`.
3. Install `JRSoftware.InnoSetup` through `winget` when needed.
4. Run `scripts/package.ps1`.
5. Compile `installer/MDRedactor.iss`.
6. Check `artifacts/installer/MDRedactorSetup-x64.exe`.

The Inno Setup installer supports English and Russian. It installs per-user without elevation to:

```text
%LOCALAPPDATA%\Programs\MD Redactor
```

It creates desktop and Start menu shortcuts, adds a standard Windows uninstall entry, and registers MD Redactor for `.md` files in HKCU "Open with...".

The installer creates:

```text
Software\Classes\Applications\MDRedactor.App.exe\shell\open\command
```

with:

```text
"{app}\MDRedactor.App.exe" "%1"
```

It also adds `MDRedactor.md` to `.md\OpenWithProgids` and `MDRedactor.App.exe` to `.md\OpenWithList`. It does not write the default value for `.md`, so it does not become the default Markdown editor automatically.

## Sample

`samples/scene.ru.md` is a diagnostic sample. It contains Russian prose, Markdown formatting, one inline edit, and one block edit with sparse ids.

## Storage rule

All edit data must stay inside the Markdown file. A database, sidecar JSON, or any other separate edit store is not allowed.
