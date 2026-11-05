using Dalamud.Game;
using FatePilot.Core;
using System.Reflection;
using System.Text.Json;

namespace FatePilot;

internal static class Localization
{
    public static readonly string[] Codes = ["auto", "en", "ja", "de", "fr", "pt-BR", "es"];
    public static readonly string[] Names = ["Auto", "English", "日本語", "Deutsch", "Français", "Português (Brasil)", "Español"];
    public static void Apply(string language)
    {
        if (language == "auto") language = Services.Client.ClientLanguage switch
        {
            ClientLanguage.Japanese => "ja", ClientLanguage.German => "de", ClientLanguage.French => "fr", _ => "en",
        };
        L.English = Load("en");
        L.Catalog = Load(language);
    }
    private static Dictionary<string, string> Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"FatePilot.Localization.{code}.json");
        return stream == null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
