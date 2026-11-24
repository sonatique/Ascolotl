using Ascolotl.Core.Resources;

namespace Ascolotl.Core.Models;

/// <summary>A key and what it does. <see cref="Keys"/> is written with Ctrl, as on Windows.</summary>
public sealed record Shortcut(string Action, string Keys);

/// <summary>A titled list of keys, for the keyboard shortcuts window.</summary>
public sealed record ShortcutGroup(string Title, IReadOnlyList<Shortcut> Rows)
{
    /// <summary>The commands' keys by category, as the palette lists them, then the keys of the
    /// source, the page, an open block and the Read view, which no command carries.</summary>
    public static IReadOnlyList<ShortcutGroup> For(IEnumerable<CommandEntry> commands)
    {
        var groups = commands.Where(c => c.Shortcut is not null).GroupBy(c => c.Category)
            .Select(g => new ShortcutGroup(g.Key, [.. g.Select(c => new Shortcut(c.Name, c.Shortcut!))]))
            .ToList();
        Shortcut palette = new(Strings.KeysCommandPalette, "Ctrl+Shift+P");
        var help = groups.FindIndex(g => g.Title == Strings.CommandCategoryHelp);
        if (help >= 0) groups[help] = groups[help] with { Rows = [palette, .. groups[help].Rows] };
        else groups.Add(new(Strings.CommandCategoryHelp, [palette]));

        groups.AddRange(
        [
            new(Strings.KeysGroupWriting,
            [
                new(Strings.FormatBold, "Ctrl+B"),
                new(Strings.FormatItalic, "Ctrl+I"),
                new(Strings.FormatCode, "Ctrl+Shift+C"),
                new(Strings.FormatLink, "Ctrl+K"),
                new(Strings.KeysHeadings, "Ctrl+Shift+1…6 / Ctrl+Shift+0"),
                new(Strings.KeysCompletion, "Ctrl+Space"),
                new(Strings.KeysUndoRedo, "Ctrl+Z / Ctrl+Y"),
            ]),
            new(Strings.KeysGroupPage,
            [
                new(Strings.KeysBlocks, "↑ / ↓"),
                new(Strings.KeysFirstLast, "Home / End"),
                new(Strings.KeysEditBlock, "Enter / F2"),
                new(Strings.KeysBlockMenu, "Shift+F10"),
                new(Strings.KeysSelectBlocks, "Shift+↑ / Shift+↓ / Ctrl+A"),
                new($"{Strings.MenuCopy} / {Strings.MenuCut} / {Strings.MenuPaste}", "Ctrl+C / Ctrl+X / Ctrl+V"),
                new($"{Strings.MenuMoveUp} / {Strings.MenuMoveDown}", "Ctrl+Shift+↑ / Ctrl+Shift+↓"),
                new($"{Strings.MenuDuplicateBlock} / {Strings.MenuDeleteBlock}", "Ctrl+Alt+D / Delete"),
                new(Strings.MenuNewParagraph, "Ctrl+Shift+N"),
                new(Strings.MenuTurnInto, "Ctrl+/"),
                new(Strings.FindOnPage, "Ctrl+F"),
                new(Strings.KeysLeavePage, "Tab / Shift+Tab"),
            ]),
            new(Strings.KeysGroupBlock,
            [
                new(Strings.KeysApplyCancel, "Ctrl+Enter / Esc"),
                new(Strings.KeysNextCell, "Tab / Shift+Tab"),
                new(Strings.KeysAddRow, "Ctrl+Enter"),
            ]),
            new(Strings.KeysGroupRead,
            [
                new(Strings.KeysReadEdit, "Enter / F2"),
                new($"{Strings.KeysSelectAll} / {Strings.MenuCopy}", "Ctrl+A / Ctrl+C"),
            ]),
        ]);
        return groups;
    }
}
