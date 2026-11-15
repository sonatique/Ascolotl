using Ascolotl.App.Services;
using Avalonia;

namespace Ascolotl.App;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Already running: hand over the files to open and stop. The running
        // instance may be busy for a moment (just starting, a dialog open), so
        // keep trying for about 10 s; only if it still can't be reached (hung,
        // closing) start normally instead.
        var files = args.Where(File.Exists).Select(Path.GetFullPath).ToList();
        if (!SingleInstance.Claim())
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                if (SingleInstance.Forward(files))
                    return;
                Thread.Sleep(500);
            }
            while (DateTime.UtcNow < deadline);
            App.ForwardFailed = true;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
