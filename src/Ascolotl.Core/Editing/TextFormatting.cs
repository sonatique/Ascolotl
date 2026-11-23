using System.Text;
using System.Text.RegularExpressions;
using Ascolotl.Core.Editing.Assist;

namespace Ascolotl.Core.Editing;

/// <summary>A formatting command of the toolbar and the editor's context menu.</summary>
public enum FormatCommand
{
    Bold,
    Italic,
    Code,
    Link,
    Heading1,
    Heading2,
    Heading3,
    BulletedList,
    NumberedList,
    Quote,
    CodeBlock,
    Image,
    Table,
    Note,
    HorizontalRule,
}

/// <summary>Replace <see cref="Length"/> characters at <see cref="Start"/> with the
/// replacement's text; its stops say what to select (one stop) or tab through (several).</summary>
public sealed record FormatEdit(int Start, int Length, ParsedSnippet Replacement);

/// <summary>
/// The source edit behind each <see cref="FormatCommand"/>, in AsciiDoc or Markdown.
/// Inline commands wrap the selection (or unwrap it when it is already wrapped),
/// line commands apply to every line the selection touches, and block commands wrap
/// the selected lines or, without a selection, add an empty block at the caret.
/// </summary>
public static partial class TextFormatting
{
    /// <summary>Admonitions are AsciiDoc only.</summary>
    public static bool Supports(FormatCommand command, bool markdown)
        => !(markdown && command == FormatCommand.Note);

    /// <summary>The lines the selection touches as headings of <paramref name="level"/> (1 to 6), or as
    /// plain lines for 0; lines at that level already become plain lines too.</summary>
    public static FormatEdit Heading(bool markdown, string text, int selectionStart, int selectionLength, int level)
    {
        var ctx = ContextFor(markdown, text, selectionStart, selectionLength);
        var heading = markdown ? MarkdownHeading() : AsciiDocHeading();
        return level <= 0 ? EachLine(ctx, lines => [.. lines.Select(l => heading.Replace(l, ""))]) : Heading(ctx, Math.Min(level, 6));
    }

    private static Context ContextFor(bool markdown, string text, int selectionStart, int selectionLength)
    {
        var s = Math.Clamp(selectionStart, 0, text.Length);
        var e = Math.Clamp(s + selectionLength, s, text.Length);
        return new Context(text, s, e, markdown, text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n");
    }

    public static FormatEdit Apply(FormatCommand command, bool markdown, string text, int selectionStart, int selectionLength)
    {
        var ctx = ContextFor(markdown, text, selectionStart, selectionLength);
        return command switch
        {
            FormatCommand.Bold => markdown ? Inline(ctx, "**", "**", ["**", "__"]) : Inline(ctx, "*", "**", ["**", "*"]),
            FormatCommand.Italic => markdown ? Inline(ctx, "*", "*", ["*", "_"]) : Inline(ctx, "_", "__", ["__", "_"]),
            FormatCommand.Code => markdown ? Inline(ctx, "`", "`", ["`"]) : Inline(ctx, "`", "``", ["``", "`"]),
            FormatCommand.Link => Link(ctx),
            FormatCommand.Image => Image(ctx),
            FormatCommand.Heading1 => Heading(ctx, 1),
            FormatCommand.Heading2 => Heading(ctx, 2),
            FormatCommand.Heading3 => Heading(ctx, 3),
            FormatCommand.BulletedList => List(ctx, numbered: false),
            FormatCommand.NumberedList => List(ctx, numbered: true),
            FormatCommand.Quote when markdown => QuoteLines(ctx),
            FormatCommand.Quote => Block(ctx, "____", "____"),
            FormatCommand.CodeBlock when markdown => Block(ctx, "```", "```"),
            FormatCommand.CodeBlock => Block(ctx, "[source]" + ctx.Nl + "----", "----"),
            FormatCommand.Note => Note(ctx),
            FormatCommand.Table => Insert(ctx, Table(ctx)),
            FormatCommand.HorizontalRule => Insert(ctx, new Out().Text(markdown ? "---" : "'''").Stop(0)),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
    }

    private sealed record Context(string Text, int Start, int End, bool Markdown, string Nl)
    {
        public bool HasSelection => End > Start;
    }

    /// <summary>Builds a replacement and its stops.</summary>
    private sealed class Out
    {
        private readonly StringBuilder _text = new();
        private readonly List<SnippetStop> _stops = [];

        public Out Text(string s)
        {
            _text.Append(s);
            return this;
        }

        public Out Stop(int index, string placeholder = "")
        {
            _stops.Add(new SnippetStop(index, _text.Length, placeholder.Length, placeholder));
            _text.Append(placeholder);
            return this;
        }

        public ParsedSnippet Build()
            => new(_text.ToString(), [.. _stops.OrderBy(s => s.Index == 0 ? int.MaxValue : s.Index)]);
    }

    // ── Inline: wrap or unwrap the selection ──────────────────────────────

    /// <param name="add">The marker added around the selection.</param>
    /// <param name="midWord">The one added when a word continues on either side
    /// (AsciiDoc's single markers only work at word boundaries).</param>
    /// <param name="markers">Markers that unwrap, longest first.</param>
    private static FormatEdit Inline(Context c, string add, string midWord, string[] markers)
    {
        var (s, e) = Trimmed(c);
        foreach (var m in markers)
        {
            // The markers sit just outside the selection, or are part of it.
            if (s >= m.Length && e + m.Length <= c.Text.Length
                && string.CompareOrdinal(c.Text, s - m.Length, m, 0, m.Length) == 0
                && string.CompareOrdinal(c.Text, e, m, 0, m.Length) == 0
                && !Continues(c.Text, s - m.Length - 1, m) && !Continues(c.Text, e + m.Length, m))
                return Select(s - m.Length, e - s + 2 * m.Length, c.Text[s..e]);
            if (e - s >= 2 * m.Length && c.Text.AsSpan(s, e - s) is var sel
                && sel.StartsWith(m) && sel.EndsWith(m)
                && !Continues(c.Text, s + m.Length, m) && !Continues(c.Text, e - m.Length - 1, m))
                return Select(s, e - s, c.Text[(s + m.Length)..(e - m.Length)]);
        }

        if (IsWordChar(c.Text, s - 1) || IsWordChar(c.Text, e))
            add = midWord;
        return new FormatEdit(s, e - s, new Out().Text(add).Stop(1, c.Text[s..e]).Text(add).Build());
    }

    /// <summary>The selection without its surrounding whitespace.</summary>
    private static (int Start, int End) Trimmed(Context c)
    {
        var (s, e) = (c.Start, c.End);
        while (s < e && char.IsWhiteSpace(c.Text[s])) s++;
        while (e > s && char.IsWhiteSpace(c.Text[e - 1])) e--;
        return s == e ? (c.Start, c.Start) : (s, e);
    }

    /// <summary>Replace a range with <paramref name="inner"/>, selected.</summary>
    private static FormatEdit Select(int start, int length, string inner)
        => new(start, length, new Out().Stop(1, inner).Build());

    private static bool Continues(string text, int i, string marker)
        => i >= 0 && i < text.Length && text[i] == marker[0];

    private static bool IsWordChar(string text, int i)
        => i >= 0 && i < text.Length && char.IsLetterOrDigit(text[i]);

    private static FormatEdit Link(Context c)
    {
        var o = new Out();
        var (s, e) = Trimmed(c);
        var selected = c.Text[s..e];
        if (selected.Length > 0 && UrlLike().IsMatch(selected))
        {
            if (c.Markdown) o.Text("[").Stop(1, "text").Text("](" + selected + ")");
            else o.Text(selected + "[").Stop(1, "text").Text("]");
        }
        else if (selected.Length > 0)
        {
            if (c.Markdown) o.Text("[" + selected + "](").Stop(1, "url").Text(")");
            else o.Text("link:").Stop(1, "url").Text("[" + selected + "]");
        }
        else if (c.Markdown) o.Text("[").Stop(1, "text").Text("](").Stop(2, "url").Text(")").Stop(0);
        else o.Text("link:").Stop(1, "url").Text("[").Stop(2, "text").Text("]").Stop(0);
        return new FormatEdit(s, e - s, o.Build());
    }

    private static FormatEdit Image(Context c)
    {
        var o = new Out();
        var (s, e) = Trimmed(c);
        var alt = c.Text[s..e];
        if (c.Markdown)
        {
            o.Text("![");
            if (alt.Length > 0) o.Text(alt + "](").Stop(1, "image.png").Text(")");
            else o.Stop(1, "alt text").Text("](").Stop(2, "image.png").Text(")").Stop(0);
        }
        else
        {
            // Alone on its line, the image is a block of its own.
            var (first, last) = Lines(c);
            var alone = c.Text[first..last].Trim().Length == alt.Length;
            o.Text(alone ? "image::" : "image:").Stop(1, "image.png").Text("[");
            if (alt.Length > 0) o.Text(alt + "]");
            else o.Stop(2, "alt text").Text("]").Stop(0);
        }
        return new FormatEdit(s, e - s, o.Build());
    }

    // ── Line commands ─────────────────────────────────────────────────────

    /// <summary>Whole lines the selection touches: [first, last), last before the newline.</summary>
    private static (int First, int Last) Lines(Context c)
    {
        var end = c.HasSelection && c.End > 0 && c.Text[c.End - 1] == '\n' ? c.End - 1 : c.End;
        return (LineStart(c.Text, c.Start), LineEnd(c.Text, Math.Max(end, c.Start)));
    }

    private static int LineStart(string text, int offset)
        => offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;

    private static int LineEnd(string text, int offset)
    {
        var nl = text.IndexOf('\n', offset);
        if (nl < 0) return text.Length;
        return nl > 0 && text[nl - 1] == '\r' ? nl - 1 : nl;
    }

    /// <summary>Rewrite each line the selection touches. The edit keeps the
    /// lines selected, or puts the caret at the end of a single line.</summary>
    private static FormatEdit EachLine(Context c, Func<string[], string[]> rewrite)
    {
        var (first, last) = Lines(c);
        var lines = c.Text[first..last].Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var replaced = string.Join(c.Nl, rewrite(lines));
        var o = c.HasSelection || lines.Length > 1 ? new Out().Stop(1, replaced) : new Out().Text(replaced).Stop(0);
        return new FormatEdit(first, last - first, o.Build());
    }

    private static FormatEdit Heading(Context c, int level) => EachLine(c, lines =>
    {
        var marker = new string(c.Markdown ? '#' : '=', level);
        var heading = c.Markdown ? MarkdownHeading() : AsciiDocHeading();
        var prose = lines.Where(l => l.Trim().Length > 0).ToList();
        var off = prose.Count > 0 && prose.All(l => heading.Match(l).Groups[1].Length == level);
        return [.. lines.Select(l => l.Trim().Length == 0 && prose.Count > 0 ? l
            : off ? heading.Replace(l, "")
            : marker + " " + heading.Replace(l, ""))];
    });

    private static FormatEdit List(Context c, bool numbered) => EachLine(c, lines =>
    {
        var own = numbered ? Numbered(c.Markdown) : Bulleted(c.Markdown);
        var other = numbered ? Bulleted(c.Markdown) : Numbered(c.Markdown);
        var prose = lines.Where(l => l.Trim().Length > 0).ToList();
        var off = prose.Count > 0 && prose.All(l => own.IsMatch(l));
        var n = 0;
        return [.. lines.Select(l =>
        {
            if (l.Trim().Length == 0 && prose.Count > 0) return l;
            var bare = own.Replace(other.Replace(l, "${indent}"), "${indent}");
            if (off) return bare;
            var indent = bare.Length - bare.TrimStart().Length;
            var marker = !numbered ? (c.Markdown ? "- " : "* ") : c.Markdown ? $"{++n}. " : ". ";
            return bare[..indent] + marker + bare[indent..];
        })];
    });

    private static Regex Bulleted(bool markdown) => markdown ? MarkdownBullet() : AsciiDocBullet();

    private static Regex Numbered(bool markdown) => markdown ? MarkdownNumber() : AsciiDocNumber();

    private static FormatEdit QuoteLines(Context c) => EachLine(c, lines =>
    {
        var prose = lines.Where(l => l.Trim().Length > 0).ToList();
        var off = prose.Count > 0 && prose.All(l => l.StartsWith('>'));
        return [.. lines.Select(l => off ? MarkdownQuote().Replace(l, "")
            : l.Trim().Length > 0 ? "> " + l
            : prose.Count > 0 ? ">" : "> ")];
    });

    // ── Blocks ────────────────────────────────────────────────────────────

    /// <summary>Wrap the selected lines between delimiters, or add an empty block at the caret.</summary>
    private static FormatEdit Block(Context c, string open, string close)
    {
        if (!c.HasSelection)
            return Insert(c, new Out().Text(open + c.Nl).Stop(1).Text(c.Nl + close));
        var (first, last) = Lines(c);
        var content = c.Text[first..last];
        return Placed(c, first, last, new Out().Text(open + c.Nl).Stop(1, content).Text(c.Nl + close));
    }

    /// <summary>A one-line note is a NOTE: paragraph; longer selections become a note block.</summary>
    private static FormatEdit Note(Context c)
    {
        var (first, last) = Lines(c);
        if (c.Text.IndexOf('\n', first, last - first) >= 0)
            return Block(c, "[NOTE]" + c.Nl + "====", "====");
        return EachLine(c, lines => [.. lines.Select(l => l.StartsWith("NOTE: ", StringComparison.Ordinal) ? l[6..] : "NOTE: " + l)]);
    }

    private static Out Table(Context c)
    {
        if (c.Markdown)
            return new Out().Text("| ").Stop(1, "Column").Text($" | Column |{c.Nl}| --- | --- |{c.Nl}| Cell | Cell |");
        var table = BlockSnippets.Table.Replace("\n", c.Nl, StringComparison.Ordinal);
        var cell = table.IndexOf("Cell", StringComparison.Ordinal);
        return new Out().Text(table[..cell]).Stop(1, "Cell").Text(table[(cell + 4)..]);
    }

    /// <summary>A new block: on the caret's blank line, else after its paragraph
    /// (or after the selected lines).</summary>
    private static FormatEdit Insert(Context c, Out block)
    {
        var (first, last) = Lines(c);
        if (c.Text[first..last].Trim().Length == 0)
            return Placed(c, first, last, block);

        // The paragraph ends at the next blank line.
        var end = last;
        while (end < c.Text.Length)
        {
            var next = c.Text.IndexOf('\n', end) + 1;
            var nextEnd = LineEnd(c.Text, next);
            if (c.Text[next..nextEnd].Trim().Length == 0) break;
            end = nextEnd;
        }
        var built = block.Build();
        var o = new Out().Text(c.Nl + c.Nl);
        return new FormatEdit(end, 0, Shifted(o, built));
    }

    /// <summary>Replace lines [first, last) with a block, keeping a blank line
    /// between it and any neighbouring text.</summary>
    private static FormatEdit Placed(Context c, int first, int last, Out block)
    {
        var o = new Out();
        if (first > 0 && LineText(c.Text, LineStart(c.Text, first - 1)).Trim().Length > 0) o.Text(c.Nl);
        var built = block.Build();
        var withBlock = Shifted(o, built);
        var after = last < c.Text.Length && LineText(c.Text, c.Text.IndexOf('\n', last) + 1).Trim().Length > 0;
        return new FormatEdit(first, last - first,
            after ? withBlock with { Text = withBlock.Text + c.Nl } : withBlock);
    }

    private static string LineText(string text, int lineStart) => text[lineStart..LineEnd(text, lineStart)];

    /// <summary>Append a built block to a prefix, moving its stops along.</summary>
    private static ParsedSnippet Shifted(Out prefix, ParsedSnippet block)
    {
        var head = prefix.Build().Text;
        return new ParsedSnippet(head + block.Text,
            [.. block.Stops.Select(s => s with { Start = s.Start + head.Length })]);
    }

    [GeneratedRegex(@"^(=+)[ \t]+(?=\S)")]
    private static partial Regex AsciiDocHeading();

    [GeneratedRegex(@"^ {0,3}(#{1,6})(?:[ \t]+|$)")]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:\*+|-)[ \t]+")]
    private static partial Regex AsciiDocBullet();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?:\.+|\d+\.)[ \t]+")]
    private static partial Regex AsciiDocNumber();

    [GeneratedRegex(@"^(?<indent>[ \t]*)[-*+][ \t]+")]
    private static partial Regex MarkdownBullet();

    [GeneratedRegex(@"^(?<indent>[ \t]*)\d{1,9}[.)][ \t]+")]
    private static partial Regex MarkdownNumber();

    [GeneratedRegex(@"^>[ \t]?")]
    private static partial Regex MarkdownQuote();

    [GeneratedRegex(@"^(?:[a-z][a-z0-9+.-]*://|mailto:)\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlLike();
}
