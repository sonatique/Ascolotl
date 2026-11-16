namespace Ascolotl.Core.Models;

/// <summary>PDF-only export settings.</summary>
/// <param name="Title">Replaces the PDF's Title metadata only; null keeps the document title.</param>
/// <param name="Accessible">Tagged PDF/UA-1 output: structure for screen readers, every font embedded.</param>
public sealed record PdfExportOptions(string? Title = null, bool Accessible = false);
