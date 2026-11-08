using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

internal sealed partial class MainWindow
{
    // 1.3: experience and seals earned this session (filled by the plugin's update loop).
    public SessionProgress Progress { get; } = new();
    private static readonly Vector4 ExpColor = new(.38f, .72f, .98f, 1);

    private uint SealIcon => ItemIcon(ProgressReader.SealItem(Progress.Seals?.Company ?? 0));

    private string PerHour(long earned) =>
        runner.ElapsedSeconds >= 300 && earned > 0 ? $"  ·  {earned * 3600 / runner.ElapsedSeconds:N0}/h" : "";

    private static string JobName(uint job)
    {
        var name = Services.Data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().GetRowOrDefault(job)?.Name.ExtractText() ?? "";
        return name.Length == 0 ? "" : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
    }

    private string? ExpTooltip()
    {
        if (Progress.Exp is not { } exp) return null;
        if (exp.MaxLevel) return L.T("{0} está no nível máximo.", JobName(exp.Job));
        var text = L.T("{0} / {1} EXP ({2}%)", exp.Current.ToString("N0"), exp.Needed.ToString("N0"), (exp.Fraction * 100).ToString("N0"))
            + "\n" + L.T("Faltam {0} para o nível {1}.", Math.Max(0, exp.Needed - exp.Current).ToString("N0"), exp.Level + 1);
        if (Progress.LevelsGained > 0) text += "\n" + L.T("Níveis ganhos nesta sessão: {0}", Progress.LevelsGained);
        return text;
    }

    private string? SealTooltip()
    {
        if (Progress.Seals is not { Company: > 0 } seals) return null;
        var text = L.T("Saldo: {0}", seals.Count.ToString("N0") + (seals.Cap > 0 ? " / " + seals.Cap.ToString("N0") : ""));
        if (seals.Cap > 0 && seals.Count >= seals.Cap) text += "\n" + L.T("Limite de seals atingido: novos seals serão perdidos.");
        return text;
    }

    // Job, level and session gain on one line, then a thin bar like the game's own experience bar.
    private void DrawExpBar(float width)
    {
        if (Progress.Exp is not { } exp) return;
        var start = ImGui.GetCursorPosX();
        GameIcon(ProgressReader.ExpIcon, 18);
        ImGui.SameLine();
        ImGui.TextColored(ExpColor, $"Lv {exp.Level}");
        ImGui.SameLine();
        ImGui.TextDisabled(JobName(exp.Job));
        var gained = "+" + L.T("{0} EXP", Progress.ExpEarned.ToString("N0"))
            + (Progress.LevelsGained > 0 ? "  ·  " + L.T("+{0} nível", Progress.LevelsGained) : "");
        var percent = $"  ·  {exp.Fraction * 100:N0}%";
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), start + width - ImGui.CalcTextSize(gained + percent).X));
        ImGui.TextColored(Success, gained);
        ImGui.SameLine(0, 0);
        ImGui.TextDisabled(percent);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, ExpColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.16f, .18f, .22f, 1));
        ImGui.ProgressBar((float)exp.Fraction, new Vector2(width, 6 * ImGuiHelpers.GlobalScale), "");
        ImGui.PopStyleColor(2);
        if (ImGui.IsItemHovered() && ExpTooltip() is { } tip) ImGui.SetTooltip(tip);
    }
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
