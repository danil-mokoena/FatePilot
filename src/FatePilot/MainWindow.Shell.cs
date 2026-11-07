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
        // Compact status at the foot of the sidebar, visible on every page.
        var footer = ImGui.GetTextLineHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.GetContentRegionAvail().Y > footer)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetContentRegionAvail().Y - footer);
        ImGui.Separator();
        StatusDot(Error != null ? Danger : runner.IsRunning ? Success : Muted, runner.IsRunning && Error == null);
        ImGui.SameLine();
        ImGui.TextDisabled(runner.IsRunning ? $"{runner.Completed} FATEs · {runner.GemstonesEarned:N0}" : L.T("Sessão parada"));
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

    private static readonly Vector4 Success = new(.36f, .82f, .58f, 1);
    private static readonly Vector4 Danger = new(.95f, .45f, .38f, 1);
    private static readonly Vector4 Muted = new(.65f, .70f, .77f, 1);

    // Small round status light; a running session breathes softly.
    private static void StatusDot(Vector4 color, bool pulse)
    {
        var h = ImGui.GetTextLineHeight();
        var center = ImGui.GetCursorScreenPos() + new Vector2(h / 2, h / 2);
        var draw = ImGui.GetWindowDrawList();
        if (pulse)
        {
            var t = (float)(Math.Sin(ImGui.GetTime() * 3) * .5 + .5);
            draw.AddCircleFilled(center, h * (.30f + .14f * t), ImGui.ColorConvertFloat4ToU32(color with { W = .25f * (1 - t) }));
        }
        draw.AddCircleFilled(center, h * .24f, ImGui.ColorConvertFloat4ToU32(color));
        ImGui.Dummy(new Vector2(h, h));
    }

    // Pill-shaped label for short states ("High level", "Group boss").
    private static void Chip(string text, Vector4 color)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padding = new Vector2(7, 2) * scale;
        var size = ImGui.CalcTextSize(text) + padding * 2;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(color with { W = .16f }), size.Y / 2);
        draw.AddRect(origin, origin + size, ImGui.ColorConvertFloat4ToU32(color with { W = .55f }), size.Y / 2);
        draw.AddText(origin + padding, ImGui.ColorConvertFloat4ToU32(color), text);
        ImGui.Dummy(size);
    }

    // The one call-to-action per screen: gold fill, dark text. Caller pops 4 colors.
    private static void PushPrimaryButton()
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.62f, .49f, .28f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.74f, .59f, .35f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.84f, .68f, .42f, 1));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(.08f, .07f, .06f, 1));
    }

    // Gold icon + title at the top of a card, with a little breathing room below.
    private static void CardTitle(FontAwesomeIcon icon, string title)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Gold);
        DrawSymbol(icon); ImGui.SameLine(); ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0, 2 * ImGuiHelpers.GlobalScale));
    }

    // Centered, muted message for lists with nothing to show.
    private static void EmptyState(FontAwesomeIcon icon, string text)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, Muted with { W = .8f });
        var width = ImGui.CalcTextSize(text).X + ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.X;
        CenterNext(Math.Min(width, ImGui.GetContentRegionAvail().X));
        DrawSymbol(icon); ImGui.SameLine(); ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        ImGui.Spacing();
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
            StatusDot(Error != null ? Danger : runner.IsRunning ? Success : Muted, runner.IsRunning && Error == null);
            ImGui.SameLine();
            ImGui.TextColored(runner.IsRunning ? Gold : Muted, L.T(runner.IsRunning ? "Sessão em andamento" : "Sessão parada"));
            if (runner.IsRunning || runner.ElapsedSeconds > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("· " + TimeSpan.FromSeconds(runner.ElapsedSeconds).ToString(@"h\:mm\:ss"));
            }
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(runner.IsRunning || startPrompt);
            PushPrimaryButton();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, L.T("Iniciar") + "###start")) { Error = null; request("start"); }
            ImGui.PopStyleColor(4);
            ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.BeginDisabled(!runner.IsRunning && !startPrompt);
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Stop, L.T("Parar") + "###stop")) request("stop");
            ImGui.EndDisabled();
            ImGui.EndTable();
        }
        if (Error != null) { ImGui.PushStyleColor(ImGuiCol.Text, Danger); ImGui.TextWrapped(Error); ImGui.PopStyleColor(); }
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
                ImGui.PushStyleColor(ImGuiCol.Text, Gold);
                DrawSymbol(FontAwesomeIcon.ExclamationTriangle); ImGui.SameLine();
                ImGui.TextUnformatted(L.T("Mantenha seu personagem sob supervisão"));
                ImGui.PopStyleColor();
                ImGui.Separator();
                ImGui.TextWrapped(L.T("O FatePilot move seu personagem, inicia combates e interage com NPCs automaticamente. Fique por perto e acompanhe a sessão. Não deixe o personagem jogando sozinho."));
                ImGui.Spacing();
                ImGui.TextWrapped(L.T("Para interromper a qualquer momento, use Parar ou /fatepilot stop."));
                ImGui.TextWrapped(L.T("Antes de iniciar, pare as outras automações de combate, movimento e viagem."));
                ImGui.Spacing();
                PushPrimaryButton();
                var confirmed = ImGui.Button(L.T("Entendi, iniciar") + "###confirmStart");
                ImGui.PopStyleColor(4);
                if (confirmed) { startPrompt = false; request("confirm-start"); ImGui.CloseCurrentPopup(); }
                ImGui.SameLine();
                if (ImGui.Button(L.T("Cancelar") + "###cancelStart"))
                { startPrompt = false; request("cancel-start"); ImGui.CloseCurrentPopup(); }
            }
            ImGui.EndPopup();
        }
        if (!visible && startPrompt) { startPrompt = false; request("cancel-start"); }
    }
}
