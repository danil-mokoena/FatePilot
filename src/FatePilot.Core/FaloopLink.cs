namespace FatePilot.Core;

public static class FaloopLink
{
    private const string Origin = "https://faloop.app";

    // Public game-world names only; never accept a URL, query string or encoded path.
    public static bool CanFilter(TrackerWorld? world) => world is { Id: > 0, Name.Length: >= 2 and <= 32 }
        && world.Name.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    public static string ForWorld(TrackerWorld? world) => CanFilter(world)
        ? $"{Origin}/{world!.Name.ToLowerInvariant()}/fate" : $"{Origin}/fate";
}
