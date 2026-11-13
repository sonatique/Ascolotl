# Ascolotl Rebrand Design

**Date:** 2026-03-13
**Status:** Approved

## Summary

Rename the application from "Ascolotl" to "Ascolotl" (AsciiDoc + axolotl).
Replace the amber color system with a new palette derived from the axolotl
mascot photo. Generate app icons in 3 styles. Straight namespace swap with
no structural changes.

## 1. Rename

Mechanical find-and-replace: `Ascolotl` → `Ascolotl`, `adocforge` → `ascolotl`.

### Files to rename

| Old | New |
|-----|-----|
| Ascolotl.slnx | Ascolotl.slnx |
| src/Ascolotl.App/ | src/Ascolotl.App/ |
| src/Ascolotl.App/Ascolotl.App.csproj | src/Ascolotl.App/Ascolotl.App.csproj |
| src/Ascolotl.Core/ | src/Ascolotl.Core/ |
| src/Ascolotl.Core/Ascolotl.Core.csproj | src/Ascolotl.Core/Ascolotl.Core.csproj |
| src/Ascolotl.Infrastructure/ | src/Ascolotl.Infrastructure/ |
| src/Ascolotl.Infrastructure/Ascolotl.Infrastructure.csproj | src/Ascolotl.Infrastructure/Ascolotl.Infrastructure.csproj |
| src/Ascolotl.Export/ | src/Ascolotl.Export/ |
| src/Ascolotl.Export/Ascolotl.Export.csproj | src/Ascolotl.Export/Ascolotl.Export.csproj |
| src/Ascolotl.Editor/ | src/Ascolotl.Editor/ |
| src/Ascolotl.Rendering/ | src/Ascolotl.Rendering/ |
| tests/Ascolotl.Tests.Unit/ | tests/Ascolotl.Tests.Unit/ |
| tests/Ascolotl.Tests.Unit/Ascolotl.Tests.Unit.csproj | tests/Ascolotl.Tests.Unit/Ascolotl.Tests.Unit.csproj |
| tests/Ascolotl.Tests.Golden/ | tests/Ascolotl.Tests.Golden/ |
| tests/Ascolotl.Tests.Integration/ | tests/Ascolotl.Tests.Integration/ |

### Content replacements (all source files)

- All C# `namespace Ascolotl.*` → `namespace Ascolotl.*`
- All C# `using Ascolotl.*` → `using Ascolotl.*`
- All AXAML `xmlns:local="...Ascolotl..."` → `...Ascolotl...`
- All AXAML `x:Class="Ascolotl.*"` → `x:Class="Ascolotl.*"`
- `.csproj` project references, InternalsVisibleTo, RootNamespace, AssemblyName, Product
- `app.manifest` assemblyIdentity name
- `publish.ps1` paths
- Plan/doc files — display name references

### Not renamed

- Root repo folder `C:\Users\me\source\repos\Ascolotl` — manual rename later
- Generated files in bin/obj — auto-regenerated on build

## 2. Icons

Generate 3 icon styles from the axolotl photo, each at all standard sizes
(16, 24, 32, 48, 64, 128, 256, 512, 1024 px PNG + multi-size .ico):

1. **Flat/minimal** — Geometric silhouette, pink gills on cream body
2. **Soft illustrated** — Cartoon axolotl with gentle gradients
3. **Photo crop** — Circular crop of photo face with subtle border

All 3 presented as HTML preview pages for comparison. User picks one;
chosen style replaces `Resources/app.ico` and `Assets/app-icon.png`.

## 3. Color Palettes

6 WCAG AA-compliant palette proposals, each as a standalone HTML page with:
- Full light + dark theme swatches
- WCAG AA contrast ratios for every text/background combination
- Sample UI mockup (editor, preview, tabs, status bar)
- All semantic tokens mapped

### Palette directions

| # | Name | Primary Accent | Source |
|---|------|---------------|--------|
| 1 | Vibrant Gill | Bold magenta-pink | Vivid pink gills |
| 2 | Soft Rose | Muted dusty rose | Gills in soft light |
| 3 | Coral Bloom | Warm coral/peach | Body peach undertones |
| 4 | Deep Lagoon | Dark teal/blue-green | Water background |
| 5 | Aqua + Blush | Teal + pink complementary | Water + gills contrast |
| 6 | Slate Whisper | Cool gray-blue + subtle blush | Gravel substrate + body |

### Semantic color tokens (per palette, per theme)

- ForgeAccentBrush (→ rename to AscolotlAccentBrush or keep Forge prefix — TBD)
- AccentDark, AccentLight, AccentSubtle, AccentText
- BgDeep, Bg, BgSurface
- Border
- Text, TextMuted
- Preview CSS: body bg, text, pre bg, borders, links, blockquote, admonition, scrollbar

### WCAG AA requirements

- Normal text (< 18pt): contrast ratio ≥ 4.5:1
- Large text (≥ 18pt bold or ≥ 24pt): contrast ratio ≥ 3:1
- UI components and graphical objects: contrast ratio ≥ 3:1

## Verification

1. `dotnet build` — 0 errors after rename
2. `dotnet test` — all 493+ tests pass
3. All 3 icon previews render correctly
4. All 6 palette HTML pages display with correct contrast ratios
5. App launches with new name in title bar
