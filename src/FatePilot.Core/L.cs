using System.Globalization;

namespace FatePilot.Core;

// Portuguese source strings are stable translation keys, like gettext's msgid.
// External plugin error details are intentionally kept as reported by their provider.
public static class L
{
    public static IReadOnlyDictionary<string, string> Catalog { get; set; } = new Dictionary<string, string>();
    public static IReadOnlyDictionary<string, string> English { get; set; } = new Dictionary<string, string>();
    public static string T(string key, params object?[] args)
    {
        var template = Catalog.GetValueOrDefault(key) ?? English.GetValueOrDefault(key) ?? key;
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }
}
