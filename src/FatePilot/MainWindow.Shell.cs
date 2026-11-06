using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

internal sealed partial class MainWindow
{
    private enum Page { Session, Route, Fates, Supplies, Tracker, Diagnostics, About, Settings }
    private Page page;
    private bool startPrompt, openStartPopup;
    private static readonly Stack<(Vector2 Origin, float Width)> cards = new();

    public void PromptStart() { open = true; startPrompt = openStartPopup = true; }
    public void CancelStart() { startPrompt = openStartPopup = false; }

    private void DrawShell()
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.BeginChild("navigation", new Vector2(176 * scale, 0), false);
        CenterNext(58 * scale); DrawLogo(58 * scale);
        CenteredText("F A T E P I L O T", 170 * scale);
        CenteredText("By Mokoena", 170 * scale);
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        NavItem(Page.Session, FontAwesomeIcon.Compass, L.T("Sessão"));
        NavItem(Page.Route, FontAwesomeIcon.Route, L.T("Rota"));
        NavItem(Page.Fates, FontAwesomeIcon.Flag, L.T("FATEs"));
        NavItem(Page.Supplies, FontAwesomeIcon.ShoppingBasket, L.T("Suprimentos"));
        NavItem(Page.Tracker, FontAwesomeIcon.Globe, "Faloop");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        NavItem(Page.Diagnostics, FontAwesomeIcon.Stethoscope, L.T("Diagnóstico"));
        NavItem(Page.About, FontAwesomeIcon.Heart, L.T("Sobre"));
        NavItem(Page.Settings, FontAwesomeIcon.Cog, L.T("Configurações"));
        ImGui.Spacing(); ImGui.TextDisabled(Version);
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("workspace", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        DrawStatusHeader();
        ImGui.Spacing();
        ImGui.BeginChild("page", Vector2.Zero, false);
        ImGui.PushItemWidth(Math.Min(220 * scale, ImGui.GetContentRegionAvail().X * .45f));
        switch (page)
        {
            case Page.Session: PageTitle(L.T("Sessão"), L.T("Metas e acompanhamento da sua jornada.")); DrawSession(); break;
            case Page.Route: PageTitle(L.T("Rota"), L.T("Escolha os destinos na ordem em que deseja visitá-los.")); DrawRoute(); break;
            case Page.Fates: PageTitle(L.T("FATEs"), L.T("Eventos da zona, progresso e prioridades.")); DrawFates(); break;
            case Page.Supplies: PageTitle(L.T("Suprimentos"), L.T("Estoque, reposição e gastos de viagem.")); DrawCosts(); break;
            case Page.Tracker: PageTitle("Faloop", L.T("Consulte os relatos do mundo atual.")); DrawTracker(); break;
            case Page.Diagnostics: PageTitle(L.T("Diagnóstico"), L.T("Integrações e registro local da sessão.")); DrawDiagnostics(); break;
            case Page.About: DrawAbout(); break;
            case Page.Settings: PageTitle(L.T("Configurações"), L.T("Idioma e preferências da interface.")); DrawInterfaceSettings(); break;
        }
        ImGui.PopItemWidth();
        ImGui.EndChild();
        ImGui.EndChild();
        DrawStartNotice();
    }

    private void NavItem(Page target, FontAwesomeIcon icon, string label)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var selected = page == target;
        if (ImGui.Selectable("###nav" + target, selected, ImGuiSelectableFlags.None, new Vector2(width, 32 * scale))) page = target;
        var after = ImGui.GetCursorScreenPos();
        if (selected) ImGui.GetWindowDrawList().AddRectFilled(origin, origin + new Vector2(3 * scale, 32 * scale), ImGui.ColorConvertFloat4ToU32(Gold));
        ImGui.SetCursorScreenPos(origin + new Vector2(11, 7) * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? Gold : new Vector4(.63f, .68f, .75f, 1));
        DrawSymbol(icon);
        ImGui.PopStyleColor();
        ImGui.SameLine(); ImGui.TextUnformatted(label);
        ImGui.SetCursorScreenPos(after);
    }

    private static void DrawSymbol(FontAwesomeIcon icon)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextUnformatted(icon.ToIconString());
        ImGui.PopFont();
    }

    private static void PageTitle(string title, string subtitle)
    {
        ImGui.TextColored(Gold, title);
        ImGui.TextWrapped(subtitle);
        ImGui.Spacing();
    }

    private static void BeginCard(string id)
    {
        var width = ImGui.GetContentRegionAvail().X;
        cards.Push((ImGui.GetCursorScreenPos(), width));
        ImGui.PushID(id); ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(width, 3 * ImGuiHelpers.GlobalScale));
        ImGui.Indent(12 * ImGuiHelpers.GlobalScale); ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 12 * ImGuiHelpers.GlobalScale);
    }
    private static void EndCard()
    {
        ImGui.PopTextWrapPos(); ImGui.EndGroup(); ImGui.Unindent(12 * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(0, 3 * ImGuiHelpers.GlobalScale)); ImGui.EndGroup();
        var card = cards.Pop();
        ImGui.GetWindowDrawList().AddRect(card.Origin, new Vector2(card.Origin.X + card.Width, ImGui.GetItemRectMax().Y),
            ImGui.GetColorU32(ImGuiCol.Border), 8 * ImGuiHelpers.GlobalScale);
        ImGui.PopID(); ImGui.Spacing();
    }

    private void DrawStatusHeader()
    {
        BeginCard("sessionStatus");
        if (ImGui.BeginTable("statusControls", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("state", ImGuiTableColumnFlags.WidthStretch);
            var controlsWidth = ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Play, L.T("Iniciar"))
                + ImGuiComponents.GetIconButtonWithTextWidth(FontAwesomeIcon.Stop, L.T("Parar")) + ImGui.GetStyle().ItemSpacing.X;
            ImGui.TableSetupColumn("controls", ImGuiTableColumnFlags.WidthFixed, controlsWidth);
            ImGui.TableNextColumn();
            ImGui.TextColored(runner.IsRunning ? Gold : new Vector4(.65f, .7f, .77f, 1), L.T(runner.IsRunning ? "Sessão em andamento" : "Sessão parada"));
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(runner.IsRunning || startPrompt);
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, L.T("Iniciar") + "###start")) { Error = null; request("start"); }
            ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.BeginDisabled(!runner.IsRunning && !startPrompt);
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Stop, L.T("Parar") + "###stop")) request("stop");
            ImGui.EndDisabled();
            ImGui.EndTable();
        }
        if (Error != null) ImGui.TextWrapped(Error);
        else ImGui.TextWrapped(runner.Status);
        if (runner.Selected is { } active)
        {
            DrawFateProgress(active);
            if (active.EffectiveKind == FateKind.Collection)
                ImGui.TextDisabled(L.T("Itens carregados: {0} / lote de {1}", active.HeldItems, config.Options.CollectionBatchSize));
        }
        if (runner.IsRunning && runner.PendingReturnGoal != ReturnGoal.None)
            ImGui.TextWrapped(L.T("{0} atingida. Retorno após o FATE em andamento.", SessionGoals.Label(runner.PendingReturnGoal)));
        if (runner.IsRunning && runner.HasPendingOptions)
            ImGui.TextWrapped(L.T(runner.PendingReturnGoal == ReturnGoal.None
                ? "Alterações pendentes: serão aplicadas após o FATE ou a ação em andamento."
                : "O retorno já foi solicitado. As alterações ficam salvas para a próxima sessão."));
        if (runner.IsRunning && runner.OptionsError != null) ImGui.TextWrapped(L.T("Alterações não aplicadas: {0}", runner.OptionsError));
        if (runtime.NeedsRecovery && !runner.IsRunning)
        {
            ImGui.TextWrapped(L.T("Há ajustes do módulo de posicionamento pendentes de restauração."));
            if (ImGui.Button(L.T("Restaurar sessão anterior"))) request("recover");
        }
        EndCard();
    }

    private void DrawInterfaceSettings()
    {
        BeginCard("languageSettings");
        var language = Array.IndexOf(Localization.Codes, config.Language);
        ImGui.SetNextItemWidth(240 * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo(L.T("Idioma"), ref language, Localization.Names, Localization.Names.Length))
        { config.Language = Localization.Codes[language]; Localization.Apply(config.Language); config.Save(); }
        ImGui.TextWrapped(L.T("Auto usa o idioma do cliente. Português (Brasil) e espanhol são escolhas manuais."));
        EndCard();
        BeginCard("supervisionSettings");
        ImGui.TextColored(Gold, L.T("Mantenha seu personagem sob supervisão"));
        ImGui.TextWrapped(L.T("O aviso aparece antes de cada sessão, inclusive ao usar /fatepilot start."));
        EndCard();
    }

    private void DrawStartNotice()
    {
        var title = L.T("Antes de iniciar") + "###startNotice";
        if (openStartPopup) { ImGui.OpenPopup(title); openStartPopup = false; }
        ImGui.SetNextWindowSize(new Vector2(520 * ImGuiHelpers.GlobalScale, 0), ImGuiCond.Appearing);
        var visible = startPrompt;
        if (ImGui.BeginPopupModal(title, ref visible, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!startPrompt) ImGui.CloseCurrentPopup();
            else
            {
                ImGui.TextColored(Gold, L.T("Mantenha seu personagem sob supervisão"));
                ImGui.TextWrapped(L.T("O FatePilot move seu personagem, inicia combates e interage com NPCs automaticamente. Fique por perto e acompanhe a sessão. Não deixe o personagem jogando sozinho."));
                ImGui.Spacing();
                ImGui.TextWrapped(L.T("Para interromper a qualquer momento, use Parar ou /fatepilot stop."));
                ImGui.TextWrapped(L.T("Antes de iniciar, pare as outras automações de combate, movimento e viagem."));
                ImGui.Spacing();
                if (ImGui.Button(L.T("Entendi, iniciar") + "###confirmStart"))
                { startPrompt = false; request("confirm-start"); ImGui.CloseCurrentPopup(); }
                ImGui.SameLine();
                if (ImGui.Button(L.T("Cancelar") + "###cancelStart"))
                { startPrompt = false; request("cancel-start"); ImGui.CloseCurrentPopup(); }
            }
            ImGui.EndPopup();
        }
        if (!visible && startPrompt) { startPrompt = false; request("cancel-start"); }
    }
}
