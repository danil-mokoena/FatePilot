using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using FatePilot.Core;

namespace FatePilot;

internal sealed partial class MainWindow
{
    private void DrawGreensSettings()
    {
        BeginCard("chocoboCompanion");
        IconText(ChocoboIcon, L.T("Companheiro chocobo"));
        var summon = config.Options.SummonChocobo;
        if (ImGui.Checkbox(L.T("Invocar o chocobo automaticamente"), ref summon))
        { config.Options.SummonChocobo = summon; SaveSettings(); }
        Help(L.T("Usa um Gysahl Greens quando o companheiro não está ativo, fora de combate e após desmontar. Não renova o tempo de um chocobo ativo."));
        if (World.CompanionSecondsLeft is > 0)
            ImGui.TextColored(Gold, L.T("Chocobo ativo: {0}", TimeSpan.FromSeconds(World.CompanionSecondsLeft.Value).ToString(@"mm\:ss")));
        else ImGui.TextDisabled(World.CompanionSecondsLeft.HasValue ? L.T("Chocobo não invocado.") : L.T("Estado do chocobo indisponível."));
        if (runner.IsRunning && runner.CompanionStatus != null) ImGui.TextWrapped(runner.CompanionStatus);
        Help(L.T("Invocação e reposição são opções independentes. Sem Greens, a rota continua; ative a reposição abaixo para comprar mais."));
        EndCard();
        BeginCard("greensStock");
        ImGui.TextColored(Gold, L.T("Reposição de estoque"));
        var o = config.Options;
        var enabled = o.RestockGreens;
        if (ImGui.Checkbox(L.T("Comprar Gysahl Greens automaticamente"), ref enabled)) { o.RestockGreens = enabled; SaveSettings(); }
        Help(L.T("Ao atingir o mínimo, termina o FATE, compra em Limsa Lominsa e retorna à zona da rota. Requer o aetheryte desbloqueado."));
        var scale = ImGuiHelpers.GlobalScale;
        if (ImGui.BeginTable("stock", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH, new System.Numerics.Vector2(CardWidth(), 0)))
        {
            ImGui.TableSetupColumn(L.T("Item"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(L.T("Na bolsa"), ImGuiTableColumnFlags.WidthFixed, 70 * scale);
            ImGui.TableSetupColumn(L.T("Comprar se ≤"), ImGuiTableColumnFlags.WidthFixed, 115 * scale);
            ImGui.TableSetupColumn(L.T("Repor até"), ImGuiTableColumnFlags.WidthFixed, 115 * scale);
            ImGui.TableHeadersRow(); ImGui.TableNextRow();
            ImGui.TableNextColumn(); GameIcon(ItemIcon(GreensRestock.ItemId), 32); ImGui.SameLine();
            ImGui.BeginGroup(); ImGui.TextUnformatted("Gysahl Greens"); ImGui.TextDisabled("Limsa Lominsa"); ImGui.EndGroup();
            ImGui.TableNextColumn();
            ImGui.TextColored(World.GreensInventory.Ready && World.GreensInventory.Count <= o.GreensThreshold ? Gold : ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
                World.GreensInventory.Ready ? World.GreensInventory.Count.ToString("N0") : "—");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            var threshold = o.GreensThreshold;
            if (ImGui.InputInt("###greensThreshold", ref threshold, 0, 0))
            { o.GreensThreshold = Math.Clamp(threshold, 0, 998); o.GreensTarget = Math.Max(o.GreensTarget, o.GreensThreshold + 1); SaveSettings(); }
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1);
            var target = o.GreensTarget;
            if (ImGui.InputInt("###greensTarget", ref target, 0, 0))
            { o.GreensTarget = Math.Clamp(target, o.GreensThreshold + 1, 999); SaveSettings(); }
            ImGui.EndTable();
        }
        if (World.GreensInventory.Ready)
        {
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Gold);
            ImGui.ProgressBar(Math.Clamp((float)World.GreensInventory.Count / Math.Max(1, o.GreensTarget), 0, 1), new(CardWidth(), 6 * scale), "");
            ImGui.PopStyleColor();
            ImGui.TextDisabled(L.T("{0} / {1} no estoque", World.GreensInventory.Count, o.GreensTarget));
        }
        var budget = o.GreensGilLimit;
        ImGui.SetNextItemWidth(140 * scale);
        if (ImGui.InputInt(L.T("Orçamento de Gysahl Greens por sessão (gil)"), ref budget))
        { o.GreensGilLimit = Math.Clamp(budget, 1, 1000000); SaveSettings(); }
        IconText(ItemIcon(1), L.T("Gil comprometido na reposição: {0}", runner.GreensGilCommitted));
        if (ImGui.CollapsingHeader(L.T("Detalhes da reposição") + "###greensDetails"))
        {
            ImGui.TextDisabled(L.T("Atalhos para o mínimo:"));
            foreach (var value in new[] { 0, 1, 10, 20, 35, 99 })
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(value + "###greensMinimum" + value))
                { o.GreensThreshold = value; o.GreensTarget = Math.Max(o.GreensTarget, value + 1); SaveSettings(); }
            }
            if (World.GreensInventory.Ready)
                ImGui.TextWrapped(L.T("Estimativa para o estoque desejado: {0} gil, sem teleportes.", Math.Max(0, o.GreensTarget - World.GreensInventory.Count) * GreensRestock.Price));
            ImGui.TextWrapped(L.T("O orçamento de compras é separado do limite de teleportes. O estoque desejado precisa ser maior que o mínimo."));
        }
        EndCard();
    }
}
