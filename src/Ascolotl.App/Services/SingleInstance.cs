using System.Diagnostics;
using System.IO.Pipes;

namespace Ascolotl.App.Services;

/// <summary>
/// One Ascolotl per user session: a second launch (e.g. double-clicking an
/// .adoc file while Ascolotl runs) hands its file paths to the running
/// instance and exits, so two instances never share the session and
/// crash-recovery files.
/// </summary>
public static class SingleInstance
{
    private static readonly string Name =
        $"Ascolotl-{Environment.UserName}-{Process.GetCurrentProcess().SessionId}";

    // Held for the process lifetime: its existence marks the running instance.
    private static Mutex? _mutex;

    /// <summary>True when this process is the first instance.</summary>
    public static bool Claim()
    {
        _mutex = new Mutex(initiallyOwned: true, Name, out var createdNew);
        return createdNew;
    }

    /// <summary>Sends <paramref name="paths"/> to the running instance; false when it can't be reached.</summary>
    public static bool Forward(IEnumerable<string> paths)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(timeout: 3000);
            using var writer = new StreamWriter(pipe);
            foreach (var path in paths)
                writer.WriteLine(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Receives paths from later launches for the rest of the process lifetime.
    /// <paramref name="received"/> runs on a background thread, once per launch.
    /// </summary>
    public static async Task ListenAsync(Action<IReadOnlyList<string>> received)
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe);
                var paths = new List<string>();
                while (await reader.ReadLineAsync() is { } line)
                    if (line.Length > 0)
                        paths.Add(line);
                received(paths);
            }
            catch (Exception)
            {
                // A broken connection must not stop listening for the next launch.
                await Task.Delay(200);
            }
        }
    }
}
