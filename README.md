<p align="center">
  <img src="src/Ascolotl.App/Assets/app-icon.png" alt="Ascolotl" width="96">
</p>

<h1 align="center">Ascolotl</h1>

<p align="center">A focused AsciiDoc and Markdown editor with live preview and PDF export.<br>
Windows · macOS · Linux</p>

---

Ascolotl keeps the screen for your document. Write AsciiDoc or Markdown on the
left and see it rendered on the right, or edit the rendered page directly. Then
export to PDF, HTML, EPUB or DocBook. Everything runs locally: no browser engine,
no Ruby and no Java.

## Features

- **AsciiDoc and Markdown:** open, edit, preview and export `.adoc` and `.md`
  files. Markdown follows CommonMark, with GitHub's tables, task lists,
  strikethrough, autolinks and footnotes. Syntax colouring follows each file's
  format, and the toolbar and menus write its syntax.
- **Three ways to work:** Source, Split (source and live preview side by side)
  and WYSIWYG. Click a rendered block (double-click in Split) to edit its source
  in place, then apply with `Ctrl+Enter` or cancel with `Esc`. The source text
  stays the single source of truth.
- **Formatting toolbar and right-click menu** in Split and WYSIWYG: bold,
  italic, code, links, headings, lists, quotes, code blocks, images, tables and,
  in AsciiDoc, notes. Each applies to the selection or inserts at the caret.
- **Table editing:** insert and delete rows and columns from the toolbar or a
  right-click, in the source, in the preview or while editing a cell. AsciiDoc
  cells can also be merged and split. Markdown tables stay aligned.
- **Live preview** with images, checklists, quotes and highlighted text. Click
  the preview to jump to the source, and scrolling stays in sync.
- **Outline panel** to jump between sections, and **project folders** with a
  file tree.
- **Export** to PDF (portrait or landscape for wide tables), HTML, EPUB,
  DocBook XML and a linked static website of a whole folder. **Export
  profiles** save one-click export settings. PDFs can be made **accessible**
  (tagged PDF/UA-1, for screen readers) with Export → Accessible PDF.
- **Styles:** load an asciidoctor-pdf theme (`.yml`) once and it styles every
  PDF, HTML and EPUB export and the live preview, title page included. A
  document's own `:pdf-theme:` still wins.
- **Page Preview** shows the document as the exported PDF's pages: title page,
  table of contents, headers and footers.
- **AsciiDoc help:** completion, snippets and a searchable syntax reference
  (`F1`). Word (`.docx`), Markdown, HTML, PDF and plain text files can be
  imported as AsciiDoc.
- **Command palette** (`Ctrl+Shift+P`) to reach every command by name.
- **Tabs and session restore:** reopen a closed tab and pick up where you left
  off. Unsaved work is recovered after a crash.
- **Light, dark or system theme**, with zoom and word wrap for each pane.
- **English, French, German, Italian or Spanish** interface, following the system language by
  default (View → Language).

## Install

Download the archive for your system from the
[Releases](https://github.com/sonatique/Ascolotl/releases) page. Each build is
self-contained, so you don't need to install .NET.

| System | Archive |
|---|---|
| Windows (x64) | `Ascolotl-<version>-win-x64.zip` |
| Windows (ARM64) | `Ascolotl-<version>-win-arm64.zip` |
| macOS, Apple Silicon | `Ascolotl-<version>-osx-arm64.tar.gz` |
| macOS, Intel | `Ascolotl-<version>-osx-x64.tar.gz` |
| Linux (x64) | `Ascolotl-<version>-linux-x64.tar.gz` |
| Linux (ARM64) | `Ascolotl-<version>-linux-arm64.tar.gz` |

Each archive holds a single executable.

**Windows:** unzip the archive and run `Ascolotl.exe`. The build is not signed,
so SmartScreen may warn you the first time. Choose *More info*, then *Run anyway*.

**macOS:** the build is not signed or notarized. Unpack it, clear the quarantine
flag, then start the app:

```bash
tar -xzf Ascolotl-0.0.1-osx-arm64.tar.gz
```

```bash
xattr -d com.apple.quarantine Ascolotl
```

```bash
./Ascolotl
```

**Linux:** needs X11, fontconfig and ICU (`libicu`), which standard desktop
distributions include. Unpack the archive and run `./Ascolotl`:

```bash
tar -xzf Ascolotl-0.0.1-linux-x64.tar.gz && ./Ascolotl
```

## Keyboard shortcuts

| Action | Shortcut |
|---|---|
| New / Open / Open folder | `Ctrl+N` / `Ctrl+O` / `Ctrl+Shift+O` |
| Save / Save As / Save All | `Ctrl+S` / `Ctrl+Shift+S` / `Ctrl+Alt+S` |
| Close tab / Reopen closed tab | `Ctrl+W` / `Ctrl+Shift+T` |
| Next / previous tab | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| Export to PDF | `Ctrl+E` |
| Command palette | `Ctrl+Shift+P` |
| Apply / cancel an in-place edit | `Ctrl+Enter` / `Esc` |
| Completion (AsciiDoc) | `Ctrl+Space` |
| AsciiDoc syntax reference | `F1` |
| Toggle outline | `Ctrl+B` |
| Zoom in / out / reset | `Ctrl++` / `Ctrl+-` / `Ctrl+0` |

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build Ascolotl.slnx
```

```bash
dotnet test Ascolotl.slnx
```

```bash
dotnet run --project src/Ascolotl.App
```

To make release builds, run `publish.ps1` in PowerShell. It runs the tests first,
then publishes one self-contained, trimmed, compressed executable per runtime to
`publish/<runtime>/` and the release archives to `publish/dist/`:

```powershell
.\publish.ps1                     # win, osx and linux, each x64 and arm64
.\publish.ps1 -Runtime win-x64    # a single runtime
```

## Project layout

| Path | Contents |
|---|---|
| `src/Ascolotl.App` | Avalonia desktop app: windows, views, dialogs and file pickers |
| `src/Ascolotl.Core` | View models, editing logic (formatting, tables, completion), document model and the AsciiDoc and Markdown parsing |
| `src/Ascolotl.Infrastructure` | Settings, session, crash-recovery storage and the log |
| `src/Ascolotl.Export` | PDF, HTML, EPUB, DocBook and website export; Word, Markdown, HTML and PDF import |
| `tests/Ascolotl.Tests.Unit` | Unit tests |
| `example-documents/` | Sample AsciiDoc files for manual testing |

Ascolotl is built on [Avalonia](https://avaloniaui.net/),
[AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) and
[AdocNet](https://github.com/sonatique/Adoc.Net), a managed AsciiDoc (and
Markdown) parser and renderer.

## License

Copyright (C) 2026 sylvain@sonatique.net

Ascolotl is free software, licensed under the
[GNU General Public License v3.0](LICENSE).
