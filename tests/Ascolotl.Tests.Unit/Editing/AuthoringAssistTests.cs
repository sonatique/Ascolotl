namespace Ascolotl.Tests.Unit.Editing;

using Ascolotl.Core.Editing.Assist;

/// <summary>
/// Tests for the authoring-assist Core layer — context analysis, the completion
/// catalog, snippet expansion, smart typing, and the status-bar syntax hint.
/// </summary>
public class AuthoringAssistTests
{
    // --- Snippet parsing ---

    [Fact]
    public void Snippet_ExpandsPlaceholders_AndOrdersStops()
    {
        var parsed = Snippet.Parse("image::${1:path}[${2:alt}]$0");

        Assert.Equal("image::path[alt]", parsed.Text);
        Assert.Equal(3, parsed.Stops.Count);
        Assert.Equal(new[] { 1, 2, 0 }, parsed.Stops.Select(s => s.Index));

        var first = parsed.Stops[0];
        Assert.Equal("path", first.Placeholder);
        Assert.Equal("image::".Length, first.Start);
        Assert.Equal(4, first.Length);

        // $0 is the final, zero-length stop at the end of the text.
        var final = parsed.Stops[^1];
        Assert.Equal(0, final.Index);
        Assert.Equal(parsed.Text.Length, final.Start);
        Assert.Equal(0, final.Length);
    }

    [Fact]
    public void Snippet_NoStops_ReturnsLiteralText()
    {
        var parsed = Snippet.Parse(":toc:");
        Assert.Equal(":toc:", parsed.Text);
        Assert.Empty(parsed.Stops);
    }

    [Fact]
    public void Snippet_BareStopAndEmptyPlaceholder()
    {
        // "${1}" is a zero-length stop; "$0" shorthand is the final caret.
        var parsed = Snippet.Parse("kbd:[${1}]$0");
        Assert.Equal("kbd:[]", parsed.Text);
        Assert.Equal(new[] { 1, 0 }, parsed.Stops.Select(s => s.Index));
        Assert.All(parsed.Stops, s => Assert.Equal(0, s.Length));
    }

    // --- Context analysis ---

    [Fact]
    public void Analyze_LineStart_IsBlockStart()
    {
        var q = AsciiDocContextAnalyzer.Analyze("no", 2);
        Assert.Equal(AssistContextKind.BlockStart, q.Kind);
        Assert.Equal("no", q.Word);
        Assert.Equal(0, q.ReplaceStart);
    }

    [Fact]
    public void Analyze_MidProse_IsInline()
    {
        var text = "Some prose with xr";
        var q = AsciiDocContextAnalyzer.Analyze(text, text.Length);
        Assert.Equal(AssistContextKind.Inline, q.Kind);
        Assert.Equal("xr", q.Word);
    }

    [Fact]
    public void Analyze_InsideBlockBracket_IsAttributeList()
    {
        var text = "[source,ja";
        var q = AsciiDocContextAnalyzer.Analyze(text, text.Length);
        Assert.Equal(AssistContextKind.AttributeList, q.Kind);
        Assert.Equal("ja", q.Word);
    }

    [Fact]
    public void Analyze_MacroBracket_IsNotAttributeList()
    {
        // link:url[te — the '[' follows macro text, not a block attribute list.
        var text = "See link:https://x[te";
        var q = AsciiDocContextAnalyzer.Analyze(text, text.Length);
        Assert.NotEqual(AssistContextKind.AttributeList, q.Kind);
    }

    [Fact]
    public void Analyze_AfterBrace_IsAttributeReference()
    {
        var text = "Version {doc";
        var q = AsciiDocContextAnalyzer.Analyze(text, text.Length);
        Assert.Equal(AssistContextKind.AttributeReference, q.Kind);
        Assert.Equal("doc", q.Word);
    }

    // --- Catalog ---

    [Fact]
    public void Catalog_BlockStart_FiltersByPrefix()
    {
        var q = new AssistQuery(AssistContextKind.BlockStart, "no", 0, 2, "");
        var items = AsciiDocAssistCatalog.ItemsFor(q);
        Assert.Contains(items, i => i.Trigger == "note");
        Assert.DoesNotContain(items, i => i.Trigger == "table");
    }

    [Fact]
    public void Catalog_AfterSourceComma_OffersLanguages()
    {
        var q = new AssistQuery(AssistContextKind.AttributeList, "ja", 8, 10, "[source,");
        var items = AsciiDocAssistCatalog.ItemsFor(q);
        Assert.Contains(items, i => i.Trigger == "java");
        Assert.All(items, i => Assert.Equal(AssistItemKind.Language, i.Kind));
    }

    [Fact]
    public void Catalog_AttributeReference_ClosesBrace()
    {
        var q = new AssistQuery(AssistContextKind.AttributeReference, "doct", 0, 4, "{");
        var items = AsciiDocAssistCatalog.ItemsFor(q);
        var doctitle = Assert.Single(items, i => i.Trigger == "doctitle");
        Assert.Equal("doctitle}", doctitle.Snippet);
    }

    // --- Smart typing: list continuation ---

    [Fact]
    public void ContinueList_Bullet_RepeatsMarker()
    {
        var c = SmartTyping.ContinueList("* first item");
        Assert.NotNull(c);
        Assert.Equal("* ", c!.Marker);
        Assert.False(c.TerminateEmpty);
    }

    [Fact]
    public void ContinueList_NestedBullet_PreservesDepthAndIndent()
    {
        var c = SmartTyping.ContinueList("  ** nested");
        Assert.NotNull(c);
        Assert.Equal("  ** ", c!.Marker);
    }

    [Fact]
    public void ContinueList_EmptyItem_Terminates()
    {
        var c = SmartTyping.ContinueList(". ");
        Assert.NotNull(c);
        Assert.True(c!.TerminateEmpty);
    }

    [Fact]
    public void ContinueList_Checklist_AddsBox()
    {
        var c = SmartTyping.ContinueList("* [x] done");
        Assert.NotNull(c);
        Assert.Equal("* [ ] ", c!.Marker);
    }

    [Fact]
    public void ContinueList_NotAList_ReturnsNull()
    {
        Assert.Null(SmartTyping.ContinueList("just a paragraph"));
        Assert.Null(SmartTyping.ContinueList("*bold* not a list"));
    }

    // --- Smart typing: delimiter close ---

    [Theory]
    [InlineData("----", "----")]
    [InlineData("====", "====")]
    [InlineData("....", "....")]
    [InlineData("****", "****")]
    [InlineData("____", "____")]
    [InlineData("++++", "++++")]
    [InlineData("|===", "|===")]
    public void ClosingDelimiter_MatchesOpener(string opener, string expected)
    {
        Assert.Equal(expected, SmartTyping.ClosingDelimiterFor(opener));
    }

    [Theory]
    [InlineData("---")]      // too short
    [InlineData("== Heading")]
    [InlineData("text")]
    [InlineData("")]
    public void ClosingDelimiter_IgnoresNonDelimiters(string line)
    {
        Assert.Null(SmartTyping.ClosingDelimiterFor(line));
    }

    // --- Syntax hint ---

    [Theory]
    [InlineData("= Title", "Document title")]
    [InlineData("== Section", "Section heading (level 1)")]
    [InlineData("=== Sub", "Section heading (level 2)")]
    [InlineData("* item", "Unordered list item")]
    [InlineData(". item", "Ordered list item")]
    [InlineData("NOTE: hi", "Admonition (NOTE)")]
    [InlineData("// a comment", "Comment (not rendered)")]
    public void SyntaxHint_DescribesLine(string line, string expected)
    {
        Assert.Equal(expected, SyntaxHint.Describe(line, line.Length));
    }

    [Fact]
    public void SyntaxHint_PlainProse_IsNull()
    {
        Assert.Null(SyntaxHint.Describe("just ordinary prose", 5));
    }

    // --- Cross-reference context ---

    [Fact]
    public void Analyze_AfterDoubleAngle_IsCrossReference()
    {
        var text = "See <<inst";
        var q = AsciiDocContextAnalyzer.Analyze(text, text.Length);
        Assert.Equal(AssistContextKind.CrossReference, q.Kind);
        Assert.Equal("inst", q.Word);
    }

    // --- Verbatim-block suppression (real tabs in code) ---

    [Fact]
    public void Analyze_InsideListingBlock_IsNone()
    {
        // Caret on the code line inside a ---- block: no completion, so Tab is
        // a real tab and typing code never triggers suggestions.
        var text = "----\nint x = ta";
        var q = AsciiDocContextAnalyzer.Analyze(text, text.Length);
        Assert.Equal(AssistContextKind.None, q.Kind);
    }

    [Fact]
    public void Analyze_AfterClosedListingBlock_ResumesCompletion()
    {
        var text = "----\ncode\n----\n\nta";
        var q = AsciiDocContextAnalyzer.Analyze(text, text.Length);
        Assert.Equal(AssistContextKind.BlockStart, q.Kind);
    }

    [Fact]
    public void Analyze_InsideTable_IsNotSuppressed()
    {
        // Tables are not verbatim — completion stays available inside them.
        var text = "|===\n| ta";
        var q = AsciiDocContextAnalyzer.Analyze(text, text.Length);
        Assert.NotEqual(AssistContextKind.None, q.Kind);
    }

    // --- Table detection (smart cell/row typing + hint) ---

    [Fact]
    public void IsInsideTable_BetweenDelimiters_True()
    {
        var text = "[cols=\"1,1\"]\n|===\n| a | b\n|===\n";
        // Caret on the "| a | b" row.
        var caret = text.IndexOf("| a", StringComparison.Ordinal) + 2;
        Assert.True(AsciiDocContextAnalyzer.IsInsideTable(text, caret));
    }

    [Fact]
    public void IsInsideTable_AfterClose_False()
    {
        var text = "|===\n| a | b\n|===\n\nAfter.";
        var caret = text.IndexOf("After", StringComparison.Ordinal);
        Assert.False(AsciiDocContextAnalyzer.IsInsideTable(text, caret));
    }

    [Fact]
    public void IsInsideTable_OutsideAnyTable_False()
    {
        Assert.False(AsciiDocContextAnalyzer.IsInsideTable("Just a paragraph.", 5));
    }

    [Fact]
    public void SyntaxHint_InsideTable_AdvertisesShortcuts()
    {
        var text = "|===\n| a | b\n|===\n";
        var caret = text.IndexOf("| a", StringComparison.Ordinal) + 2;
        var hint = SyntaxHint.Describe(text, caret);
        Assert.NotNull(hint);
        Assert.Contains("Table", hint!);
        Assert.Contains("Enter", hint);
    }

    // --- Document scanner (doc-aware completion) ---

    [Fact]
    public void Scanner_FindsExplicitAnchors()
    {
        var src = "[[install]]\n== Setup\n\n[#config]\nText.";
        var ctx = AsciiDocDocScanner.Scan(src);
        Assert.Contains(ctx.Anchors, a => a.Trigger == "install");
        Assert.Contains(ctx.Anchors, a => a.Trigger == "config");
        // Each anchor completes to the closing "id>>".
        var install = ctx.Anchors.First(a => a.Trigger == "install");
        Assert.Equal("install>>$0", install.Snippet);
    }

    [Fact]
    public void Scanner_DerivesSectionIds()
    {
        var ctx = AsciiDocDocScanner.Scan("== Getting Started\n\nText.");
        Assert.Contains(ctx.Anchors, a => a.Trigger == "_getting_started");
    }

    [Fact]
    public void Scanner_FindsDefinedAttributes()
    {
        var ctx = AsciiDocDocScanner.Scan(":product: AdocForge\n:version!:\n\nText.");
        Assert.Contains(ctx.Attributes, a => a.Trigger == "product" && a.Snippet == "product}");
        Assert.Contains(ctx.Attributes, a => a.Trigger == "version");
    }

    [Theory]
    [InlineData("My Section", "_my_section")]
    [InlineData("Hello, World!", "_hello_world")]
    [InlineData("  spaced  out  ", "_spaced_out")]
    [InlineData("123", "_123")]
    public void Scanner_SectionId_FollowsAsciidoctorDefaults(string title, string expected)
    {
        Assert.Equal(expected, AsciiDocDocScanner.SectionId(title));
    }

    [Fact]
    public void Scanner_Empty_IsEmptyContext()
    {
        var ctx = AsciiDocDocScanner.Scan("");
        Assert.Empty(ctx.Anchors);
        Assert.Empty(ctx.Attributes);
    }

    // --- Catalog breadth ---

    [Fact]
    public void Catalog_IncludesExpandedSyntaxes()
    {
        var block = AsciiDocAssistCatalog.ItemsFor(
            new AssistQuery(AssistContextKind.BlockStart, "", 0, 0, ""));
        foreach (var trigger in new[] { "include", "ifdef", "anchor", "literal", "open", "pagebreak" })
            Assert.Contains(block, i => i.Trigger == trigger);

        var inline = AsciiDocAssistCatalog.ItemsFor(
            new AssistQuery(AssistContextKind.Inline, "", 0, 0, ""));
        Assert.Contains(inline, i => i.Trigger == "ref");   // <<>> cross-reference
        Assert.Contains(inline, i => i.Trigger == "stem");
    }
}
