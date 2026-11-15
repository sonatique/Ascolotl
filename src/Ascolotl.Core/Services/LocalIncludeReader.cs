using System.Text;
using AdocNet;

namespace Ascolotl.Core.Services;

/// <summary>
/// Reads <c>include::</c> targets for the preview and every export. Includes
/// may leave the document's folder (a shared <c>../copyright.adoc</c>, as with
/// Asciidoctor's CLI), but are never read from another computer (Windows
/// would sign in to it over SMB and hand it the user's NTLM hash) or from a
/// device file (<c>/dev/zero</c> never ends), and never past
/// <see cref="MaxChars"/>.
/// </summary>
public sealed class LocalIncludeReader(string? baseDirectory) : IIncludeReader
{
    public const int MaxChars = 16 * 1024 * 1024;

    /// <summary>Parse settings shared by the preview and all exports.</summary>
    public static ParseOptions ParseOptions(string? basePath, IReadOnlyDictionary<string, string>? attributes = null) => new()
    {
        BaseDirectory = basePath,
        IncludeMaxDepth = 10,
        AllowUriRead = false,
        // Safe mode would also block ../ includes; this reader does the guarding.
        SafeMode = SafeMode.Unsafe,
        Attributes = attributes,
        IncludeReader = new LocalIncludeReader(basePath),
    };

    // A blocked path "exists" so that Read's refusal reaches the diagnostics.
    public bool Exists(string path) => IsBlocked(path, baseDirectory) || File.Exists(path);

    public string Read(string path)
    {
        if (IsBlocked(path, baseDirectory))
            throw new IOException("network and device paths are not read");

        using var reader = new StreamReader(path);
        var text = new StringBuilder();
        var buffer = new char[81920];
        int n;
        while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (text.Length + n > MaxChars)
                throw new IOException($"larger than {MaxChars / (1024 * 1024)} MB");
            text.Append(buffer, 0, n);
        }
        return text.ToString();
    }

    /// <summary>
    /// True for a path on another computer (<c>\\server</c>, <c>//server</c>,
    /// including <c>\\?\</c> and <c>\\.\</c> device paths) unless the document
    /// itself lives on that same server, and for Unix device and kernel files.
    /// </summary>
    public static bool IsBlocked(string path, string? baseDirectory)
    {
        var p = path.Replace('\\', '/');
        if (p.StartsWith("/dev/", StringComparison.Ordinal) || p.StartsWith("/proc/", StringComparison.Ordinal)
            || p.StartsWith("/sys/", StringComparison.Ordinal))
            return true;
        if (!p.StartsWith("//", StringComparison.Ordinal))
            return false;
        var server = ServerPrefix(p);
        return server is "//?/" or "//./"
            || baseDirectory is null
            || !string.Equals(server, ServerPrefix(baseDirectory.Replace('\\', '/')), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>//server/</c> for a network path, else null.</summary>
    private static string? ServerPrefix(string p)
    {
        if (!p.StartsWith("//", StringComparison.Ordinal))
            return null;
        var end = p.IndexOf('/', 2);
        return end < 0 ? p + "/" : p[..(end + 1)];
    }
}
