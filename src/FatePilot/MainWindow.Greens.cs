using Dalamud.Bindings.ImGui;
using FatePilot.Core;

namespace FatePilot;

internal sealed partial class MainWindow
{
    private void DrawGreensSettings()
    {
        ImGui.Spacing(); ImGui.Separator();
        ImGui.TextColored(Gold, "Gysahl Greens");
        if (World.GreensInventory.Ready)
            ImGui.TextUnformatted(L.T("Na bolsa: {0}  |  Gil comprometido na reposição: {1}", World.GreensInventory.Count, runner.GreensGilCommitted));
        var o = config.Options;
        var enabled = o.RestockGreens;
        if (ImGui.Checkbox(L.T("Comprar Gysahl Greens automaticamente"), ref enabled)) { o.RestockGreens = enabled; SaveSettings(); }
        if (enabled)
        {
            var threshold = o.GreensThreshold;
            ImGui.SetNextItemWidth(140);
            if (ImGui.InputInt(L.T("Comprar quando houver esta quantidade ou menos"), ref threshold))
            { o.GreensThreshold = Math.Clamp(threshold, 0, 998); o.GreensTarget = Math.Max(o.GreensTarget, o.GreensThreshold + 1); SaveSettings(); }
            ImGui.TextDisabled(L.T("Atalhos para o mínimo:"));
            foreach (var value in new[] { 0, 1, 10, 20, 35, 99 })
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(value + "###greensMinimum" + value))
                { o.GreensThreshold = value; o.GreensTarget = Math.Max(o.GreensTarget, value + 1); SaveSettings(); }
            }
            var target = o.GreensTarget;
            ImGui.SetNextItemWidth(140);
            if (ImGui.InputInt(L.T("Repor até esta quantidade"), ref target))
            { o.GreensTarget = Math.Clamp(target, o.GreensThreshold + 1, 999); SaveSettings(); }
            var budget = o.GreensGilLimit;
            ImGui.SetNextItemWidth(140);
            if (ImGui.InputInt(L.T("Orçamento de Gysahl Greens por sessão (gil)"), ref budget))
            { o.GreensGilLimit = Math.Clamp(budget, 1, 1000000); SaveSettings(); }
            var count = World.GreensInventory.Ready ? World.GreensInventory.Count : 0;
            if (World.GreensInventory.Ready)
                ImGui.TextDisabled(L.T("Estimativa para o estoque desejado: {0} gil, sem teleportes.", Math.Max(0, o.GreensTarget - count) * GreensRestock.Price));
            ImGui.TextWrapped(L.T("Ao atingir o mínimo, termina o FATE, compra em Limsa Lominsa e retorna à zona da rota. Requer o aetheryte desbloqueado."));
            ImGui.TextWrapped(L.T("O orçamento de compras é separado do limite de teleportes. O estoque desejado precisa ser maior que o mínimo."));
            ImGui.TextDisabled(L.T("A reposição não invoca o chocobo automaticamente."));
        }
    }
}
