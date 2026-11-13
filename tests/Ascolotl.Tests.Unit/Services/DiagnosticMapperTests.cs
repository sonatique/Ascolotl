using AdocNet;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Services;

public sealed class DiagnosticMapperTests
{
    private static Diagnostic Warn(int line, int col = 1, string? filePath = null) =>
        new(AdocNet.DiagnosticSeverity.Warning, "boom",
            new SourceRange(new SourcePosition(line, col), new SourcePosition(line, col)))
        { FilePath = filePath! };

    // AdocNet 1.0.17 (Adoc.Net#67) reports ranges in original source
    // coordinates, so a main-document diagnostic maps straight through.
    [Fact]
    public void Map_MainDocumentDiagnostic_UsesSourceLineDirectly()
    {
        var mapped = DiagnosticMapper.Map([Warn(67)]);

        Assert.Single(mapped);
        Assert.Equal(66, mapped[0].Line); // 0-based editor line, no translation
        Assert.False(mapped[0].IsUnanchored);
    }

    // A diagnostic inside an include carries that file's path; its line is
    // relative to that file, not the main editor, so it isn't squiggled — but
    // the file name + in-file line are kept for the status-bar summary.
    [Fact]
    public void Map_DiagnosticInsideAnInclude_IsUnanchoredButKeepsFileLocation()
    {
        var mapped = DiagnosticMapper.Map([Warn(7, filePath: @"C:\docs\inc.adoc")]);

        Assert.Single(mapped);
        Assert.True(mapped[0].IsUnanchored);
        Assert.Equal(-1, mapped[0].Line);
        Assert.Equal("inc.adoc", mapped[0].SourceFile); // file name only
        Assert.Equal(7, mapped[0].SourceFileLine);
    }

    [Fact]
    public void Map_RangelessDiagnostic_IsUnanchored()
    {
        var none = new Diagnostic(AdocNet.DiagnosticSeverity.Error, "no range", default);

        var mapped = DiagnosticMapper.Map([none]);

        Assert.Single(mapped);
        Assert.True(mapped[0].IsUnanchored);
    }

    [Fact]
    public void Map_NullDiagnostics_YieldsEmpty()
    {
        Assert.Empty(DiagnosticMapper.Map(null));
    }
}
