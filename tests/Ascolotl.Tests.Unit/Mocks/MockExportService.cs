using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Mocks;

public sealed class MockExportService : IExportService
{
    public ExportResult? ExportResult { get; set; }

    public int ExportCalled { get; private set; }
    public string? LastSource { get; private set; }
    public string? LastBasePath { get; private set; }
    public string? LastOutputPath { get; private set; }
    public ExportFormat? LastFormat { get; private set; }
    public ExportTheme? LastTheme { get; private set; }
    public string? LastStylePath { get; private set; }
    public string? LastPdfTitle { get; private set; }
    public bool LastAccessiblePdf { get; private set; }

    public bool LastMarkdown { get; private set; }

    public Task<ExportResult> ExportAsync(
        string source, bool markdown, string? basePath, string outputPath, ExportFormat format,
        ExportTheme theme, string? stylePath, PdfExportOptions? pdf, CancellationToken cancellationToken)
    {
        LastPdfTitle = pdf?.Title;
        LastAccessiblePdf = pdf?.Accessible == true;
        LastStylePath = stylePath;
        ExportCalled++;
        LastSource = source;
        LastMarkdown = markdown;
        LastBasePath = basePath;
        LastOutputPath = outputPath;
        LastFormat = format;
        LastTheme = theme;
        return Task.FromResult(ExportResult ?? Ascolotl.Core.Models.ExportResult.Failure("No result configured"));
    }
}
