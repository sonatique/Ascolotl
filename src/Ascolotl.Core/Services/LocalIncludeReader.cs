using System.Text;
using AdocNet;

namespace Ascolotl.Core.Services;

/// <summary>
/// Reads <c>include::</c> targets for the preview and every export. Includes
/// may leave the document's folder (a shared <c>../copyright.adoc</c>, as with
/// Asciidoctor's CLI), but not the folder open in the app, else the document's
/// Git repository, else its own folder: a page in a cloned repository must not
/// pull <c>~/.ssh/id_rsa</c> into an exported site. Links can't lead out either.
/// Nor are includes ever read from another computer (Windows
/// would sign in to it over SMB and hand it the user's NTLM hash) or from a
/// device file (<c>/dev/zero</c> never ends), and never past
/// <see cref="MaxChars"/>.
/// </summary>
public sealed class LocalIncludeReader(string? baseDirectory, string? projectFolder = null) : IIncludeReader
{
    private readonly string? _root = IncludeRoot(baseDirectory, projectFolder);

    public const int MaxChars = 16 * 1024 * 1024;

    // One parse's budget: files that each include the next several times would
    // otherwise multiply into billions of reads (a new reader serves each parse).
    public const int MaxReads = 5_000;
    public const long MaxTotalChars = 64L * 1024 * 1024;
    private int _reads;
    private long _totalChars;

    /// <summary>Parse settings shared by the preview and all exports.</summary>
    public static ParseOptions ParseOptions(string? basePath, IReadOnlyDictionary<string, string>? attributes = null,
        string? projectFolder = null) => new()
    {
        BaseDirectory = basePath,
        IncludeMaxDepth = 10,
        AllowUriRead = false,
        // Safe mode would also block ../ includes; this reader does the guarding.
        SafeMode = SafeMode.Unsafe,
        Attributes = attributes,
        IncludeReader = new LocalIncludeReader(basePath, projectFolder),
    };

    // A blocked path "exists" so that Read's refusal reaches the diagnostics.
    public bool Exists(string path) => IsBlocked(path, baseDirectory) || OutsideRoot(path) || File.Exists(path);

    public string Read(string path)
    {
        if (IsBlocked(path, baseDirectory))
            throw new IOException("network and device paths are not read");
        if (OutsideRoot(path))
            throw new IOException("files outside the project, repository or document folder are not read");
        if (++_reads > MaxReads || _totalChars > MaxTotalChars)
            throw new IOException($"the document includes more than {MaxReads} files or {MaxTotalChars / (1024 * 1024)} MB in all");

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
        _totalChars += text.Length;
        return text.ToString();
    }

    /// <summary>
    /// The folder includes stay in: the one open in the app when it holds the document,
    /// else the document's Git repository, else the document's folder. Null for an
    /// unsaved document, whose includes are the user's own typing.
    /// </summary>
    internal static string? IncludeRoot(string? baseDirectory, string? projectFolder)
    {
        if (baseDirectory is null) return null;
        var dir = Path.GetFullPath(baseDirectory);
        if (projectFolder is not null && IsInside(dir, Path.GetFullPath(projectFolder)))
            return Path.GetFullPath(projectFolder);
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
            if (Path.Exists(Path.Combine(d.FullName, ".git")))
                return d.FullName;
        return dir;
    }

    private bool OutsideRoot(string path)
    {
        if (_root is null) return false;
        try
        {
            // By name, then as the system opens it: links, even chained or followed by "..", may lead out.
            return !IsInside(Path.GetFullPath(path), _root) || !IsInside(RealPath(path), RealPath(_root));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>The path with every link on the way resolved, one name at a time, as the system
    /// opens it: on Linux and macOS a ".." after a link goes up from the link's target.</summary>
    internal static string RealPath(string path)
    {
        var start = OperatingSystem.IsWindows() || Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
        if (OperatingSystem.IsWindows()) start = Path.GetFullPath(start); // Windows takes ".." by name
        var real = Path.GetPathRoot(start)!;
        var names = new Stack<string>();
        Push(start[real.Length..]);
        for (var links = 0; names.Count > 0;)
        {
            var name = names.Pop();
            if (name == ".") continue;
            if (name == "..")
            {
                real = Path.GetDirectoryName(real) ?? real;
                continue;
            }
            var next = Path.Combine(real, name);
            FileSystemInfo item = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (item.LinkTarget is not { } target)
            {
                real = next;
                continue;
            }
            if (++links > 40) throw new IOException("too many links");
            if (Path.IsPathRooted(target)) real = Path.GetPathRoot(target)!;
            Push(Path.IsPathRooted(target) ? target[real.Length..] : target);
        }
        return real;

        void Push(string rest)
        {
            foreach (var part in rest.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Reverse())
                names.Push(part);
        }
    }

    private static bool IsInside(string path, string folder)
    {
        var comparison = FilePaths.Comparison;
        var root = Path.TrimEndingDirectorySeparator(folder); // keeps C:\ and / whole
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(root, comparison) || path.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// True for a path on another computer (<c>\\server</c>, <c>//server</c>,
    /// including <c>\\?\</c>, <c>\\.\</c> and <c>\??\</c> device paths) unless the
    /// document itself lives on that same server, and for Unix device and kernel
    /// files. The path is made absolute first, so <c>..</c> cannot walk into them.
    /// </summary>
    public static bool IsBlocked(string path, string? baseDirectory)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
        // Both forms: the full one resolves .., the raw one keeps what GetFullPath
        // rewrites per OS (Unix collapses //server, Windows roots /dev on C:).
        return IsBlockedForm(path, baseDirectory) || IsBlockedForm(full, baseDirectory);
    }

    private static bool IsBlockedForm(string path, string? baseDirectory)
    {
        var p = path.Replace('\\', '/');
        // \??\ is the NT object namespace: \??\UNC\server reaches SMB like \\server.
        if (p.StartsWith("/dev/", StringComparison.Ordinal) || p.StartsWith("/proc/", StringComparison.Ordinal)
            || p.StartsWith("/sys/", StringComparison.Ordinal) || p.StartsWith("/??/", StringComparison.Ordinal))
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
