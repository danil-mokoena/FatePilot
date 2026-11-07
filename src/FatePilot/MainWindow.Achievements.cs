using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// Achievements page: FATE achievements grouped like the in-game log, with progress and
// one-click actions (prioritise a boss FATE, route to a zone).
internal sealed partial class MainWindow
{
    public AchievementCatalog Achievements { get; } = new();
    public bool AchievementsVisible => open && page == Page.Achievements;
    private readonly HashSet<AchievementGroup> collapsedGroups = [];
    private bool hideCompleted = true;
    private string achievementSearch = "";
    private readonly Dictionary<uint, float> achievementRowHeights = [];

    private static string GroupLabel(AchievementGroup g) => g switch
    {
        AchievementGroup.Fate => L.T("Chefes e FATEs específicos"),
        AchievementGroup.Zone => L.T("Por zona (Shared FATE)"),
        _ => L.T("Contagem de FATEs"),
    };

    private static FontAwesomeIcon GroupIcon(AchievementGroup g) => g switch
    {
        AchievementGroup.Fate => FontAwesomeIcon.Dragon,
        AchievementGroup.Zone => FontAwesomeIcon.MapMarkedAlt,
        _ => FontAwesomeIcon.ListOl,
    };

    private void DrawAchievements()
    {
        Achievements.EnsureBuilt();
        if (Achievements.Building)
        {
            BeginCard("achievementsLoading");
            EmptyState(FontAwesomeIcon.Hourglass, L.T("Lendo as conquistas e as zonas dos FATEs nos dados do jogo…"));
            EndCard();
            return;
        }
        var all = Achievements.Rows;
        var done = all.Count(a => a.Complete);
        ImGui.TextDisabled(L.T("{0} de {1} concluídas", done, all.Count));
        if (!Achievements.Loaded)
        {
            BeginCard("achievementsHint");
            CardTitle(FontAwesomeIcon.InfoCircle, L.T("Abra as conquistas do jogo uma vez"));
            ImGui.TextWrapped(L.T("O jogo só carrega as conquistas depois que a janela de conquistas é aberta. Abra-a uma vez (atalho padrão: tecla do menu de personagem → Conquistas) e volte aqui."));
            EndCard();
        }

        ImGui.SetNextItemWidth(Math.Min(CardWidth() * .5f, 300 * ImGuiHelpers.GlobalScale));
        ImGui.InputTextWithHint("###achSearch", L.T("Buscar conquista ou FATE"), ref achievementSearch, 80);
        ImGui.SameLine();
        ImGui.Checkbox(L.T("Ocultar concluídas"), ref hideCompleted);
        ImGui.Spacing();

        foreach (var group in new[] { AchievementGroup.Fate, AchievementGroup.Zone, AchievementGroup.Count })
        {
            var rows = all.Where(a => a.Group == group).ToList();
            var missing = rows.Count(a => !a.Complete);
            var shown = rows.Where(a => (!hideCompleted || !a.Complete) &&
                (achievementSearch.Length == 0 || a.Name.Contains(achievementSearch, StringComparison.CurrentCultureIgnoreCase)
                 || a.Description.Contains(achievementSearch, StringComparison.CurrentCultureIgnoreCase))).ToList();
            BeginCard("achGroup" + group);
            var collapsed = collapsedGroups.Contains(group);
            // Clickable header: caret, group icon and title, "N to go" counter.
            var headerStart = ImGui.GetCursorPos();
            if (ImGui.InvisibleButton("###toggle" + group, new Vector2(CardWidth(), ImGui.GetFrameHeight())))
            { if (!collapsedGroups.Remove(group)) collapsedGroups.Add(group); }
            var hovered = ImGui.IsItemHovered();
            ImGui.SetCursorPos(headerStart + new Vector2(0, ImGui.GetStyle().FramePadding.Y));
            ImGui.PushStyleColor(ImGuiCol.Text, hovered ? Gold : Muted);
            DrawSymbol(collapsed ? FontAwesomeIcon.CaretRight : FontAwesomeIcon.CaretDown);
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Gold); DrawSymbol(GroupIcon(group)); ImGui.PopStyleColor();
            ImGui.SameLine(); ImGui.TextColored(Gold, GroupLabel(group));
            ImGui.SameLine(); ImGui.TextDisabled(missing > 0 ? L.T("{0} faltando", missing) : L.T("Tudo concluído"));
            ImGui.SetCursorPos(headerStart + new Vector2(0, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y));
            if (!collapsed)
            {
                if (shown.Count == 0) EmptyState(FontAwesomeIcon.Check, L.T("Nada para mostrar aqui."));
                else DrawAchievementTable(group, shown);
            }
            EndCard();
        }
    }

    private void DrawAchievementTable(AchievementGroup group, List<FateAchievement> rows)
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (!ImGui.BeginTable("achTable" + group, 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg, new Vector2(CardWidth(), 0)))
            return;
        ImGui.TableSetupColumn(L.T("Conquista"), ImGuiTableColumnFlags.WidthStretch, 3);
        ImGui.TableSetupColumn(L.T("Progresso"), ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, 84 * scale);
        ImGui.TableHeadersRow();
        foreach (var a in rows)
        {
            ImGui.PushID((int)a.Id);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, 46 * scale);
            ImGui.TableNextColumn();
            // Small breathing room at the top of every row.
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4 * scale);
            var rowTop = ImGui.GetCursorPosY();
            // The icon is centred against the text height measured on the previous frame.
            var iconSize = 36 * scale;
            var knownHeight = achievementRowHeights.GetValueOrDefault(a.Id, iconSize);
            ImGui.SetCursorPosY(rowTop + Math.Max(0, (knownHeight - iconSize) / 2));
            GameIcon(a.Icon, 36);
            ImGui.SameLine();
            ImGui.SetCursorPosY(rowTop);
            ImGui.BeginGroup();
            ImGui.TextColored(a.Complete ? Success : ImGui.GetStyle().Colors[(int)ImGuiCol.Text], a.Name);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
            ImGui.TextDisabled(a.Description);
            ImGui.PopTextWrapPos();
            if (a.Territory != 0 && zones.FirstOrDefault(z => z.TerritoryId == a.Territory) is { } zoneCard)
            {
                GameIcon(AetheryteIcon, 14); ImGui.SameLine();
                ImGui.TextColored(Gold with { W = .85f }, zoneCard.Name);
            }
            ImGui.EndGroup();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(a.Description);
            // The row is as tall as the name + wrapped description (+ zone); the other
            // columns are centred vertically against that height instead of hugging the top.
            var rowHeight = Math.Max(ImGui.GetItemRectSize().Y, iconSize);
            achievementRowHeights[a.Id] = rowHeight;
            ImGui.SetCursorPosY(rowTop + rowHeight);
            ImGui.Dummy(new Vector2(0, 4 * scale)); // matching breathing room at the bottom
            void CenterIn(float contentHeight) => ImGui.SetCursorPosY(rowTop + Math.Max(0, (rowHeight - contentHeight) / 2));

            ImGui.TableNextColumn();
            var line = ImGui.GetTextLineHeight();
            if (a.Complete) { CenterIn(line + 4 * scale); Chip(L.T("Concluída"), Success); }
            else if (a.Max > 0)
            {
                CenterIn(line + ImGui.GetStyle().ItemSpacing.Y + 6 * scale);
                ImGui.TextUnformatted(L.T("{0} de {1}", a.Current.ToString("N0"), a.Max.ToString("N0")));
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, Gold);
                ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.16f, .18f, .22f, 1));
                ImGui.ProgressBar(Math.Clamp((float)a.Current / a.Max, 0, 1), new Vector2(-1, 6 * scale), "");
                ImGui.PopStyleColor(2);
            }
            else { CenterIn(line); ImGui.TextDisabled(a.Group == AchievementGroup.Fate ? L.T("Pendente") : "—"); }

            ImGui.TableNextColumn();
            CenterIn(ImGui.GetFrameHeight());
            DrawAchievementActions(a);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawAchievementActions(FateAchievement a)
    {
        var o = config.Options;
        if (a.Complete) return;
        if (a.FateIds.Length > 0)
        {
            var prioritized = a.FateIds.All(o.PriorityFates.Contains);
            if (prioritized) PushPrimaryButton();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Crosshairs))
            {
                if (prioritized) o.PriorityFates.RemoveAll(a.FateIds.Contains);
                else foreach (var id in a.FateIds) if (!o.PriorityFates.Contains(id)) o.PriorityFates.Add(id);
                SaveSettings();
            }
            if (prioritized) ImGui.PopStyleColor(4);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T(prioritized ? "Prioridade ativa: este FATE é escolhido primeiro quando aparecer (clique para remover)" : "Priorizar este FATE quando ele aparecer"));
            ImGui.SameLine();
        }
        var territory = a.Territory != 0 ? a.Territory
            : a.FateIds.Select(id => config.FateZones.TryGetValue(id, out var t) ? t : 0).FirstOrDefault(t => t != 0);
        var destination = territory == 0 ? null : Destinations.FirstOrDefault(d => d.TerritoryId == territory);
        ImGui.BeginDisabled(destination == null);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Route) && destination != null)
        {
            o.Route = [destination];
            SaveSettings();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(destination != null ? L.T("Montar a rota só com {0}", zones.FirstOrDefault(z => z.TerritoryId == territory)?.Name ?? destination.Name)
                : territory == 0 ? L.T("Zona não encontrada nos dados do jogo para este FATE.")
                : L.T("Sem aetheryte desbloqueado nesta zona."));
    }
}
