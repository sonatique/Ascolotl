using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ascolotl.Core.Resources;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit;

/// <summary>Every interface text is translated, formats, and has a unique access key.</summary>
public sealed partial class LocalizationTests
{
    private static readonly string Src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src"));
    private static readonly string Resources = Path.Combine(Src, "Ascolotl.Core", "Resources");

    public static IEnumerable<object[]> Translations() =>
        UiLanguage.Codes.Where(c => c != "en").Select(c => new object[] { c });

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(Path.Combine(Resources, file)).Root!.Elements("data")
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")?.Value ?? "");

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_HasEveryKey_WithTheSamePlaceholders(string code)
    {
        var english = Read("Strings.resx");
        var translated = Read($"Strings.{code}.resx");

        Assert.Empty(english.Keys.Except(translated.Keys));
        Assert.Empty(translated.Keys.Except(english.Keys));
        foreach (var (key, text) in english)
        {
            Assert.False(string.IsNullOrWhiteSpace(translated[key]), $"{code}: {key} is empty");
            Assert.True(Placeholders(text).SetEquals(Placeholders(translated[key])), $"{code}: {key} placeholders differ");
            Assert.True(HasAccessKey(text) == HasAccessKey(translated[key]), $"{code}: {key} access key");
        }
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translation_LoadsAndFormats(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        Assert.NotEqual(Strings.Ready, Strings.ResourceManager.GetString(nameof(Strings.Ready), culture));
        foreach (var (key, text) in Read("Strings.resx"))
        {
            var value = Strings.ResourceManager.GetString(key, culture)!;
            var count = Placeholders(text).Select(p => int.Parse(PlaceholderIndex().Match(p).Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
            if (count > 0)
                _ = string.Format(culture, value, Enumerable.Repeat<object>(1, count).ToArray()); // throws on a broken format
        }
    }

    [Fact]
    public void Layouts_HoldNoHardCodedText()
    {
        var literal = LiteralText();
        var found = Directory.GetFiles(Path.Combine(Src, "Ascolotl.App"), "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => literal.Matches(File.ReadAllText(f)).Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();
        Assert.Empty(found);
    }

    public static IEnumerable<object[]> Languages() => UiLanguage.Codes.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Languages))]
    public void Menus_HaveUniqueAccessKeysAtEachLevel(string code)
    {
        var culture = CultureInfo.GetCultureInfo(code);
        XNamespace ns = "https://github.com/avaloniaui";
        var menus = XDocument.Load(Path.Combine(Src, "Ascolotl.App", "Views", "AppMenus.axaml"));
        var clashes = new List<string>();
        foreach (var parent in menus.Descendants().Where(e => e.Elements(ns + "MenuItem").Any()))
        {
            var keys = parent.Elements(ns + "MenuItem")
                .Select(i => ResourceKey().Match(i.Attribute("Header")?.Value ?? ""))
                .Where(m => m.Success)
                .Select(m => Strings.ResourceManager.GetString(m.Groups[1].Value, culture)!)
                .Select(h => AccessKey().Match(h))
                .Where(m => m.Success)
                .GroupBy(m => char.ToLowerInvariant(m.Groups[1].Value[0]))
                .Where(g => g.Count() > 1);
            clashes.AddRange(keys.Select(g => $"'{g.Key}' in {string.Join(", ", g.Select(m => m.Value))}"));
        }
        Assert.Empty(clashes);
    }

    private static HashSet<string> Placeholders(string text) => [.. NumberedPlaceholder().Matches(text).Select(m => m.Value)];

    private static bool HasAccessKey(string text) => AccessKey().IsMatch(text);

    [GeneratedRegex(@"\{\d+(?::[^}]*)?\}")] private static partial Regex NumberedPlaceholder();
    [GeneratedRegex(@"^\{(\d+)")] private static partial Regex PlaceholderIndex();
    [GeneratedRegex(@"(?<!_)_([^_\s])")] private static partial Regex AccessKey();
    [GeneratedRegex(@"Strings\.(\w+)\}")] private static partial Regex ResourceKey();
    [GeneratedRegex(@"\b(Text|Header|Content|ToolTip\.Tip|Title|Watermark|PlaceholderText|AutomationProperties\.Name)=""[^""{]*[A-Za-z][^""]*""")] private static partial Regex LiteralText();
}
