using System.Globalization;

namespace Ascolotl.Core.Services;

/// <summary>
/// The interface language: one of <see cref="Codes"/>, or "" to follow the
/// system. Applied once at startup, before any window reads its texts.
/// </summary>
public static class UiLanguage
{
    /// <summary>Languages with a full translation of Strings.resx.</summary>
    public static readonly IReadOnlyList<string> Codes = ["en", "fr", "de", "it", "es"];

    public static void Apply(string? code)
    {
        if (string.IsNullOrEmpty(code) || !Codes.Contains(code))
            return; // the system's language, or English where there is no translation
        var culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
