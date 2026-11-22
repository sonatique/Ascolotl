using Ascolotl.App.Views;
using Ascolotl.Core.Resources;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace Ascolotl.Tests.Unit.Ui;

public sealed class RecoveryDialogTests
{
    [AvaloniaFact]
    public void ClosingThePrompt_KeepsTheWork_OnlyDiscardDropsIt()
    {
        var closed = new RecoveryDialog(2);
        closed.Show();
        closed.Close(); // the title-bar X, Alt+F4
        Assert.True(closed.ShouldRecover);

        var discarded = new RecoveryDialog(2);
        discarded.Show();
        discarded.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, Strings.Discard))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(discarded.ShouldRecover);
    }
}
