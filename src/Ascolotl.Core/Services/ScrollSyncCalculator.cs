namespace Ascolotl.Core.Services;

/// <summary>
/// Pure-logic component that maps editor scroll state to a preview scroll ratio.
/// Line-based mapping is word-wrap-agnostic: it works on document line numbers
/// rather than pixel offsets, so wrapped lines don't skew the ratio.
/// </summary>
public static class ScrollSyncCalculator
{
    /// <summary>
    /// Compute the preview scroll ratio (0.0–1.0) from the first visible
    /// document line and a sub-line fraction.
    /// </summary>
    /// <param name="firstVisibleLine">0-based document line at the top of the viewport.</param>
    /// <param name="totalLines">Total number of document lines.</param>
    /// <param name="lineSubFraction">Fraction (0.0–1.0) into the first visible line.</param>
    public static double ComputePreviewRatio(int firstVisibleLine, int totalLines, double lineSubFraction)
    {
        if (totalLines <= 1)
            return 0.0;

        var effectiveLine = Math.Max(0, firstVisibleLine) + Math.Clamp(lineSubFraction, 0.0, 1.0);
        var maxLine = totalLines - 1;
        var ratio = effectiveLine / maxLine;

        return Math.Clamp(ratio, 0.0, 1.0);
    }

    /// <summary>
    /// Determine which document line is at the top of the editor viewport,
    /// plus a sub-line fraction for smooth scrolling.
    /// </summary>
    /// <param name="scrollOffsetY">Current vertical scroll offset in pixels.</param>
    /// <param name="lineHeight">Height of a single unwrapped line in pixels.</param>
    /// <param name="totalLines">Total document lines.</param>
    /// <param name="wordWrapEnabled">Whether word wrap is active.</param>
    /// <param name="visualLineStarts">
    /// When word wrap is enabled, the Y-offset where each document line starts.
    /// Array length must equal <paramref name="totalLines"/>.
    /// May be null as a fallback (uses lineHeight-based calculation).
    /// </param>
    /// <returns>Tuple of (documentLine, subFraction).</returns>
    public static (int Line, double Fraction) GetFirstVisibleLine(
        double scrollOffsetY,
        double lineHeight,
        int totalLines,
        bool wordWrapEnabled,
        double[]? visualLineStarts)
    {
        if (totalLines <= 0 || lineHeight <= 0)
            return (0, 0.0);

        scrollOffsetY = Math.Max(0, scrollOffsetY);

        // Use visual line starts when available (word wrap aware)
        if (wordWrapEnabled && visualLineStarts is { Length: > 0 })
            return GetLineFromVisualStarts(scrollOffsetY, lineHeight, totalLines, visualLineStarts);

        // Simple calculation: uniform line heights
        return GetLineFromUniformHeight(scrollOffsetY, lineHeight, totalLines);
    }

    private static (int Line, double Fraction) GetLineFromUniformHeight(
        double scrollOffsetY, double lineHeight, int totalLines)
    {
        var exactLine = scrollOffsetY / lineHeight;
        var line = (int)exactLine;
        var fraction = exactLine - line;

        line = Math.Clamp(line, 0, totalLines - 1);

        return (line, fraction);
    }

    private static (int Line, double Fraction) GetLineFromVisualStarts(
        double scrollOffsetY, double lineHeight, int totalLines, double[] visualLineStarts)
    {
        // Binary search for the document line whose visual start is ≤ scrollOffsetY
        var line = Array.BinarySearch(visualLineStarts, scrollOffsetY);

        if (line < 0)
        {
            // BinarySearch returns ~index of next larger element
            line = ~line - 1;
        }

        line = Math.Clamp(line, 0, totalLines - 1);

        // Calculate sub-fraction within this line's visual extent
        var lineStart = visualLineStarts[line];
        double lineExtent;

        if (line + 1 < visualLineStarts.Length)
            lineExtent = visualLineStarts[line + 1] - lineStart;
        else
            lineExtent = lineHeight; // Last line: use default height

        var fraction = lineExtent > 0
            ? Math.Clamp((scrollOffsetY - lineStart) / lineExtent, 0.0, 1.0)
            : 0.0;

        return (line, fraction);
    }

    /// <summary>
    /// Compute the absolute pixel offset to scroll the preview to, using a sparse
    /// map of {sourceLine → previewYPosition} obtained from the rendered HTML.
    /// Interpolates between known positions for lines not in the map.
    /// </summary>
    /// <param name="sourceLine">0-based document line at the top of the editor.</param>
    /// <param name="fraction">Sub-line fraction (0.0–1.0) within the source line.</param>
    /// <param name="lineMap">
    /// Sparse map of source line numbers to their Y positions in the preview.
    /// Null triggers fallback to ratio-based calculation.
    /// </param>
    /// <param name="previewContentHeight">Total scrollable height of the preview content.</param>
    /// <param name="previewViewportHeight">Visible viewport height of the preview.</param>
    /// <returns>Pixel offset to scroll the preview to (clamped to valid range).</returns>
    public static double ComputePreviewOffset(
        int sourceLine,
        double fraction,
        SortedList<int, double>? lineMap,
        double previewContentHeight,
        double previewViewportHeight,
        int totalLines = 0)
    {
        var maxScroll = Math.Max(0, previewContentHeight - previewViewportHeight);

        if (lineMap is null || lineMap.Count == 0)
        {
            if (totalLines <= 1)
                return 0;

            var ratio = ComputePreviewRatio(sourceLine, totalLines, fraction);
            return Math.Clamp(ratio * maxScroll, 0, maxScroll);
        }

        if (lineMap.Count == 1)
        {
            return Math.Clamp(lineMap.Values[0], 0, maxScroll);
        }

        var effectiveLine = sourceLine + Math.Clamp(fraction, 0.0, 1.0);
        var keys = lineMap.Keys;
        var values = lineMap.Values;

        // Find the bracketing entries
        int lowerIdx = -1;
        int upperIdx = -1;

        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] <= effectiveLine)
                lowerIdx = i;
            if (keys[i] >= effectiveLine && upperIdx < 0)
                upperIdx = i;
        }

        double y;

        if (lowerIdx >= 0 && upperIdx >= 0 && lowerIdx != upperIdx)
        {
            // Interpolate between lower and upper
            double lLine = keys[lowerIdx];
            double uLine = keys[upperIdx];
            double lY = values[lowerIdx];
            double uY = values[upperIdx];
            var t = (effectiveLine - lLine) / (uLine - lLine);
            y = lY + t * (uY - lY);
        }
        else if (lowerIdx >= 0 && upperIdx >= 0 && lowerIdx == upperIdx)
        {
            // Exact match
            y = values[lowerIdx];
        }
        else if (lowerIdx >= 0)
        {
            // Beyond last mapped line — extrapolate using last segment slope
            if (lowerIdx > 0)
            {
                double prevLine = keys[lowerIdx - 1];
                double prevY = values[lowerIdx - 1];
                double lastLine = keys[lowerIdx];
                double lastY = values[lowerIdx];
                var slope = (lastLine - prevLine) > 0
                    ? (lastY - prevY) / (lastLine - prevLine)
                    : 0;
                y = lastY + (effectiveLine - lastLine) * slope;
            }
            else
            {
                y = values[lowerIdx];
            }
        }
        else if (upperIdx >= 0)
        {
            // Before first mapped line — extrapolate using first segment slope
            if (upperIdx + 1 < keys.Count)
            {
                double firstLine = keys[upperIdx];
                double firstY = values[upperIdx];
                double nextLine = keys[upperIdx + 1];
                double nextY = values[upperIdx + 1];
                var slope = (nextLine - firstLine) > 0
                    ? (nextY - firstY) / (nextLine - firstLine)
                    : 0;
                y = firstY + (effectiveLine - firstLine) * slope;
            }
            else
            {
                y = values[upperIdx];
            }
        }
        else
        {
            y = 0;
        }

        return Math.Clamp(y, 0, maxScroll);
    }

    /// <summary>
    /// Inverse of <see cref="ComputePreviewOffset"/>: given a preview Y
    /// position (in content-space pixels), return the source line at that
    /// position by interpolating between bracketing markers in the line map.
    /// Used to drive the editor's scroll position from the preview's scroll
    /// position (reverse sync) and to resolve a click on a preview control
    /// to a caret line.
    /// </summary>
    /// <param name="previewContentY">Target Y in preview content space.</param>
    /// <param name="lineMap">Sparse (sourceLine → Y) map from the last render.</param>
    /// <param name="totalLines">Total document lines, used for ratio fallback when the map is absent or single-entry.</param>
    /// <param name="previewContentHeight">Total scrollable height of preview content.</param>
    /// <returns>0-based source line (with fractional part) for the requested Y.</returns>
    public static (int Line, double Fraction) ComputeSourceLineFromPreviewY(
        double previewContentY,
        SortedList<int, double>? lineMap,
        int totalLines,
        double previewContentHeight)
    {
        if (totalLines <= 1)
            return (0, 0.0);

        if (lineMap is null || lineMap.Count == 0)
        {
            // Ratio fallback: line = totalLines * Y / contentHeight.
            if (previewContentHeight <= 0)
                return (0, 0.0);
            var ratio = Math.Clamp(previewContentY / previewContentHeight, 0.0, 1.0);
            var eff = ratio * (totalLines - 1);
            var line = (int)eff;
            return (Math.Clamp(line, 0, totalLines - 1), eff - line);
        }

        var values = lineMap.Values;
        var keys = lineMap.Keys;

        if (lineMap.Count == 1)
            return (keys[0], 0.0);

        // Binary search for the first value > Y. The bracket is (idx-1, idx).
        // Values are monotonically non-decreasing because they're Y positions
        // assigned in document order.
        int lo = 0, hi = values.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] <= previewContentY) lo = mid + 1;
            else hi = mid;
        }

        // lo is now first index with values[lo] > Y (or lineMap.Count if none).
        if (lo == 0)
        {
            // Before first marker — return the first line.
            return (keys[0], 0.0);
        }
        if (lo >= values.Count)
        {
            // Past the last marker — extrapolate from the final segment if
            // we can, otherwise clamp to the last mapped line.
            if (lo >= 2)
            {
                double prevLine = keys[lo - 2], prevY = values[lo - 2];
                double lastLine = keys[lo - 1], lastY = values[lo - 1];
                var dy = lastY - prevY;
                if (dy > 0)
                {
                    var slope = (lastLine - prevLine) / dy;
                    var extLine = lastLine + (previewContentY - lastY) * slope;
                    var clamped = Math.Clamp(extLine, 0, totalLines - 1);
                    var l = (int)clamped;
                    return (l, clamped - l);
                }
            }
            return (keys[lo - 1], 0.0);
        }

        // Interpolate between (lo-1) and lo.
        double yLow = values[lo - 1], yHigh = values[lo];
        double lineLow = keys[lo - 1], lineHigh = keys[lo];
        double span = yHigh - yLow;
        double t = span > 0 ? (previewContentY - yLow) / span : 0;
        double effLine = lineLow + t * (lineHigh - lineLow);
        var lineFloor = (int)effLine;
        return (Math.Clamp(lineFloor, 0, totalLines - 1), effLine - lineFloor);
    }
}
