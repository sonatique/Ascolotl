namespace Ascolotl.Core.Spelling;

/// <summary>Whether the editors mark misspellings, app-wide: the setting the View menu toggles.
/// Off until the app has read it: editors made elsewhere (tests) check nothing.</summary>
public static class SpellCheck
{
    private static bool _enabled;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Turned on or off: the editors find their misspellings again, or drop them.</summary>
    public static event Action? Changed;
}
