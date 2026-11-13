using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Ascolotl.Core.Models;

namespace Ascolotl.App.Views;

/// <summary>
/// Draws wavy underlines (squiggles) under parser-diagnostic ranges in the
/// source editor, coloured by severity. Diagnostics are in editor coordinates
/// (0-based line, 1-based columns); unanchored ones (Line &lt; 0) are skipped.
/// </summary>
internal sealed class DiagnosticRenderer : IBackgroundRenderer
{
    public IReadOnlyList<DocumentDiagnostic> Diagnostics { get; set; } = Array.Empty<DocumentDiagnostic>();

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Diagnostics.Count == 0) return;
        var doc = textView.Document;
        if (doc is null) return;
        textView.EnsureVisualLines();

        foreach (var d in Diagnostics)
        {
            if (d.Line < 0) continue;
            var lineNumber = d.Line + 1; // AvaloniaEdit lines are 1-based
            if (lineNumber < 1 || lineNumber > doc.LineCount) continue;

            var line = doc.GetLineByNumber(lineNumber);
            var startCol = Math.Clamp(d.StartColumn - 1, 0, line.Length);
            var endCol = Math.Clamp(d.EndColumn - 1, startCol, line.Length);
            var start = line.Offset + startCol;
            var end = line.Offset + endCol;
            // Point or near-empty range (e.g. a "85:1-85:1" table diagnostic):
            // underline the whole line so the marker is actually visible.
            if (end - start < 2)
            {
                start = line.Offset;
                end = line.EndOffset;
            }
            if (end <= start) continue; // genuinely empty line

            var pen = new Pen(BrushFor(d.Severity), 1.4);
            var segment = new TextSegment { StartOffset = start, EndOffset = end };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                DrawSquiggle(drawingContext, pen, rect);
        }
    }

    private static void DrawSquiggle(DrawingContext dc, Pen pen, Rect rect)
    {
        const double step = 3.0;
        var y = rect.Bottom - 1;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(rect.Left, y), false);
            var up = true;
            for (var x = rect.Left; x < rect.Right; x += step)
            {
                var nextX = Math.Min(x + step, rect.Right);
                ctx.LineTo(new Point(nextX, up ? y - 2 : y));
                up = !up;
            }
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    private static IBrush BrushFor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => new SolidColorBrush(Color.Parse("#C0241F")),   // dark red
        DiagnosticSeverity.Warning => new SolidColorBrush(Color.Parse("#E5322B")), // red
        _ => new SolidColorBrush(Color.Parse("#3B7FD6")),
    };
}
