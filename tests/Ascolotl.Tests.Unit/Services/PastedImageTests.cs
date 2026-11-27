namespace Ascolotl.Tests.Unit.Services;

using Ascolotl.App.Services;

public class PastedImageTests
{
    private static readonly string Doc = Path.Combine(Path.GetTempPath(), "doc-folder");

    [Theory]
    [InlineData("= Doc\n", false, "images", "images/")]
    [InlineData("= Doc\n:imagesdir: img\n", false, "img", "")]
    [InlineData("= Doc\r\n:imagesdir: pics/2026 \r\n", false, "pics/2026", "")]
    [InlineData(":imagesdir: .\n", false, ".", "")]
    [InlineData(":imagesdir: img\n", true, "images", "images/")] // Markdown has no imagesdir
    public void Place_IsTheImagesDirInsideTheDocumentsFolder(string text, bool markdown, string folder, string prefix)
    {
        var place = PastedImage.Place(Doc, text, markdown);
        Assert.NotNull(place);
        Assert.Equal(Path.GetFullPath(Path.Combine(Doc, folder)), place.Value.Folder);
        Assert.Equal(prefix, place.Value.Prefix);
    }

    [Fact]
    public void Place_ForADocumentAtTheDrivesRoot_IsInsideIt()
    {
        var drive = Path.GetPathRoot(Path.GetTempPath())!; // C:\ or /
        var place = PastedImage.Place(drive, "= Doc\n", markdown: false);
        Assert.NotNull(place);
        Assert.Equal(Path.Combine(drive, "images"), place.Value.Folder);
    }

    [Theory]
    [InlineData(":imagesdir: ../elsewhere\n")]
    [InlineData(":imagesdir: ../doc-folder-other\n")] // a name that only starts like the folder
    public void Place_OutsideTheDocumentsFolder_IsNone(string text)
        => Assert.Null(PastedImage.Place(Doc, text, markdown: false));

    [Fact]
    public void Place_AbsoluteImagesDir_IsNone()
        => Assert.Null(PastedImage.Place(Doc, $":imagesdir: {Path.GetTempPath()}\n", markdown: false));

    [Theory]
    [InlineData(false, true, "image::images/a.png[]")]
    [InlineData(false, false, "image:images/a.png[]")]
    [InlineData(true, true, "![](images/a.png)")]
    public void Reference_IsABlockOnItsOwnLine_ElseInline(bool markdown, bool ownLine, string expected)
        => Assert.Equal(expected, PastedImage.Reference("images/a.png", markdown, ownLine));
}
