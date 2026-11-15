namespace Ascolotl.Tests.Unit.Models;

using Ascolotl.Core.Models;

/// <summary>
/// Tests for <see cref="ImportSummary"/> — the status line describing a
/// Word import's fidelity and contents.
/// </summary>
public class ImportSummaryTests
{
    private static ImportOutcome Ok(
        double fidelity = 1.0, int images = 0, int tables = 0, params string[] issues) =>
        new()
        {
            IsSuccess = true,
            OutputPath = @"C:\docs\guide.adoc",
            Fidelity = fidelity,
            Images = images,
            Tables = tables,
            Issues = issues,
        };

    [Fact]
    public void Failure_reports_the_error()
    {
        var text = ImportSummary.Describe(ImportOutcome.Failure("Not a readable Word document: bad zip"));
        Assert.Equal("Not a readable Word document: bad zip", text);
    }

    [Fact]
    public void Perfect_import_says_fully_mapped()
    {
        Assert.Equal("Imported guide.adoc — fully mapped", ImportSummary.Describe(Ok()));
    }

    [Fact]
    public void Partial_fidelity_is_reported_as_a_percentage()
    {
        Assert.Contains("87% mapped", ImportSummary.Describe(Ok(fidelity: 0.876)));
    }

    [Fact]
    public void Near_perfect_fidelity_does_not_round_up_to_fully_mapped()
    {
        // 99.7% must not read as "fully mapped" — that would hide real loss.
        var text = ImportSummary.Describe(Ok(fidelity: 0.997));
        Assert.Contains("99% mapped", text);
        Assert.DoesNotContain("fully mapped", text);
    }

    [Fact]
    public void Counts_are_singular_or_plural()
    {
        Assert.Contains("1 image", ImportSummary.Describe(Ok(images: 1)));
        Assert.Contains("3 images", ImportSummary.Describe(Ok(images: 3)));
        Assert.Contains("1 table", ImportSummary.Describe(Ok(tables: 1)));
        Assert.Contains("2 tables", ImportSummary.Describe(Ok(tables: 2)));
    }

    [Fact]
    public void Zero_counts_are_omitted()
    {
        var text = ImportSummary.Describe(Ok());
        Assert.DoesNotContain("image", text);
        Assert.DoesNotContain("table", text);
        Assert.DoesNotContain("note", text);
    }

    [Fact]
    public void Issues_are_counted_as_notes()
    {
        Assert.Contains("2 notes", ImportSummary.Describe(Ok(issues: ["a", "b"])));
    }

    [Fact]
    public void Everything_together_reads_as_one_line()
    {
        var text = ImportSummary.Describe(Ok(fidelity: 0.9, images: 2, tables: 1, issues: ["x"]));
        Assert.Equal("Imported guide.adoc — 90% mapped, 2 images, 1 table, 1 note", text);
    }
}
