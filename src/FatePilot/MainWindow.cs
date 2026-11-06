using Dalamud.Bindings.ImGui;
using FatePilot.Core;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;

namespace FatePilot;

internal sealed partial class MainWindow(Configuration config, Runner runner, GameRuntime runtime, Action<string> request)
{
    public static readonly uint[] CityTerritories = [129, 130, 132, 418, 478, 628, 819, 962, 1185, 1186];
    public List<Destination> Destinations { get; set; } = [];
    private readonly List<ZoneCard> zones = ZoneCatalog.Read();
    private static readonly string Version = typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(Plugin).Assembly.GetName().Version!.ToString(3);
    public List<(string Name, bool Available)> Dependencies { get; set; } = [];
    public World World { get; set; } = new();
    public string[] Messages { get; set; } = [];
    public string? Error { get; set; }
    private bool open;
    public bool IsOpen => open;
    private string search = "";
    private static readonly Vector4 Gold = new(0.87f, 0.72f, 0.46f, 1);
    private readonly Dalamud.Interface.Textures.ISharedImmediateTexture logo = Services.Textures.GetFromManifestResource(Assembly.GetExecutingAssembly(), "FatePilot.Assets.logo.png");
    private readonly Dalamud.Interface.Textures.ISharedImmediateTexture cat = Services.Textures.GetFromManifestResource(Assembly.GetExecutingAssembly(), "FatePilot.Assets.cat.png");
    public void Open() => open = true;

    private void SaveSettings()
    {
        config.Save();
        if (runner.IsRunning) request("settings");
    }

    public void Draw()
    {
        if (!open) return;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(1040, 740) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(820, 560) * scale, new(float.MaxValue, float.MaxValue));
        PushTheme();
        try
        {
            if (ImGui.Begin("FatePilot###FatePilotMain", ref open, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)) DrawShell();
            ImGui.End();
            if (!open && startPrompt) { CancelStart(); request("cancel-start"); }
        }
        finally { ImGui.PopStyleColor(20); ImGui.PopStyleVar(5); }
    }

    private void DrawLogo(float size)
    {
        if (logo.TryGetWrap(out var texture, out _)) ImGui.Image(texture.Handle, new(size, size));
        else ImGui.Dummy(new(size, size));
    }

    private static void Stat(string id, string label, string value, uint icon)
    {
        ImGui.TableNextColumn();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * ImGuiHelpers.GlobalScale);
        ImGui.BeginChild(id, new Vector2(0, 80 * ImGuiHelpers.GlobalScale), true, ImGuiWindowFlags.NoScrollbar);
        GameIcon(icon, 24); ImGui.SameLine(); ImGui.TextColored(Gold, value);
        ImGui.TextWrapped(label);
        ImGui.EndChild(); ImGui.PopStyleVar();
    }

    private static void PushTheme()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9, 5) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f * scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.055f, .065f, .085f, .98f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.08f, .095f, .12f, 1));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.91f, .91f, .89f, 1));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled, new Vector4(.59f, .64f, .71f, 1));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.21f, .25f, .30f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.11f, .13f, .17f, 1));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.16f, .19f, .24f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.40f, .32f, .20f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.50f, .39f, .23f, 1));
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Gold);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Gold);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(.30f, .25f, .17f, 1));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(.40f, .32f, .20f, 1));
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(.10f, .12f, .16f, 1));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(.32f, .28f, .21f, 1));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(.27f, .23f, .17f, 1));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(.055f, .065f, .085f, 1));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(.09f, .11f, .14f, 1));
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, new Vector4(.98f, .83f, .57f, 1));
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(.21f, .25f, .30f, 1));
    }

    private void DrawSession()
    {
        if (ImGui.BeginTable("sessionStats", 4, ImGuiTableFlags.SizingStretchSame))
        {
            Stat("complete", L.T("FATEs concluídos"), runner.Completed.ToString(), 60721);
            Stat("gemstones", L.T("Gemstones obtidas"), runner.GemstonesEarned.ToString("N0"), ItemIcon(26807));
            Stat("zones", L.T("Zonas na rota"), config.Options.Route.Count.ToString(), AetheryteIcon);
            Stat("gil", L.T("Gil comprometido"), (runtime.ReservedGil + runner.GreensGilCommitted).ToString("N0"), ItemIcon(1));
            ImGui.EndTable();
        }
        if (runner.Unconfirmed > 0) ImGui.TextDisabled(L.T("Concluídos: {0}  |  Sem confirmação: {1}", runner.Completed, runner.Unconfirmed));
        ImGui.Spacing();
        BeginCard("returnGoals");
        ImGui.TextColored(Gold, L.T("Metas de retorno"));
        var o = config.Options;
        var goal = o.ReturnAfterFates;
        ImGui.SetNextItemWidth(130 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(L.T("Voltar após X FATEs (0 = sem meta)"), ref goal)) { o.ReturnAfterFates = Math.Clamp(goal, 0, 9999); SaveSettings(); }
        DrawGoalSettings();
        GameIcon(AetheryteIcon); ImGui.SameLine();
        if (ImGui.BeginCombo(L.T("Cidade de retorno"), o.ReturnDestination?.Name ?? L.T("Selecionar")))
        {
            foreach (var city in Destinations.Where(d => CityTerritories.Contains(d.TerritoryId)))
                if (ImGui.Selectable(city.Name, o.ReturnDestination?.AetheryteId == city.AetheryteId)) { o.ReturnDestination = city; SaveSettings(); }
            ImGui.EndCombo();
        }
        DrawGoalProgress();
        EndCard();
        BeginCard("travelPreferences");
        ImGui.TextColored(Gold, L.T("Movimento"));
        var mount = o.UseMount;
        if (ImGui.Checkbox(L.T("Usar montaria"), ref mount)) { o.UseMount = mount; SaveSettings(); }
        var fly = o.UseFlight;
        if (ImGui.Checkbox(L.T("Voar onde estiver desbloqueado"), ref fly)) { o.UseFlight = fly; SaveSettings(); }
        EndCard();
    }

    private void DrawEntryFilters()
    {
        var o = config.Options;
        var bonus = o.PreferBonus;
        if (ImGui.Checkbox(L.T("Preferir bônus em empate de distância"), ref bonus)) { o.PreferBonus = bonus; SaveSettings(); }
        var above = o.LevelAbovePlayer;
        if (ImGui.SliderInt(L.T("Níveis acima do personagem"), ref above, 0, 10)) { o.LevelAbovePlayer = above; SaveSettings(); }
        var progress = o.MaximumProgress;
        if (ImGui.SliderInt(L.T("Progresso máximo para entrar (%)"), ref progress, 10, 100)) { o.MaximumProgress = progress; SaveSettings(); }
        var time = o.MinimumSecondsLeft;
        if (ImGui.SliderInt(L.T("Tempo restante mínimo (segundos)"), ref time, 0, 600)) { o.MinimumSecondsLeft = time; SaveSettings(); }
    }

    private void DrawRoute()
    {
        if (runner.IsRunning) ImGui.TextWrapped(L.T("Edite livremente. As mudanças valem após o FATE ou a ação em andamento."));
        BeginCard("selectedRoute"); DrawSelectedRoute(); EndCard();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("###zoneSearch", L.T("Buscar zona ou aetheryte"), ref search, 100);
        foreach (var expansion in zones.GroupBy(z => (z.ExpansionId, z.Expansion)))
        {
            var matching = expansion.Where(z => z.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                Destinations.Any(d => d.TerritoryId == z.TerritoryId && d.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))).ToArray();
            if (matching.Length == 0) continue;
            ImGui.SetNextItemOpen(false, ImGuiCond.Once);
            if (!ImGui.CollapsingHeader(expansion.Key.Expansion + "###expansion" + expansion.Key.ExpansionId)) continue;
            var columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / (250 * ImGuiHelpers.GlobalScale)), 1, 3);
            if (ImGui.BeginTable("zones" + expansion.Key.ExpansionId, columns, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var zone in matching) { ImGui.TableNextColumn(); DrawZoneCard(zone); }
                ImGui.EndTable();
            }
        }
        ImGui.Spacing();
        ImGui.Separator();
        var instances = config.Options.RotateInstances;
        if (ImGui.Checkbox(L.T("Visitar outras instâncias antes da próxima zona"), ref instances)) { config.Options.RotateInstances = instances; SaveSettings(); }
        var idle = config.Options.IdleSeconds;
        if (ImGui.SliderInt(L.T("Espera sem FATEs (segundos)"), ref idle, 10, 180)) { config.Options.IdleSeconds = idle; SaveSettings(); }
        ImGui.TextWrapped(L.T("Para descobrir as instâncias, abra uma vez o menu de troca no aetheryte. Ative o Instance Switcher no módulo de viagens."));
        ImGui.TextDisabled(L.T("Zona: {0}  |  Instância: {1}/{2}", World.Territory, World.Instance, World.InstanceCount));
    }

    private void DrawSelectedRoute()
    {
        ImGui.TextColored(Gold, L.T("Sua rota ({0} zonas)", config.Options.Route.Count));
        for (var i = 0; i < config.Options.Route.Count; i++)
        {
            ImGui.PushID(i);
            var d = config.Options.Route[i];
            ImGui.BeginDisabled(i == 0);
            if (ImGui.ArrowButton("up", ImGuiDir.Up)) { (config.Options.Route[i], config.Options.Route[i - 1]) = (config.Options.Route[i - 1], d); SaveSettings(); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(i == config.Options.Route.Count - 1);
            if (ImGui.ArrowButton("down", ImGuiDir.Down)) { (config.Options.Route[i], config.Options.Route[i + 1]) = (config.Options.Route[i + 1], d); SaveSettings(); }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.SmallButton("X")) { config.Options.Route.RemoveAt(i); SaveSettings(); ImGui.PopID(); break; }
            ImGui.SameLine(); ImGui.TextUnformatted($"{i + 1}. {zones.FirstOrDefault(z => z.TerritoryId == d.TerritoryId)?.Name ?? d.Name}");
            var hubs = Destinations.Where(x => x.TerritoryId == d.TerritoryId).ToArray();
            if (hubs.Length > 1)
            {
                GameIcon(AetheryteIcon); ImGui.SameLine();
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("###hub", d.Name))
                {
                    foreach (var hub in hubs)
                        if (ImGui.Selectable(hub.Name, hub.AetheryteId == d.AetheryteId)) { config.Options.Route[i] = hub; SaveSettings(); }
                    ImGui.EndCombo();
                }
            }
            else IconText(AetheryteIcon, d.Name);
            ImGui.PopID();
        }
        if (config.Options.Route.Count == 0) ImGui.TextWrapped(L.T("Rota vazia: apenas a zona atual, sem troca de instância."));
        else ImGui.TextDisabled(L.T("A rota se repete até atingir a meta ou parar a sessão."));
        ImGui.Spacing();
    }

    private void DrawZoneCard(ZoneCard zone)
    {
        ImGui.PushID((int)zone.TerritoryId);
        var destination = Destinations.FirstOrDefault(d => d.TerritoryId == zone.TerritoryId);
        var routeIndex = config.Options.Route.FindIndex(d => d.TerritoryId == zone.TerritoryId);
        var selected = routeIndex >= 0;
        var enabled = destination != null || selected;
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 100 * scale);
        var position = ImGui.GetCursorScreenPos();
        ImGui.BeginDisabled(!enabled);
        if (ImGui.InvisibleButton("zone", size))
        {
            if (selected) config.Options.Route.RemoveAt(routeIndex);
            else if (destination != null) config.Options.Route.Add(destination);
            SaveSettings();
        }
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(position, position + size, 0xFF24201A, 6 * scale);
        if (Services.Textures.GetFromGame(zone.ImagePath).TryGetWrap(out var texture, out _))
        {
            var ratio = (float)texture.Width / texture.Height;
            var desiredRatio = size.X / size.Y;
            var crop = ratio > desiredRatio ? new Vector2(desiredRatio / ratio, 1) : new Vector2(1, ratio / desiredRatio);
            draw.AddImageRounded(texture.Handle, position, position + size, (Vector2.One - crop) / 2, (Vector2.One + crop) / 2, enabled ? 0xFFFFFFFFu : 0xAAFFFFFFu, 6 * scale);
        }
        draw.AddRectFilled(position, position + size, enabled ? 0x22000000u : 0x99000000u, 6 * scale);
        draw.AddRectFilled(position + new Vector2(0, size.Y - 34 * scale), position + size, 0xDD15120F, 6 * scale);
        draw.AddRect(position, position + size, ImGui.ColorConvertFloat4ToU32(selected ? Gold : hovered ? new Vector4(.75f,.76f,.78f,1) : new Vector4(.25f,.28f,.32f,1)), 6 * scale, ImDrawFlags.None, selected ? 2 * scale : scale);
        draw.PushClipRect(position + new Vector2(8 * scale, 0), position + size - new Vector2(8 * scale, 0), true);
        draw.AddText(position + new Vector2(10 * scale, size.Y - 26 * scale), 0xFFFFFFFF, zone.Name);
        draw.PopClipRect();
        var badge = selected ? $"{routeIndex + 1}" : enabled ? "+" : "--";
        var badgePosition = position + new Vector2(size.X - 30 * scale, 8 * scale);
        draw.AddRectFilled(badgePosition, badgePosition + new Vector2(22 * scale), selected ? ImGui.ColorConvertFloat4ToU32(Gold) : 0xCC201B16, 4 * scale);
        draw.AddText(badgePosition + (new Vector2(22 * scale) - ImGui.CalcTextSize(badge)) / 2, selected ? 0xFF141414u : 0xFFFFFFFFu, badge);
        if (hovered)
            ImGui.SetTooltip(zone.Name + "\n" + (destination?.Name ?? L.T("Sem aetheryte desbloqueado nesta zona. A viagem automática exige um destino de teleporte.")));
        ImGui.EndDisabled();
        ImGui.PopID();
    }

    private void DrawFates()
    {
        BeginCard("fatePreferences");
        var forlorn = config.Options.PrioritizeForlorn;
        if (ImGui.Checkbox(L.T("Priorizar Forlorn Maiden e The Forlorn"), ref forlorn)) { config.Options.PrioritizeForlorn = forlorn; SaveSettings(); }
        var groupBosses = config.Options.ExcludeGroupBosses;
        if (ImGui.Checkbox(L.T("Ignorar chefes de grupo conhecidos (Daivadipa, Chi e similares)"), ref groupBosses)) { config.Options.ExcludeGroupBosses = groupBosses; SaveSettings(); }
        if (ImGui.CollapsingHeader(L.T("Tipos de FATE e filtros") + "###fateFilters"))
        {
            var collection = config.Options.IncludeCollection;
            if (ImGui.Checkbox(L.T("Incluir FATEs de coleta"), ref collection)) { config.Options.IncludeCollection = collection; SaveSettings(); }
            var batch = config.Options.CollectionBatchSize;
            if (ImGui.SliderInt(L.T("Itens por entrega"), ref batch, 1, 30)) { config.Options.CollectionBatchSize = batch; SaveSettings(); }
            Help(L.T("Entrega ao atingir o lote, após 45 segundos com itens ou perto do fim do FATE."));
            var escort = config.Options.IncludeEscort;
            if (ImGui.Checkbox(L.T("Incluir FATEs de escolta"), ref escort)) { config.Options.IncludeEscort = escort; SaveSettings(); }
            ImGui.Separator(); DrawEntryFilters();
        }
        EndCard();
        BeginCard("visibleFates");
        ImGui.TextColored(Gold, L.T("FATEs nesta zona"));
        DrawFateRows();
        if (World.Fates.Count == 0) ImGui.TextUnformatted(L.T("Nenhum FATE visível nesta zona."));
        if (config.Options.ExcludedFates.Count > 0 && ImGui.Button(L.T("Limpar lista de ignorados"))) { config.Options.ExcludedFates.Clear(); SaveSettings(); }
        EndCard();
    }

    private void DrawFateRows()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var wide = ImGui.GetContentRegionAvail().X >= 620 * scale;
        if (!ImGui.BeginTable("fateRows", wide ? 4 : 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg)) return;
        ImGui.TableSetupColumn("enabled", ImGuiTableColumnFlags.WidthFixed, 24 * scale);
        ImGui.TableSetupColumn("icon", ImGuiTableColumnFlags.WidthFixed, 36 * scale);
        ImGui.TableSetupColumn("event", ImGuiTableColumnFlags.WidthStretch);
        if (wide) ImGui.TableSetupColumn("progress", ImGuiTableColumnFlags.WidthFixed, 220 * scale);
        foreach (var f in World.Fates.OrderBy(f => System.Numerics.Vector3.DistanceSquared(World.Position, f.Position)))
        {
            ImGui.PushID(f.Id); ImGui.TableNextRow(ImGuiTableRowFlags.None, 64 * scale);
            ImGui.TableNextColumn();
            var groupBoss = config.Options.ExcludeGroupBosses && FateRules.IsGroupBoss(f.Id);
            var included = !groupBoss && !config.Options.ExcludedFates.Contains(f.Id);
            ImGui.BeginDisabled(groupBoss);
            if (ImGui.Checkbox("##included", ref included))
            {
                if (included) config.Options.ExcludedFates.Remove(f.Id); else config.Options.ExcludedFates.Add(f.Id);
                SaveSettings();
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(L.T(groupBoss ? "Chefe de grupo: ignorado automaticamente." : "Participar deste FATE"));
            ImGui.TableNextColumn(); GameIcon(FateIcon(f.Id), 32);
            ImGui.TableNextColumn();
            if (runner.Selected?.Id == f.Id) ImGui.PushStyleColor(ImGuiCol.Text, Gold);
            ImGui.TextWrapped(f.Name);
            if (runner.Selected?.Id == f.Id) ImGui.PopStyleColor();
            ImGui.TextDisabled($"Lv {f.Level}" + (f.Bonus ? "  + " + L.T("Bônus") : ""));
            if (wide) ImGui.TableNextColumn();
            DrawFateProgress(f);
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void DrawCosts()
    {
        DrawGreensSettings();
        BeginCard("teleportCosts");
        ImGui.TextColored(Gold, L.T("Gastos de viagem"));
        if (ImGui.BeginTable("travelStats", 3, ImGuiTableFlags.SizingStretchSame))
        {
            Stat("tickets", L.T("Tickets na bolsa"), TicketCount.ToString("N0"), ItemIcon(7569));
            Stat("usedTickets", L.T("Tickets consumidos"), runtime.UsedTickets.ToString("N0"), ItemIcon(7569));
            Stat("travelGil", L.T("Gil comprometido"), runtime.ReservedGil.ToString("N0"), ItemIcon(1));
            ImGui.EndTable();
        }
        var o = config.Options;
        var tickets = o.UseTickets;
        if (ImGui.Checkbox(L.T("Usar Aetheryte Tickets"), ref tickets)) { o.UseTickets = tickets; SaveSettings(); }
        Help(L.T("Quando ativado, usa o ticket no lugar do gil em teleportes pagos. Destinos gratuitos não gastam tickets."));
        ImGui.BeginDisabled(!o.UseTickets);
        var minimum = o.TicketMinimumFare;
        if (ImGui.InputInt(L.T("Usar ticket a partir de (gil)"), ref minimum)) { o.TicketMinimumFare = Math.Max(0, minimum); SaveSettings(); }
        var fallback = o.PayGilWhenTicketsRunOut;
        if (ImGui.Checkbox(L.T("Continuar pagando gil quando os tickets acabarem"), ref fallback)) { o.PayGilWhenTicketsRunOut = fallback; SaveSettings(); }
        Help(L.T("Desativado: para quando faltar ticket. Abaixo do valor mínimo, o pagamento é em gil."));
        ImGui.EndDisabled();
        var budget = o.MaximumTeleportGil;
        if (ImGui.InputInt(L.T("Limite de gil por sessão (0 = sem limite)"), ref budget)) { o.MaximumTeleportGil = Math.Max(0, budget); SaveSettings(); }
        Help(L.T("O limite inclui a volta à cidade. Um teleporte aceito reserva o custo, mesmo se for cancelado depois."));
        EndCard();
    }

    public int TicketCount { get; set; }

    private void DrawDiagnostics()
    {
        foreach (var (name, available) in Dependencies)
            ImGui.TextColored(ImGui.ColorConvertFloat4ToU32(available ? new Vector4(0.3f, 0.85f, 0.6f, 1) : new Vector4(1, 0.5f, 0.35f, 1)), $"{(available ? "OK" : "--")}  {name}");
        ImGui.Separator();
        if (ImGui.SmallButton(L.T("Copiar diagnóstico")))
            ImGui.SetClipboardText("FatePilot " + Version + " / Dalamud API 15\n" + string.Join("\n", Dependencies.Select(d => $"{d.Name}: {d.Available}")) + "\n" + string.Join("\n", Messages));
        ImGui.BeginChild("log", new Vector2(0, 0), true);
        foreach (var line in Messages.Reverse()) ImGui.TextWrapped(line);
        ImGui.EndChild();
    }

    private void DrawAbout()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.Spacing();
        CenterNext(190 * scale);
        ImGui.BeginGroup();
        DrawLogo(42 * scale);
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextColored(Gold, "F A T E P I L O T");
        ImGui.TextDisabled("By Mokoena");
        ImGui.EndGroup();
        ImGui.EndGroup();
        ImGui.Spacing();
        CenteredText(L.T("Um caminho entre possibilidades. Uma estrela para guiar sua jornada."), 550 * scale);
        ImGui.Spacing();
        var portraitSize = new Vector2(192 * scale);
        CenterNext(portraitSize.X);
        var portraitPosition = ImGui.GetCursorScreenPos();
        if (cat.TryGetWrap(out var catTexture, out _))
            ImGui.GetWindowDrawList().AddImageRounded(catTexture.Handle, portraitPosition, portraitPosition + portraitSize,
                Vector2.Zero, Vector2.One, 0xFFFFFFFF, 14 * scale);
        ImGui.Dummy(portraitSize);
        ImGui.Spacing();
        var supportWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Heart, "Buy my cat a treat");
        CenterNext(supportWidth);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.37f, .29f, .17f, 1));
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Heart, "Buy my cat a treat"))
            Dalamud.Utility.Util.OpenLink("https://ko-fi.com/mokoena");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("ko-fi.com/mokoena");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        CenteredText(L.T("Configurações locais. Sem telemetria própria, conta ou envio de dados pelo FatePilot."), 560 * scale);
        ImGui.PopStyleColor();
        ImGui.Spacing();
        if (ImGui.CollapsingHeader(L.T("Detalhes do plugin") + "###aboutDetails"))
        {
            CenteredText(L.T("Plugin independente para Dalamud. Versão experimental {0}.", Version), 560 * scale);
            CenteredText(L.T("O idioma do plugin não altera nomes de FATEs e zonas, que seguem o cliente do jogo."), 560 * scale);
            CenteredText(L.T("Compilação e testes locais não substituem a validação dentro do jogo."), 560 * scale);
            CenteredText("/fatepilot   /fatepilot start   /fatepilot stop", 560 * scale);
        }
    }

    private static void CenterNext(float width) => ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2));

    private static void CenteredText(string text, float maximumWidth)
    {
        var width = Math.Min(ImGui.GetContentRegionAvail().X, maximumWidth);
        var measured = ImGui.CalcTextSize(text, false, width);
        CenterNext(Math.Min(width, measured.X));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}
