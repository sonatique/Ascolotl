using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using AvaloniaEdit;
using AvaloniaEdit.Editing;

namespace Ascolotl.App.Views;

/// <summary>
/// AvaloniaEdit's editor, made visible to screen readers. AvaloniaEdit 11 gives them
/// nothing, so they met an unnamed element with no text. Its text area now presents
/// itself as an edit field, with the name given to it, holding the whole text.
/// ponytail: no line-by-line reading; that needs a text pattern Avalonia 11 lacks.
/// </summary>
public class AccessibleTextEditor : TextEditor
{
    public AccessibleTextEditor() : base(new AccessibleTextArea()) { }

    public AccessibleTextEditor(string name) : this() => AutomationProperties.SetName(TextArea, name);

    protected override Type StyleKeyOverride => typeof(TextEditor);

    private sealed class AccessibleTextArea : TextArea
    {
        protected override Type StyleKeyOverride => typeof(TextArea);

        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    }

    private sealed class Peer(TextArea area) : ControlAutomationPeer(area), IValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

        public bool IsReadOnly => area.IsReadOnly;

        public string? Value => area.Document?.Text;

        public void SetValue(string? value)
        {
            if (!IsReadOnly && area.Document is { } document)
                document.Text = value ?? "";
        }
    }
}
