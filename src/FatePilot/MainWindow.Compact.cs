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
            if (ImGui.Begin("FatePilot###FatePilotCompact", flags)) DrawCompact();
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

        // Header: FATE icon, name, live status, completed counter on the right.
        var counter = L.T("{0} FATEs", runner.Completed);
        var counterWidth = ImGui.CalcTextSize(counter).X + 14 * scale;
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
        Chip(counter, Success);

        // Progress of the current FATE.
        ImGui.Spacing();
        if (f != null) DrawFateProgress(f);
        else ImGui.TextDisabled(L.T("Aguardando FATEs elegíveis."));

        // Session numbers.
        ImGui.Spacing();
        if (ImGui.BeginTable("compactStats", 3, ImGuiTableFlags.SizingStretchSame))
        {
            CompactStat("Gemstones", "+" + runner.GemstonesEarned.ToString("N0"));
            CompactStat(L.T("Tempo"), TimeSpan.FromSeconds(runner.ElapsedSeconds).ToString(@"h\:mm\:ss"));
            CompactStat("Gil", (runtime.ReservedGil + runner.GreensGilCommitted).ToString("N0"));
            ImGui.EndTable();
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
        ImGui.BeginDisabled(f == null);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FastForward, L.T("Pular") + "###compactSkip")) request("skip");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(L.T("Ignora este FATE por 5 minutos e segue para o próximo."));
        ImGui.SameLine();
        ImGui.SetCursorPosX(startX + full - IconButtonWidth(FontAwesomeIcon.ExternalLinkAlt));
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ExternalLinkAlt)) Expand();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Abrir a janela completa"));
    }

    private static void CompactStat(string label, string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.TextUnformatted(value);
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
