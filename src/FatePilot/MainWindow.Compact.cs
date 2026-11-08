using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// Small always-on panel shown while a session runs: current FATE, progress,
// session numbers and Stop / Skip / open-full-window controls.
internal sealed partial class MainWindow
{
    private bool compact, wasRunning;

    // Session start hides the full window and shows the compact panel;
    // session end closes the panel and brings the full window back.
    private void SyncCompactMode()
    {
        if (runner.IsRunning && !wasRunning) { compact = true; open = false; }
        if (!runner.IsRunning && wasRunning) { if (compact) open = true; compact = false; }
        wasRunning = runner.IsRunning;
    }

    private void Minimize() { compact = true; open = false; }
    private void Expand() { compact = false; open = true; }

    private void DrawCompactWindow()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(380, 0) * scale, new Vector2(380 * scale, float.MaxValue));
        PushTheme();
        try
        {
            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollWithMouse;
            if (ImGui.Begin("FatePilot###FatePilotCompact", flags)) { PaintClassicBackground(false); DrawCompact(); }
            ImGui.End();
        }
        finally { ImGui.PopStyleColor(20); ImGui.PopStyleVar(5); }
    }

    private void DrawCompact()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var startX = ImGui.GetCursorPosX();
        var full = ImGui.GetContentRegionAvail().X;
        var f = runner.Selected;

        // Header: FATE icon, name, live status; FATE and gemstone counters on the right.
        var counter = L.T("{0} FATEs", runner.Completed);
        var gems = "+" + runner.GemstonesEarned.ToString("N0");
        var counterWidth = IconChipWidth(counter) + ImGui.GetStyle().ItemSpacing.X + IconChipWidth(gems);
        GameIcon(f != null ? FateIcon(f.Id) : AetheryteIcon, 40);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(startX + full - counterWidth - 8 * scale);
        ImGui.TextColored(f != null ? Gold : Muted, f?.Name ?? L.T("Nenhum FATE selecionado."));
        ImGui.TextDisabled(runner.Status);
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.SetCursorPosX(startX + full - counterWidth);
        IconChip(f != null ? FateIcon(f.Id) : 0, counter, Success);
        ImGui.SameLine();
        IconChip(ItemIcon(26807), gems, Gold);

        // Progress of the current FATE.
        ImGui.Spacing();
        if (f != null) DrawFateProgress(f);
        else ImGui.TextDisabled(L.T("Aguardando FATEs elegíveis."));

        // Optional live map (Map page → "Show the map in the compact panel").
        if (config.CompactMap && World.LoggedIn && !World.Loading && World.Territory != 0)
        {
            ImGui.Spacing();
            DrawZoneMap("###compactMapCanvas", new Vector2(full, 170 * scale), CompactMapRadius, false);
        }

        // Session numbers.
        ImGui.Spacing();
        var hasSeals = Progress.Seals is { Company: > 0 };
        if (ImGui.BeginTable("compactStats", hasSeals ? 4 : 3, ImGuiTableFlags.SizingStretchSame))
        {
            CompactStat(ItemIcon(26807), null, "Gemstones", gems);
            if (hasSeals)
            {
                CompactStat(SealIcon, null, "Seals", "+" + Progress.SealsEarned.ToString("N0"));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(SealTooltip());
            }
            CompactStat(0, FontAwesomeIcon.Stopwatch, L.T("Tempo"), TimeSpan.FromSeconds(runner.ElapsedSeconds).ToString(@"h\:mm\:ss"));
            CompactStat(ItemIcon(1), null, "Gil", (runtime.ReservedGil + runner.GreensGilCommitted).ToString("N0"));
            ImGui.EndTable();
        }

        // Experience bar for a job that is still levelling (hidden at the job's max level).
        if (Progress.Exp is { MaxLevel: false })
        {
            ImGui.Spacing();
            DrawExpBar(full);
        }

        // Alerts worth noticing at a glance.
        var greens = World.GreensInventory;
        if (Error != null) Alert(Error, Danger);
        else if (runner.PendingReturnGoal != ReturnGoal.None) Alert(L.T("Retorno após este FATE."), Gold);
        else if (config.Options.SummonChocobo && greens.Ready && greens.Count <= 3)
            Alert(L.T("Gysahl Greens: {0} restantes", greens.Count), Danger);

        // What comes after the current FATE.
        var none = new Dictionary<FateKey, double>();
        var next = Selection.Choose(World with { Fates = World.Fates.Where(x => x.Id != f?.Id).ToList() }, config.Options, none, 0);
        if (next != null) ImGui.TextDisabled(L.T("Próximo: {0}", next.Name));

        // Controls: Stop, Skip, and open the full window on the right.
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, Danger with { W = .14f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Danger with { W = .28f });
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Danger with { W = .4f });
        ImGui.PushStyleColor(ImGuiCol.Text, Danger);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Stop, L.T("Parar") + "###compactStop")) request("stop");
        ImGui.PopStyleColor(4);
        ImGui.SameLine();
        if (runner.IsPaused)
        { if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, L.T("Continuar") + "###compactResume")) request("resume"); }
        else if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Pause, L.T("Pausar") + "###compactPause")) request("pause");
        ImGui.SameLine();
        ImGui.BeginDisabled(f == null);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FastForward, L.T("Pular") + "###compactSkip")) request("skip");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(L.T("Ignora este FATE por 5 minutos e segue para o próximo."));
        ImGui.SameLine();
        ImGui.SetCursorPosX(startX + full - IconButtonWidth(FontAwesomeIcon.ExternalLinkAlt));
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ExternalLinkAlt)) Expand();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Abrir a janela completa"));
    }

    // Label on top, icon + value below. Game icon when there is one, otherwise a gold glyph.
    private static void CompactStat(uint gameIcon, FontAwesomeIcon? glyph, string label, string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        if (gameIcon != 0) GameIcon(gameIcon, 18);
        else if (glyph is { } g) { ImGui.PushStyleColor(ImGuiCol.Text, Gold); DrawSymbol(g); ImGui.PopStyleColor(); }
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }

    private static float IconChipWidth(string text) =>
        ImGui.GetTextLineHeight() + ImGui.CalcTextSize(text).X + 18 * ImGuiHelpers.GlobalScale;

    // Pill with a small game icon followed by text.
    private static void IconChip(uint icon, string text, Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var line = ImGui.GetTextLineHeight();
        var size = new Vector2(IconChipWidth(text), line + 4 * scale);
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(color with { W = .16f }), size.Y / 2);
        draw.AddRect(origin, origin + size, ImGui.ColorConvertFloat4ToU32(color with { W = .55f }), size.Y / 2);
        var iconPos = origin + new Vector2(6 * scale, 2 * scale);
        if (icon != 0 && Services.Textures.TryGetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(icon), out var shared)
            && shared != null && shared.TryGetWrap(out var texture, out _))
            draw.AddImage(texture.Handle, iconPos, iconPos + new Vector2(line));
        draw.AddText(origin + new Vector2(6 * scale + line + 6 * scale, 2 * scale), ImGui.ColorConvertFloat4ToU32(color), text);
        ImGui.Dummy(size);
    }

    // Tinted one-line notice with a warning icon.
    private static void Alert(string text, Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padding = new Vector2(8, 5) * scale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetTextLineHeight() + padding.Y * 2;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(color with { W = .14f }), 5 * scale);
        draw.AddRect(origin, origin + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(color with { W = .45f }), 5 * scale);
        ImGui.SetCursorScreenPos(origin + padding);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        DrawSymbol(FontAwesomeIcon.ExclamationTriangle); ImGui.SameLine();
        ImGui.PushClipRect(origin, origin + new Vector2(width - padding.X, height), true);
        ImGui.TextUnformatted(text);
        ImGui.PopClipRect();
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(origin + new Vector2(0, height + ImGui.GetStyle().ItemSpacing.Y));
        ImGui.Dummy(new Vector2(0, 0));
    }
}
