# MD Redactor

English | [Russian](README_RU.md)

MD Redactor is a Windows Markdown editor for writers, editors, and anyone who prepares text for review by an AI agent. It shows Markdown as a formatted document, lets you select a fragment, attach a comment to it, and saves the comment back into the same `.md` file.

The file stays portable. There is no database, sidecar JSON, or separate review store. You can send the Markdown file to another person or an AI agent and the edits travel with the text.

## What it solves

- You can mark exact fragments in prose without copying them into a separate task list.
- Comments stay next to the text they describe.
- The editor hides service tags while you work, so Markdown looks like a document, not raw source.
- Existing edit ids are never renumbered. Links between comments and fragments stay stable.
- Russian text and legacy Windows-1251 Markdown files are handled safely.

## Main features

- Native Windows 10+ desktop app built with WPF, .NET 10, and WebView2.
- WYSIWYG Markdown editing powered by TypeScript, Vite, and ProseMirror.
- Light, dark, and system themes.
- Russian and English interface languages, with system language by default.
- Open `.md` files from the app, with `Ctrl+O`, drag-and-drop, or Windows "Open with...".
- Create an edit by selecting text and pressing `Enter`.
- Right review panel with edit cards, comments, navigation, and deletion.
- Safe save pipeline: edit tag validation, atomic write, and a `.bak` backup before the first save in a session.
- UTF-8, UTF-8 BOM, UTF-16 LE/BE BOM, and Windows-1251 fallback.

## Install the app

Download or build the installer:

```text
artifacts\installer\MDRedactorSetup-x64.exe
```

Run the installer. It installs MD Redactor for the current Windows user without administrator rights:

```text
%LOCALAPPDATA%\Programs\MD Redactor
```

The installer adds shortcuts to the desktop and Start menu. It also registers MD Redactor for Windows "Open with..." on `.md` files, but it does not make MD Redactor the default Markdown editor automatically.

The release build includes the .NET runtime for Windows x64. WebView2 Runtime must be installed on the target machine.

## Basic workflow

1. Open a Markdown file.
2. Select a fragment in the formatted document.
3. Press `Enter`.
4. Write a comment in the card on the right.
5. Save with `Ctrl+S` or the Save button.
6. Send the same Markdown file to an AI agent or another reviewer.

If a selection overlaps an existing edit, MD Redactor does not create a nested edit. It activates the existing edit and shows a notification.

## Edit tag format

Block edit:

```markdown
<!-- ed-start id="1" -->
fragment
<!-- ed-comm id="1"
comment
-->
<!-- ed-end id="1" -->
```

Inline edit:

```markdown
Text before <!-- ed-start id="2" -->fragment<!-- ed-comm id="2"
comment
--><!-- ed-end id="2" --> text after.
```

All three markers of one edit use the same positive `id`. Ids do not need to be continuous. If edit `#2` is deleted from `#1`, `#2`, `#5`, the remaining ids are still `#1` and `#5`. A new edit receives `max(existing id) + 1`.

Edits have no status. Attributes such as `status`, `resolved`, or `open` are not part of the format.

## Saving and backups

Before writing a file, MD Redactor validates edit tags. It blocks saving when it finds duplicate ids, mismatched ids, unclosed markers, unsupported `status` attributes, or unsafe HTML comment sequences.

Saving is atomic:

1. A temporary file is written next to the Markdown file.
2. A backup `filename.md.bak` is created before the first save in the current session.
3. The original file is replaced by the temporary file.
4. If an error happens, the original file is left untouched.

If the file was changed by another program after opening, MD Redactor asks before overwriting it.

Read and save errors are logged to:

```text
%LOCALAPPDATA%\MDRedactor\logs
```

The log contains error type, context, and file path. It does not store the document text.

## Encodings

Inside the app, text is handled as Unicode. When reading files, MD Redactor detects:

- UTF-8 with BOM;
- UTF-8 without BOM;
- UTF-16 LE/BE by BOM;
- Windows-1251 fallback when the file is not valid UTF-8.

When saving an opened file, MD Redactor keeps the detected encoding where possible. New files and unknown encodings are saved as UTF-8 without BOM.

## Keyboard shortcuts

- `Ctrl+O`: open a Markdown file.
- `Ctrl+S`: save the current file.
- `Enter` with a selection in the editor: create an edit.
- `Enter` in a comment field: insert a line break.
- `Escape` in the editor: clear the active edit.

## Build from source

Requirements:

- Windows 10 or newer;
- .NET 10 SDK;
- Node.js and npm;
- WebView2 Runtime.

Run the full build:

```powershell
.\scripts\build.ps1
```

The script checks the environment, builds `web/editor`, runs web tests, and then restores, builds, and tests the .NET solution.

Run the app from source:

```powershell
dotnet run --project .\src\MDRedactor.App\MDRedactor.App.csproj
```

Before running the app, build the web editor with `.\scripts\build.ps1` or run `npm run build` in `web\editor`.

## Package and installer

Create the release publish folder:

```powershell
.\scripts\package.ps1
```

Output:

```text
artifacts\publish\win-x64
```

Create the Windows installer:

```powershell
.\scripts\installer.ps1
```

Output:

```text
artifacts\installer\MDRedactorSetup-x64.exe
```

The installer is built with Inno Setup. It supports English and Russian.

## Sample file

Use `samples\scene.ru.md` for a quick check. It contains Russian Markdown text, one inline edit `#1`, and one block edit `#3`.

## License

MIT. See [LICENSE](LICENSE).
