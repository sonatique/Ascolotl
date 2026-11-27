using Ascolotl.Core.Resources;
using System.Text;

namespace Ascolotl.Core.Services;

/// <summary>A file's decoded text and the encoding to write it back with.</summary>
public sealed record FileText(string Text, Encoding Encoding);

/// <summary>
/// Reads text files keeping their encoding (BOM included) and writes them
/// atomically, so a save never truncates the original before the new text is
/// on disk and never turns undecodable bytes into U+FFFD.
/// </summary>
public static class TextFile
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    static TextFile() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Legacy 8-bit files (not valid UTF-8) are read as Windows-1252, which decodes every byte.</summary>
    public static Encoding Ansi => Encoding.GetEncoding(1252);

    public static FileText Read(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>A text's line end, "\r\n" or "\n": its first one's, as most editors take it; null
    /// without any.</summary>
    public static string? NewLineOf(string text) =>
        text.IndexOf('\n') is var i and >= 0 ? i > 0 && text[i - 1] == '\r' ? "\r\n" : "\n" : null;

    /// <summary>The text with every line end written as <paramref name="newLine"/>; a lone CR stays.</summary>
    public static string WithLineEnds(string text, string newLine)
    {
        var lf = text.Replace("\r\n", "\n");
        return newLine == "\n" ? lf : lf.Replace("\n", newLine);
    }

    public static async Task<FileText> ReadAsync(string path) => Decode(await File.ReadAllBytesAsync(path));

    public static FileText Decode(byte[] bytes)
    {
        try
        {
            // UTF-32 LE before UTF-16 LE: its BOM starts with the UTF-16 LE one.
            foreach (var encoding in new Encoding[] { Encoding.UTF8, Encoding.UTF32, Encoding.Unicode, Encoding.BigEndianUnicode })
            {
                var bom = encoding.Preamble;
                if (!bytes.AsSpan().StartsWith(bom))
                    continue;
                var strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ReplacementFallback, DecoderFallback.ExceptionFallback);
                return new FileText(strict.GetString(bytes, bom.Length, bytes.Length - bom.Length), encoding);
            }

            return new FileText(StrictUtf8.GetString(bytes), Utf8NoBom);
        }
        catch (DecoderFallbackException)
        {
            // Not valid in any Unicode encoding: 1252 maps every byte, so a save writes them back unchanged.
            return new FileText(Ansi.GetString(bytes), Ansi);
        }
    }

    /// <summary>
    /// <paramref name="preferred"/> when it can represent <paramref name="text"/>,
    /// else UTF-8: a legacy encoding must not turn new characters into '?'.
    /// </summary>
    public static Encoding EncodingFor(string text, Encoding? preferred)
    {
        if (preferred is null)
            return Utf8NoBom;
        if (preferred is UTF8Encoding or UnicodeEncoding or UTF32Encoding)
            return preferred;

        var strict = Encoding.GetEncoding(preferred.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        try
        {
            strict.GetByteCount(text);
            return preferred;
        }
        catch (EncoderFallbackException)
        {
            return Utf8NoBom;
        }
    }

    /// <summary>When the file was last written (UTC), through symlinks: a link's own time stays
    /// when its target changes.</summary>
    public static DateTime WriteTime(string path)
    {
        var info = new FileInfo(path);
        return (info.ResolveLinkTarget(returnFinalTarget: true) ?? info).LastWriteTimeUtc;
    }

    /// <summary>
    /// Writes to a temp file next to <paramref name="path"/>, then swaps it in, so a
    /// failed write never leaves a truncated file. Writes through symlinks.
    /// </summary>
    public static async Task WriteAsync(string path, string text, Encoding encoding)
    {
        path = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        var exists = File.Exists(path);
        // A rename replaces even a read-only file on Linux and macOS; Windows refuses, and so do we.
        if (exists && !OperatingSystem.IsWindows())
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        (long Length, DateTime Written)? original = null;
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(stream, encoding))
            {
                await writer.WriteAsync(text);
                await writer.FlushAsync();
                stream.Flush(flushToDisk: true); // on disk before it replaces the original
            }
            if (!exists)
            {
                File.Move(temp, path);
                return;
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
            var info = new FileInfo(path);
            original = (info.Length, info.LastWriteTimeUtc); // read now: FileInfo reads lazily
            try
            {
                // Replace keeps the original's attributes and Windows permissions.
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            catch (IOException)
            {
                // Some drives and sharing modes refuse a swap but allow writing in place.
                File.Copy(temp, path, overwrite: true);
                File.Delete(temp);
            }
        }
        catch (Exception ex) when (original is not null && File.Exists(temp) && !Unchanged(path, original.Value))
        {
            // The swap failed midway: the file may be gone or cut short, so the temp file is the only full copy.
            throw new IOException(string.Format(Strings.SaveTextKeptIn, temp), ex);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>The file still has the length and time it had before the swap.</summary>
    private static bool Unchanged(string path, (long Length, DateTime Written) before)
    {
        var now = new FileInfo(path);
        return now.Exists && now.Length == before.Length && now.LastWriteTimeUtc == before.Written;
    }
}
