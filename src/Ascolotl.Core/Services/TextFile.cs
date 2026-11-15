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

    /// <summary>
    /// Writes to a temp file next to <paramref name="path"/>, then swaps it in, so a
    /// failed write never leaves a truncated file. Writes through symlinks.
    /// </summary>
    public static async Task WriteAsync(string path, string text, Encoding encoding)
    {
        path = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        var exists = File.Exists(path);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, text, encoding);
            if (!exists)
            {
                File.Move(temp, path);
                return;
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
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
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }
}
