using System.Globalization;
using System.Runtime.CompilerServices;

namespace Ascolotl.Tests.Unit;

internal static class TestCulture
{
    /// <summary>
    /// Tests assert English messages; without this, a machine with a French or
    /// German Windows would read the translated resources and fail them.
    /// </summary>
    [ModuleInitializer]
    internal static void UseEnglishInterface()
    {
        var english = CultureInfo.GetCultureInfo("en");
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentUICulture = english;
    }
}
