using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace Ascolotl.Core.Spelling;

/// <summary>A Hunspell dictionary, with the user's own words and the words ignored this session.
/// Checked from a background thread while the user adds words from the UI one.</summary>
public sealed partial class Speller
{
    private readonly WordList _words;

    public Speller(WordList words) => _words = words;

    /// <summary>The user's words, every language's, kept in one file; and those ignored until the app closes.</summary>
    public static readonly PersonalWords Personal = new();

    public bool IsKnown(string word) =>
        Known(word)
        // A sentence's first word is capitalised: Bonjour is right when bonjour is.
        || char.IsUpper(word[0]) && Known(char.ToLowerInvariant(word[0]) + word[1..]);

    private bool Known(string word) => Personal.Contains(word) || _words.Check(word);

    public IReadOnlyList<string> Suggest(string word, int max = 5) => [.. _words.Suggest(word).Take(max)];

    // ── Dictionaries ──────────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, Lazy<Speller?>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where dictionaries are looked for: the user's folder first, then the app's own.
    /// Replaced whole, never changed in place: a check in the background reads it.</summary>
    public static IReadOnlyList<string> Folders { get; set; } = [];

    static Speller() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // ISO-8859-15 and the like

    /// <summary>The speller for a language (fr, fr-CH, en_GB), loaded once: from the user's or the
    /// app's folder, else one bundled with the app; null when there is none.</summary>
    public static Speller? For(string language) =>
        Loaded.GetOrAdd(language, l => new Lazy<Speller?>(() =>
        {
            if (Find(l) is { } aff)
                return new Speller(WordList.CreateFromFiles(Path.ChangeExtension(aff, ".dic"), aff));
            if (Pick(Bundled, l) is not { } name) return null;
            using var dic = Bundle.GetManifestResourceStream($"Dictionaries.{name}.dic")!;
            using var affix = Bundle.GetManifestResourceStream($"Dictionaries.{name}.aff")!;
            return new Speller(WordList.CreateFromStreams(dic, affix));
        })).Value;

    private static readonly System.Reflection.Assembly Bundle = typeof(Speller).Assembly;

    /// <summary>The dictionaries bundled with the app: en_US, en_GB, fr, de_DE_frami, it_IT, es_ES.</summary>
    public static IReadOnlyList<string> Bundled { get; } = [.. Bundle.GetManifestResourceNames()
        .Where(n => n.StartsWith("Dictionaries.", StringComparison.Ordinal) && n.EndsWith(".aff", StringComparison.Ordinal))
        .Select(n => n["Dictionaries.".Length..^".aff".Length])];

    /// <summary>For tests and for a dictionary from elsewhere: the speller a language uses.</summary>
    public static void Use(string language, Speller? speller) => Loaded[language] = new Lazy<Speller?>(() => speller);

    /// <summary>The .aff of the dictionary for <paramref name="language"/>: its region's (fr_CH), else
    /// its language's (fr.aff, fr_FR.aff, then any fr_*), in the first folder that has one.</summary>
    public static string? Find(string language)
    {
        foreach (var folder in Folders.Where(Directory.Exists))
        {
            var affs = Directory.GetFiles(folder, "*.aff").Where(f => File.Exists(Path.ChangeExtension(f, ".dic"))).ToList();
            if (Pick([.. affs.Select(Path.GetFileNameWithoutExtension).OfType<string>()], language) is { } name)
                return affs.First(f => Path.GetFileNameWithoutExtension(f) == name);
        }
        return null;
    }

    /// <summary>Of dictionary names (fr_CH, de_DE_frami…), the one for a language: its region's, else its
    /// language's (fr, fr_FR, en_US for en, then any fr_*).</summary>
    public static string? Pick(IReadOnlyList<string> names, string language)
    {
        var tag = language.Replace('-', '_');
        var lang = tag.Split('_')[0];
        string? Named(string name) => names.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        return Named(tag) ?? Named(lang) ?? Named($"{lang}_{lang.ToUpperInvariant()}") ?? (lang == "en" ? Named("en_US") : null)
            ?? names.FirstOrDefault(n => n.StartsWith(lang + "_", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The document's language: AsciiDoc's <c>:lang:</c>, or a Markdown front matter's
    /// <c>lang:</c>; null when it says none.</summary>
    public static string? DocumentLanguage(string text, bool markdown) =>
        (markdown ? FrontMatterLang() : LangAttribute()).Match(text) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"^:lang:[ \t]*([A-Za-z]{2,3}(?:[-_][A-Za-z]{2})?)[ \t\r]*$", RegexOptions.Multiline)]
    private static partial Regex LangAttribute();

    [GeneratedRegex(@"\A---\r?\n(?:.*\r?\n)*?lang:[ \t]*[""']?([A-Za-z]{2,3}(?:[-_][A-Za-z]{2})?)")]
    private static partial Regex FrontMatterLang();
}

/// <summary>Words the user added (kept in a file, one a line) or ignored for this session.</summary>
public sealed class PersonalWords
{
    private readonly ConcurrentDictionary<string, bool> _words = new();
    private string? _file;

    /// <summary>Changed: misspellings are to be found again.</summary>
    public event Action? Changed;

    public void Load(string file)
    {
        _file = file;
        if (File.Exists(file))
            foreach (var word in File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0))
                _words[word] = true;
    }

    public bool Contains(string word) => _words.ContainsKey(word);

    /// <summary>Known from now on, and in the file for the sessions to come.</summary>
    public void Add(string word)
    {
        if (!_words.TryAdd(word, true)) return;
        if (_file is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.AppendAllText(_file, word + Environment.NewLine);
        }
        Changed?.Invoke();
    }

    /// <summary>Known until the app closes.</summary>
    public void Ignore(string word)
    {
        if (_words.TryAdd(word, false)) Changed?.Invoke();
    }
}
