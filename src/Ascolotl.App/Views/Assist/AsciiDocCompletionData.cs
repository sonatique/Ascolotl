using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Ascolotl.Core.Editing.Assist;

namespace Ascolotl.App.Views.Assist;

/// <summary>
/// Wraps an <see cref="AssistItem"/> as an AvaloniaEdit completion entry. On
/// commit it expands the snippet and hands the caller the resulting tab-stops so
/// a <see cref="SnippetSession"/> can be started.
/// </summary>
internal sealed class AsciiDocCompletionData : ICompletionData
{
    private readonly AssistItem _item;
    private readonly Action<int, ParsedSnippet> _activate;

    public AsciiDocCompletionData(AssistItem item, Action<int, ParsedSnippet> activate)
    {
        _item = item;
        _activate = activate;
    }

    public IImage? Image => null;

    /// <summary>The text AvaloniaEdit filters on as the user keeps typing.</summary>
    public string Text => _item.Trigger;

    public object Content => _item.Label;

    public object Description => _item.Description;

    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var parsed = Snippet.Parse(_item.Snippet);
        // Capture the start offset BEFORE the replace: completionSegment tracks
        // the edit, so afterwards its Offset has moved to the END of the inserted
        // text. Using the post-replace value placed every tab-stop past the
        // snippet (wrong navigation on a populated doc; out-of-range crash on an
        // empty one).
        var baseOffset = completionSegment.Offset;
        textArea.Document.Replace(completionSegment, parsed.Text);
        _activate(baseOffset, parsed);
    }
}
