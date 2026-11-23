using Avalonia.Data.Converters;
using Avalonia.Input;
using AvaloniaEdit;

namespace Ascolotl.App.Views;

/// <summary>
/// The platform's command key: Cmd on a Mac, Ctrl elsewhere, as the editors take it for copy and paste.
/// The app's shortcuts are written with Ctrl and mean this key (Ctrl+Tab and Ctrl+Space stay Ctrl).
/// </summary>
public static class CommandKey
{
    public static KeyModifiers Modifier => ApplicationCommands.Copy.Gesture?.KeyModifiers ?? KeyModifiers.Control;

    /// <summary>Modifiers as the shortcuts are written: on a Mac, Cmd reads as Ctrl and Ctrl as Cmd.</summary>
    public static KeyModifiers AsWritten(KeyModifiers pressed, KeyModifiers? command = null)
    {
        var either = pressed & (KeyModifiers.Control | KeyModifiers.Meta);
        return (command ?? Modifier) == KeyModifiers.Meta && either is KeyModifiers.Control or KeyModifiers.Meta
            ? pressed ^ (KeyModifiers.Control | KeyModifiers.Meta)
            : pressed;
    }

    /// <summary>A gesture written with Ctrl, as this platform's: Cmd on a Mac.</summary>
    public static KeyGesture OnPlatform(KeyGesture written, KeyModifiers? command = null) =>
        new(written.Key, AsWritten(written.KeyModifiers, command));

    /// <summary>A shortcut's text written with Ctrl, as this platform's: "Cmd+S" on a Mac.</summary>
    public static string? Text(string? written, KeyModifiers? command = null) =>
        (command ?? Modifier) == KeyModifiers.Meta && written is not null && !written.StartsWith("Ctrl+Tab")
            ? written.Replace("Ctrl+", "Cmd+")
            : written;

    public static readonly IValueConverter TextConverter = new FuncValueConverter<string?, string?>(s => Text(s));
}
