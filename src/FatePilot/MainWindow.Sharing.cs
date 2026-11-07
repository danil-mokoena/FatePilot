using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// 0.9 shareable profiles: copy a saved profile as a text code, paste a code to preview it and
// save it as a new profile. Nothing is sent anywhere; the player moves the text themselves.
internal sealed partial class MainWindow
{
    private string importCode = "", importError = "";
    private (string Name, RunOptions Options)? imported;
    private double copiedAt = double.NegativeInfinity;

    private void DrawProfileSharing()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var profiles = config.Profiles;
        BeginCard("profileSharing");
        CardTitle(FontAwesomeIcon.ShareAlt, L.T("Compartilhar perfis"));
        ImGui.TextWrapped(L.T("Copie um perfil como código de texto e cole onde quiser. O código leva só as configurações da sessão; a fila de classes fica de fora. O FatePilot não envia nada."));
        ImGui.Spacing();

        ImGui.BeginDisabled(selectedProfile < 0 || selectedProfile >= profiles.Count);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, L.T("Copiar código do perfil escolhido")))
        {
            var profile = profiles[selectedProfile];
            ImGui.SetClipboardText(ProfileCode.Encode(profile.Name, profile.Options));
            copiedAt = Seconds;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (Seconds - copiedAt < 2.5) ImGui.TextColored(Success, L.T("Copiado!"));
        else if (selectedProfile < 0) ImGui.TextDisabled(L.T("Escolha um perfil acima."));
        else ImGui.NewLine();

        ImGui.Spacing();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var buttons = IconButtonWidth(FontAwesomeIcon.Paste) + ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.FileImport, L.T("Ler código")) + spacing * 2;
        ImGui.SetNextItemWidth(Math.Max(120 * scale, CardWidth() - buttons));
        if (ImGui.InputTextWithHint("###importCode", L.T("Cole aqui um código FATEPILOT1:…"), ref importCode, ProfileCode.MaxCodeLength))
        { imported = null; importError = ""; }
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Paste))
        {
            var clip = ImGui.GetClipboardText() ?? "";
            imported = null;
            importError = clip.Length > ProfileCode.MaxCodeLength ? L.T("Código de perfil grande demais.") : "";
            importCode = clip.Length > ProfileCode.MaxCodeLength ? "" : clip.Trim();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Colar da área de transferência"));
        ImGui.SameLine();
        ImGui.BeginDisabled(importCode.Trim().Length == 0);
        PushPrimaryButton();
        var read = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FileImport, L.T("Ler código"));
        ImGui.PopStyleColor(4);
        ImGui.EndDisabled();
        if (read)
        {
            if (ProfileCode.TryDecode(importCode, out var name, out var options, out var error)) { imported = (name, WithLocalNames(options)); importError = ""; }
            else { imported = null; importError = error; }
        }
        if (importError.Length > 0) { ImGui.PushStyleColor(ImGuiCol.Text, Danger); ImGui.TextWrapped(importError); ImGui.PopStyleColor(); }
        if (imported is { } preview) DrawImportPreview(preview.Name, preview.Options);
        EndCard();
    }

    private void DrawImportPreview(string name, RunOptions options)
    {
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, name);
        if (options.Route.Count == 0) ImGui.TextDisabled(L.T("Sem zonas na rota."));
        foreach (var d in options.Route)
        {
            GameIcon(AetheryteIcon, 18); ImGui.SameLine(); ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(d.Name);
            if (!IsUnlocked(d)) { ImGui.SameLine(); Chip(L.T("não desbloqueado"), Danger); }
        }
        if (options.ReturnDestination is { } home) ImGui.TextDisabled(L.T("Cidade de retorno: {0}", home.Name));
        ImGui.TextDisabled(L.T("Depois de salvar, confira Suprimentos (gastos) e Inventário antes de iniciar."));
        ImGui.Spacing();
        PushPrimaryButton();
        var save = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save, L.T("Salvar como perfil"));
        ImGui.PopStyleColor(4);
        ImGui.SameLine();
        if (ImGui.Button(L.T("Cancelar") + "###cancelImport")) { imported = null; importCode = ""; }
        if (!save) return;
        var unique = name;
        for (var n = 2; config.Profiles.Any(p => string.Equals(p.Name, unique, StringComparison.OrdinalIgnoreCase)); n++) unique = $"{name} ({n})";
        config.Profiles.Add(new RouteProfile { Name = unique, Options = options });
        selectedProfile = config.Profiles.Count - 1;
        profileName = unique;
        imported = null; importCode = "";
        config.Save();
    }

    private bool IsUnlocked(Destination d) => Destinations.Any(x => x.AetheryteId == d.AetheryteId && x.TerritoryId == d.TerritoryId);

    // Names inside a code come from someone else's client: show this client's own names when known.
    private RunOptions WithLocalNames(RunOptions o)
    {
        Destination Local(Destination d) => Destinations.FirstOrDefault(x => x.AetheryteId == d.AetheryteId && x.TerritoryId == d.TerritoryId) ?? d;
        o.Route = o.Route.Select(Local).ToList();
        if (o.ReturnDestination is { } home) o.ReturnDestination = Local(home);
        if (o.GemstoneDestination is { } gems) o.GemstoneDestination = Local(gems);
        return o;
    }
}
