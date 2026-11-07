using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;

namespace FatePilot;

// 0.8 settings: notifications and automatic retry.
internal sealed partial class MainWindow
{
    private void DrawAlertSettings()
    {
        var o = config.Options;
        BeginCard("alerts");
        CardTitle(FontAwesomeIcon.Bell, L.T("Avisos"));
        ImGui.TextWrapped(L.T("Som do chat do jogo e uma linha local no chat. Nada é enviado para fora do jogo."));
        ImGui.Spacing();
        var sound = o.NotifySound;
        if (ImGui.Checkbox(L.T("Tocar som"), ref sound)) { o.NotifySound = sound; SaveSettings(); }
        ImGui.SameLine();
        ImGui.BeginDisabled(!o.NotifySound);
        var effect = o.NotifySoundEffect;
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("###sfx", ref effect, 1, 16, "<se.%d>")) { o.NotifySoundEffect = effect; SaveSettings(); }
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.VolumeUp))
            FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(o.NotifySoundEffect, 1, 16));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Testar o som"));
        ImGui.EndDisabled();
        var chat = o.NotifyChat;
        if (ImGui.Checkbox(L.T("Mostrar no chat"), ref chat)) { o.NotifyChat = chat; SaveSettings(); }
        ImGui.Spacing();
        ImGui.TextDisabled(L.T("Avisar quando:"));
        void Toggle(string label, bool value, Action<bool> set) { var v = value; if (ImGui.Checkbox(label, ref v)) { set(v); SaveSettings(); } }
        Toggle(L.T("a sessão parar por conta própria"), o.NotifyOnStop, v => o.NotifyOnStop = v);
        Toggle(L.T("uma meta for atingida ou o retorno terminar"), o.NotifyOnGoal, v => o.NotifyOnGoal = v);
        Toggle(L.T("um FATE priorizado aparecer"), o.NotifyOnPriorityFate, v => o.NotifyOnPriorityFate = v);
        Toggle(L.T("reparo, comida ou Chocobo falharem, ou houver uma retomada"), o.NotifyOnProblem, v => o.NotifyOnProblem = v);
        Toggle(L.T("chegar uma tell durante a sessão"), o.NotifyOnTell, v => o.NotifyOnTell = v);
        Help(L.T("Só avisa que chegou uma tell. Quem mandou e o texto nunca são lidos nem guardados."));
        EndCard();

        BeginCard("retry");
        CardTitle(FontAwesomeIcon.Redo, L.T("Retomada automática"));
        var retry = o.AutoRetry;
        if (ImGui.Checkbox(L.T("Tentar retomar após interrupções recuperáveis"), ref retry)) { o.AutoRetry = retry; SaveSettings(); }
        Help(L.T("Diálogo inesperado, tela de carregamento ou mudança de zona inesperada, ou módulo de viagem ocupado: o FatePilot devolve o controle, espera o jogo estabilizar (até 2 minutos) e continua. Morte, logout, conteúdo instanciado e chefes de grupo sempre param a sessão."));
        ImGui.BeginDisabled(!o.AutoRetry);
        var max = o.MaxRetries;
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt(L.T("Tentativas por sessão"), ref max, 1, 5)) { o.MaxRetries = max; SaveSettings(); }
        ImGui.EndDisabled();
        if (runner.IsRunning && runner.RetriesUsed > 0) ImGui.TextDisabled(L.T("Tentativas usadas nesta sessão: {0}", runner.RetriesUsed));
        EndCard();
    }
}
