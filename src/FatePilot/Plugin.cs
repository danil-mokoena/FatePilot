using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FatePilot.Core;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace FatePilot;

public sealed class Plugin : IDalamudPlugin
{
    private readonly Configuration config;
    private readonly GameRuntime runtime;
    private readonly Runner runner;
    private readonly MainWindow window;
    private readonly ConcurrentQueue<string> requests = new();
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
        window = new(config, runner, runtime, requests.Enqueue);
        Services.Commands.AddHandler("/fatepilot", new CommandInfo(OnCommand)
        {
            HelpMessage = "/fatepilot | start | stop | status | recover",
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
        else if (action is "start" or "stop" or "status" or "recover") requests.Enqueue(action);
        else Services.Chat.Print("/fatepilot | start | stop | status | recover");
    }

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
                if (config.Options.ReturnDestination == null && window.Destinations.Count > 0)
                {
                    config.Options.ReturnDestination = window.Destinations.FirstOrDefault(d => d.AetheryteId == 8)
                        ?? window.Destinations.FirstOrDefault(d => MainWindow.CityTerritories.Contains(d.TerritoryId));
                    config.Save();
                }
            }
            while (requests.TryDequeue(out var request))
            {
                switch (request)
                {
                    case "start": runner.Start(config.Options, world, now); break;
                    case "stop": runner.Stop(L.T("Parado pelo usuário.")); break;
                    case "status": Services.Chat.Print("FatePilot: " + runner.Status); break;
                    case "recover": if (!runner.IsRunning) runtime.Recover(); break;
                }
            }
            var oldState = runner.State;
            runtime.ValidateControl();
            runner.Tick(world, now);
            runtime.Update(world);
            if (oldState == RunState.InstanceChange && runner.State == RunState.Selecting) runtime.AcknowledgeTravelComplete();
            window.World = world;
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
