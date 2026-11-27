using System.Text.RegularExpressions;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Ascolotl.Core.Resources;

namespace Ascolotl.App.Services;

/// <summary>An image pasted into a document: saved as a PNG beside it, and referred to from it.</summary>
internal static partial class PastedImage
{
    /// <summary>The clipboard holds an image and no text: with text, the text is what's pasted.</summary>
    public static async Task<bool> OnlyImageAsync(IClipboard clipboard)
    {
        var formats = await clipboard.GetDataFormatsAsync();
        return formats.Contains(DataFormat.Bitmap) && !formats.Contains(DataFormat.Text);
    }

    /// <summary>
    /// Saves the clipboard's image in the document's <c>:imagesdir:</c>, or else in <c>images/</c>,
    /// both inside <paramref name="documentFolder"/>, under a new name. Returns the image as the
    /// document refers to it, or null when it wasn't saved (the reason given to <paramref name="status"/>).
    /// </summary>
    public static async Task<string?> SaveAsync(IClipboard clipboard, string documentFolder, string documentText,
        bool markdown, Action<string> status)
    {
        if (Place(documentFolder, documentText, markdown) is not var (folder, prefix))
        {
            status(Strings.StatusImagesDirOutside);
            return null;
        }
        using var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is null) return null;
        try
        {
            Directory.CreateDirectory(folder);
            var name = $"pasted-{DateTime.Now:yyyyMMdd-HHmmss}";
            var file = name + ".png";
            for (var n = 2; File.Exists(Path.Combine(folder, file)); n++) file = $"{name}-{n}.png";
            bitmap.Save(Path.Combine(folder, file));
            return prefix + file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status(string.Format(Strings.StatusImagePasteFailed, ex.Message));
            return null;
        }
    }

    /// <summary>Where a pasted image goes: the document's <c>:imagesdir:</c> (AsciiDoc), else <c>images/</c>,
    /// and what the document puts before its name. Null when that folder is outside the document's:
    /// a document's own attribute never picks where a file is written outside it.</summary>
    public static (string Folder, string Prefix)? Place(string documentFolder, string documentText, bool markdown)
    {
        var imagesDir = markdown ? null : ImagesDir().Match(documentText) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
        var root = Path.GetFullPath(documentFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folder = Path.GetFullPath(Path.Combine(root, imagesDir ?? "images"));
        var names = Ascolotl.Core.Services.FilePaths.Comparison;
        if (!(folder + Path.DirectorySeparatorChar).StartsWith(root + Path.DirectorySeparatorChar, names))
            return null;
        return (folder, imagesDir is null ? "images/" : "");
    }

    /// <summary>The macro or Markdown that shows <paramref name="target"/>: a block of its own on
    /// an empty line, else inline.</summary>
    public static string Reference(string target, bool markdown, bool ownLine) =>
        markdown ? $"![]({target})" : ownLine ? $"image::{target}[]" : $"image:{target}[]";

    // ponytail: the value as written; one built from {attributes} isn't resolved.
    [GeneratedRegex(@"^:imagesdir:[ \t]*(\S.*?)[ \t\r]*$", RegexOptions.Multiline)]
    private static partial Regex ImagesDir();
}
