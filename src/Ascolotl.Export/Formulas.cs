using AdocNet;
using AdocNet.Stem;

namespace Ascolotl.Export;

/// <summary>
/// The typesetter for STEM content (LaTeX, AsciiMath) that the preview and every export share:
/// offline, thread-safe, and caching what it has typeset.
/// </summary>
public static class Formulas
{
    public static IStemRenderer Renderer { get; } = new StemRenderer();

    /// <summary>
    /// Loads the math fonts on a worker thread (a few hundred milliseconds), so the first formula
    /// the preview shows does not pay for it on the UI thread.
    /// </summary>
    public static void WarmUp() => Task.Run(() => Renderer.Render("x", "latexmath", display: false));
}
