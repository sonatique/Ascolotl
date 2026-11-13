namespace Ascolotl.Tests.Unit.Services;

using Ascolotl.Core.Services;

/// <summary>
/// Tests for <see cref="ScrollSyncCalculator"/> — the pure-logic component
/// that maps editor scroll state to a preview scroll ratio (0.0–1.0).
/// </summary>
public class ScrollSyncCalculatorTests
{
    // ── Basic ratio calculation ──────────────────────────────────

    [Fact]
    public void AtTopOfDocument_ReturnsZero()
    {
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: 0, totalLines: 100, lineSubFraction: 0.0);

        Assert.Equal(0.0, ratio);
    }

    [Fact]
    public void AtBottomOfDocument_ReturnsOne()
    {
        // Last line visible = line 99 (0-based) of 100 total
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: 99, totalLines: 100, lineSubFraction: 0.0);

        Assert.Equal(1.0, ratio);
    }

    [Fact]
    public void AtMiddleOfDocument_ReturnsApproxHalf()
    {
        // 50 / 99 ≈ 0.5051 (denominator is totalLines-1 so last line maps to 1.0)
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: 50, totalLines: 100, lineSubFraction: 0.0);

        Assert.Equal(50.0 / 99.0, ratio, precision: 4);
    }

    [Fact]
    public void SubFractionAddsPartialLine()
    {
        // (50 + 0.5) / 99 ≈ 0.5101
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: 50, totalLines: 100, lineSubFraction: 0.5);

        Assert.Equal(50.5 / 99.0, ratio, precision: 4);
    }

    // ── Edge cases ───────────────────────────────────────────────

    [Fact]
    public void SingleLineDocument_ReturnsZero()
    {
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: 0, totalLines: 1, lineSubFraction: 0.0);

        Assert.Equal(0.0, ratio);
    }

    [Fact]
    public void EmptyDocument_ReturnsZero()
    {
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: 0, totalLines: 0, lineSubFraction: 0.0);

        Assert.Equal(0.0, ratio);
    }

    [Fact]
    public void NegativeLine_ClampsToZero()
    {
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: -5, totalLines: 100, lineSubFraction: 0.0);

        Assert.Equal(0.0, ratio);
    }

    [Fact]
    public void LineBeyondTotal_ClampsToOne()
    {
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: 200, totalLines: 100, lineSubFraction: 0.0);

        Assert.Equal(1.0, ratio);
    }

    [Fact]
    public void SubFractionClamped_NeverExceedsOne()
    {
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine: 99, totalLines: 100, lineSubFraction: 0.99);

        Assert.InRange(ratio, 0.0, 1.0);
    }

    // ── Line-based mapping (word-wrap-agnostic) ──────────────────

    [Theory]
    [InlineData(0, 200, 0.0, 0.0)]             // first line
    [InlineData(100, 200, 0.0, 0.5025)]        // 100/199
    [InlineData(199, 200, 0.0, 1.0)]           // last line
    [InlineData(50, 200, 0.5, 0.2538)]         // (50+0.5)/199
    public void ComputePreviewRatio_LineBasedMappings(
        int firstVisibleLine, int totalLines, double subFraction, double expectedRatio)
    {
        var ratio = ScrollSyncCalculator.ComputePreviewRatio(
            firstVisibleLine, totalLines, subFraction);

        Assert.Equal(expectedRatio, ratio, precision: 4);
    }

    // ── Pixel-to-line conversion (used to extract editor state) ──

    [Fact]
    public void GetFirstVisibleLine_NoWrap_UsesLineHeight()
    {
        // 20px line height, scrolled 300px → line 15, no sub-fraction
        var (line, fraction) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 300.0, lineHeight: 20.0,
            totalLines: 100, wordWrapEnabled: false,
            visualLineStarts: null);

        Assert.Equal(15, line);
        Assert.Equal(0.0, fraction, precision: 4);
    }

    [Fact]
    public void GetFirstVisibleLine_NoWrap_SubFraction()
    {
        // 20px line height, scrolled 310px → line 15 + 0.5 sub-fraction
        var (line, fraction) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 310.0, lineHeight: 20.0,
            totalLines: 100, wordWrapEnabled: false,
            visualLineStarts: null);

        Assert.Equal(15, line);
        Assert.Equal(0.5, fraction, precision: 4);
    }

    [Fact]
    public void GetFirstVisibleLine_NoWrap_AtZero()
    {
        var (line, fraction) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 0.0, lineHeight: 20.0,
            totalLines: 100, wordWrapEnabled: false,
            visualLineStarts: null);

        Assert.Equal(0, line);
        Assert.Equal(0.0, fraction, precision: 4);
    }

    [Fact]
    public void GetFirstVisibleLine_WordWrap_UsesVisualLineStarts()
    {
        // Document with 5 lines. With wrap, their visual Y starts are:
        // Line 0: Y=0   (1 visual line, height 20)
        // Line 1: Y=20  (3 visual lines due to wrapping, height 60)
        // Line 2: Y=80  (1 visual line, height 20)
        // Line 3: Y=100 (2 visual lines, height 40)
        // Line 4: Y=140 (1 visual line, height 20)
        var visualLineStarts = new double[] { 0, 20, 80, 100, 140 };

        // Scrolled to Y=50 → inside line 1 (which starts at Y=20, ends at Y=80)
        var (line, fraction) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 50.0, lineHeight: 20.0,
            totalLines: 5, wordWrapEnabled: true,
            visualLineStarts: visualLineStarts);

        Assert.Equal(1, line);
        // fraction = (50-20) / (80-20) = 30/60 = 0.5
        Assert.Equal(0.5, fraction, precision: 4);
    }

    [Fact]
    public void GetFirstVisibleLine_WordWrap_AtLineStart()
    {
        var visualLineStarts = new double[] { 0, 20, 80, 100, 140 };

        // Scrolled to exactly Y=80 → line 2, fraction 0
        var (line, fraction) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 80.0, lineHeight: 20.0,
            totalLines: 5, wordWrapEnabled: true,
            visualLineStarts: visualLineStarts);

        Assert.Equal(2, line);
        Assert.Equal(0.0, fraction, precision: 4);
    }

    [Fact]
    public void GetFirstVisibleLine_WordWrap_LastLine()
    {
        var visualLineStarts = new double[] { 0, 20, 80, 100, 140 };

        // Scrolled to Y=145 → inside line 4 (starts at Y=140)
        var (line, fraction) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 145.0, lineHeight: 20.0,
            totalLines: 5, wordWrapEnabled: true,
            visualLineStarts: visualLineStarts);

        Assert.Equal(4, line);
        // Last line: fraction based on lineHeight since no next line
        // (145-140) / 20 = 0.25
        Assert.Equal(0.25, fraction, precision: 4);
    }

    [Fact]
    public void GetFirstVisibleLine_WordWrap_NullArray_FallsBackToNoWrap()
    {
        // If visualLineStarts is null even with wrap enabled, fall back to lineHeight calc
        var (line, fraction) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 300.0, lineHeight: 20.0,
            totalLines: 100, wordWrapEnabled: true,
            visualLineStarts: null);

        Assert.Equal(15, line);
        Assert.Equal(0.0, fraction, precision: 4);
    }

    // ── Full pipeline: editor state → preview ratio ─────────────

    [Fact]
    public void FullPipeline_NoWrap_MiddleOfDoc()
    {
        var (line, frac) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 1000.0, lineHeight: 20.0,
            totalLines: 200, wordWrapEnabled: false,
            visualLineStarts: null);

        var ratio = ScrollSyncCalculator.ComputePreviewRatio(line, 200, frac);

        // Line 50 of 200 → 50/199 ≈ 0.2513
        Assert.Equal(50.0 / 199.0, ratio, precision: 4);
    }

    [Fact]
    public void FullPipeline_WordWrap_LongLineSkewsPixelRatio()
    {
        // Scenario: 4 lines, line 1 is very long (wraps to 10 visual lines)
        // Without word-wrap-aware calc, pixel ratio would be skewed.
        // Line 0: Y=0   (20px)
        // Line 1: Y=20  (200px - 10 visual lines)
        // Line 2: Y=220 (20px)
        // Line 3: Y=240 (20px)
        var visualLineStarts = new double[] { 0, 20, 220, 240 };

        // Pixel ratio at Y=120: 120 / (260-viewport) ≈ skewed
        // Line-based: inside line 1 → line 1 of 4 = 0.25 base
        var (line, frac) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 120.0, lineHeight: 20.0,
            totalLines: 4, wordWrapEnabled: true,
            visualLineStarts: visualLineStarts);

        var ratio = ScrollSyncCalculator.ComputePreviewRatio(line, 4, frac);

        // line=1, frac=(120-20)/(220-20)=100/200=0.5
        // ratio = (1 + 0.5) / 3 = 0.5 (denominator is totalLines-1=3)
        // vs pixel ratio ≈ 120/(260-viewport) would be skewed differently
        Assert.Equal(1, line);
        Assert.Equal(0.5, frac, precision: 4);
        Assert.Equal(0.5, ratio, precision: 4);
    }

    // ══════════════════════════════════════════════════════════════
    // Source-line-mapped preview offset (ComputePreviewOffset)
    //
    // Instead of a linear line-number ratio, this uses a sparse map
    // of {sourceLine → previewYPosition} from the rendered HTML to
    // interpolate the exact preview scroll offset for any editor line.
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Helper to build a SortedList from line/position pairs.
    /// </summary>
    private static SortedList<int, double> MakeLineMap(params (int line, double y)[] pairs)
    {
        var map = new SortedList<int, double>();
        foreach (var (line, y) in pairs)
            map.Add(line, y);
        return map;
    }

    // ── Basic interpolation ───────────────────────────────────────

    [Fact]
    public void PreviewOffset_AtExactMappedLine_ReturnsExactPosition()
    {
        // Preview has markers at lines 0, 10, 20, 30
        var lineMap = MakeLineMap(
            (0, 0.0), (10, 200.0), (20, 500.0), (30, 900.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 10, fraction: 0.0, lineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        Assert.Equal(200.0, offset, precision: 1);
    }

    [Fact]
    public void PreviewOffset_BetweenMappedLines_Interpolates()
    {
        // Between line 10 (Y=200) and line 20 (Y=500):
        // line 15 → halfway → 200 + (15-10)/(20-10) * (500-200) = 200 + 150 = 350
        var lineMap = MakeLineMap(
            (0, 0.0), (10, 200.0), (20, 500.0), (30, 900.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 15, fraction: 0.0, lineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        Assert.Equal(350.0, offset, precision: 1);
    }

    [Fact]
    public void PreviewOffset_WithSubFraction_InterpolatesWithinLine()
    {
        // Line 10 (Y=200), line 20 (Y=500). At line 10 + 0.5 fraction:
        // 200 + (10.5-10)/(20-10) * (500-200) = 200 + 0.05 * 300 = 215
        var lineMap = MakeLineMap(
            (0, 0.0), (10, 200.0), (20, 500.0), (30, 900.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 10, fraction: 0.5, lineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        Assert.Equal(215.0, offset, precision: 1);
    }

    [Fact]
    public void PreviewOffset_AtTop_ReturnsZero()
    {
        var lineMap = MakeLineMap(
            (0, 0.0), (10, 200.0), (20, 500.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 0, fraction: 0.0, lineMap,
            previewContentHeight: 800.0, previewViewportHeight: 400.0);

        Assert.Equal(0.0, offset, precision: 1);
    }

    [Fact]
    public void PreviewOffset_ClampsToMaxScroll()
    {
        // Max scroll = contentHeight - viewportHeight = 1200 - 400 = 800
        // Line 30 maps to Y=900, which exceeds max scroll
        var lineMap = MakeLineMap(
            (0, 0.0), (10, 200.0), (30, 900.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 30, fraction: 0.0, lineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        Assert.Equal(800.0, offset, precision: 1);
    }

    // ── Edge cases ────────────────────────────────────────────────

    [Fact]
    public void PreviewOffset_EmptyLineMap_ReturnsZero()
    {
        var lineMap = new SortedList<int, double>();

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 10, fraction: 0.0, lineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        Assert.Equal(0.0, offset, precision: 1);
    }

    [Fact]
    public void PreviewOffset_SingleEntry_ReturnsItRegardlessOfLine()
    {
        var lineMap = MakeLineMap((0, 0.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 50, fraction: 0.0, lineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        // Only one reference point — can't interpolate, return that position
        Assert.Equal(0.0, offset, precision: 1);
    }

    [Fact]
    public void PreviewOffset_LineBeyondLastMapping_Extrapolates()
    {
        // Last mapped line is 20 at Y=500. Source line 25 is beyond.
        // Should extrapolate using the slope of the last segment:
        // slope = (500-200)/(20-10) = 30 per line
        // offset = 500 + (25-20) * 30 = 500 + 150 = 650
        var lineMap = MakeLineMap(
            (0, 0.0), (10, 200.0), (20, 500.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 25, fraction: 0.0, lineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        Assert.Equal(650.0, offset, precision: 1);
    }

    [Fact]
    public void PreviewOffset_LineBeforeFirstMapping_Extrapolates()
    {
        // First mapped line is 5 at Y=100. Source line 2 is before.
        // slope = (200-100)/(10-5) = 20 per line
        // offset = 100 - (5-2) * 20 = 100 - 60 = 40
        var lineMap = MakeLineMap(
            (5, 100.0), (10, 200.0), (20, 500.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 2, fraction: 0.0, lineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        Assert.Equal(40.0, offset, precision: 1);
    }

    [Fact]
    public void PreviewOffset_NegativeExtrapolation_ClampsToZero()
    {
        // Extrapolating before first line would go negative → clamp to 0
        var lineMap = MakeLineMap(
            (10, 100.0), (20, 200.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: 0, fraction: 0.0, lineMap,
            previewContentHeight: 800.0, previewViewportHeight: 400.0);

        Assert.Equal(0.0, offset, precision: 1);
    }

    // ── Word wrap scenarios (full pipeline) ───────────────────────

    [Fact]
    public void FullPipeline_WordWrap_WithLineMap_AccurateSyncAtHeading()
    {
        // Simulate the user's scenario: 40-line document with word wrap.
        // Line 31 ("== Notes") is near end of editor but HTML renders it
        // at 90% of preview height — line-number ratio would say 77%.
        //
        // Editor: line 31 visible at top due to word wrap expanding lines above
        // Preview: "Notes" heading at Y=1080 in a 1200px-tall page
        var visualLineStarts = new double[40];
        double y = 0;
        for (var i = 0; i < 40; i++)
        {
            visualLineStarts[i] = y;
            // Lines 35-36 are long → wrap to 3 visual lines each
            y += (i == 35 || i == 36) ? 60.0 : 20.0;
        }

        // Editor scrolled to Y where line 31 is at top
        var (line, frac) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: visualLineStarts[31], lineHeight: 20.0,
            totalLines: 40, wordWrapEnabled: true,
            visualLineStarts: visualLineStarts);

        Assert.Equal(31, line);

        // Preview line map (from HTML data-line markers)
        // The preview distributes content unevenly — headings/TOC take space
        var previewLineMap = MakeLineMap(
            (0, 0.0), (5, 120.0), (10, 240.0), (16, 400.0),
            (18, 520.0), (31, 1080.0), (32, 1120.0), (39, 1180.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: line, fraction: frac, previewLineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        // Should scroll to Y=1080 (where "Notes" actually is in preview),
        // clamped to max scroll = 800
        Assert.Equal(800.0, offset, precision: 1);
    }

    [Fact]
    public void FullPipeline_NoWrap_WithLineMap_StillWorks()
    {
        // Even without word wrap, line map provides better sync than ratio
        var (line, frac) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 200.0, lineHeight: 20.0,
            totalLines: 40, wordWrapEnabled: false,
            visualLineStarts: null);

        Assert.Equal(10, line);

        var previewLineMap = MakeLineMap(
            (0, 0.0), (10, 300.0), (20, 500.0), (30, 900.0), (39, 1100.0));

        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: line, fraction: frac, previewLineMap,
            previewContentHeight: 1200.0, previewViewportHeight: 400.0);

        Assert.Equal(300.0, offset, precision: 1);
    }

    [Fact]
    public void FullPipeline_WordWrap_EditorOnlyNoMap_FallsBackToRatio()
    {
        // When no preview line map available, ComputePreviewOffset should
        // still work by falling back to a ratio-based approach
        var (line, frac) = ScrollSyncCalculator.GetFirstVisibleLine(
            scrollOffsetY: 300.0, lineHeight: 20.0,
            totalLines: 100, wordWrapEnabled: false,
            visualLineStarts: null);

        // Null line map → fall back to ratio
        var offset = ScrollSyncCalculator.ComputePreviewOffset(
            sourceLine: line, fraction: frac, lineMap: null,
            previewContentHeight: 2000.0, previewViewportHeight: 500.0,
            totalLines: 100);

        // Falls back to ratio: (15 + 0) / 99 * (2000-500) = 0.1515 * 1500 ≈ 227.3
        var expectedRatio = 15.0 / 99.0;
        var expected = expectedRatio * (2000.0 - 500.0);
        Assert.Equal(expected, offset, precision: 1);
    }

    // ── Inverse: preview Y → source line (reverse sync) ───────────

    [Fact]
    public void InverseLookup_AtExactMappedY_ReturnsExactLine()
    {
        var map = MakeLineMap((0, 0.0), (10, 200.0), (20, 500.0), (30, 900.0));

        var (line, frac) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            previewContentY: 500.0, map, totalLines: 31, previewContentHeight: 1200.0);

        Assert.Equal(20, line);
        Assert.Equal(0.0, frac, precision: 3);
    }

    [Fact]
    public void InverseLookup_BetweenMappedYs_InterpolatesLine()
    {
        // Halfway between (10→200) and (20→500): Y=350 → line=15.
        var map = MakeLineMap((0, 0.0), (10, 200.0), (20, 500.0), (30, 900.0));

        var (line, frac) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            previewContentY: 350.0, map, totalLines: 31, previewContentHeight: 1200.0);

        Assert.Equal(15, line);
        Assert.Equal(0.0, frac, precision: 2);
    }

    [Fact]
    public void InverseLookup_QuarterIntoSegment_ReportsFraction()
    {
        // (10→200), (20→500). Y=275 is 25% of the segment: line 12, frac 0.5.
        var map = MakeLineMap((0, 0.0), (10, 200.0), (20, 500.0));

        var (line, frac) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            previewContentY: 275.0, map, totalLines: 21, previewContentHeight: 800.0);

        Assert.Equal(12, line);
        Assert.Equal(0.5, frac, precision: 2);
    }

    [Fact]
    public void InverseLookup_BeforeFirstMarker_ReturnsFirstLine()
    {
        var map = MakeLineMap((5, 100.0), (15, 300.0));

        var (line, frac) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            previewContentY: 0.0, map, totalLines: 20, previewContentHeight: 800.0);

        Assert.Equal(5, line);
        Assert.Equal(0.0, frac, precision: 3);
    }

    [Fact]
    public void InverseLookup_PastLastMarker_ExtrapolatesFromLastSegment()
    {
        // (10→200), (20→500): slope 30 pixels/line. Y=650 is 150px past 20
        // → 5 lines past 20 → line 25.
        var map = MakeLineMap((0, 0.0), (10, 200.0), (20, 500.0));

        var (line, frac) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            previewContentY: 650.0, map, totalLines: 40, previewContentHeight: 800.0);

        Assert.Equal(25, line);
        Assert.Equal(0.0, frac, precision: 2);
    }

    [Fact]
    public void InverseLookup_EmptyMap_FallsBackToRatio()
    {
        var (line, frac) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            previewContentY: 600.0, lineMap: null, totalLines: 101, previewContentHeight: 1200.0);

        // 600/1200 = 0.5 of (101-1) = 50.0 → line 50, frac 0.
        Assert.Equal(50, line);
        Assert.Equal(0.0, frac, precision: 3);
    }

    [Fact]
    public void InverseLookup_SingleEntry_ReturnsThatLine()
    {
        var map = MakeLineMap((7, 100.0));

        var (line, _) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
            previewContentY: 500.0, map, totalLines: 20, previewContentHeight: 800.0);

        Assert.Equal(7, line);
    }

    [Fact]
    public void InverseLookup_RoundTrip_PreservesLineWithinTolerance()
    {
        // Forward then inverse on the same map should return the original line
        // at exact-mapped lines.
        var map = MakeLineMap((0, 0.0), (10, 200.0), (25, 600.0), (50, 1100.0));

        foreach (var sourceLine in new[] { 0, 10, 25, 50 })
        {
            var forwardY = ScrollSyncCalculator.ComputePreviewOffset(
                sourceLine, 0.0, map, previewContentHeight: 2000.0, previewViewportHeight: 400.0);

            var (line, _) = ScrollSyncCalculator.ComputeSourceLineFromPreviewY(
                forwardY, map, totalLines: 60, previewContentHeight: 2000.0);

            Assert.Equal(sourceLine, line);
        }
    }
}
