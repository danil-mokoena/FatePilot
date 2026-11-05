using Dalamud.Bindings.ImGui;
using FatePilot.Core;
using System.Numerics;
using System.Reflection;

namespace FatePilot;

internal sealed class MainWindow(Configuration config, Runner runner, GameRuntime runtime, Action<string> request)
{
    public static readonly uint[] CityTerritories = [129, 130, 132, 418, 478, 628, 819, 962, 1185, 1186];
    public List<Destination> Destinations { get; set; } = [];
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

    public void Draw()
    {
        if (!open) return;
        ImGui.SetNextWindowSize(new Vector2(790, 700), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new(640, 500), new(float.MaxValue, float.MaxValue));
        PushTheme();
        try
        {
        if (ImGui.Begin("FatePilot###FatePilotMain", ref open))
        {
            DrawLogo(54);
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextColored(ImGui.ColorConvertFloat4ToU32(Gold), "F A T E P I L O T");
            ImGui.TextDisabled(L.T("Trace sua rota. Encontre seu destino."));
            ImGui.TextDisabled("By Mokoena  /  0.1.0");
            ImGui.EndGroup();
            ImGui.Spacing();
            if (ImGui.BeginTable("sessionStats", 3, ImGuiTableFlags.SizingStretchSame))
            {
                Stat("complete", L.T("FATEs concluídos"), runner.Completed.ToString());
                Stat("zones", L.T("Zonas na rota"), config.Options.Route.Count.ToString());
                Stat("gil", L.T("Gil comprometido"), runtime.ReservedGil.ToString("N0"));
                ImGui.EndTable();
            }
            ImGui.Spacing();
            ImGui.TextColored(ImGui.ColorConvertFloat4ToU32(runner.IsRunning ? Gold : new Vector4(.65f, .7f, .77f, 1)), L.T(runner.IsRunning ? "Sessão em andamento" : "Sessão parada"));
            ImGui.TextWrapped(runner.Status);
            if (runner.Unconfirmed > 0) ImGui.TextDisabled(L.T("Concluídos: {0}  |  Sem confirmação: {1}", runner.Completed, runner.Unconfirmed));
            ImGui.BeginDisabled(runner.IsRunning);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.37f, .29f, .17f, 1));
            if (ImGui.Button(L.T("Iniciar") + "###start", new(105, 30))) { Error = null; request("start"); }
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!runner.IsRunning);
            if (ImGui.Button(L.T("Parar") + "###stop", new(105, 30))) request("stop");
            ImGui.EndDisabled();
            ImGui.SameLine(); ImGui.TextDisabled("/fatepilot stop");
            if (Error != null) ImGui.TextWrapped(Error);
            if (runtime.NeedsRecovery && !runner.IsRunning)
            {
                ImGui.TextWrapped(L.T("Há ajustes do módulo de posicionamento pendentes de restauração."));
                if (ImGui.Button(L.T("Restaurar sessão anterior"))) request("recover");
            }
            ImGui.Spacing();
            ImGui.PushItemWidth(220);
            if (ImGui.BeginTabBar("tabs"))
            {
                if (ImGui.BeginTabItem(L.T("Sessão") + "###session")) { DrawSession(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem(L.T("Rota") + "###route")) { DrawRoute(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem(L.T("Gastos") + "###costs")) { DrawCosts(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem(L.T("FATEs") + "###fates")) { DrawFates(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem(L.T("Diagnóstico") + "###diagnostics")) { DrawDiagnostics(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem(L.T("Sobre") + "###about")) { DrawAbout(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            ImGui.PopItemWidth();
        }
        ImGui.End();
        }
        finally { ImGui.PopStyleColor(20); ImGui.PopStyleVar(5); }
    }

    private void DrawLogo(float size)
    {
        if (logo.TryGetWrap(out var texture, out _)) ImGui.Image(texture.Handle, new(size, size));
        else ImGui.Dummy(new(size, size));
    }

    private static void Stat(string id, string label, string value)
    {
        ImGui.TableNextColumn();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 8));
        ImGui.BeginChild(id, new Vector2(0, 66), true, ImGuiWindowFlags.NoScrollbar);
        ImGui.TextDisabled(label);
        ImGui.TextColored(ImGui.ColorConvertFloat4ToU32(Gold), value);
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private static void PushTheme()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18, 16));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 9));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
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
        var o = config.Options;
        ImGui.BeginDisabled(runner.IsRunning);
        var language = Array.IndexOf(Localization.Codes, config.Language);
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo(L.T("Idioma"), ref language, Localization.Names, Localization.Names.Length))
        {
            config.Language = Localization.Codes[language]; Localization.Apply(config.Language); config.Save();
        }
        ImGui.TextWrapped(L.T("Auto usa o idioma do cliente. Português (Brasil) e espanhol são escolhas manuais."));
        ImGui.Separator();
        var goal = o.ReturnAfterFates;
        ImGui.SetNextItemWidth(130);
        if (ImGui.InputInt(L.T("Voltar após X FATEs (0 = sem meta)"), ref goal)) { o.ReturnAfterFates = Math.Clamp(goal, 0, 9999); config.Save(); }
        if (ImGui.BeginCombo(L.T("Cidade de retorno"), o.ReturnDestination?.Name ?? L.T("Selecionar")))
        {
            foreach (var city in Destinations.Where(d => CityTerritories.Contains(d.TerritoryId)))
                if (ImGui.Selectable(city.Name, o.ReturnDestination?.AetheryteId == city.AetheryteId)) { o.ReturnDestination = city; config.Save(); }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped(L.T("A meta conta sucessos observados em FATEs dos quais você participou. Desaparecimentos sem confirmação não contam."));
        var mount = o.UseMount;
        if (ImGui.Checkbox(L.T("Usar montaria"), ref mount)) { o.UseMount = mount; config.Save(); }
        var fly = o.UseFlight;
        if (ImGui.Checkbox(L.T("Voar onde estiver desbloqueado"), ref fly)) { o.UseFlight = fly; config.Save(); }
        var bonus = o.PreferBonus;
        if (ImGui.Checkbox(L.T("Preferir FATEs com bônus"), ref bonus)) { o.PreferBonus = bonus; config.Save(); }
        var above = o.LevelAbovePlayer;
        if (ImGui.SliderInt(L.T("Níveis acima do personagem"), ref above, 0, 10)) { o.LevelAbovePlayer = above; config.Save(); }
        var progress = o.MaximumProgress;
        if (ImGui.SliderInt(L.T("Progresso máximo para entrar (%)"), ref progress, 10, 100)) { o.MaximumProgress = progress; config.Save(); }
        var time = o.MinimumSecondsLeft;
        if (ImGui.SliderInt(L.T("Tempo restante mínimo (segundos)"), ref time, 0, 600)) { o.MinimumSecondsLeft = time; config.Save(); }
        ImGui.EndDisabled();
        ImGui.Spacing();
        ImGui.TextWrapped(L.T("Antes de iniciar, pare as outras automações de combate, movimento e viagem."));
    }

    private void DrawRoute()
    {
        ImGui.TextWrapped(L.T("Adicione um aetheryte por zona. A ordem da lista define o ciclo de viagem."));
        ImGui.BeginDisabled(runner.IsRunning);
        var instances = config.Options.RotateInstances;
        if (ImGui.Checkbox(L.T("Visitar outras instâncias antes da próxima zona"), ref instances)) { config.Options.RotateInstances = instances; config.Save(); }
        var idle = config.Options.IdleSeconds;
        if (ImGui.SliderInt(L.T("Espera sem FATEs (segundos)"), ref idle, 10, 180)) { config.Options.IdleSeconds = idle; config.Save(); }
        ImGui.TextWrapped(L.T("Para descobrir as instâncias, abra uma vez o menu de troca no aetheryte. Ative o Instance Switcher no módulo de viagens."));
        ImGui.InputText(L.T("Buscar zona ou aetheryte"), ref search, 100);
        if (ImGui.BeginCombo(L.T("Adicionar zona"), L.T("Selecionar")))
        {
            foreach (var d in Destinations.Where(d => !CityTerritories.Contains(d.TerritoryId) && d.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)))
            {
                if (config.Options.Route.Any(r => r.TerritoryId == d.TerritoryId)) continue;
                if (ImGui.Selectable(d.Name)) { config.Options.Route.Add(d); config.Save(); }
            }
            ImGui.EndCombo();
        }
        for (var i = 0; i < config.Options.Route.Count; i++)
        {
            ImGui.PushID(i);
            var d = config.Options.Route[i];
            if (ImGui.SmallButton("X")) { config.Options.Route.RemoveAt(i); config.Save(); ImGui.PopID(); break; }
            ImGui.SameLine();
            if (ImGui.SmallButton("^/up") && i > 0)
            { (config.Options.Route[i], config.Options.Route[i - 1]) = (config.Options.Route[i - 1], config.Options.Route[i]); config.Save(); }
            ImGui.SameLine(); ImGui.TextUnformatted($"{i + 1}. {d.Name}");
            ImGui.PopID();
        }
        if (config.Options.Route.Count == 0) ImGui.TextWrapped(L.T("Rota vazia: apenas a zona atual, sem troca de instância."));
        ImGui.EndDisabled();
        ImGui.TextDisabled(L.T("Zona: {0}  |  Instância: {1}/{2}", World.Territory, World.Instance, World.InstanceCount));
    }

    private void DrawFates()
    {
        ImGui.TextWrapped(L.T("Esta versão participa de FATEs de combate já iniciados. Coleta, escolta e ativação por NPC ficam fora da seleção."));
        foreach (var f in World.Fates)
        {
            ImGui.PushID(f.Id);
            var excluded = config.Options.ExcludedFates.Contains(f.Id);
            ImGui.BeginDisabled(runner.IsRunning);
            if (ImGui.Checkbox("##excluded", ref excluded))
            {
                if (excluded) config.Options.ExcludedFates.Add(f.Id); else config.Options.ExcludedFates.Remove(f.Id);
                config.Save();
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Ignorar este FATE"));
            ImGui.SameLine(); ImGui.TextUnformatted($"Lv {f.Level}  {f.Progress}%  {f.Name}  [{f.Id}]");
            ImGui.PopID();
        }
        if (World.Fates.Count == 0) ImGui.TextUnformatted(L.T("Nenhum FATE visível nesta zona."));
        ImGui.BeginDisabled(runner.IsRunning);
        if (config.Options.ExcludedFates.Count > 0 && ImGui.Button(L.T("Limpar lista de ignorados"))) { config.Options.ExcludedFates.Clear(); config.Save(); }
        ImGui.EndDisabled();
    }

    private void DrawCosts()
    {
        ImGui.BeginDisabled(runner.IsRunning);
        var o = config.Options;
        var tickets = o.UseTickets;
        if (ImGui.Checkbox(L.T("Usar Aetheryte Tickets"), ref tickets)) { o.UseTickets = tickets; config.Save(); }
        ImGui.TextWrapped(L.T("Quando ativado, usa o ticket no lugar do gil em teleportes pagos. Destinos gratuitos não gastam tickets."));
        ImGui.BeginDisabled(!o.UseTickets);
        var minimum = o.TicketMinimumFare;
        if (ImGui.InputInt(L.T("Usar ticket a partir de (gil)"), ref minimum)) { o.TicketMinimumFare = Math.Max(0, minimum); config.Save(); }
        var fallback = o.PayGilWhenTicketsRunOut;
        if (ImGui.Checkbox(L.T("Continuar pagando gil quando os tickets acabarem"), ref fallback)) { o.PayGilWhenTicketsRunOut = fallback; config.Save(); }
        ImGui.TextWrapped(L.T("Desativado: para quando faltar ticket. Abaixo do valor mínimo, o pagamento é em gil."));
        ImGui.EndDisabled();
        var budget = o.MaximumTeleportGil;
        if (ImGui.InputInt(L.T("Limite de gil por sessão (0 = sem limite)"), ref budget)) { o.MaximumTeleportGil = Math.Max(0, budget); config.Save(); }
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.TextUnformatted(L.T("Tickets no inventário: {0}", TicketCount));
        ImGui.TextUnformatted(L.T("Tickets consumidos: {0}", runtime.UsedTickets));
        ImGui.TextUnformatted(L.T("Gil comprometido nos teleportes: {0}", runtime.ReservedGil));
        ImGui.TextWrapped(L.T("O limite inclui a volta à cidade. Um teleporte aceito reserva o custo, mesmo se for cancelado depois."));
    }

    public int TicketCount { get; set; }

    private void DrawDiagnostics()
    {
        foreach (var (name, available) in Dependencies)
            ImGui.TextColored(ImGui.ColorConvertFloat4ToU32(available ? new Vector4(0.3f, 0.85f, 0.6f, 1) : new Vector4(1, 0.5f, 0.35f, 1)), $"{(available ? "OK" : "--")}  {name}");
        ImGui.Separator();
        if (ImGui.SmallButton(L.T("Copiar diagnóstico")))
            ImGui.SetClipboardText("FatePilot 0.1.0 / Dalamud API 15\n" + string.Join("\n", Dependencies.Select(d => $"{d.Name}: {d.Available}")) + "\n" + string.Join("\n", Messages));
        ImGui.BeginChild("log", new Vector2(0, 0), true);
        foreach (var line in Messages.Reverse()) ImGui.TextWrapped(line);
        ImGui.EndChild();
    }

    private void DrawAbout()
    {
        ImGui.Spacing();
        var available = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (available - 120) / 2));
        DrawLogo(120);
        ImGui.Spacing();
        ImGui.TextColored(ImGui.ColorConvertFloat4ToU32(Gold), "F A T E P I L O T");
        ImGui.TextUnformatted("By Mokoena");
        ImGui.Spacing();
        if (cat.TryGetWrap(out var catTexture, out _)) ImGui.Image(catTexture.Handle, new Vector2(64, 64));
        else ImGui.Dummy(new Vector2(64, 64));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.37f, .29f, .17f, 1));
        if (ImGui.Button("Buy my cat a treat", new Vector2(196, 32)))
            Dalamud.Utility.Util.OpenLink("https://ko-fi.com/mokoena");
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("ko-fi.com/mokoena");
        ImGui.TextDisabled("ko-fi.com/mokoena");
        ImGui.EndGroup();
        ImGui.TextWrapped(L.T("Um caminho entre possibilidades. Uma estrela para guiar sua jornada."));
        ImGui.Separator();
        ImGui.TextWrapped(L.T("Plugin independente para Dalamud, desenvolvido em C#. Versão experimental 0.1.0."));
        ImGui.TextWrapped(L.T("Configurações locais. Sem telemetria própria, conta ou envio de dados pelo FatePilot."));
        ImGui.TextWrapped(L.T("O idioma do plugin não altera nomes de FATEs e zonas, que seguem o cliente do jogo."));
        ImGui.TextWrapped(L.T("Compilação e testes locais não substituem a validação dentro do jogo."));
        ImGui.TextUnformatted("/fatepilot   /fatepilot start   /fatepilot stop");
    }
}
