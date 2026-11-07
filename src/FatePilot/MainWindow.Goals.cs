using Dalamud.Bindings.ImGui;
using FatePilot.Core;

namespace FatePilot;

internal sealed partial class MainWindow
{
    private void DrawGoalSettings()
    {
        var o = config.Options;
        var gems = o.ReturnAfterGemstones;
        ImGui.SetNextItemWidth(130);
        if (ImGui.InputInt(L.T("Gemstones a obter (0 = sem meta)"), ref gems))
        { o.ReturnAfterGemstones = Math.Clamp(gems, 0, 1000000); SaveSettings(); }
        var minutes = o.ReturnAfterMinutes;
        ImGui.SetNextItemWidth(130);
        if (ImGui.InputInt(L.T("Minutos de sessão (0 = sem meta)"), ref minutes))
        { o.ReturnAfterMinutes = Math.Clamp(minutes, 0, 10080); SaveSettings(); }
        ImGui.TextDisabled(L.T("Saldo máximo de gemstones e cidade de troca: Inventário → Gemstones."));
        if (ImGui.CollapsingHeader(L.T("Como as metas funcionam") + "###goalHelp"))
        {
            ImGui.TextWrapped(L.T("A primeira meta atingida solicita o retorno após o FATE atual e suas entregas. O saldo inicial não conta como ganho."));
            ImGui.TextWrapped(L.T("A meta conta sucessos observados em FATEs dos quais você participou. Desaparecimentos sem confirmação não contam."));
        }
    }

    private void DrawGoalProgress()
    {
        var o = config.Options;
        ImGui.Separator();
        var elapsed = TimeSpan.FromSeconds(runner.ElapsedSeconds);
        ImGui.TextUnformatted(L.T("Tempo de sessão: {0}", $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"));
        if (World.LoggedIn && !World.Loading && World.GemstoneCap > 0)
            ImGui.TextDisabled(L.T("Saldo de gemstones: {0} / {1}", World.Gemstones, World.GemstoneCap));
        if (o.ReturnAfterFates > 0) GoalProgress(SessionGoals.Label(ReturnGoal.Fates), runner.Completed, o.ReturnAfterFates);
        if (o.ReturnAfterGemstones > 0) GoalProgress(SessionGoals.Label(ReturnGoal.Gemstones), runner.GemstonesEarned, o.ReturnAfterGemstones);
        if (o.ReturnAfterMinutes > 0) GoalProgress(SessionGoals.Label(ReturnGoal.Time), (int)(runner.ElapsedSeconds / 60), o.ReturnAfterMinutes);
        ImGui.Spacing();
    }

    private static void GoalProgress(string label, int current, int target)
    {
        ImGui.TextUnformatted(label);
        ImGui.ProgressBar(Math.Clamp((float)current / target, 0, 1), new(-1, 0), $"{current:N0} / {target:N0}");
    }
}
