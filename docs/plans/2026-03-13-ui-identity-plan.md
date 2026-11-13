# UI Identity "Writer's Forge" — Implementation Plan

**Goal:** Transform Ascolotl from stock FluentTheme into a distinctive warm-amber writing tool with bundled typography and WCAG AA compliant colors.

**Architecture:** Style overlay on FluentTheme — custom brush resources defined in App.axaml with ThemeVariant-aware light/dark values, font bundled as AvaloniaResource, and targeted Style selectors for TabStrip, StatusBar, GridSplitter, CommandPalette, and dialogs. No new C# classes; changes are purely XAML resources + CSS + font asset + minor code-behind tweaks.

**Tech Stack:** Avalonia 11.3, FluentTheme, AvaloniaEdit 11.4, JetBrains Mono font (OFL license)

**Design reference:** `docs/plans/2026-03-13-ui-identity-design.md`

---

### Task 1: Download and bundle JetBrains Mono font

**Files:**
- Create: `src/Ascolotl.App/Assets/Fonts/JetBrainsMono-Regular.ttf`
- Modify: `src/Ascolotl.App/Ascolotl.App.csproj`

**Step 1: Download the font file**

```bash
mkdir -p src/Ascolotl.App/Assets/Fonts
curl -L -o src/Ascolotl.App/Assets/Fonts/JetBrainsMono-Regular.ttf \
  "https://github.com/JetBrains/JetBrainsMono/raw/master/fonts/ttf/JetBrainsMono-Regular.ttf"
```

Verify: file should be ~160-200KB.

**Step 2: Add font as AvaloniaResource in csproj**

In `src/Ascolotl.App/Ascolotl.App.csproj`, add after the existing `EmbeddedResource` ItemGroup:

```xml
<ItemGroup>
  <AvaloniaResource Include="Assets\Fonts\JetBrainsMono-Regular.ttf" />
</ItemGroup>
```

**Step 3: Build to verify font is included**

```bash
dotnet build --nologo -v q
```

Expected: 0 errors, 0 warnings.

**Step 4: Commit**

```bash
git add src/Ascolotl.App/Assets/Fonts/JetBrainsMono-Regular.ttf src/Ascolotl.App/Ascolotl.App.csproj
git commit -m "Add bundled JetBrains Mono font for editor identity"
```

---

### Task 2: Define custom color resources in App.axaml

**Files:**
- Modify: `src/Ascolotl.App/App.axaml`

**Step 1: Add theme-variant-aware brush resources**

Replace the entire `App.axaml` content with:

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Ascolotl.App.App"
             RequestedThemeVariant="Default">

    <Application.Resources>
        <!-- Dark theme colors -->
        <ResourceDictionary>
            <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Dark">
                    <SolidColorBrush x:Key="ForgeAccentBrush" Color="#C8956C" />
                    <SolidColorBrush x:Key="ForgeAccentDarkBrush" Color="#A87B55" />
                    <SolidColorBrush x:Key="ForgeAccentLightBrush" Color="#D4A87E" />
                    <SolidColorBrush x:Key="ForgeAccentSubtleBrush" Color="#20C8956C" />
                    <SolidColorBrush x:Key="ForgeAccentTextBrush" Color="#C8956C" />
                    <SolidColorBrush x:Key="ForgeBgDeepBrush" Color="#161514" />
                    <SolidColorBrush x:Key="ForgeBgBrush" Color="#1C1B1A" />
                    <SolidColorBrush x:Key="ForgeBgSurfaceBrush" Color="#272523" />
                    <SolidColorBrush x:Key="ForgeBorderBrush" Color="#2A2826" />
                    <SolidColorBrush x:Key="ForgeTextBrush" Color="#D8D3CC" />
                    <SolidColorBrush x:Key="ForgeTextMutedBrush" Color="#8A8580" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Light">
                    <SolidColorBrush x:Key="ForgeAccentBrush" Color="#B07A42" />
                    <SolidColorBrush x:Key="ForgeAccentDarkBrush" Color="#966A3F" />
                    <SolidColorBrush x:Key="ForgeAccentLightBrush" Color="#C8956C" />
                    <SolidColorBrush x:Key="ForgeAccentSubtleBrush" Color="#20C8956C" />
                    <SolidColorBrush x:Key="ForgeAccentTextBrush" Color="#8B6914" />
                    <SolidColorBrush x:Key="ForgeBgDeepBrush" Color="#F0EFED" />
                    <SolidColorBrush x:Key="ForgeBgBrush" Color="#FAFAF8" />
                    <SolidColorBrush x:Key="ForgeBgSurfaceBrush" Color="#F0EFED" />
                    <SolidColorBrush x:Key="ForgeBorderBrush" Color="#E8E6E3" />
                    <SolidColorBrush x:Key="ForgeTextBrush" Color="#333333" />
                    <SolidColorBrush x:Key="ForgeTextMutedBrush" Color="#736E69" />
                </ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
        </ResourceDictionary>
    </Application.Resources>

    <Application.Styles>
        <FluentTheme />
        <StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml" />
    </Application.Styles>

</Application>
```

**Step 2: Build to verify resources are valid**

```bash
dotnet build --nologo -v q
```

Expected: 0 errors, 0 warnings.

**Step 3: Commit**

```bash
git add src/Ascolotl.App/App.axaml
git commit -m "Define warm amber theme color resources with light/dark variants"
```

---

### Task 3: Style the editor — font, line numbers, current line

**Files:**
- Modify: `src/Ascolotl.App/Views/SourceEditorView.axaml`
- Modify: `src/Ascolotl.App/Views/SourceEditorView.axaml.cs`

**Step 1: Update editor XAML font**

In `SourceEditorView.axaml`, change the `FontFamily` attribute on the TextEditor:

```xml
<avaloniaEdit:TextEditor x:Name="Editor"
                         ShowLineNumbers="True"
                         FontFamily="avares://Ascolotl.App/Assets/Fonts/JetBrainsMono-Regular.ttf#JetBrains Mono,Cascadia Code,Consolas,monospace"
                         FontSize="14"
                         WordWrap="True"
                         HorizontalScrollBarVisibility="Auto"
                         VerticalScrollBarVisibility="Auto" />
```

**Step 2: Set line number foreground and current line border in code-behind**

In `SourceEditorView.axaml.cs`, in the constructor after `Editor.Options.HighlightCurrentLine = true;`, add:

```csharp
// Apply forge theme colors to editor chrome
Editor.LineNumbersForeground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8A8580"));

// Current line highlight: accent-tinted border
Editor.TextArea.TextView.CurrentLineBorder = new Avalonia.Media.Pen(
    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#40C8956C")), 1);
```

Note: These are dark-theme defaults. To make them theme-aware, subscribe to the Application's `ActualThemeVariantChanged` event:

```csharp
if (Application.Current is not null)
    Application.Current.ActualThemeVariantChanged += OnThemeChanged;
```

And add the handler:

```csharp
private void OnThemeChanged(object? sender, EventArgs e)
{
    var isDark = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
    Editor.LineNumbersForeground = new Avalonia.Media.SolidColorBrush(
        Avalonia.Media.Color.Parse(isDark ? "#8A8580" : "#736E69"));
    Editor.TextArea.TextView.CurrentLineBorder = new Avalonia.Media.Pen(
        new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse(isDark ? "#40C8956C" : "#40B07A42")), 1);
}
```

Call `OnThemeChanged(null, EventArgs.Empty)` at end of constructor to set initial values.

**Step 3: Build**

```bash
dotnet build --nologo -v q
```

**Step 4: Commit**

```bash
git add src/Ascolotl.App/Views/SourceEditorView.axaml src/Ascolotl.App/Views/SourceEditorView.axaml.cs
git commit -m "Apply JetBrains Mono font and forge accent to editor chrome"
```

---

### Task 4: Replace DynamicResource keys in MainWindow.axaml

**Files:**
- Modify: `src/Ascolotl.App/Views/MainWindow.axaml`

**Step 1: Replace all stock FluentTheme resource references**

Replace these DynamicResource references throughout MainWindow.axaml:

| Old | New |
|-----|-----|
| `SystemControlBackgroundChromeMediumLowBrush` | `ForgeBorderBrush` (for borders/splitters) or `ForgeBgDeepBrush` (for backgrounds) |
| `SystemControlBackgroundAltHighBrush` | `ForgeBgSurfaceBrush` |
| `SystemControlForegroundBaseMediumBrush` | `ForgeTextMutedBrush` |

Specific replacements:

1. **Status bar** (line ~118): `Background="{DynamicResource ForgeBgDeepBrush}"`
2. **Outline pane border** (line ~158): `BorderBrush="{DynamicResource ForgeBorderBrush}"`
3. **Outline header** (line ~166): `Background="{DynamicResource ForgeBgDeepBrush}"`
4. **Outline splitter** (line ~189): `Background="{DynamicResource ForgeBorderBrush}"`
5. **Editor/preview splitter** (line ~241): `Background="{DynamicResource ForgeBorderBrush}"`
6. **Command palette background** (line ~287): `Background="{DynamicResource ForgeBgSurfaceBrush}"`
7. **Command palette border** (line ~288): `BorderBrush="{DynamicResource ForgeBorderBrush}"`
8. **Command palette shortcut text** (line ~312): `Foreground="{DynamicResource ForgeTextMutedBrush}"`

Also update the command palette:
- Change `CornerRadius="8"` to `CornerRadius="12"` (line ~286)

**Step 2: Build**

```bash
dotnet build --nologo -v q
```

**Step 3: Commit**

```bash
git add src/Ascolotl.App/Views/MainWindow.axaml
git commit -m "Replace stock FluentTheme resources with forge palette in MainWindow"
```

---

### Task 5: Style tab strip with accent underline

**Files:**
- Modify: `src/Ascolotl.App/App.axaml` (add Styles section)

**Step 1: Add tab strip styles**

In `App.axaml`, add a `<Styles>` section inside `<Application.Styles>` after the FluentTheme and AvaloniaEdit includes. These styles override the TabStripItem look:

```xml
<!-- Forge tab strip styling -->
<Style Selector="TabStripItem">
    <Setter Property="Foreground" Value="{DynamicResource ForgeTextMutedBrush}" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Padding" Value="12,8" />
    <Setter Property="BorderThickness" Value="0,0,0,2" />
    <Setter Property="BorderBrush" Value="Transparent" />
</Style>
<Style Selector="TabStripItem:selected">
    <Setter Property="Foreground" Value="{DynamicResource ForgeTextBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource ForgeAccentBrush}" />
</Style>
<Style Selector="TabStripItem:pointerover">
    <Setter Property="Foreground" Value="{DynamicResource ForgeTextBrush}" />
</Style>
```

**Step 2: Build and visually verify**

```bash
dotnet build --nologo -v q
```

**Step 3: Commit**

```bash
git add src/Ascolotl.App/App.axaml
git commit -m "Style tab strip with accent underline and muted inactive tabs"
```

---

### Task 6: Style status bar, splitter hover, and menu

**Files:**
- Modify: `src/Ascolotl.App/App.axaml` (add more styles)
- Modify: `src/Ascolotl.App/Views/MainWindow.axaml` (status bar opacity)

**Step 1: Add splitter and menu styles to App.axaml**

Append to the styles section in App.axaml:

```xml
<!-- Splitter: thin hairline, accent on hover -->
<Style Selector="GridSplitter">
    <Setter Property="Background" Value="{DynamicResource ForgeBorderBrush}" />
</Style>
<Style Selector="GridSplitter:pointerover">
    <Setter Property="Background" Value="{DynamicResource ForgeAccentLightBrush}" />
</Style>
```

**Step 2: Update status bar in MainWindow.axaml**

Change the status bar Border to add lower opacity and accent on hover:

```xml
<Border DockPanel.Dock="Bottom"
        Padding="8,4"
        Background="{DynamicResource ForgeBgDeepBrush}"
        Opacity="0.7">
```

Note: The `0.7` opacity gives the subtle, receding status bar feel. Text within will inherit the reduced opacity.

**Step 3: Build**

```bash
dotnet build --nologo -v q
```

**Step 4: Commit**

```bash
git add src/Ascolotl.App/App.axaml src/Ascolotl.App/Views/MainWindow.axaml
git commit -m "Style splitter hover and reduce status bar chrome"
```

---

### Task 7: Update preview CSS with forge theme

**Files:**
- Modify: `src/Ascolotl.App/Views/PreviewView.axaml.cs`

**Step 1: Replace the PreviewCss constant**

Replace the entire `PreviewCss` constant (lines ~133-161 in PreviewView.axaml.cs) with:

```csharp
private const string PreviewCss = """
    body { font-family: Georgia, 'Times New Roman', serif; padding: 20px 24px; line-height: 1.8; font-size: 15px; color: #333; max-width: 720px; margin: 0 auto; }
    h1, h2, h3, h4, h5, h6 { font-family: -apple-system, 'Segoe UI', system-ui, sans-serif; margin-top: 1.4em; margin-bottom: 0.4em; font-weight: 700; letter-spacing: -0.02em; }
    pre { background: #F0EFED; padding: 14px; border-radius: 4px; overflow-x: auto; font-size: 0.88em; border: 1px solid #E8E6E3; font-family: 'JetBrains Mono', Consolas, monospace; }
    code { background: #F0EFED; padding: 2px 5px; border-radius: 3px; font-size: 0.88em; font-family: 'JetBrains Mono', Consolas, monospace; }
    pre code { background: none; padding: 0; border: none; }
    table { border-collapse: collapse; margin: 1em 0; width: 100%; }
    th, td { border: 1px solid #E8E6E3; padding: 8px; text-align: left; }
    th { background: #F0EFED; font-weight: 600; font-family: -apple-system, 'Segoe UI', sans-serif; }
    blockquote { border-left: 3px solid #B07A42; margin: 1em 0; padding: 0.5em 1em; color: #736E69; font-style: italic; }
    a { color: #8B6914; text-decoration: none; }
    a:hover { text-decoration: underline; }
    img { max-width: 100%; height: auto; }
    .admonitionblock { margin: 1em 0; padding: 12px; border-radius: 4px; background: #FDF6EE; border-left: 4px solid #B07A42; }
    #toc { background: #F0EFED; border: 1px solid #E8E6E3; padding: 12px 16px; margin-bottom: 1.5em; border-radius: 4px; }
    #toc ul { list-style: none; padding-left: 1em; margin: 0.3em 0; }
    #toc > ul { padding-left: 0; }
    #toc a { text-decoration: none; color: #8B6914; }
    #toc a:hover { text-decoration: underline; }
    #toctitle { font-weight: 600; margin-bottom: 0.5em; font-family: -apple-system, 'Segoe UI', sans-serif; }
    @media (prefers-color-scheme: dark) {
        body { background: #1C1B1A; color: #D8D3CC; }
        pre { background: #272523; border-color: #2A2826; }
        code { background: #272523; }
        th { background: #272523; }
        th, td { border-color: #2A2826; }
        blockquote { border-color: #C8956C; color: #8A8580; }
        a { color: #C8956C; }
        #toc { background: #272523; border-color: #2A2826; }
        #toc a { color: #C8956C; }
        .admonitionblock { background: #2A2318; border-color: #C8956C; }
    }
    """;
```

**Step 2: Build**

```bash
dotnet build --nologo -v q
```

**Step 3: Commit**

```bash
git add src/Ascolotl.App/Views/PreviewView.axaml.cs
git commit -m "Update preview CSS with forge typography and warm color palette"
```

---

### Task 8: Fix hardcoded colors and style dialogs

**Files:**
- Modify: `src/Ascolotl.App/Views/VisualEditorView.axaml` — fix `#ccc` separator
- Modify: `src/Ascolotl.App/Views/PreviewView.axaml` — fix `#E0CC5500` error overlay
- Modify: `src/Ascolotl.App/Views/RecoveryDialog.axaml` — replace stock resource
- Modify: `src/Ascolotl.App/Views/MainWindow.axaml` — outline header label styling

**Step 1: Fix VisualEditorView separator**

In `VisualEditorView.axaml` line 18, change:
```xml
<Border Width="1" Background="#ccc" Margin="4,2" />
```
to:
```xml
<Border Width="1" Background="{DynamicResource ForgeBorderBrush}" Margin="4,2" />
```

**Step 2: Fix PreviewView error overlay**

In `PreviewView.axaml` line 17, change:
```xml
Background="#E0CC5500"
```
to:
```xml
Background="{DynamicResource ForgeAccentDarkBrush}"
```

And update StatusText foreground to ensure contrast (line 19):
```xml
Foreground="White"
```
(Keep as-is — white on dark accent is fine.)

**Step 3: Fix RecoveryDialog**

In `RecoveryDialog.axaml` line 19, change:
```xml
Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"
```
to:
```xml
Foreground="{DynamicResource ForgeTextMutedBrush}"
```

**Step 4: Update outline pane header**

In `MainWindow.axaml`, change the outline pane header TextBlock (around line 162):
```xml
<TextBlock DockPanel.Dock="Top"
           Text="OUTLINE"
           FontWeight="SemiBold"
           FontSize="11"
           Padding="12,8"
           Foreground="{DynamicResource ForgeTextMutedBrush}"
           Background="{DynamicResource ForgeBgDeepBrush}" />
```

Key changes: text "Outline" → "OUTLINE", FontSize 12 → 11, added Foreground muted.

**Step 5: Build**

```bash
dotnet build --nologo -v q
```

**Step 6: Commit**

```bash
git add src/Ascolotl.App/Views/VisualEditorView.axaml src/Ascolotl.App/Views/PreviewView.axaml \
  src/Ascolotl.App/Views/RecoveryDialog.axaml src/Ascolotl.App/Views/MainWindow.axaml
git commit -m "Fix hardcoded colors and apply forge theme to dialogs and outline"
```

---

### Task 9: Final build and test verification

**Step 1: Full clean build**

```bash
dotnet build --nologo -v q
```

Expected: 0 errors, 0 warnings.

**Step 2: Run all tests**

```bash
dotnet test --nologo -v q
```

Expected: All 452+ tests pass.

**Step 3: Manual verification checklist**

Launch the app and verify:
- [ ] Editor shows JetBrains Mono font
- [ ] Line numbers are muted color
- [ ] Current line has accent-tinted highlight
- [ ] Active tab has amber/gold bottom border
- [ ] Inactive tabs are muted
- [ ] Status bar is subtle with low opacity
- [ ] Splitter shows accent on hover
- [ ] Command palette has 12px corners and accent selection
- [ ] Preview uses Georgia serif, narrower max-width, warm code blocks
- [ ] Preview links are amber
- [ ] Blockquote borders are accent-colored
- [ ] Switch to Dark theme: warm charcoal backgrounds, amber accents
- [ ] Switch to Light theme: warm paper backgrounds, darker amber accents
- [ ] Switch to System theme: follows OS preference
- [ ] Outline pane header says "OUTLINE" in muted small text
- [ ] Visual editor toolbar separator matches theme
- [ ] Error overlay in preview uses accent background

---

### Task 10: Clean up temporary palette files

**Step 1: Remove palette explorer files**

```bash
rm -rf _tmp_palette
```

**Step 2: Final commit**

```bash
git add -A
git commit -m "Remove temporary palette explorer files"
```
