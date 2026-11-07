using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// 0.9 colour themes. Every theme is dark (like the game UI); Chocobo keeps the original gold palette.
internal sealed record UiTheme(string Key, string Name,
    Vector4 Window, Vector4 Child, Vector4 Frame, Vector4 Button, Vector4 Border, Vector4 Tab, Vector4 TitleBg, Vector4 TitleActive,
    Vector4 Accent, Vector4 ButtonHovered, Vector4 ButtonActive, Vector4 Header, Vector4 HeaderHovered, Vector4 TabHovered,
    Vector4 TabActive, Vector4 AccentBright, Vector4 Primary, Vector4 PrimaryHovered, Vector4 PrimaryActive)
{
    public static readonly UiTheme Chocobo = new("chocobo", "Chocobo",
        new(.055f, .065f, .085f, .98f), new(.08f, .095f, .12f, 1), new(.11f, .13f, .17f, 1), new(.16f, .19f, .24f, 1),
        new(.21f, .25f, .30f, 1), new(.10f, .12f, .16f, 1), new(.055f, .065f, .085f, 1), new(.09f, .11f, .14f, 1),
        new(.87f, .72f, .46f, 1), new(.40f, .32f, .20f, 1), new(.50f, .39f, .23f, 1), new(.30f, .25f, .17f, 1),
        new(.40f, .32f, .20f, 1), new(.32f, .28f, .21f, 1), new(.27f, .23f, .17f, 1), new(.98f, .83f, .57f, 1),
        new(.62f, .49f, .28f, 1), new(.74f, .59f, .35f, 1), new(.84f, .68f, .42f, 1));

    // Same structure as Chocobo, built from a background tone and an accent colour.
    private static UiTheme Derive(string key, string name, Vector3 background, Vector3 accent)
    {
        static Vector4 C(Vector3 v, float alpha = 1) => new(Math.Clamp(v.X, 0, 1), Math.Clamp(v.Y, 0, 1), Math.Clamp(v.Z, 0, 1), alpha);
        Vector4 Lift(float amount) => C(background + new Vector3(amount));
        return new(key, name, C(background, .98f), Lift(.025f), Lift(.055f), Lift(.105f), Lift(.155f), Lift(.045f), C(background), Lift(.035f),
            C(accent), C(accent * .46f), C(accent * .57f), C(accent * .345f), C(accent * .46f), C(accent * .37f), C(accent * .31f),
            C(accent * 1.13f), C(accent * .71f), C(accent * .85f), C(accent * .96f));
    }

    public static readonly UiTheme[] All =
    [
        Chocobo,
        Derive("ishgard", "Ishgard", new(.05f, .065f, .095f), new(.64f, .80f, .96f)),
        Derive("gridania", "Gridania", new(.05f, .075f, .06f), new(.62f, .86f, .52f)),
        Derive("limsa", "Limsa Lominsa", new(.04f, .065f, .085f), new(.42f, .82f, .86f)),
        Derive("uldah", "Ul'dah", new(.085f, .065f, .05f), new(.98f, .64f, .32f)),
        Derive("sharlayan", "Sharlayan", new(.06f, .055f, .09f), new(.75f, .65f, .98f)),
    ];

    public static UiTheme Find(string? key) => All.FirstOrDefault(t => t.Key == key) ?? Chocobo;
}

internal sealed partial class MainWindow
{
    private static UiTheme theme = UiTheme.Chocobo;
    private static Vector4 Gold => theme.Accent;
    private static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    private void DrawAppearanceSettings()
    {
        var scale = ImGuiHelpers.GlobalScale;
        BeginCard("appearance");
        CardTitle(FontAwesomeIcon.Palette, L.T("Aparência"));
        ImGui.TextDisabled(L.T("Tema de cores da janela e do painel compacto."));
        ImGui.Spacing();
        var size = new Vector2(120, 78) * scale;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var perRow = Math.Max(1, (int)((CardWidth() + gap) / (size.X + gap)));
        for (var i = 0; i < UiTheme.All.Length; i++)
        {
            if (i % perRow != 0) ImGui.SameLine();
            ThemeSwatch(UiTheme.All[i], size);
        }
        EndCard();
    }

    // A miniature of the window in that theme: sidebar, card, accent title and primary button.
    private void ThemeSwatch(UiTheme t, Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var p0 = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("###theme" + t.Key, size)) { config.Theme = t.Key; config.Save(); }
        var hovered = ImGui.IsItemHovered();
        var selected = theme.Key == t.Key;
        var draw = ImGui.GetWindowDrawList();
        var r = 8 * scale;
        var label = ImGui.GetTextLineHeight() + 10 * scale;
        draw.AddRectFilled(p0, p0 + size, U32(t.Window with { W = 1 }), r);
        // Sidebar strip with a selected item.
        draw.AddRectFilled(p0 + new Vector2(7, 7) * scale, p0 + new Vector2(28 * scale, size.Y - label), U32(t.Child), 4 * scale);
        draw.AddRectFilled(p0 + new Vector2(7, 14) * scale, p0 + new Vector2(9, 22) * scale, U32(t.Accent));
        // Card with a title line, a text line and a primary button.
        var card0 = p0 + new Vector2(33, 7) * scale;
        var card1 = new Vector2(p0.X + size.X - 7 * scale, p0.Y + size.Y - label);
        draw.AddRectFilled(card0, card1, U32(t.Child), 4 * scale);
        draw.AddRect(card0, card1, U32(t.Border), 4 * scale);
        draw.AddRectFilled(card0 + new Vector2(6, 6) * scale, card0 + new Vector2(40, 10) * scale, U32(t.Accent), 2 * scale);
        draw.AddRectFilled(card0 + new Vector2(6, 15) * scale, card0 + new Vector2(60, 18) * scale, U32(t.Frame), 2 * scale);
        draw.AddRectFilled(new Vector2(card1.X - 30 * scale, card1.Y - 13 * scale), card1 - new Vector2(6, 5) * scale, U32(t.Primary), 3 * scale);
        draw.AddText(new Vector2(p0.X + 9 * scale, p0.Y + size.Y - label + 4 * scale), U32(selected ? t.Accent : new Vector4(.85f, .87f, .9f, 1)), t.Name);
        var outline = selected ? t.Accent : hovered ? t.Accent with { W = .55f } : t.Border;
        draw.AddRect(p0, p0 + size, U32(outline), r, ImDrawFlags.None, (selected ? 2.2f : 1f) * scale);
        if (hovered) ImGui.SetTooltip(selected ? L.T("Tema atual") : L.T("Usar este tema"));
    }
}
