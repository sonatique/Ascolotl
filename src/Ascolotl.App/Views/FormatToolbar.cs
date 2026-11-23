using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Ascolotl.Core.Editing;
using Ascolotl.Core.Resources;
using static Ascolotl.Core.Editing.FormatCommand;

namespace Ascolotl.App.Views;

/// <summary>
/// One slim row of formatting buttons. The buttons never take the keyboard, so
/// the editor keeps its selection and an open in-place edit stays open; the
/// window decides which editor <see cref="FormatRequested"/> applies to.
/// </summary>
public sealed class FormatToolbar : Border
{
    public static readonly StyledProperty<bool> MarkdownProperty =
        AvaloniaProperty.Register<FormatToolbar, bool>(nameof(Markdown));

    /// <summary>Hides the commands Markdown doesn't have.</summary>
    public bool Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public event Action<FormatCommand>? FormatRequested;

    public static readonly StyledProperty<bool> EditOpenProperty =
        AvaloniaProperty.Register<FormatToolbar, bool>(nameof(EditOpen));

    /// <summary>An in-place edit is open: show its Apply and Cancel.</summary>
    public bool EditOpen
    {
        get => GetValue(EditOpenProperty);
        set => SetValue(EditOpenProperty, value);
    }

    /// <summary>Apply (true) or Cancel (false) pressed.</summary>
    public event Action<bool>? EditActionRequested;

    public static readonly StyledProperty<bool> InTableProperty =
        AvaloniaProperty.Register<FormatToolbar, bool>(nameof(InTable));

    /// <summary>The caret is in a table: show the row and column tools.</summary>
    public bool InTable
    {
        get => GetValue(InTableProperty);
        set => SetValue(InTableProperty, value);
    }

    public event Action<TableOp>? TableRequested;

    private readonly List<Control> _tableTools = [];

    private static readonly (TableOp Op, string Icon)[] TableTools =
    [
        (TableOp.RowAbove, "M3.5 12.5 H20.5 V19.5 H3.5 Z M3.5 16 H20.5 M12 3.5 V9.5 M9 6.5 H15"),
        (TableOp.RowBelow, "M3.5 4.5 H20.5 V11.5 H3.5 Z M3.5 8 H20.5 M12 14.5 V20.5 M9 17.5 H15"),
        (TableOp.ColumnLeft, "M11.5 3.5 H19.5 V20.5 H11.5 Z M15.5 3.5 V20.5 M6.5 9 V15 M3.5 12 H9.5"),
        (TableOp.ColumnRight, "M4.5 3.5 H12.5 V20.5 H4.5 Z M8.5 3.5 V20.5 M17.5 9 V15 M14.5 12 H20.5"),
        (TableOp.DeleteRow, "M3.5 5.5 H20.5 V12.5 H3.5 Z M8.5 17.5 H15.5"),
        (TableOp.DeleteColumn, "M4.5 3.5 H11.5 V20.5 H4.5 Z M15.5 12 H20.5"),
    ];

    private readonly StackPanel _editActions;

    private readonly Dictionary<FormatCommand, Button> _buttons = [];

    // Outline icons on a 24-unit grid, drawn at 16 px.
    private static readonly (FormatCommand Command, string Icon)[][] Groups =
    [
        [
            (Bold, "M7 5 H12.5 A3.5 3.5 0 0 1 12.5 12 H7 Z M7 12 H13.5 A3.5 3.5 0 0 1 13.5 19 H7 Z"),
            (Italic, "M10 5 H17 M7 19 H14 M13.5 5 L10.5 19"),
            (Code, "M8.5 7 L3.5 12 L8.5 17 M15.5 7 L20.5 12 L15.5 17"),
            (Link, "M10 14 L14 10 M8.5 11.5 L6.5 13.5 A3.5 3.5 0 0 0 11.5 18.5 L13.5 16.5 M10.5 7.5 L12.5 5.5 A3.5 3.5 0 0 1 17.5 10.5 L15.5 12.5"),
        ],
        [
            (Heading1, "M3.5 6 V18 M11.5 6 V18 M3.5 12 H11.5 M15.5 9.8 L18 8 V18"),
            (Heading2, "M3.5 6 V18 M11.5 6 V18 M3.5 12 H11.5 M15 10 C15 7.5 20.5 7.5 20.5 10.2 C20.5 12.8 15 14.5 15 18 H20.5"),
            (Heading3, "M3.5 6 V18 M11.5 6 V18 M3.5 12 H11.5 M15 8 H20.3 L17.3 11.8 C19.6 11.6 20.8 13 20.6 14.8 C20.4 17.8 16.4 18.6 15 17"),
        ],
        [
            (BulletedList, "M9.5 7 H20 M9.5 12 H20 M9.5 17 H20 M4 7 A1 1 0 1 0 6 7 A1 1 0 1 0 4 7 M4 12 A1 1 0 1 0 6 12 A1 1 0 1 0 4 12 M4 17 A1 1 0 1 0 6 17 A1 1 0 1 0 4 17"),
            (NumberedList, "M10 7 H20 M10 12 H20 M10 17.5 H20 M4 5.8 L5.6 4.8 V9.2 M3.8 11.2 C3.8 9.6 7 9.6 7 11.3 C7 12.6 3.8 13.4 3.8 14.4 H7 M3.8 15.6 H6.9 L5.2 17.4 C6.6 17.3 7.2 18.1 7 19 C6.8 20.4 4.6 20.6 3.8 19.8"),
            (Quote, "M5 5.5 V18.5 M9.5 7.5 H19 M9.5 12 H19 M9.5 16.5 H16"),
            (CodeBlock, "M3.5 4.5 H20.5 V19.5 H3.5 Z M10 9 L7.5 12 L10 15 M14 9 L16.5 12 L14 15"),
        ],
        [
            (FormatCommand.Image, "M3.5 4.5 H20.5 V19.5 H3.5 Z M3.5 16.5 L8.5 11.5 L13.5 16.5 M12 15 L15 12 L20.5 17.5 M15.5 8.5 A1 1 0 1 0 17.5 8.5 A1 1 0 1 0 15.5 8.5"),
            (Table, "M3.5 4.5 H20.5 V19.5 H3.5 Z M3.5 9.5 H20.5 M3.5 14.5 H20.5 M12 4.5 V19.5"),
            (Note, "M3 12 A9 9 0 1 1 21 12 A9 9 0 1 1 3 12 Z M12 11 V16.5 M12 7.5 L12 7.6"),
        ],
    ];

    public FormatToolbar()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
        };
        foreach (var group in Groups)
        {
            if (row.Children.Count > 0)
                row.Children.Add(Separator());
            foreach (var (command, icon) in group)
            {
                var button = IconButton(icon, Tooltip(command), () => FormatRequested?.Invoke(command));
                if (KeyFor(command) is { } key)
                {
                    ToolTip.SetTip(button, $"{Tooltip(command)} ({key.ToString("p", null)})"); // "p": as the menus show keys
                    AutomationProperties.SetAcceleratorKey(button, key.ToString("p", null));
                }
                _buttons[command] = button;
                row.Children.Add(button);
            }
        }
        _tableTools.Add(Separator());
        foreach (var (op, icon) in TableTools)
            _tableTools.Add(IconButton(icon, TableTooltip(op), () => TableRequested?.Invoke(op)));
        foreach (var tool in _tableTools)
        {
            tool.IsVisible = false;
            row.Children.Add(tool);
        }

        var apply = EditAction(Strings.InlineApply, Strings.InlineApplyTip, apply: true);
        apply.Classes.Add("apply");
        _editActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
            Spacing = 4,
            IsVisible = false,
            Children = { apply, EditAction(Strings.InlineCancel, Strings.InlineCancelTip, apply: false) },
        };
        DockPanel.SetDock(_editActions, Dock.Right);
        Child = new DockPanel { Children = { _editActions, row } };
    }

    private Button EditAction(string label, string tip, bool apply)
    {
        var button = new Button { Classes = { "format-action" }, Focusable = false, Content = label };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => EditActionRequested?.Invoke(apply);
        return button;
    }

    private static Border Separator() =>
        new() { Classes = { "format-separator" }, Width = 1, Height = 14, Margin = new Thickness(6, 0) };

    private static Button IconButton(string icon, string tip, Action click)
    {
        var geometry = Geometry.Parse(icon);
        geometry.Transform = new ScaleTransform(16 / 24.0, 16 / 24.0);
        var button = new Button
        {
            Classes = { "format" },
            Focusable = false,
            Content = new Path { Classes = { "icon" }, Data = geometry, Stretch = Stretch.None, Width = 16, Height = 16 },
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private static string TableTooltip(TableOp op) => op switch
    {
        TableOp.RowAbove => Strings.TableRowAbove,
        TableOp.RowBelow => Strings.TableRowBelow,
        TableOp.ColumnLeft => Strings.TableColumnLeft,
        TableOp.ColumnRight => Strings.TableColumnRight,
        TableOp.DeleteRow => Strings.MenuDeleteRow,
        _ => Strings.MenuDeleteColumn,
    };

    /// <summary>The formatting keys, in the source and in an open block; the window handles them.</summary>
    public static KeyGesture? KeyFor(FormatCommand command) => command switch
    {
        Bold => new KeyGesture(Key.B, KeyModifiers.Control),
        Italic => new KeyGesture(Key.I, KeyModifiers.Control),
        Code => new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift),
        Link => new KeyGesture(Key.K, KeyModifiers.Control),
        Heading1 => new KeyGesture(Key.D1, KeyModifiers.Control | KeyModifiers.Shift),
        Heading2 => new KeyGesture(Key.D2, KeyModifiers.Control | KeyModifiers.Shift),
        Heading3 => new KeyGesture(Key.D3, KeyModifiers.Control | KeyModifiers.Shift),
        _ => null,
    };

    private static string Tooltip(FormatCommand command) => command switch
    {
        Bold => Strings.FormatBold,
        Italic => Strings.FormatItalic,
        Code => Strings.FormatCode,
        Link => Strings.FormatLink,
        Heading1 => string.Format(Strings.FormatHeading, 1),
        Heading2 => string.Format(Strings.FormatHeading, 2),
        Heading3 => string.Format(Strings.FormatHeading, 3),
        BulletedList => Strings.InsertBulletedList,
        NumberedList => Strings.InsertNumberedList,
        Quote => Strings.FormatQuote,
        CodeBlock => Strings.InsertCodeBlock,
        FormatCommand.Image => Strings.FormatImage,
        Table => Strings.InsertTable,
        Note => Strings.InsertNote,
        _ => command.ToString(),
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
            foreach (var (command, button) in _buttons)
                button.IsVisible = TextFormatting.Supports(command, Markdown);
        else if (change.Property == EditOpenProperty)
            _editActions.IsVisible = EditOpen;
        else if (change.Property == InTableProperty)
            foreach (var tool in _tableTools)
                tool.IsVisible = InTable;
    }
}
