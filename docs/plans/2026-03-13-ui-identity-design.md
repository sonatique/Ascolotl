# Ascolotl UI Identity — "Writer's Forge" Design

## Goal

Transform Ascolotl from stock FluentTheme into a distinctive, recognizable writing tool
with warm amber identity, WCAG AA compliant colors, and bundled typography.

## Color System

### Accent Palette — Warm Amber

| Token | Hex | Usage |
|-------|-----|-------|
| ForgeAccent | `#C8956C` | Dark theme: active tab underline, links, current line border, modified dot, cmd palette selection |
| ForgeAccentDark | `#A87B55` | Pressed/active states |
| ForgeAccentLight | `#D4A87E` | Hover states, lighter highlights |
| ForgeAccentSubtle | `#C8956C20` | Selection background (12% opacity) |
| ForgeAccentOnLight | `#B07A42` | Light theme: UI chrome (tab underlines, dots, borders, blockquote borders) — 3.5:1 passes UI component threshold |
| ForgeAccentText | `#8B6914` | Light theme: link text in preview — 4.6:1 passes AA body text |

### Dark Theme — Warm Charcoal

| Token | Hex | Usage |
|-------|-----|-------|
| ForgeBgDeep | `#161514` | Title bar, deep borders |
| ForgeBg | `#1C1B1A` | Main background |
| ForgeBgSurface | `#272523` | Elevated surfaces (code blocks, inputs, cards) |
| ForgeBorder | `#2A2826` | Borders, splitters, dividers |
| ForgeText | `#D8D3CC` | Primary text (warm off-white) |
| ForgeTextMuted | `#8A8580` | Secondary text, line numbers — 4.8:1 on ForgeBg |

### Light Theme — Warm Paper

| Token | Hex | Usage |
|-------|-----|-------|
| ForgeBgDeep | `#F0EFED` | Title bar, deep borders |
| ForgeBg | `#FAFAF8` | Main background |
| ForgeBgSurface | `#F0EFED` | Elevated surfaces |
| ForgeBorder | `#E8E6E3` | Borders, splitters, dividers |
| ForgeText | `#333333` | Primary text |
| ForgeTextMuted | `#736E69` | Secondary text, line numbers — 4.8:1 on ForgeBg (was #999 at 2.7:1) |

### WCAG AA Compliance

All combinations verified:
- Dark primary text: 11.7:1 ✓
- Dark muted text: 4.8:1 ✓
- Dark accent: 6.6:1 ✓
- Light primary text: 12.1:1 ✓
- Light muted text: 4.8:1 ✓ (fixed from 2.7:1)
- Light accent UI chrome: 3.5:1 ✓ (UI component threshold 3.0:1)
- Light accent body text: 4.6:1 ✓ (using #8B6914 for links)

## Typography

### Editor Font: JetBrains Mono (bundled)

- **Bundled as embedded resource** (~200KB) — guarantees identical look across Windows/Mac/Linux
- Font stack: `'JetBrains Mono', 'Cascadia Code', Consolas, monospace`
- Size: 14px, line-height: 1.7
- Replaces: `Consolas, Courier New, monospace`
- Asset location: `src/Ascolotl.App/Assets/Fonts/JetBrainsMono-Regular.ttf`

### Preview Typography

- **Body**: `Georgia, 'Times New Roman', serif` — serif for "reading a manuscript" feel, already on all platforms
- **Headings**: `'Segoe UI', system-ui, sans-serif` with `letter-spacing: -0.02em`, `font-weight: 700`
- **Code blocks**: `'JetBrains Mono', Consolas, monospace` — matches editor
- Size: 15-16px, `line-height: 1.8`, `max-width: 720px` (narrower, more book-like)

## Avalonia Style Overlay

All changes in `App.axaml` as `<Style>` overrides on FluentTheme. Custom brush resources
defined in `<Application.Resources>` with `ThemeVariant` support.

### Tab Strip

- Flat text tabs, no background fill
- Active tab: 2px bottom border in accent color, primary text color
- Inactive tabs: muted text color, no border
- Modified indicator: `●` dot in accent (replaces `*`)

### Status Bar

- Reduced chrome — lighter background, no strong border
- Text in muted color at `Opacity="0.6"`, `0.9` on hover
- Same information: status text, cursor position, editor mode, zoom

### Splitter

- 1px hairline in border color
- Accent-tinted on hover

### Command Palette

- `CornerRadius="12"` (up from 8)
- Selected item: accent background with white text
- Surface background matching ForgeBgSurface
- Border in ForgeBorder

### Menu Bar

- Warm background matching theme
- Text in theme-appropriate colors

### Outline Pane

- Remove header background contrast
- Label: small-caps "OUTLINE" in muted color
- Generous `line-height` on items
- Active/selected item: accent left-border

### Dialogs

- Warm surface backgrounds matching theme palette
- Accent-colored primary action buttons

## Preview CSS Update

Replace current `PreviewCss` constant with theme-aware version:

- Body: Georgia serif, `line-height: 1.8`, `max-width: 720px`
- Headings: sans-serif, `letter-spacing: -0.02em`
- Code: JetBrains Mono, ForgeBgSurface background
- Links: `#C8956C` (dark), `#8B6914` (light)
- Blockquote border: accent color
- Dark mode: full warm charcoal palette (replaces cold `#1e1e1e`/`#2d2d2d`)

## Editor Refinements

- Font: JetBrains Mono (bundled)
- Current line highlight: attempt accent-tinted via AvaloniaEdit API if available
- Line number color: muted color
- Note: AvaloniaEdit has limited styling hooks — apply what's possible without forking

## Files Changed

| File | Change |
|------|--------|
| `src/Ascolotl.App/App.axaml` | Add custom brush resources, style overrides for TabStrip, StatusBar, GridSplitter, Command Palette, Menu, Outline, Dialogs |
| `src/Ascolotl.App/Assets/Fonts/JetBrainsMono-Regular.ttf` | **New** — bundled editor font |
| `src/Ascolotl.App/Ascolotl.App.csproj` | Add font as EmbeddedResource or AvaloniaResource |
| `src/Ascolotl.App/Views/SourceEditorView.axaml` | Update FontFamily to JetBrains Mono stack |
| `src/Ascolotl.App/Views/SourceEditorView.axaml.cs` | Line number color, current line styling via AvaloniaEdit API |
| `src/Ascolotl.App/Views/PreviewView.axaml.cs` | Replace PreviewCss with warm-themed version |
| `src/Ascolotl.App/Views/MainWindow.axaml` | Update command palette CornerRadius, outline pane header, splitter width |
| `src/Ascolotl.App/Views/VisualEditorView.axaml` | Update toolbar separator color to use theme |
| `src/Ascolotl.App/Views/SaveConfirmationDialog.axaml` | Warm dialog styling |
| `src/Ascolotl.App/Views/RecoveryDialog.axaml` | Warm dialog styling |
| `src/Ascolotl.App/Views/FileConflictDialog.axaml` | Warm dialog styling |

## Explicitly Out of Scope

- Custom title bar / ExtendClientAreaToDecorationsHint (high effort, platform edge cases)
- Backdrop blur on command palette (limited Avalonia support)
- Paper texture / custom cursor styling (too risky for v1)
- Animations / transitions
- Font ligatures in editor (AvaloniaEdit config complexity)
