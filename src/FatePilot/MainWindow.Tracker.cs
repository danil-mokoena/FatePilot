using Dalamud.Bindings.ImGui;
using FatePilot.Core;

namespace FatePilot;

internal sealed partial class MainWindow
{
    private void DrawTracker()
    {
        ImGui.TextColored(Gold, L.T("FATEs especiais"));
        ImGui.TextWrapped(L.T("O mundo acompanha o personagem, inclusive durante visitas a outros servidores."));
        ImGui.Spacing();
        var current = World.LoggedIn && !World.Loading ? World.CurrentWorld : null;
        if (current != null)
        {
            ImGui.TextUnformatted(L.T("Mundo atual: {0}", current.Name));
            ImGui.TextDisabled(L.T("Data center: {0}", current.DataCenter));
            if (ImGui.SmallButton(L.T("Copiar nome do mundo"))) ImGui.SetClipboardText(current.Name);
        }
        else ImGui.TextDisabled(L.T("Aguardando o mundo atual do personagem."));
        ImGui.Spacing();
        var link = FaloopLink.ForWorld(current);
        if (ImGui.Button(L.T("Abrir Faloop no navegador")))
            Dalamud.Utility.Util.OpenLink(link);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(link);
        if (FaloopLink.CanFilter(current))
            ImGui.TextWrapped(L.T("Abre o Faloop com o mundo {0} selecionado.", current!.Name));
        else ImGui.TextWrapped(L.T("Mundo indisponível para o link automático. Selecione o mundo no Faloop."));
        ImGui.TextWrapped(L.T("Os timers ainda não são importados para o plugin."));
        ImGui.Separator();
        ImGui.TextWrapped(L.T("Janelas de reaparecimento são estimativas da comunidade. Consultar o tracker não inicia combates nem altera os filtros de chefes."));
        ImGui.TextWrapped(L.T("O site só abre ao clicar. O link inclui apenas o nome do mundo; o navegador faz a conexão normal com o Faloop."));
    }
}
