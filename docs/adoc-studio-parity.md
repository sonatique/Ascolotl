# Adoc Studio parity — status

Tracking Ascolotl against Adoc Studio (the closest commercial analog: native
AsciiDoc studio, Mac/iPad-only, subscription). Our wedge: cross-platform,
no subscription, true block WYSIWYG. This file records what parity work is
done, what's deferred, and why.

## Delivered

| Capability | Adoc Studio | Ascolotl |
|---|---|---|
| Live preview | HTML/PDF as you type | Native Avalonia render per keystroke + block WYSIWYG (beyond parity) |
| Coach / authoring assist | Autocomplete + inline syntax help | Context-aware completion, snippet tab-stops, smart typing, doc-aware anchors/attributes, status-bar hints |
| Syntax reference | Inline explanations | F1 searchable cheat-sheet backed by the completion catalog; Enter inserts |
| Project workspace | Sidebar, multi-doc projects | Open Folder → Files sidebar tab (.adoc tree), persisted across launches |
| Single-source publishing | Export profiles | Named export profiles (format + theme + destination), one-click from Export menu |
| Website export | Full websites from one source | Project → static HTML site: structure mirrored, `.adoc` links rewritten, generated index (own `index.adoc` wins) |
| Unified styling | One stylesheet for HTML + PDF | `ExportTheme` (Asciidoctor / Clean / GitHub) drives HtmlTheme **and** a matching PDF palette per profile |
| Document stats | Word counts | Status-bar prose word count (structural lines excluded), reading time + characters on hover |
| Word import | — (not offered) | File → Import from Word…: .docx → AsciiDoc via AdocNet's importer, images extracted beside the output, fidelity summary in the status bar |

## Deferred (with reasons)

- **PDF live preview** — RESOLVED via upstream Adoc.Net #80 (v1.0.22):
  `PagedAvaloniaRenderer` flows blocks into fixed pages by Avalonia
  measurement. Ascolotl's View → Page Preview renders A4 pages mirroring the
  PDF export geometry (`FromPdfPoints(595, 842)`); `<<<` breaks are honored.
  Print-*like* at block granularity — actual PDF page breaks remain the PDF
  renderer's own (different font metrics by design).
- **Translation management** — enterprise workflow; revisit if/when a
  localization use-case shows up.
- **PDF/UA accessibility** — requires tagged-PDF support in AdocNet's writer;
  track upstream.

## Structural advantages already held over Adoc Studio

- Windows + Linux + macOS (they are Apple-only)
- True in-place block WYSIWYG editing (they have live preview only)
- No subscription; PDF export not paywalled
- Span-aware visual table editing (merge/split/rows/columns)
