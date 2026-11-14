using Ascolotl.App.Services;
using Avalonia;

namespace Ascolotl.App;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Already running: hand over the files to open and stop. If the running
        // instance can't be reached (hung, closing), start normally instead.
        var files = args.Where(File.Exists).Select(Path.GetFullPath).ToList();
        if (!SingleInstance.Claim() && SingleInstance.Forward(files))
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
