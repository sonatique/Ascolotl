using Ascolotl.Core.Models;
using Ascolotl.Core.ViewModels;

namespace Ascolotl.Tests.Unit.ViewModels;

public sealed class DocumentViewModelTests : IDisposable
{
    public DocumentViewModelTests()
    {
        DocumentViewModel.ResetUntitledCounter();
    }

    public void Dispose()
    {
        DocumentViewModel.ResetUntitledCounter();
    }

    [Fact]
    public void CreateNew_SetsIncrementingUntitledName()
    {
        var doc1 = DocumentViewModel.CreateNew();
        var doc2 = DocumentViewModel.CreateNew();

        Assert.Equal("Untitled 1", doc1.DisplayName);
        Assert.Equal("Untitled 2", doc2.DisplayName);
    }

    [Fact]
    public void CreateNew_IsNotDirty()
    {
        var doc = DocumentViewModel.CreateNew();

        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void CreateNew_HasNewState()
    {
        var doc = DocumentViewModel.CreateNew();

        Assert.Equal(DocumentState.New, doc.State);
    }

    [Fact]
    public void CreateNew_HasNullFilePath()
    {
        var doc = DocumentViewModel.CreateNew();

        Assert.Null(doc.FilePath);
    }

    [Fact]
    public void SettingSourceText_SetsDirty()
    {
        var doc = DocumentViewModel.CreateNew();

        doc.SourceText = "hello";

        Assert.True(doc.IsDirty);
        Assert.Equal(DocumentState.Dirty, doc.State);
    }

    [Fact]
    public void SettingSourceText_DuringLoad_DoesNotSetDirty()
    {
        var doc = DocumentViewModel.CreateNew();

        doc.BeginLoad();
        doc.SourceText = "loaded content";
        doc.EndLoad();

        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void MarkClean_ResetsDirtyFlag()
    {
        var doc = DocumentViewModel.CreateNew();
        doc.SourceText = "changed";
        Assert.True(doc.IsDirty);

        doc.MarkClean();

        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void MarkClean_WithFilePath_SetsStateToClean()
    {
        var doc = DocumentViewModel.CreateNew();
        doc.BeginLoad();
        doc.FilePath = "/tmp/test.adoc";
        doc.SourceText = "content";
        doc.EndLoad();
        doc.MarkClean();

        Assert.Equal(DocumentState.Clean, doc.State);
    }

    [Fact]
    public void MarkClean_WithoutFilePath_SetsStateToNew()
    {
        var doc = DocumentViewModel.CreateNew();
        doc.SourceText = "changed";
        doc.MarkClean();

        Assert.Equal(DocumentState.New, doc.State);
    }

    [Fact]
    public void TabTitle_ShowsAsteriskWhenDirty()
    {
        var doc = DocumentViewModel.CreateNew();
        doc.SourceText = "changed";

        Assert.EndsWith(" *", doc.TabTitle);
    }

    [Fact]
    public void TabTitle_NoAsteriskWhenClean()
    {
        var doc = DocumentViewModel.CreateNew();

        Assert.Equal(doc.DisplayName, doc.TabTitle);
        Assert.DoesNotContain("*", doc.TabTitle);
    }

    [Fact]
    public void PropertyChanged_RaisedForTabTitle_WhenDirtyChanges()
    {
        var doc = DocumentViewModel.CreateNew();
        var raised = new List<string>();
        doc.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        doc.SourceText = "changed";

        Assert.Contains("TabTitle", raised);
    }

    // --- Render state defaults ---

    [Fact]
    public void CreateNew_HasEmptyRenderState()
    {
        var doc = DocumentViewModel.CreateNew();

        Assert.Equal(string.Empty, doc.RenderedHtml);
        Assert.False(doc.LastRenderSucceeded);
        Assert.Null(doc.RenderError);
        Assert.False(doc.IsRendering);
    }

    // --- Zoom / FontSize ---

    [Fact]
    public void FontSize_DefaultIs14()
    {
        var doc = DocumentViewModel.CreateNew();

        Assert.Equal(14.0, doc.FontSize);
    }

    [Fact]
    public void FontSize_ScalesWithZoomLevel()
    {
        var doc = DocumentViewModel.CreateNew();

        doc.ZoomLevel = 200;

        Assert.Equal(28.0, doc.FontSize);
    }

    [Fact]
    public void FontSize_AtMinZoom_Is7()
    {
        var doc = DocumentViewModel.CreateNew();

        doc.ZoomLevel = DocumentViewModel.ZoomMin; // 50

        Assert.Equal(7.0, doc.FontSize);
    }

    [Fact]
    public void ZoomLevel_Change_NotifiesFontSize()
    {
        var doc = DocumentViewModel.CreateNew();
        var raised = new List<string>();
        doc.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        doc.ZoomLevel = 150;

        Assert.Contains("FontSize", raised);
    }

    // --- Diagnostics ---

    [Fact]
    public void Diagnostics_UpdateDerivedCounts()
    {
        var doc = DocumentViewModel.CreateNew();
        Assert.False(doc.HasDiagnostics);
        Assert.Equal(0, doc.ErrorCount);
        Assert.Equal(0, doc.WarningCount);

        var raised = new List<string>();
        doc.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        doc.Diagnostics =
        [
            new DocumentDiagnostic(0, 1, 3, DiagnosticSeverity.Error, "err"),
            new DocumentDiagnostic(2, 1, 3, DiagnosticSeverity.Warning, "warn"),
            new DocumentDiagnostic(-1, 1, 1, DiagnosticSeverity.Warning, "unanchored"),
        ];

        Assert.True(doc.HasDiagnostics);
        Assert.Equal(1, doc.ErrorCount);
        Assert.Equal(2, doc.WarningCount);
        Assert.True(doc.HasErrors);
        Assert.True(doc.HasWarnings);
        Assert.Contains("ErrorCount", raised);
        Assert.Contains("WarningCount", raised);
    }

    [Fact]
    public void Hint_TranslatesIncompleteRowDeficitIntoAnEdit()
    {
        var d = new DocumentDiagnostic(66, 1, 1, DiagnosticSeverity.Warning,
            "Table ended with an incomplete row: expected 6 cell(s) but found 1; dropping the incomplete row");

        Assert.NotNull(d.Hint);
        Assert.Contains("Add 5 more cells", d.Hint);
        Assert.Contains("rowspan", d.Hint);
    }

    [Fact]
    public void Hint_SingularCellForADeficitOfOne()
    {
        var d = new DocumentDiagnostic(10, 1, 1, DiagnosticSeverity.Warning,
            "Table ended with an incomplete row: expected 4 cell(s) but found 3; dropping the incomplete row");

        Assert.NotNull(d.Hint);
        Assert.Contains("Add 1 more cell ", d.Hint);
    }

    [Fact]
    public void Hint_NullForUnrelatedMessages()
    {
        var d = new DocumentDiagnostic(0, 1, 3, DiagnosticSeverity.Error, "some other problem");
        Assert.Null(d.Hint);
    }

    [Fact]
    public void DiagnosticsSummary_AppendsHintWhenAvailable()
    {
        var doc = DocumentViewModel.CreateNew();
        doc.Diagnostics =
        [
            new DocumentDiagnostic(66, 1, 1, DiagnosticSeverity.Warning,
                "Table ended with an incomplete row: expected 6 cell(s) but found 1; dropping the incomplete row"),
        ];

        Assert.Contains("Line 67:", doc.DiagnosticsSummary);
        Assert.Contains("→ This row fills 1 of the 6 cells", doc.DiagnosticsSummary);
    }

    [Fact]
    public void DiagnosticsSummary_ShowsFileAndLineForIncludeDiagnostics()
    {
        var doc = DocumentViewModel.CreateNew();
        doc.Diagnostics =
        [
            // Unanchored (Line = -1) but originating in an include.
            new DocumentDiagnostic(-1, 1, 1, DiagnosticSeverity.Warning, "broken",
                SourceFile: "inc.adoc", SourceFileLine: 7),
        ];

        Assert.Contains("inc.adoc:7: broken", doc.DiagnosticsSummary);
    }
}
