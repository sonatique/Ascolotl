using AdocNet.Extensions;
using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

/// <summary>
/// The diagram renderer the preview and the exports share, as the user set it: none (the default),
/// the local tools, or a Kroki server. Images are cached by diagram source in the user's local
/// application data, so a diagram is drawn once.
/// </summary>
public static class Diagrams
{
    public const string DefaultKrokiServer = "https://kroki.io";

    /// <summary>Turns diagram blocks into images; null while diagrams are off.</summary>
    public static DiagramPass? Pass { get; private set; }

    private static string CacheFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "Ascolotl", "diagrams");

    public static void Configure(DiagramMode mode, string? krokiServer = null) => Pass = mode switch
    {
        DiagramMode.LocalTools => new DiagramPass(LocalTools(), CacheFolder),
        DiagramMode.Kroki => new DiagramPass(
            new KrokiDiagramToolRunner(string.IsNullOrWhiteSpace(krokiServer) ? DefaultKrokiServer : krokiServer!.Trim()),
            CacheFolder),
        _ => null,
    };

    /// <summary>Uses <paramref name="pass"/> as is (tests).</summary>
    internal static void Use(DiagramPass? pass) => Pass = pass;

    // The usual command of each tool; PlantUML writes the PNG beside its input, under the same name.
    private static Dictionary<string, IDiagramToolRunner> LocalTools()
    {
        var graphviz = new LocalTool("dot", "-Tpng {input} -o {output}");
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["graphviz"] = graphviz,
            ["dot"] = graphviz,
            ["plantuml"] = new LocalTool("plantuml", "-tpng {input}"),
            ["mermaid"] = new LocalTool("mmdc", "-i {input} -o {output}"),
        };
    }

    /// <summary>
    /// A diagram program found on the PATH. Its availability is the file's presence: nothing is
    /// started to ask (not every tool answers <c>--version</c>).
    /// </summary>
    internal sealed class LocalTool(string command, string arguments) : IDiagramToolRunner
    {
        private readonly Lazy<string?> _path = new(() => Find(command));
        private ProcessDiagramToolRunner? _runner;

        public bool IsAvailable => _path.Value is not null;

        public string? Generate(string language, string source, string outputDirectory) =>
            _path.Value is { } path
                ? (_runner ??= new ProcessDiagramToolRunner(path, arguments)).Generate(language, source, outputDirectory)
                : null;

        /// <summary>The full path of <paramref name="name"/> on the PATH (with a PATHEXT extension on Windows), or null.</summary>
        internal static string? Find(string name)
        {
            var extensions = OperatingSystem.IsWindows()
                ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
                : [""];
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var extension in extensions)
                {
                    try
                    {
                        var candidate = Path.Combine(folder.Trim('"'), name + extension);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException)
                    {
                        // a malformed PATH entry
                    }
                }
            }
            return null;
        }
    }
}
