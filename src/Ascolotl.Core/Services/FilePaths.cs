namespace Ascolotl.Core.Services;

/// <summary>How file paths compare: with case on Linux, where Readme.adoc and README.adoc are two
/// files; case aside on Windows and macOS, whose usual file systems ignore it.</summary>
public static class FilePaths
{
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static bool Same(string? a, string? b) => string.Equals(a, b, Comparison);
}
