using Ascolotl.App.Views;
using Avalonia.Input;

namespace Ascolotl.Tests.Unit.Ui;

/// <summary>The keys, written with Ctrl, take Cmd on a Mac.</summary>
public sealed class CommandKeyTests
{
    private const KeyModifiers Mac = KeyModifiers.Meta;

    [Fact]
    public void OnAMac_CmdStandsForCtrl()
    {
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift),
            CommandKey.OnPlatform(new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift), Mac));
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, CommandKey.AsWritten(KeyModifiers.Meta | KeyModifiers.Shift, Mac));
        Assert.Equal(KeyModifiers.Meta, CommandKey.AsWritten(KeyModifiers.Control, Mac)); // Ctrl on a Mac is not the command key
        Assert.Equal("Cmd+Shift+P", CommandKey.Text("Ctrl+Shift+P", Mac));
        Assert.Equal("Ctrl+Tab", CommandKey.Text("Ctrl+Tab", Mac));
        Assert.Equal("Ctrl+Shift+Tab", CommandKey.Text("Ctrl+Shift+Tab", Mac));
        Assert.Equal("Ctrl+Space", CommandKey.Text("Ctrl+Space", Mac));
        Assert.Equal("Cmd+Alt+D / Delete", CommandKey.Text("Ctrl+Alt+D / Delete", Mac));
    }

    [Fact]
    public void ElsewhereCtrlStaysCtrl()
    {
        var ctrlS = new KeyGesture(Key.S, KeyModifiers.Control);
        Assert.Equal(ctrlS, CommandKey.OnPlatform(ctrlS, KeyModifiers.Control));
        Assert.Equal(KeyModifiers.Control, CommandKey.AsWritten(KeyModifiers.Control, KeyModifiers.Control));
        Assert.Equal("Ctrl+S", CommandKey.Text("Ctrl+S", KeyModifiers.Control));
    }

    [Theory]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, "ś", true, true)]   // AltGr+S on a Polish keyboard
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, null, true, false)] // Ctrl+Alt+S: types nothing
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, "\u0013", true, false)]
    [InlineData(KeyModifiers.Control, "s", true, false)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, "s", false, false)] // elsewhere AltGr is no Ctrl+Alt
    public void AltGrTypingACharacter_IsNoCtrlAltShortcut(KeyModifiers modifiers, string? symbol, bool windows, bool typed)
        => Assert.Equal(typed, CommandKey.TypesWithAltGr(new KeyEventArgs { Key = Key.S, KeyModifiers = modifiers, KeySymbol = symbol }, windows));
}
