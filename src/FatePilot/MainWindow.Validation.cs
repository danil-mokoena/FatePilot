using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

// 1.0: simulation (what a session would do now, without moving or clicking anything) and the
// in-game validation checklist on the Diagnostics page.
internal sealed partial class MainWindow
{
    private bool simulating;
    private PreviewStep? simulationStep;
    private readonly SimulationLog simulationLog = new();
    private bool Simulating => simulating && !runner.IsRunning;

    private void UpdateSimulation()
    {
        if (runner.IsRunning) simulating = false;
        if (!simulating || !World.LoggedIn || World.Loading) { simulationStep = null; return; }
        simulationStep = SessionPreview.Next(World, config.Options, runner.History);
        simulationLog.Observe(simulationStep, DateTime.Now);
    }

    private void ToggleSimulation()
    {
        simulating = !simulating;
        simulationLog.Clear();
    }

    private static FontAwesomeIcon StepIcon(PreviewAction action) => action switch
    {
        PreviewAction.Defend => FontAwesomeIcon.ShieldAlt,
        PreviewAction.Return => FontAwesomeIcon.Home,
        PreviewAction.Restock => FontAwesomeIcon.ShoppingBasket,
        PreviewAction.Repair => FontAwesomeIcon.Wrench,
        PreviewAction.SwitchJob => FontAwesomeIcon.UserShield,
        PreviewAction.Eat => FontAwesomeIcon.Utensils,
        PreviewAction.Teleport => FontAwesomeIcon.MapMarkedAlt,
        PreviewAction.Summon => FontAwesomeIcon.Feather,
        PreviewAction.GoToFate => FontAwesomeIcon.Flag,
        PreviewAction.WaitHotspot => FontAwesomeIcon.MapPin,
        _ => FontAwesomeIcon.Hourglass,
    };

    private void DrawSimulation()
    {
        var scale = ImGuiHelpers.GlobalScale;
        BeginCard("simulation");
        CardTitle(FontAwesomeIcon.Eye, L.T("Simulação"));
        ImGui.TextDisabled(L.T("Mostra o que uma sessão faria agora, com as suas configurações. Nada é movido nem clicado."));
        ImGui.Spacing();
        if (simulationStep is not { } step) { EmptyState(FontAwesomeIcon.Hourglass, L.T("A simulação funciona no mundo aberto.")); EndCard(); return; }
        ImGui.PushStyleColor(ImGuiCol.Text, Gold);
        DrawSymbol(StepIcon(step.Action)); ImGui.SameLine();
        ImGui.TextWrapped(step.Text);
        ImGui.PopStyleColor();
        ImGui.Spacing();

        var none = new Dictionary<FateKey, double>();
        var fates = World.Fates.Where(f => f.TerritoryId == World.Territory)
            .OrderBy(f => Selection.Check(f, World, config.Options, none, 0) != FateSkip.None)
            .ThenBy(f => Vector3.Distance(World.Position, f.Position)).ToList();
        if (fates.Count == 0) ImGui.TextDisabled(L.T("Nenhum FATE ativo nesta zona agora."));
        else if (ImGui.BeginTable("simulationFates", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg, new Vector2(CardWidth(), 0)))
        {
            ImGui.TableSetupColumn("FATE", ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableSetupColumn(L.T("Nível"), ImGuiTableColumnFlags.WidthStretch, .7f);
            ImGui.TableSetupColumn(L.T("Progresso"), ImGuiTableColumnFlags.WidthStretch, .9f);
            ImGui.TableSetupColumn(L.T("Distância"), ImGuiTableColumnFlags.WidthStretch, .9f);
            ImGui.TableSetupColumn(L.T("Situação"), ImGuiTableColumnFlags.WidthStretch, 1.6f);
            ImGui.TableHeadersRow();
            foreach (var f in fates)
            {
                ImGui.PushID(f.Id);
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 28 * scale);
                ImGui.TableNextColumn(); GameIcon(FateIcon(f.Id), 22); ImGui.SameLine(); ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(f.Name);
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(f.Level.ToString());
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(f.Progress + "%");
                ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(Vector3.Distance(World.Position, f.Position).ToString("N0") + " y");
                ImGui.TableNextColumn();
                var skip = Selection.Check(f, World, config.Options, none, 0);
                if (step.Fate?.Id == f.Id) Chip(L.T("Escolhido"), Success);
                else Chip(Selection.Reason(skip), skip == FateSkip.None ? Gold : Muted);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (simulationLog.Entries.Count > 1)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(L.T("Decisões desde o início da simulação:"));
            foreach (var (at, text) in simulationLog.Entries.Take(10))
            {
                ImGui.TextDisabled(at.ToString("HH:mm:ss")); ImGui.SameLine(); ImGui.TextUnformatted(text);
            }
        }
        EndCard();
    }

    // ---------- In-game validation checklist ----------
    private static (string Key, string Title, string How)[] ChecklistItems() =>
    [
        ("teleport", L.T("Teleporte voando ou montado"), L.T("Inicie uma sessão voando, fora da rota: ele deve teleportar na hora.")),
        ("levelroute", L.T("Rota que acompanha o nível"), L.T("Ative em Rota → Rota para o meu nível, inicie e suba de nível ou troque de classe.")),
        ("simulation", L.T("Simulação"), L.T("Ligue o olho ao lado de Iniciar e compare com o FATE que a sessão escolhe de verdade.")),
        ("trader", L.T("Ida ao trocador de gemstones"), L.T("Em Inventário → Ir ao trocador agora. Com a loja aberta, use Copiar dados da janela de troca e cole aqui.")),
        ("pause", L.T("Pausar e continuar no meio de um FATE"), L.T("Pause pelo painel compacto, ande um pouco e continue.")),
        ("food", L.T("Comida renovada sozinha"), L.T("Ative em Inventário com o Well Fed abaixo do limite.")),
        ("repair", L.T("Reparo com Dark Matter"), L.T("Ative em Inventário com o equipamento abaixo do limite.")),
        ("jobs", L.T("Troca de classe pela fila"), L.T("Coloque duas classes na fila, com a primeira já na meta.")),
        ("achievements", L.T("Progresso das conquistas"), L.T("Abra a janela de conquistas do jogo uma vez e depois a página Conquistas.")),
        ("hotspot", L.T("Espera no ponto onde mais aparecem"), L.T("Fique sem FATE elegível numa zona com histórico.")),
        ("alerts", L.T("Som e linha no chat dos avisos"), L.T("Use Testar o som em Configurações → Avisos.")),
        ("retry", L.T("Retomada depois de uma janela inesperada"), L.T("Durante uma sessão, fale com um NPC qualquer.")),
        ("greens", L.T("Duas reposições de Greens na mesma sessão"), L.T("Sessão longa com reposição ativa e estoque mínimo alto.")),
        ("map", L.T("Mapa: seta e FATEs alinhados"), L.T("Compare a página Mapa com o mapa do jogo.")),
        ("themes", L.T("Temas"), L.T("Troque de tema em Configurações → Aparência.")),
        ("hotkeys", L.T("Atalho não dispara digitando no chat"), L.T("Grave um atalho e digite a mesma combinação no chat.")),
        ("profiles", L.T("Copiar e importar código de perfil"), L.T("Copie o código de um perfil e leia de volta em Rota.")),
    ];

    private void DrawChecklist()
    {
        var items = ChecklistItems();
        var states = config.Checklist;
        var ok = items.Count(i => states.GetValueOrDefault(i.Key) == 1);
        var bad = items.Count(i => states.GetValueOrDefault(i.Key) == 2);
        BeginCard("checklist");
        CardTitle(FontAwesomeIcon.ClipboardCheck, L.T("Validação no jogo"));
        ImGui.TextDisabled(L.T("Marque o que já conferiu no jogo. Funcionou: ✓. Deu problema: ✗."));
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, bad > 0 ? Danger : Success);
        ImGui.ProgressBar((float)(ok + bad) / items.Length, new Vector2(CardWidth(), 8 * ImGuiHelpers.GlobalScale), "");
        ImGui.PopStyleColor();
        ImGui.TextDisabled(L.T("{0} de {1} conferidos · {2} com problema", ok + bad, items.Length, bad));
        ImGui.Spacing();
        foreach (var (key, title, how) in items)
        {
            ImGui.PushID(key);
            var state = states.GetValueOrDefault(key);
            var color = state == 1 ? Success : state == 2 ? Danger : Muted;
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            DrawSymbol(state == 1 ? FontAwesomeIcon.CheckCircle : state == 2 ? FontAwesomeIcon.TimesCircle : FontAwesomeIcon.Circle);
            ImGui.PopStyleColor();
            ImGui.SameLine(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(title);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(how);
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), CardWidth() - IconButtonWidth(FontAwesomeIcon.Check) * 2.4f));
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Check)) { states[key] = state == 1 ? 0 : 1; config.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Funcionou"));
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times)) { states[key] = state == 2 ? 0 : 2; config.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Deu problema"));
            ImGui.PopID();
        }
        ImGui.Spacing();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Copy, L.T("Copiar resultado")))
            ImGui.SetClipboardText($"FatePilot {Version} checklist\n" + string.Join("\n", items.Select(i =>
                (states.GetValueOrDefault(i.Key) switch { 1 => "[ok] ", 2 => "[problema] ", _ => "[ ] " }) + i.Title)));
        EndCard();
    }
}
