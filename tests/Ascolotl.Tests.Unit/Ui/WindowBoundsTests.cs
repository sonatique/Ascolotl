using Ascolotl.App;
using Avalonia;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>The window comes back where it was, but never off-screen.</summary>
public sealed class WindowBoundsTests
{
    private static readonly (PixelRect, double)[] Laptop150 = [(new PixelRect(0, 0, 1920, 1032), 1.5)];

    [Fact]
    public void FirstStart_OnA150PercentLaptop_FitsTheScreen()
    {
        // The defaults: 1280 x 800 DIPs at (100, 100) would end at pixel 2020 x 1300.
        var fit = App.App.FitToScreen(new PixelPoint(100, 100), 1280, 800, Laptop150)!.Value;

        Assert.Equal(new PixelPoint(0, 0), fit.Position);
        Assert.Equal(1280, fit.Width);
        Assert.Equal(688, fit.Height);
    }

    [Fact]
    public void AWindowThatFits_StaysAsSaved()
    {
        var fit = App.App.FitToScreen(new PixelPoint(300, 200), 1000, 600, [(new PixelRect(0, 0, 2560, 1400), 1.0)])!.Value;

        Assert.Equal((new PixelPoint(300, 200), 1000d, 600d), fit);
    }

    [Fact]
    public void OnTheSecondScreen_StaysOnIt_PulledInside()
    {
        (PixelRect, double)[] two = [(new PixelRect(0, 0, 1920, 1040), 1.0), (new PixelRect(1920, 0, 2560, 1400), 1.0)];

        var fit = App.App.FitToScreen(new PixelPoint(3900, 100), 1200, 800, two)!.Value;

        Assert.Equal(new PixelPoint(4480 - 1200, 100), fit.Position);
    }

    [Theory]
    [InlineData(-32000, -32000)] // closed while minimized
    [InlineData(2500, 100)] // its monitor was unplugged
    [InlineData(100, -50)] // title bar above the screen
    public void OffEveryScreen_IsNull_SoTheWindowCenters(int x, int y)
        => Assert.Null(App.App.FitToScreen(new PixelPoint(x, y), 1000, 600, Laptop150));
}
