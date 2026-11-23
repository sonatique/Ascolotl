namespace Ascolotl.Core.Models;

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Interface language code ("en", "fr", "de"); "" follows the system.</summary>
    public string Language { get; set; } = "";
    public bool ShowTitleAndToc { get; set; }
    public bool SyncScrolling { get; set; } = true;
    /// <summary>Misspellings marked in the editors.</summary>
    public bool CheckSpelling { get; set; } = true;
    public bool ShowOutlinePane { get; set; }
    public double SidebarWidth { get; set; } = 220;
    public bool HamburgerMenu { get; set; } = true;

    /// <summary>Print-like page preview (A4 pages) instead of continuous flow.</summary>
    public bool PagePreview { get; set; }
    /// <summary>asciidoctor-pdf YAML theme for PDF export; null = built-in look.</summary>
    public string? StylePath { get; set; }
    /// <summary>How PDF exports derive their Title metadata from the document title.</summary>
    public PdfTitleRule PdfTitleRule { get; set; } = PdfTitleRule.UpToFirstColon;
    /// <summary>Show the PDF title for editing before each PDF export.</summary>
    public bool AskPdfTitle { get; set; }

    /// <summary>Export PDFs as tagged PDF/UA-1.</summary>
    public bool AccessiblePdf { get; set; }
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Project workspace folder to reopen on launch, or null.</summary>
    public string? ProjectFolderPath { get; set; }

    /// <summary>Named export configurations (single-source publishing).</summary>
    public List<ExportProfile> ExportProfiles { get; set; } = [];
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public double WindowX { get; set; } = 100;
    public double WindowY { get; set; } = 100;
    public bool WindowMaximized { get; set; }
}
