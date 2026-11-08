using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FatePilot.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace FatePilot;

public sealed class Plugin : IDalamudPlugin
{
    private readonly Configuration config;
    private readonly GameRuntime runtime;
    private readonly Runner runner;
    private readonly MainWindow window;
    private readonly ConcurrentQueue<(string Action, RunOptions? Options)> requests = new();
    private readonly StartConfirmation startConfirmation = new();
    private readonly HotkeyTracker hotkeys = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTick, lastCatalog = -100, lastError = -100, lastHistorySave;
    private bool wasRunning;
    private bool disposed;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        config = pluginInterface.GetPluginConfig() as Configuration ?? new();
        Localization.Apply(config.Language);
        runtime = new();
        runner = new(runtime) { History = config.History };
        window = new(config, runner, runtime, Request);
        Services.Commands.AddHandler("/fatepilot", new CommandInfo(OnCommand)
        {
            HelpMessage = "/fatepilot | start | stop | pause | resume | toggle | skip | status | recover",
        });
        Services.Framework.Update += Update;
        Services.Chat.ChatMessage += OnChat;
        Services.Plugin.UiBuilder.Draw += window.Draw;
        Services.Plugin.UiBuilder.OpenMainUi += window.Open;
        Services.Plugin.UiBuilder.OpenConfigUi += window.Open;
    }

    // Only the fact that a tell arrived is used; sender and text are never read or stored.
    private void OnChat(Dalamud.Game.Chat.IHandleableChatMessage message)
    {
        if (message.LogKind != Dalamud.Game.Text.XivChatType.TellIncoming || !runner.IsRunning || !config.Options.NotifyOnTell) return;
        try { GameRuntime.Alert(config.Options, L.T("Você recebeu uma tell.")); } catch (Exception) { }
    }

    private void OnCommand(string command, string args)
    {
        var action = args.Trim().ToLowerInvariant();
        if (action.Length == 0) window.Open();
        else if (action is "start" or "stop" or "status" or "recover" or "pause" or "resume" or "toggle" or "skip") Request(action);
        else Services.Chat.Print("/fatepilot | start | stop | pause | resume | toggle | skip | status | recover");
    }

    private static RunOptions Clone(RunOptions o) => JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(o))!;

    // 1.0: keep the route on the best zones for the current job's real level (never the synced
    // level inside a FATE). Rebuilt when the job or its level changes, and right before a start.
    private (uint Job, int Level) levelKey;
    private void FollowLevelRoute(World world, bool force)
    {
        if (!config.Options.FollowLevelRoute || !world.LoggedIn || world.Loading || world.Job == 0) return;
        var level = GameRuntime.JobLevel(world.Job);
        if (level <= 0 || (!force && levelKey == (world.Job, level))) return;
        var catalog = window.LevelCatalog;
        catalog.EnsureBuilt();
        if (!catalog.Ready || window.Destinations.Count == 0) return;
        levelKey = (world.Job, level);
        var best = catalog.Best(level, config.Options.LevelAbovePlayer, window.Destinations).Select(z => z.Destination).ToList();
        if (best.Count == 0 || best.Select(d => d.TerritoryId).SequenceEqual(config.Options.Route.Select(d => d.TerritoryId))) return;
        config.Options.Route = best;
        config.Save();
        runtime.Log(L.T("Nível {0}: rota atualizada para {1}.", level, string.Join(", ", best.Select(d => d.Name))));
        if (runner.IsRunning) runner.QueueOptions(Clone(config.Options));
    }

    // 0.9 keyboard shortcuts: polled every frame (presses are short), never while typing.
    private void PollHotkeys()
    {
        if (config.Hotkeys.Count == 0) return;
        bool typing;
        try { typing = window.HotkeysBlocked || !Services.Client.IsLoggedIn || ImGui.GetIO().WantTextInput || GameTextInputActive(); }
        catch (Exception) { typing = true; }
        foreach (var action in hotkeys.Update(key => Services.Keys[(VirtualKey)key], typing, config.Hotkeys))
        {
            if (action == "window") window.Toggle();
            else if (action == "start" && runner.IsRunning) continue;
            else if (action is "toggle" or "skip" or "stop" && !runner.IsRunning) continue;
            else Request(action);
        }
    }

    private static unsafe bool GameTextInputActive()
    {
        var ui = FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance();
        if (ui == null) return true;
        var atk = ui->GetRaptureAtkModule();
        return atk == null || atk->IsTextInputActive();
    }

    private void Request(string action) => requests.Enqueue((action, action is "confirm-start" or "confirm-trip" or "settings"
        ? JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(config.Options))! : null));

    private void Update(IFramework framework)
    {
        if (disposed) return;
        var now = clock.Elapsed.TotalSeconds;
        PollHotkeys();
        if (now - lastTick < 0.25) return;
        lastTick = now;
        try
        {
            if (!runner.IsRunning && !window.IsOpen && requests.IsEmpty) { runtime.ValidateControl(); return; }
            var world = runtime.ReadWorld();
            if (now - lastCatalog > 5 && !world.Loading)
            {
                lastCatalog = now;
                window.Destinations = runtime.Destinations();
                window.Dependencies = runtime.Dependencies();
                window.Gearsets = GameRuntime.Gearsets();
                window.Foods = GameRuntime.FoodChoices();
                window.DarkMatter = GameRuntime.DarkMatterCount();
                if (config.Options.ReturnDestination == null && window.Destinations.Count > 0)
                {
                    config.Options.ReturnDestination = window.Destinations.FirstOrDefault(d => d.AetheryteId == 8)
                        ?? window.Destinations.FirstOrDefault(d => MainWindow.CityTerritories.Contains(d.TerritoryId));
                    config.Save();
                }
            }
            while (requests.TryDequeue(out var request))
            {
                switch (request.Action)
                {
                    case "start":
                        if (startConfirmation.Request(runner.IsRunning)) window.PromptStart();
                        break;
                    case "start-trip":
                        if (startConfirmation.Request(runner.IsRunning)) window.PromptStart(trip: true);
                        break;
                    case "confirm-start":
                        if (startConfirmation.Confirm(runner.IsRunning))
                        {
                            FollowLevelRoute(world, true);
                            runner.Start(config.Options.FollowLevelRoute ? Clone(config.Options) : request.Options!, world, now);
                        }
                        break;
                    case "confirm-trip":
                        if (startConfirmation.Confirm(runner.IsRunning)) runner.StartTraderTrip(request.Options!, world, now);
                        break;
                    case "cancel-start": startConfirmation.Cancel(); break;
                    case "settings": runner.QueueOptions(request.Options!); break;
                    case "stop":
                        startConfirmation.Cancel(); window.CancelStart();
                        runner.Stop(L.T("Parado pelo usuário."));
                        break;
                    case "status": Services.Chat.Print("FatePilot: " + runner.Status); break;
                    case "recover": if (!runner.IsRunning) runtime.Recover(); break;
                    case "skip": runner.Skip(now); break;
                    case "pause": runner.RequestPause(); break;
                    case "pause-after": runner.TogglePauseAfterFate(); break;
                    case "resume": runner.Resume(world, now); break;
                    case "toggle": if (runner.IsPaused) runner.Resume(world, now); else runner.RequestPause(); break;
                    case "check-deps": window.Dependencies = runtime.Dependencies(); window.DependenciesCheckedAt = DateTime.Now; break;
                }
            }
            FollowLevelRoute(world, false);
            var oldState = runner.State;
            runtime.ValidateControl();
            runner.Tick(world, now);
            // 1.3: experience and seals earned this session.
            var exp = ProgressReader.Exp();
            var seals = ProgressReader.Seals();
            if (runner.IsRunning && !wasRunning) window.Progress.Start(exp, seals);
            else window.Progress.Update(exp, seals, runner.IsRunning);
            // Purchases seen by the session are marked in the saved queue too.
            var bought = runner.TakePurchases();
            foreach (var (item, quantity) in bought)
                if (config.Options.PurchaseQueue.FirstOrDefault(o => o.ItemId == item && o.Remaining > 0) is { } order)
                    order.Bought = Math.Min(order.Quantity, order.Bought + quantity);
            if (bought.Count > 0) config.Save();
            runtime.Update(world);
            if ((oldState == RunState.InstanceChange || oldState == RunState.Gateway) && runner.State != oldState) runtime.AcknowledgeTravelComplete();
            window.World = world;
            // Learn which zone each seen FATE belongs to (used by the Achievements page).
            var learned = false;
            foreach (var f in world.Fates)
                if (f.TerritoryId != 0 && !config.FateZones.ContainsKey(f.Id)) { config.FateZones[f.Id] = f.TerritoryId; learned = true; }
            if (learned) config.Save();
            if (window.AchievementsVisible) window.Achievements.Tick();
            // Persist the zone history every minute while running, and once when a session ends.
            if ((runner.IsRunning && now - lastHistorySave > 60) || (wasRunning && !runner.IsRunning)) { lastHistorySave = now; config.Save(); }
            wasRunning = runner.IsRunning;
            window.Messages = runtime.Messages;
            window.TicketCount = runtime.TicketCount;
        }
        catch (Exception ex)
        {
            if (runner.IsRunning) runner.Stop(ex.Message, true);
            else window.Error = ex.Message;
            if (now - lastError >= 10) { lastError = now; Services.Log.Error("FatePilot update failed; details are available in its local panel."); }
        }
    }

    public void Dispose()
    {
        disposed = true;
        Services.Framework.Update -= Update;
        Services.Chat.ChatMessage -= OnChat;
        Services.Plugin.UiBuilder.Draw -= window.Draw;
        Services.Plugin.UiBuilder.OpenMainUi -= window.Open;
        Services.Plugin.UiBuilder.OpenConfigUi -= window.Open;
        Services.Commands.RemoveHandler("/fatepilot");
        try { runtime.Dispose(); }
        catch (Exception) { Services.Log.Error("FatePilot cleanup failed; use recovery next time."); }
    }
}
