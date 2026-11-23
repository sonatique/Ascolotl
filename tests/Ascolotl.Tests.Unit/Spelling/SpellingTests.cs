namespace Ascolotl.Tests.Unit.Spelling;

using Ascolotl.Core.Spelling;
using WeCantSpell.Hunspell;

public class SpellingTests
{
    private static string[] Words(string text, bool markdown = false) =>
        [.. SpellingWords.Find(text, markdown).Select(w => text.Substring(w.Start, w.Length))];

    [Fact]
    public void AsciiDoc_ProseOnly()
    {
        const string doc = """
            = Teh Title
            :author: Jane Doe
            :imagesdir: picturez

            Some prose with a speling error, see https://exampel.com/pagee and `codde`.
            Use kbd:[Ctrl+Shft] and link:https://x.org[the sitte] or <<anker,the refrence>>.
            A {attrname} reference, [[ankor]] an anchor, an iPhone, NOTE, a_b, x2, I.

            [source,pythn]
            ----
            prnt("hello")
            ----

            // a coment
            ////
            blokc comment
            ////
            image::pictur.png[]
            Lastt.
            """;
        Assert.Equal(
            ["Teh", "Title", "Some", "prose", "with", "speling", "error", "see", "and", "Use", "and", "the", "sitte", "or", "the", "refrence",
             "reference", "an", "anchor", "an", "Lastt"],
            Words(doc));
    }

    [Fact]
    public void Markdown_ProseOnly()
    {
        const string doc = """
            ---
            title: Frontt
            ---
            # A Heding

            See [the lnk](https://exmple.com) and <https://autolnk.org>, `inlne` code.

            ```js
            consle.log(1)
            ```

                indnted code
            <span class="clss">Taggs</span> end.
            """;
        Assert.Equal(["Heding", "See", "the", "lnk", "and", "code", "Taggs", "end"], Words(doc, markdown: true));
    }

    [Fact]
    public void ShownText_LeavesOutUrlsAndEmails()
    {
        const string text = "See https://exmple.org/pagee, mail jane@exmple.org, teh end.";
        Assert.Equal(["See", "mail", "teh", "end"], SpellingWords.InShownText(text).Select(w => text.Substring(w.Start, w.Length)));
    }

    [Fact]
    public void Apostrophes_StayInTheWord()
        => Assert.Equal(["l'homme", "don’t", "know"], Words("l'homme don’t 'know'"));

    [Fact]
    public void Speller_KnowsItsWords_ACapitalisedFirstWord_AndThePersonalOnes()
    {
        var speller = new Speller(WordList.CreateFromWords(["hello", "world", "Paris"]));
        Assert.True(speller.IsKnown("hello"));
        Assert.True(speller.IsKnown("Hello")); // at a sentence's start
        Assert.False(speller.IsKnown("paris"));
        Assert.False(speller.IsKnown("helo"));
        Assert.Contains("hello", speller.Suggest("helo"));

        Speller.Personal.Ignore("Ascolotl");
        Assert.True(speller.IsKnown("Ascolotl"));
    }

    [Fact]
    public void PersonalWords_AddedOnes_AreKeptInTheFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ascolotl-words-{Guid.NewGuid():N}.txt");
        try
        {
            var words = new PersonalWords();
            words.Load(file);
            var changed = 0;
            words.Changed += () => changed++;
            words.Add("Kinkajou");
            words.Add("Kinkajou");
            Assert.Equal(1, changed);
            var again = new PersonalWords();
            again.Load(file);
            Assert.True(again.Contains("Kinkajou"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("fr-CH", "fr_CH")]   // the region's own
    [InlineData("fr", "fr_FR")]       // the language's
    [InlineData("de-AT", "de_DE")]    // another region of the language
    [InlineData("en", "en_US")]
    [InlineData("it", null)]
    public void Find_TheRegionsDictionary_ElseTheLanguages(string language, string? expected)
    {
        var folder = Directory.CreateTempSubdirectory("ascolotl-dic-").FullName;
        var folders = Speller.Folders;
        try
        {
            foreach (var name in new[] { "fr_CH", "fr_FR", "de_DE", "en_GB", "en_US" })
            {
                File.WriteAllText(Path.Combine(folder, name + ".aff"), "");
                File.WriteAllText(Path.Combine(folder, name + ".dic"), "0");
            }
            File.WriteAllText(Path.Combine(folder, "it_IT.aff"), ""); // no .dic: not a dictionary
            Speller.Folders = [folder, .. folders];
            Assert.Equal(expected, Speller.Find(language) is { } aff ? Path.GetFileNameWithoutExtension(aff) : null);
        }
        finally
        {
            Speller.Folders = folders;
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SixDictionaries_AreBundled()
        => Assert.Equal(["de_DE_frami", "en_GB", "en_US", "es_ES", "fr", "it_IT"], Speller.Bundled.Order(StringComparer.Ordinal));

    [Theory]
    [InlineData("en", "hello", "helo")]
    [InlineData("en-US", "color", "colour")]
    [InlineData("en-GB", "colour", "colr")]
    [InlineData("fr-CH", "bonjour", "bonjur")]
    [InlineData("de-CH", "Straße", "Strasze")] // a Latin-1 dictionary, read as such
    [InlineData("it", "ciao", "ciaao")]
    [InlineData("es", "hola", "hoola")]
    public void BundledDictionaries_KnowTheirLanguage(string language, string known, string unknown)
    {
        var speller = Speller.For(language);
        Assert.NotNull(speller);
        Assert.True(speller.IsKnown(known), known);
        Assert.False(speller.IsKnown(unknown), unknown);
    }

    [Theory]
    [InlineData("= Doc\n:lang: fr\n", false, "fr")]
    [InlineData("= Doc\r\n:lang: de-CH\r\n", false, "de-CH")]
    [InlineData("= Doc\n", false, null)]
    [InlineData("---\ntitle: x\nlang: \"it\"\n---\n# A\n", true, "it")]
    [InlineData("# A\nlang: fr\n", true, null)] // not front matter
    public void DocumentLanguage(string text, bool markdown, string? expected)
        => Assert.Equal(expected, Speller.DocumentLanguage(text, markdown));
}
