using System.Text;

namespace FatePilot.Core;

public static class DialogueSpeaker
{
    // Compare complete NPC labels after stripping formatting in the native reader.
    // Whitespace and Unicode composition vary by language; never accept substrings.
    public static bool Matches(string? expectedNpc, string? visibleSpeaker)
    {
        if (string.IsNullOrWhiteSpace(expectedNpc) || string.IsNullOrWhiteSpace(visibleSpeaker) ||
            expectedNpc.Length > 256 || visibleSpeaker.Length > 256) return false;
        return string.Equals(Normalize(expectedNpc), Normalize(visibleSpeaker), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => string.Join(' ', value.Normalize(NormalizationForm.FormC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
