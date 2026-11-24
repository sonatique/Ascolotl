using Avalonia.Headless.XUnit;
using Avalonia.Platform;

namespace Ascolotl.Tests.Unit.Ui;

public sealed class WindowIconTests
{
    /// <summary>The window's .ico decodes through Skia, as Linux loads window icons, and holds the
    /// small sizes Windows draws in the title bar and the taskbar.</summary>
    [AvaloniaFact]
    public void TheWindowIcon_DecodesEverywhere_AndHasTheTaskbarSizes()
    {
        using (var stream = AssetLoader.Open(new Uri("avares://Ascolotl/Resources/app.ico")))
        using (var bitmap = SkiaSharp.SKBitmap.Decode(stream))
            Assert.NotNull(bitmap);

        using var ico = new BinaryReader(AssetLoader.Open(new Uri("avares://Ascolotl/Resources/app.ico")));
        ico.ReadBytes(4);
        var sizes = Enumerable.Range(0, ico.ReadUInt16()).Select(_ => { var w = ico.ReadByte(); ico.ReadBytes(15); return w == 0 ? 256 : w; }).ToList();
        Assert.Subset(sizes.ToHashSet(), new HashSet<int> { 16, 24, 32, 48 });
    }
}
