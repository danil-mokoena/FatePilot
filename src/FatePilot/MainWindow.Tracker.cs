using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using FatePilot.Core;

namespace FatePilot;

internal sealed partial class MainWindow
{
    private void DrawTracker()
    {
        var current = World.LoggedIn && !World.Loading ? World.CurrentWorld : null;

        BeginCard("trackerWorld");
        CardTitle(FontAwesomeIcon.GlobeAmericas, L.T("FATEs especiais"));
        ImGui.TextWrapped(L.T("O mundo acompanha o personagem, inclusive durante visitas a outros servidores."));
        ImGui.Spacing();
        if (current != null)
        {
            ImGui.TextUnformatted(L.T("Mundo atual: {0}", current.Name));
            ImGui.SameLine(); Chip(current.DataCenter, Muted);
            ImGui.Spacing();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, L.T("Copiar nome do mundo"))) ImGui.SetClipboardText(current.Name);
        }
        else EmptyState(FontAwesomeIcon.Hourglass, L.T("Aguardando o mundo atual do personagem."));
        EndCard();

        BeginCard("trackerLink");
        CardTitle(FontAwesomeIcon.ExternalLinkAlt, "Faloop");
        var link = FaloopLink.ForWorld(current);
        PushPrimaryButton();
        var openLink = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.ExternalLinkAlt, L.T("Abrir Faloop no navegador"));
        ImGui.PopStyleColor(4);
        if (openLink) Dalamud.Utility.Util.OpenLink(link);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(link);
        ImGui.Spacing();
        if (FaloopLink.CanFilter(current))
            ImGui.TextWrapped(L.T("Abre o Faloop com o mundo {0} selecionado.", current!.Name));
        else ImGui.TextWrapped(L.T("Mundo indisponível para o link automático. Selecione o mundo no Faloop."));
        ImGui.TextDisabled(L.T("Os timers ainda não são importados para o plugin."));
        EndCard();

        BeginCard("trackerNotes");
        CardTitle(FontAwesomeIcon.InfoCircle, L.T("Como funciona"));
        ImGui.TextWrapped(L.T("Janelas de reaparecimento são estimativas da comunidade. Consultar o tracker não inicia combates nem altera os filtros de chefes."));
        ImGui.Spacing();
        ImGui.TextWrapped(L.T("O site só abre ao clicar. O link inclui apenas o nome do mundo; o navegador faz a conexão normal com o Faloop."));
        EndCard();
    }
}
