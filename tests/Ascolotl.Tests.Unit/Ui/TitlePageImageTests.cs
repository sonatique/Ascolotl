using Ascolotl.App.Services;
using Avalonia.Headless.XUnit;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>A title-page SVG is drawn only when it links to nothing outside itself.</summary>
public sealed class TitlePageImageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ascolotl-logo-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Svg(string body)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="10" height="10">{body}</svg>""");
        return path;
    }

    [AvaloniaTheory]
    [InlineData("""<image href="https://attacker.example/b.png" width="10" height="10"/>""")]
    [InlineData("""<image xlink:href='\\attacker\share\b.png' width="10" height="10"/>""")]
    [InlineData("""<rect width="10" height="10" style="fill: url(file:///etc/passwd)"/>""")]
    public void AnSvgLinkingOut_IsNotDrawn(string body)
        => Assert.Null(PreviewTitlePage.LoadImage(Svg(body)));

    [AvaloniaFact]
    public void AnSvgWithInternalReferences_IsDrawn()
        => Assert.NotNull(PreviewTitlePage.LoadImage(Svg("""
            <defs><linearGradient id="g"><stop offset="0" stop-color="red"/></linearGradient></defs>
            <rect id="r" width="10" height="10" fill="url(#g)"/><use xlink:href="#r"/>
            <image href="data:image/png;base64,iVBORw0KGgo=" width="1" height="1"/>
            """)));
}
