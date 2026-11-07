using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

internal sealed partial class MainWindow
{
    // inTable: the cell already ends before the card padding, so use its full width.
    private static void DrawFateProgress(FateInfo fate, bool inTable = false)
    {
        var width = inTable ? ImGui.GetContentRegionAvail().X : CardWidth();
        var progress = Math.Clamp(fate.Progress, 0, 100);
        var seconds = float.IsFinite(fate.SecondsLeft) ? (int)Math.Clamp(fate.SecondsLeft, 0, 86400) : 0;
        var timer = fate.State == EventState.Preparing ? L.T("Aguardando início")
            : L.T("{0} restantes", $"{seconds / 60:00}:{seconds % 60:00}");
        var start = ImGui.GetCursorPosX();
        ImGui.TextDisabled($"{progress}%");
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), start + width - ImGui.CalcTextSize(timer).X));
        // Last two minutes of a running FATE: timer turns warm red so it reads as urgent.
        var urgent = fate.State != EventState.Preparing && seconds is > 0 and < 120;
        if (urgent) ImGui.TextColored(Danger, timer); else ImGui.TextDisabled(timer);
        // Bar is gold while in progress and green once the FATE is nearly done.
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, progress >= 90 ? Success : Gold);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.16f, .18f, .22f, 1));
        ImGui.ProgressBar(progress / 100f, new Vector2(width, 6 * ImGuiHelpers.GlobalScale), "");
        ImGui.PopStyleColor(2);
    }
}
