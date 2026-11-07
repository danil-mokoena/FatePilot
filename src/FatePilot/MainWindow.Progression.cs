using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// 0.7: route for a job level, local zone history, reward weighting and zone timing.
internal sealed partial class MainWindow
{
    private readonly FateLevelCatalog levelCatalog = new();

    // Replace the route with the best unlocked zones for this level.
    private bool ApplyLevelRoute(int level)
    {
        var best = levelCatalog.Best(level, config.Options.LevelAbovePlayer, Destinations);
        if (best.Count == 0) return false;
        config.Options.Route = best.Select(z => z.Destination).ToList();
        SaveSettings();
        return true;
    }

    private string ZoneName(uint territory) => zones.FirstOrDefault(z => z.TerritoryId == territory)?.Name ?? territory.ToString();

    // ---------- Route page: route for my level ----------
    private void DrawLevelRoute()
    {
        levelCatalog.EnsureBuilt();
        BeginCard("levelRoute");
        CardTitle(FontAwesomeIcon.ChartLine, L.T("Rota para o meu nível"));
        if (!levelCatalog.Ready) { EmptyState(FontAwesomeIcon.Hourglass, L.T("Lendo os FATEs de cada zona nos dados do jogo…")); EndCard(); return; }
        var level = World.Level;
        var (min, max) = FateLevelCatalog.Band(level, config.Options.LevelAbovePlayer);
        GameIcon(JobIcon(World.Job), 26); ImGui.SameLine(); ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(L.T("Nível {0} · FATEs do nível {1} ao {2}", level, min, max));
        var best = levelCatalog.Best(level, config.Options.LevelAbovePlayer, Destinations);
        if (best.Count == 0) ImGui.TextDisabled(L.T("Nenhuma zona desbloqueada com FATEs nessa faixa."));
        foreach (var z in best)
        {
            GameIcon(AetheryteIcon, 16); ImGui.SameLine();
            ImGui.TextUnformatted(ZoneName(z.Destination.TerritoryId)); ImGui.SameLine();
            ImGui.TextDisabled(L.T("{0} FATEs na faixa", z.Fates));
        }
        ImGui.Spacing();
        ImGui.BeginDisabled(best.Count == 0);
        PushPrimaryButton();
        var apply = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Route, L.T("Montar esta rota"));
        ImGui.PopStyleColor(4);
        ImGui.EndDisabled();
        if (apply) ApplyLevelRoute(level);
        Help(L.T("Substitui a rota atual pelas zonas desbloqueadas com mais FATEs na faixa do seu nível (do seu nível −2 até o limite de níveis acima, em FATEs)."));
        EndCard();
    }

    // ---------- Route page: time per zone and idle behaviour ----------
    private void DrawRouteTiming()
    {
        var o = config.Options;
        BeginCard("routeTiming");
        CardTitle(FontAwesomeIcon.Stopwatch, L.T("Tempo e espera"));
        var minutes = o.MinutesPerZone;
        ImGui.SetNextItemWidth(130 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(L.T("Minutos em cada zona (0 = só quando ficar sem FATEs)"), ref minutes)) { o.MinutesPerZone = Math.Clamp(minutes, 0, 600); SaveSettings(); }
        var hotspot = o.IdleToHotspot;
        if (ImGui.Checkbox(L.T("Esperar no ponto onde mais aparecem FATEs"), ref hotspot)) { o.IdleToHotspot = hotspot; SaveSettings(); }
        Help(L.T("Sem FATE elegível, voa até o ponto da zona onde FATEs mais apareceram no seu histórico (depois de alguns registros)."));
        EndCard();
    }

    // ---------- FATEs page: reward weighting ----------
    private void DrawRewardOptions()
    {
        var o = config.Options;
        var prefer = o.PreferRewards;
        if (ImGui.Checkbox(L.T("Preferir FATEs com bônus mesmo um pouco mais longe"), ref prefer)) { o.PreferRewards = prefer; SaveSettings(); }
        ImGui.BeginDisabled(!o.PreferRewards);
        var worth = o.BonusWorthYalms;
        if (ImGui.SliderInt(L.T("Um bônus vale até (yalms a mais)"), ref worth, 50, 800)) { o.BonusWorthYalms = worth; SaveSettings(); }
        ImGui.EndDisabled();
    }

    // ---------- Session page: local history per zone ----------
    private void DrawZoneHistory()
    {
        var stats = config.History.Zones.Where(z => z.Value.Seconds >= 60).OrderByDescending(z => z.Value.GemsPerHour).ToList();
        BeginCard("zoneHistory");
        CardTitle(FontAwesomeIcon.History, L.T("Histórico por zona"));
        if (stats.Count == 0) { EmptyState(FontAwesomeIcon.ChartBar, L.T("O histórico aparece depois de alguns minutos de sessão.")); EndCard(); return; }
        var scale = ImGuiHelpers.GlobalScale;
        if (ImGui.BeginTable("zoneHistoryTable", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg, new Vector2(CardWidth(), 0)))
        {
            ImGui.TableSetupColumn(L.T("Zona"), ImGuiTableColumnFlags.WidthStretch, 2.2f);
            ImGui.TableSetupColumn(L.T("Tempo"), ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Gemstones/h", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn(L.T("FATEs/h"), ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn(L.T("Falhas"), ImGuiTableColumnFlags.WidthStretch, .8f);
            ImGui.TableHeadersRow();
            var first = true;
            foreach (var (territory, s) in stats.Take(12))
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 26 * scale);
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
                if (first) { ImGui.PushStyleColor(ImGuiCol.Text, Gold); DrawSymbol(FontAwesomeIcon.Crown); ImGui.PopStyleColor(); ImGui.SameLine(); }
                ImGui.TextUnformatted(ZoneName(territory));
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextDisabled(TimeSpan.FromSeconds(s.Seconds).ToString(@"h\:mm"));
                ImGui.TableNextColumn(); GameIcon(ItemIcon(26807), 16); ImGui.SameLine(); ImGui.TextColored(first ? Gold : ImGui.GetStyle().Colors[(int)ImGuiCol.Text], s.GemsPerHour.ToString("N0"));
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(s.FatesPerHour.ToString("N1"));
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding();
                ImGui.TextColored(s.FailureRate > .2 ? Danger : Muted, $"{s.FailureRate:P0}");
                first = false;
            }
            ImGui.EndTable();
        }
        if (ImGui.SmallButton(L.T("Limpar histórico"))) { config.History.Zones.Clear(); config.Save(); }
        EndCard();
    }
}
