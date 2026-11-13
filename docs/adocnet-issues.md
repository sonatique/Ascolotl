# AdocNet Integration Issues

Issues discovered while integrating AdocNet 1.0.0-beta.1 into Ascolotl.

## 1. Preview pane shows nothing (under investigation)

**Symptom:** After switching from asciidoctor.js to AdocNet for HTML rendering, the preview pane in split mode shows no content.

**Diagnosis so far:**
- AdocNet rendering works correctly — produces valid HTML (confirmed via stderr logging)
- `PrepareHtml()` correctly wraps the fragment in a full HTML document with CSS
- `WebView.HtmlContent` is being set with the prepared HTML
- `WebView.IsVisible` becomes `true` after first content set
- The HTML output starts with `<h1>` (fragment mode) and is properly wrapped via `WrapHtml()`

**Possible causes:**
- WebView2 initialization timing changed after removing the hidden render window
- The old hidden render WebView (`_renderWindow`) may have triggered WebView2 process startup that the preview WebView depended on

## 2. PDF renderer embeds literal newlines in text commands (BUG)

**Symptom:** PDF output has paragraph text concatenated without proper wrapping.

**Root cause:** `PdfRenderer` emits `Tj` PDF text commands containing literal `\n` characters from the AsciiDoc source. For example, a paragraph spanning two lines in the source:

```
This is the first paragraph.
It has multiple lines that should wrap properly.
```

Produces the raw PDF command:
```
(This is the first paragraph.\nIt has multiple lines that should wrap properly.) Tj
```

The PDF `Tj` operator does not interpret newlines — they are rendered as-is or ignored, resulting in all lines being concatenated on a single line without word wrapping.

**Expected:** The PDF renderer should use `WriteWrappedText` or `WriteWrappedSegments` to reflow paragraph text within the page margins, splitting into multiple `Td`/`Tj` lines as needed.

**Workaround:** None in Ascolotl — this requires a fix in `AdocNet.Converters.Pdf.PdfRenderer`.

## 3. API discovery: init-only properties

**Note (not a bug):** `ParseOptions.BaseDirectory` is an init-only property — must use object initializer syntax, not post-construction assignment. Discovered via build error CS8852.

## 4. API discovery: renderer constructors take no arguments

**Note (not a bug):** `HtmlRenderer`, `PdfRenderer`, etc. take no constructor arguments. Options are passed to the `RenderToString(doc, options)` / `RenderToBytes(doc, options)` extension methods, not the constructor. Discovered via build error CS1729.

## 5. DocBook and EPUB converters are separate packages

**Note (not a bug):** The main `AdocNet` package only pulls in `AdocNet.Converters.Html` and `AdocNet.Converters.Pdf` as dependencies. `AdocNet.Converters.DocBook` and `AdocNet.Converters.Epub` must be added explicitly as separate NuGet package references.
