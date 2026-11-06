using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

internal sealed partial class MainWindow
{
    private static void DrawFateProgress(FateInfo fate)
    {
        var progress = Math.Clamp(fate.Progress, 0, 100);
        var seconds = float.IsFinite(fate.SecondsLeft) ? (int)Math.Clamp(fate.SecondsLeft, 0, 86400) : 0;
        var timer = fate.State == EventState.Preparing ? L.T("Aguardando início")
            : L.T("{0} restantes", $"{seconds / 60:00}:{seconds % 60:00}");
        var start = ImGui.GetCursorPosX();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.TextDisabled($"{progress}%");
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), start + width - ImGui.CalcTextSize(timer).X));
        ImGui.TextDisabled(timer);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Gold);
        ImGui.ProgressBar(progress / 100f, new Vector2(-1, 6 * ImGuiHelpers.GlobalScale), "");
        ImGui.PopStyleColor();
    }
}
