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
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTick, lastCatalog = -100, lastError = -100;
    private bool disposed;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        config = pluginInterface.GetPluginConfig() as Configuration ?? new();
        Localization.Apply(config.Language);
        runtime = new();
        runner = new(runtime);
        window = new(config, runner, runtime, Request);
        Services.Commands.AddHandler("/fatepilot", new CommandInfo(OnCommand)
        {
            HelpMessage = "/fatepilot | start | stop | pause | resume | status | recover",
        });
        Services.Framework.Update += Update;
        Services.Plugin.UiBuilder.Draw += window.Draw;
        Services.Plugin.UiBuilder.OpenMainUi += window.Open;
        Services.Plugin.UiBuilder.OpenConfigUi += window.Open;
    }

    private void OnCommand(string command, string args)
    {
        var action = args.Trim().ToLowerInvariant();
        if (action.Length == 0) window.Open();
        else if (action is "start" or "stop" or "status" or "recover" or "pause" or "resume") Request(action);
        else Services.Chat.Print("/fatepilot | start | stop | pause | resume | status | recover");
    }

    private void Request(string action) => requests.Enqueue((action, action is "confirm-start" or "settings"
        ? JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(config.Options))! : null));

    private void Update(IFramework framework)
    {
        if (disposed) return;
        var now = clock.Elapsed.TotalSeconds;
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
                    case "confirm-start":
                        if (startConfirmation.Confirm(runner.IsRunning)) runner.Start(request.Options!, world, now);
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
                    case "check-deps": window.Dependencies = runtime.Dependencies(); window.DependenciesCheckedAt = DateTime.Now; break;
                }
            }
            var oldState = runner.State;
            runtime.ValidateControl();
            runner.Tick(world, now);
            runtime.Update(world);
            if ((oldState == RunState.InstanceChange || oldState == RunState.Gateway) && runner.State != oldState) runtime.AcknowledgeTravelComplete();
            window.World = world;
            // Learn which zone each seen FATE belongs to (used by the Achievements page).
            var learned = false;
            foreach (var f in world.Fates)
                if (f.TerritoryId != 0 && !config.FateZones.ContainsKey(f.Id)) { config.FateZones[f.Id] = f.TerritoryId; learned = true; }
            if (learned) config.Save();
            if (window.AchievementsVisible) window.Achievements.Tick();
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
        Services.Plugin.UiBuilder.Draw -= window.Draw;
        Services.Plugin.UiBuilder.OpenMainUi -= window.Open;
        Services.Plugin.UiBuilder.OpenConfigUi -= window.Open;
        Services.Commands.RemoveHandler("/fatepilot");
        try { runtime.Dispose(); }
        catch (Exception) { Services.Log.Error("FatePilot cleanup failed; use recovery next time."); }
    }
}
