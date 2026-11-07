using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// 0.9 keyboard shortcuts (Settings) and the slash commands that work as game macros.
internal sealed partial class MainWindow
{
    private string? capturingHotkey;
    private double captureEndedAt = double.NegativeInfinity;
    private static double Seconds => Environment.TickCount64 / 1000d;

    // While a shortcut is being recorded (and just after), the plugin must not run it.
    public bool HotkeysBlocked => capturingHotkey != null || Seconds - captureEndedAt < .6;

    public void Toggle()
    {
        if (open) { open = false; compact = runner.IsRunning; }
        else { open = true; compact = false; }
    }

    private static (FontAwesomeIcon Icon, string Label) HotkeyInfo(string action) => action switch
    {
        "toggle" => (FontAwesomeIcon.Pause, L.T("Pausar / continuar")),
        "skip" => (FontAwesomeIcon.FastForward, L.T("Pular o FATE atual")),
        "stop" => (FontAwesomeIcon.Stop, L.T("Parar a sessão")),
        "start" => (FontAwesomeIcon.Play, L.T("Iniciar (com a confirmação de sempre)")),
        _ => (FontAwesomeIcon.WindowMaximize, L.T("Mostrar / esconder a janela")),
    };

    private void DrawHotkeySettings()
    {
        var scale = ImGuiHelpers.GlobalScale;
        BeginCard("hotkeys");
        CardTitle(FontAwesomeIcon.Keyboard, L.T("Atalhos de teclado"));
        ImGui.TextWrapped(L.T("Ctrl e/ou Alt com uma letra, número ou tecla F. Não funcionam enquanto você digita no chat. Escolha combinações que o jogo não usa: a tecla também chega ao jogo."));
        ImGui.Spacing();
        if (ImGui.BeginTable("hotkeyTable", 2, ImGuiTableFlags.SizingStretchProp, new Vector2(CardWidth(), 0)))
        {
            ImGui.TableSetupColumn("action", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("binding", ImGuiTableColumnFlags.WidthFixed, 270 * scale);
            foreach (var action in Hotkeys.Actions)
            {
                ImGui.PushID(action);
                var (icon, label) = HotkeyInfo(action);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.PushStyleColor(ImGuiCol.Text, Gold); DrawSymbol(icon); ImGui.PopStyleColor();
                ImGui.SameLine(); ImGui.TextUnformatted(label);
                ImGui.TableNextColumn();
                var binding = config.Hotkeys.FirstOrDefault(b => b.Action == action && Hotkeys.IsValid(b));
                var recording = capturingHotkey == action;
                var text = recording ? L.T("Pressione a combinação… (Esc cancela)") : binding != null ? Hotkeys.Label(binding) : L.T("Sem atalho");
                if (recording) PushPrimaryButton();
                var clicked = ImGui.Button(text + "###bind", new Vector2(200 * scale, 0));
                if (recording) ImGui.PopStyleColor(4);
                if (clicked) { if (recording) EndCapture(); else capturingHotkey = action; }
                if (binding != null && !recording)
                {
                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Times)) { config.Hotkeys.RemoveAll(b => b.Action == action); config.Save(); }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Remover atalho"));
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (capturingHotkey != null) CaptureHotkey();
        EndCard();

        BeginCard("macros");
        CardTitle(FontAwesomeIcon.Terminal, L.T("Comandos para macros"));
        ImGui.TextWrapped(L.T("Crie uma macro do jogo com um destes comandos e coloque na hotbar."));
        ImGui.Spacing();
        foreach (var (command, description) in new[]
        {
            ("/fatepilot toggle", L.T("Pausa ou continua a sessão")),
            ("/fatepilot skip", L.T("Pula o FATE atual por 5 minutos")),
            ("/fatepilot stop", L.T("Para a sessão")),
            ("/fatepilot start", L.T("Abre a confirmação de início")),
            ("/fatepilot", L.T("Abre esta janela")),
        })
        {
            ImGui.TextColored(Gold, command);
            ImGui.SameLine(190 * scale);
            ImGui.TextDisabled(description);
        }
        EndCard();
    }

    private void EndCapture() { capturingHotkey = null; captureEndedAt = Seconds; }

    // Records Ctrl/Alt (+Shift) + key from the keyboard state; Esc cancels.
    private void CaptureHotkey()
    {
        var keys = Services.Keys;
        if (keys[(VirtualKey)Hotkeys.EscapeKey]) { EndCapture(); return; }
        bool ctrl = keys[(VirtualKey)Hotkeys.CtrlKey], alt = keys[(VirtualKey)Hotkeys.AltKey], shift = keys[(VirtualKey)Hotkeys.ShiftKey];
        if (!ctrl && !alt) return;
        foreach (var key in Hotkeys.CandidateKeys())
        {
            if (!keys[(VirtualKey)key]) continue;
            var binding = new HotkeyBinding { Action = capturingHotkey!, Key = key, Ctrl = ctrl, Alt = alt, Shift = shift };
            config.Hotkeys.RemoveAll(b => b.Action == binding.Action || Hotkeys.SameCombo(b, binding));
            config.Hotkeys.Add(binding);
            config.Save();
            EndCapture();
            return;
        }
    }
}
