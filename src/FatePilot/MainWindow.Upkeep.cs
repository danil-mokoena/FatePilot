using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;
using System.Text.Json;

namespace FatePilot;

// 0.5 pages and cards: Inventory (repair, food, bag limits), Jobs (level queue),
// route profiles and the end-of-session summary.
internal sealed partial class MainWindow
{
    public List<GearsetInfo> Gearsets { get; set; } = [];
    public List<FoodChoice> Foods { get; set; } = [];
    public int DarkMatter { get; set; }
    private string profileName = "";
    private int selectedProfile = -1;

    private const uint DarkMatterIcon = 33916;   // Grade 8 Dark Matter (item)
    private const uint MealIcon = 4650;          // generic meal item for the food card
    private static uint JobIcon(uint job) => 62100 + job;

    private static void IconTitle(uint gameIcon, string title)
    {
        GameIcon(gameIcon, 22); ImGui.SameLine(); ImGui.AlignTextToFramePadding(); ImGui.TextColored(Gold, title);
        ImGui.Dummy(new Vector2(0, 2 * ImGuiHelpers.GlobalScale));
    }

    // A thin labelled bar; red below the danger threshold.
    private static void LevelBar(float fraction, string text, bool danger)
    {
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, danger ? Danger : Success);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.16f, .18f, .22f, 1));
        ImGui.ProgressBar(Math.Clamp(fraction, 0, 1), new Vector2(CardWidth(), 8 * ImGuiHelpers.GlobalScale), "");
        ImGui.PopStyleColor(2);
        ImGui.TextDisabled(text);
    }

    // ================= Inventory page =================
    private void DrawInventory()
    {
        var o = config.Options;
        DrawGemstones(o);

        BeginCard("repair");
        IconTitle(ItemIcon(DarkMatterIcon) is > 0 and var dm ? dm : 0, L.T("Reparo"));
        if (World.MinDurability >= 0)
            LevelBar(World.MinDurability / 100f, L.T("Menor durabilidade equipada: {0}%", World.MinDurability), World.MinDurability < o.RepairBelowPercent);
        else ImGui.TextDisabled(L.T("Durabilidade indisponível no momento."));
        ImGui.Spacing();
        var repair = o.SelfRepair;
        if (ImGui.Checkbox(L.T("Reparar automaticamente com Dark Matter"), ref repair)) { o.SelfRepair = repair; SaveSettings(); }
        Help(L.T("Entre um FATE e outro, fora de combate, usa a janela de reparo do jogo (Reparar tudo) e confirma. Exige Dark Matter e nível de artesão suficiente; se o reparo não for confirmado, fica desativado até a próxima sessão."));
        ImGui.BeginDisabled(!o.SelfRepair);
        var below = o.RepairBelowPercent;
        if (ImGui.SliderInt(L.T("Reparar abaixo de (%)"), ref below, 5, 90)) { o.RepairBelowPercent = below; SaveSettings(); }
        ImGui.EndDisabled();
        GameIcon(ItemIcon(DarkMatterIcon), 18); ImGui.SameLine(); ImGui.AlignTextToFramePadding();
        if (DarkMatter > 0) ImGui.TextDisabled(L.T("Dark Matter na bolsa: {0}", DarkMatter));
        else ImGui.TextColored(Danger, L.T("Nenhum Dark Matter na bolsa."));
        EndCard();

        BeginCard("food");
        var chosen = Foods.FirstOrDefault(f => f.ItemId == o.FoodItemId && f.Hq == o.FoodHq);
        IconTitle(chosen?.Icon ?? ItemIcon(MealIcon), L.T("Comida"));
        var eat = o.UseFood;
        if (ImGui.Checkbox(L.T("Manter o bônus de comida ativo"), ref eat)) { o.UseFood = eat; SaveSettings(); }
        Help(L.T("Come a comida escolhida fora de combate quando o Well Fed estiver para acabar. Se o efeito não for confirmado, o uso fica suspenso até a próxima sessão."));
        ImGui.SetNextItemWidth(Math.Min(CardWidth(), 360 * ImGuiHelpers.GlobalScale));
        var preview = chosen != null ? FoodLabel(chosen) : o.FoodItemId != 0 ? L.T("Comida escolhida não está na bolsa") : L.T("Escolher comida da bolsa");
        if (ImGui.BeginCombo("###food", preview))
        {
            if (Foods.Count == 0) ImGui.TextDisabled(L.T("Nenhuma comida na bolsa."));
            foreach (var food in Foods)
            {
                ImGui.PushID($"{food.ItemId}{food.Hq}");
                var pos = ImGui.GetCursorPos();
                if (ImGui.Selectable("###pick", food.ItemId == o.FoodItemId && food.Hq == o.FoodHq, ImGuiSelectableFlags.None, new Vector2(0, 26 * ImGuiHelpers.GlobalScale)))
                { o.FoodItemId = food.ItemId; o.FoodHq = food.Hq; SaveSettings(); }
                ImGui.SetCursorPos(pos);
                GameIcon(food.Icon, 24); ImGui.SameLine(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(FoodLabel(food));
                ImGui.PopID();
            }
            ImGui.EndCombo();
        }
        ImGui.BeginDisabled(!o.UseFood);
        var refresh = o.FoodRefreshMinutes;
        if (ImGui.SliderInt(L.T("Renovar com menos de (min)"), ref refresh, 1, 30)) { o.FoodRefreshMinutes = refresh; SaveSettings(); }
        ImGui.EndDisabled();
        if (World.FoodSecondsLeft is { } left)
        {
            if (left > 0) LevelBar(left / 3600f, L.T("Well Fed: {0} restantes", TimeSpan.FromSeconds(left).ToString(@"mm\:ss")), left < o.FoodRefreshMinutes * 60);
            else ImGui.TextDisabled(L.T("Sem bônus de comida ativo."));
        }
        EndCard();

        BeginCard("bagLimits");
        CardTitle(FontAwesomeIcon.BoxOpen, L.T("Limites da bolsa e do equipamento"));
        if (World.FreeBagSlots >= 0)
            LevelBar(1 - World.FreeBagSlots / 140f, L.T("Espaços livres na bolsa: {0} de 140", World.FreeBagSlots),
                o.ReturnWhenBagSlotsBelow > 0 && World.FreeBagSlots < o.ReturnWhenBagSlotsBelow);
        ImGui.Spacing();
        var slots = o.ReturnWhenBagSlotsBelow;
        ImGui.SetNextItemWidth(130 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(L.T("Voltar com menos de X espaços livres (0 = desativado)"), ref slots)) { o.ReturnWhenBagSlotsBelow = Math.Clamp(slots, 0, 140); SaveSettings(); }
        var durability = o.ReturnWhenDurabilityBelow;
        ImGui.SetNextItemWidth(130 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(L.T("Voltar com durabilidade abaixo de X% (0 = desativado)"), ref durability)) { o.ReturnWhenDurabilityBelow = Math.Clamp(durability, 0, 99); SaveSettings(); }
        Help(L.T("Funcionam como metas: o FATE atual termina e o personagem volta à cidade de retorno. Com o reparo automático ativo, a volta por durabilidade só acontece se o reparo falhar."));
        EndCard();
    }

    // Balance vs. cap, a target balance (stored as free space below the cap) and the trader city.
    private void DrawGemstones(RunOptions o)
    {
        BeginCard("gemstones");
        IconTitle(ItemIcon(26807), "Gemstones");
        var cap = World.GemstoneCap > 0 ? World.GemstoneCap : 1500;
        if (World.LoggedIn && World.GemstoneCap > 0)
            LevelBar((float)World.Gemstones / cap, L.T("Saldo: {0} / {1}", World.Gemstones.ToString("N0"), cap.ToString("N0")),
                o.AvoidGemstoneCap && World.Gemstones >= cap - o.GemstoneCapReserve);
        ImGui.Spacing();
        var avoid = o.AvoidGemstoneCap;
        if (ImGui.Checkbox(L.T("Voltar ao atingir um saldo de gemstones"), ref avoid)) { o.AvoidGemstoneCap = avoid; SaveSettings(); }
        Help(L.T("Quando o saldo chega ao valor escolhido, o FATE atual termina e o personagem vai trocar as gemstones. Deixe uma folga abaixo do limite para a recompensa do último FATE; não há garantia contra excesso."));
        ImGui.BeginDisabled(!o.AvoidGemstoneCap);
        var target = Math.Clamp(cap - o.GemstoneCapReserve, 0, cap);
        ImGui.SetNextItemWidth(Math.Min(CardWidth() * .6f, 320 * ImGuiHelpers.GlobalScale));
        if (ImGui.SliderInt(L.T("Saldo para voltar"), ref target, 100, cap)) { o.GemstoneCapReserve = Math.Clamp(cap - target, 0, cap); SaveSettings(); }
        ImGui.SameLine(); ImGui.TextDisabled(L.T("folga de {0}", o.GemstoneCapReserve));
        ImGui.EndDisabled();
        ImGui.Spacing();
        var traders = Destinations.Where(GemstoneTraders.Has).ToList();
        GameIcon(AetheryteIcon, 20); ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Min(CardWidth() * .6f, 320 * ImGuiHelpers.GlobalScale));
        if (ImGui.BeginCombo(L.T("Cidade para trocar gemstones"), o.GemstoneDestination?.Name ?? L.T("Mesma cidade de retorno")))
        {
            if (ImGui.Selectable(L.T("Mesma cidade de retorno"), o.GemstoneDestination == null)) { o.GemstoneDestination = null; SaveSettings(); }
            foreach (var city in traders)
                if (ImGui.Selectable(city.Name, o.GemstoneDestination?.AetheryteId == city.AetheryteId)) { o.GemstoneDestination = city; SaveSettings(); }
            if (traders.Count == 0) ImGui.TextDisabled(L.T("Nenhuma cidade com trocador de gemstones desbloqueada."));
            ImGui.EndCombo();
        }
        Help(L.T("Usada quando a volta é causada por gemstones (saldo ou meta de gemstones). As outras metas usam a cidade de retorno da Sessão."));
        EndCard();
    }

    private static string FoodLabel(FoodChoice f) => f.Name + (f.Hq ? " (HQ)" : "") + $"  ×{f.Count}";

    // ================= Jobs page =================
    private void DrawJobs()
    {
        var o = config.Options;
        BeginCard("jobQueue");
        CardTitle(FontAwesomeIcon.ListOl, L.T("Fila de classes"));
        var use = o.UseJobQueue;
        if (ImGui.Checkbox(L.T("Usar fila de classes"), ref use)) { o.UseJobQueue = use; SaveSettings(); }
        Help(L.T("Entre um FATE e outro, quando a classe atual atinge o nível-alvo, o FatePilot equipa o próximo gearset da fila e continua. Ao terminar a fila, volta à cidade de retorno."));
        ImGui.Spacing();
        if (o.JobQueue.Count == 0) EmptyState(FontAwesomeIcon.UserPlus, L.T("Adicione gearsets abaixo para montar a fila."));
        for (var i = 0; i < o.JobQueue.Count; i++)
        {
            var goal = o.JobQueue[i];
            ImGui.PushID(i);
            var gear = Gearsets.FirstOrDefault(g => g.Index == goal.Gearset);
            var current = World.Job == goal.Job && runner.IsRunning;
            GameIcon(JobIcon(goal.Job), 30);
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextColored(current ? Gold : ImGui.GetStyle().Colors[(int)ImGuiCol.Text], $"{i + 1}. {goal.Name}");
            var level = gear?.Level ?? 0;
            ImGui.TextDisabled(level > 0 ? L.T("Nível {0} → meta {1}", level, goal.TargetLevel) : L.T("Meta: nível {0}", goal.TargetLevel));
            ImGui.EndGroup();
            if (level >= goal.TargetLevel) { ImGui.SameLine(); Chip(L.T("Concluída"), Success); }
            else if (current) { ImGui.SameLine(); Chip(L.T("Agora"), Gold); }
            var controls = 120 * ImGuiHelpers.GlobalScale + IconButtonWidth(FontAwesomeIcon.ChevronUp) * 3 * 1.4f;
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, CardWidth() - controls));
            var target = goal.TargetLevel;
            ImGui.SetNextItemWidth(110 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputInt("###target", ref target)) { goal.TargetLevel = Math.Clamp(target, 1, 100); SaveSettings(); }
            ImGui.SameLine();
            ImGui.BeginDisabled(i == 0);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ChevronUp)) { (o.JobQueue[i], o.JobQueue[i - 1]) = (o.JobQueue[i - 1], goal); SaveSettings(); }
            ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.BeginDisabled(i == o.JobQueue.Count - 1);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.ChevronDown)) { (o.JobQueue[i], o.JobQueue[i + 1]) = (o.JobQueue[i + 1], goal); SaveSettings(); }
            ImGui.EndDisabled(); ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt)) { o.JobQueue.RemoveAt(i); SaveSettings(); ImGui.PopID(); break; }
            ImGui.PopID();
            ImGui.Separator();
        }
        EndCard();

        BeginCard("gearsets");
        CardTitle(FontAwesomeIcon.Tshirt, L.T("Seus gearsets de combate"));
        if (Gearsets.Count == 0) EmptyState(FontAwesomeIcon.Hourglass, L.T("Nenhum gearset de combate encontrado."));
        var columns = Math.Clamp((int)(CardWidth() / (230 * ImGuiHelpers.GlobalScale)), 1, 3);
        if (Gearsets.Count > 0 && ImGui.BeginTable("gearsetGrid", columns, ImGuiTableFlags.SizingStretchSame, new Vector2(CardWidth(), 0)))
        {
            levelCatalog.EnsureBuilt();
            var scale = ImGuiHelpers.GlobalScale;
            foreach (var g in Gearsets)
            {
                ImGui.TableNextColumn();
                ImGui.PushID(g.Index);
                // Fixed layout per cell: job icon and two text lines centred together, and the
                // two buttons always in the same right-hand column, one under the other row to row.
                var cellLeft = ImGui.GetCursorPosX();
                var cellWidth = ImGui.GetContentRegionAvail().X;
                var top = ImGui.GetCursorPosY() + 3 * scale;
                var textHeight = ImGui.GetTextLineHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y;
                var iconSize = 28 * scale;
                ImGui.SetCursorPos(new Vector2(cellLeft, top + Math.Max(0, (textHeight - iconSize) / 2)));
                GameIcon(JobIcon(g.Job), 28);
                ImGui.SameLine();
                ImGui.SetCursorPosY(top);
                ImGui.BeginGroup();
                ImGui.TextUnformatted(g.Name);
                ImGui.TextDisabled(L.T("Nível {0} · gearset {1}", g.Level, g.Index + 1));
                ImGui.EndGroup();
                var buttons = (IconButtonWidth(FontAwesomeIcon.Route) + IconButtonWidth(FontAwesomeIcon.Plus)) * 1.15f + ImGui.GetStyle().ItemSpacing.X;
                ImGui.SetCursorPos(new Vector2(cellLeft + Math.Max(0, cellWidth - buttons), top + Math.Max(0, (textHeight - ImGui.GetFrameHeight()) / 2)));
                var hasRoute = levelCatalog.Best(g.Level, o.LevelAbovePlayer, Destinations).Count > 0;
                ImGui.BeginDisabled(!hasRoute || g.Level >= 100);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Route)) ApplyLevelRoute(g.Level);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(g.Level >= 100 ? L.T("Nível máximo.") : L.T("Montar a rota com as zonas de FATEs para o nível {0}", g.Level));
                ImGui.SameLine();
                var queued = o.JobQueue.Any(j => j.Gearset == g.Index);
                ImGui.BeginDisabled(queued);
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
                { o.JobQueue.Add(new JobGoal { Gearset = g.Index, Job = g.Job, Name = g.Name, TargetLevel = Math.Min(100, Math.Max(g.Level + 1, 100)) }); SaveSettings(); }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(L.T(queued ? "Já está na fila" : "Adicionar à fila"));
                ImGui.SetCursorPosY(top + textHeight + 6 * scale);
                ImGui.Dummy(Vector2.Zero);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        EndCard();
    }

    // ================= Route profiles =================
    private void DrawProfiles()
    {
        BeginCard("profiles");
        CardTitle(FontAwesomeIcon.Bookmark, L.T("Perfis"));
        ImGui.TextWrapped(L.T("Salve tudo (rota, filtros, metas, gastos, inventário e classes) com um nome e carregue com um clique."));
        ImGui.Spacing();
        var profiles = config.Profiles;
        if (selectedProfile >= profiles.Count) selectedProfile = -1;
        ImGui.SetNextItemWidth(Math.Min(CardWidth() * .5f, 260 * ImGuiHelpers.GlobalScale));
        if (ImGui.BeginCombo("###profile", selectedProfile >= 0 ? profiles[selectedProfile].Name : L.T("Escolher perfil")))
        {
            if (profiles.Count == 0) ImGui.TextDisabled(L.T("Nenhum perfil salvo."));
            for (var i = 0; i < profiles.Count; i++)
                if (ImGui.Selectable(profiles[i].Name + "###p" + i, i == selectedProfile)) { selectedProfile = i; profileName = profiles[i].Name; }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(selectedProfile < 0);
        PushPrimaryButton();
        var load = ImGuiComponents.IconButtonWithText(FontAwesomeIcon.FolderOpen, L.T("Carregar"));
        ImGui.PopStyleColor(4);
        if (load)
        {
            config.Options = JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(profiles[selectedProfile].Options))!;
            SaveSettings();
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.TrashAlt)) { profiles.RemoveAt(selectedProfile); selectedProfile = -1; config.Save(); }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(L.T("Excluir perfil"));
        ImGui.EndDisabled();

        ImGui.SetNextItemWidth(Math.Min(CardWidth() * .5f, 260 * ImGuiHelpers.GlobalScale));
        ImGui.InputTextWithHint("###profileName", L.T("Nome do perfil (ex.: Farm de gemstones)"), ref profileName, 40);
        ImGui.SameLine();
        var name = profileName.Trim();
        ImGui.BeginDisabled(name.Length == 0);
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save, L.T("Salvar")))
        {
            var copy = JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(config.Options))!;
            var existing = profiles.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) profiles[existing].Options = copy;
            else profiles.Add(new RouteProfile { Name = name, Options = copy });
            selectedProfile = existing >= 0 ? existing : profiles.Count - 1;
            config.Save();
        }
        ImGui.EndDisabled();
        if (name.Length > 0 && profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        { ImGui.SameLine(); ImGui.TextDisabled(L.T("substitui o perfil existente")); }
        EndCard();
    }

    // ================= Session summary =================
    private void DrawSummary()
    {
        if (runner.IsRunning || runner.LastSummary is not { } s) return;
        BeginCard("summary");
        CardTitle(FontAwesomeIcon.Trophy, L.T("Última sessão"));
        ImGui.TextDisabled(s.EndedAt.ToString("dd/MM HH:mm") + "  ·  " + s.Reason);
        ImGui.Spacing();
        var hours = s.Seconds / 3600;
        string PerHour(int value) => hours >= 1d / 12 ? $"  ·  {value / hours:N0}/h" : "";
        if (ImGui.BeginTable("summaryStats", 4, ImGuiTableFlags.SizingStretchSame, new Vector2(CardWidth(), 0)))
        {
            SummaryStat(60722, L.T("FATEs concluídos"), s.Completed.ToString("N0") + PerHour(s.Completed));
            SummaryStat(ItemIcon(26807), "Gemstones", s.Gemstones.ToString("N0") + PerHour(s.Gemstones));
            SummaryStat(0, L.T("Tempo"), TimeSpan.FromSeconds(s.Seconds).ToString(@"h\:mm\:ss"));
            SummaryStat(ItemIcon(1), "Gil", (runtime.ReservedGil + s.Gil).ToString("N0"));
            SummaryStat(ItemIcon(GreensRestock.ItemId), "Gysahl Greens", s.GreensUsed.ToString("N0"));
            SummaryStat(ItemIcon(MealIcon), L.T("Comida"), s.FoodUsed.ToString("N0"));
            SummaryStat(ItemIcon(DarkMatterIcon), L.T("Reparos"), s.Repairs.ToString("N0"));
            SummaryStat(JobIcon(World.Job), L.T("Trocas de classe"), s.JobSwitches.ToString("N0"));
            ImGui.EndTable();
        }
        if (s.Unconfirmed > 0) ImGui.TextDisabled(L.T("Sem confirmação: {0}", s.Unconfirmed));
        EndCard();
    }

    private static void SummaryStat(uint icon, string label, string value)
    {
        ImGui.TableNextColumn();
        if (icon != 0) { GameIcon(icon, 24); ImGui.SameLine(); }
        else { ImGui.PushStyleColor(ImGuiCol.Text, Gold); DrawSymbol(FontAwesomeIcon.Stopwatch); ImGui.PopStyleColor(); ImGui.SameLine(); }
        ImGui.BeginGroup();
        ImGui.TextColored(Gold, value);
        ImGui.TextDisabled(label);
        ImGui.EndGroup();
    }
}
