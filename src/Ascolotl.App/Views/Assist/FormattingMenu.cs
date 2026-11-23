using Avalonia.Controls;
using AvaloniaEdit;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Resources;
using static Ascolotl.Core.Editing.FormatCommand;

namespace Ascolotl.App.Views.Assist;

/// <summary>
/// The source editors' right-click menu: clipboard, then formatting for the
/// selection, or, without one, what to insert at the caret. Only the commands
/// the document's format has are offered.
/// </summary>
internal static class FormattingMenu
{
    public static ContextMenu Open(CompletionController assist, TextEditor editor)
    {
        var selection = editor.SelectionLength > 0;
        var items = assist.Spelling.MenuItems(); // a misspelled word's suggestions first

        MenuItem Do(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        MenuItem Format(FormatCommand command, string header) => Do(header, () => assist.Format(command));

        void AddFormat(FormatCommand command, string header)
        {
            if (TextFormatting.Supports(command, assist.Markdown))
                items.Add(Format(command, header));
        }

        if (selection)
        {
            items.Add(Do(Strings.MenuCut, editor.Cut));
            items.Add(Do(Strings.MenuCopy, editor.Copy));
        }
        items.Add(Do(Strings.MenuPaste, editor.Paste));
        items.Add(new Separator());

        if (assist.InTable)
        {
            MenuItem Table(TableOp op, string header) => Do(header, () => assist.Table(op));
            items.Add(new MenuItem
            {
                Header = Strings.MenuRow,
                ItemsSource = new Control[] { Table(TableOp.RowAbove, Strings.MenuInsertAbove),
                    Table(TableOp.RowBelow, Strings.MenuInsertBelow), Table(TableOp.DeleteRow, Strings.MenuDeleteRow),
                    new Separator(), Table(TableOp.MoveRowUp, Strings.MenuMoveUp), Table(TableOp.MoveRowDown, Strings.MenuMoveDown) },
            });
            items.Add(new MenuItem
            {
                Header = Strings.MenuColumn,
                ItemsSource = new Control[] { Table(TableOp.ColumnLeft, Strings.MenuInsertLeft),
                    Table(TableOp.ColumnRight, Strings.MenuInsertRight), Table(TableOp.DeleteColumn, Strings.MenuDeleteColumn),
                    new Separator(), Table(TableOp.MoveColumnLeft, Strings.MenuMoveLeft), Table(TableOp.MoveColumnRight, Strings.MenuMoveRight),
                    new Separator(), Table(TableOp.SortAscending, Strings.MenuSortAscending), Table(TableOp.SortDescending, Strings.MenuSortDescending) },
            });
            if (TableEditing.Supports(TableOp.MergeRight, assist.Markdown))
                items.Add(new MenuItem
                {
                    Header = Strings.MenuMerge,
                    ItemsSource = new[] { Table(TableOp.MergeRight, Strings.MenuMergeRight),
                        Table(TableOp.MergeDown, Strings.MenuMergeDown), Table(TableOp.SplitCell, Strings.MenuSplitCell) },
                });
            items.Add(new Separator());
        }

        AddFormat(Bold, Strings.FormatBold);
        AddFormat(Italic, Strings.FormatItalic);
        AddFormat(Code, Strings.FormatCode);
        AddFormat(Link, Strings.FormatLink);
        items.Add(new Separator());

        items.Add(new MenuItem
        {
            Header = Strings.FormatHeadingMenu,
            ItemsSource = new[]
            {
                Format(Heading1, string.Format(Strings.FormatHeading, 1)),
                Format(Heading2, string.Format(Strings.FormatHeading, 2)),
                Format(Heading3, string.Format(Strings.FormatHeading, 3)),
            },
        });
        AddFormat(BulletedList, Strings.InsertBulletedList);
        AddFormat(NumberedList, Strings.InsertNumberedList);
        AddFormat(Quote, Strings.FormatQuote);

        if (selection)
        {
            AddFormat(CodeBlock, Strings.InsertCodeBlock);
            AddFormat(Note, Strings.InsertNote);
        }
        else
        {
            items.Add(new Separator());
            var insert = new List<Control> { Format(FormatCommand.Image, Strings.FormatImage), Format(Table, Strings.InsertTable),
                Format(CodeBlock, Strings.InsertCodeBlock) };
            if (TextFormatting.Supports(Note, assist.Markdown))
                insert.Add(Format(Note, Strings.InsertNote));
            insert.Add(Format(HorizontalRule, Strings.InsertHorizontalRule));
            items.Add(new MenuItem { Header = Strings.FormatInsertMenu, ItemsSource = insert });
        }

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(editor.TextArea);
        return menu;
    }
}
