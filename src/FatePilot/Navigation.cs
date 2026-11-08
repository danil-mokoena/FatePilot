using FatePilot.Core;
using System.Numerics;

namespace FatePilot;

internal sealed class Navigation
{
    private readonly Dalamud.Plugin.IDalamudPluginInterface pi = Services.Plugin;
    private Task<List<Vector3>>? pending;
    private CancellationTokenSource? cancellation;
    private bool ownsPath, fly;
    public string? Error { get; private set; }

    public bool Ready => pi.GetIpcSubscriber<bool>(IntegrationIds.NavigationNavIsReady).InvokeFunc();
    public bool Busy => pending != null || (ownsPath && pi.GetIpcSubscriber<bool>(IntegrationIds.NavigationPathIsRunning).InvokeFunc());

    public void Move(Vector3 destination, bool flying)
    {
        Stop();
        if (!Ready) throw new InvalidOperationException(L.T("A malha do módulo de navegação ainda não está pronta."));
        var start = Services.Objects.LocalPlayer?.Position ?? throw new InvalidOperationException(L.T("Personagem indisponível."));
        var ground = NearestReachable(destination) ?? throw new InvalidOperationException(L.T("Não há ponto navegável próximo ao destino."));
        cancellation = new();
        fly = flying;
        pending = pi.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>(IntegrationIds.NavigationNavPathfindCancelable)
            .InvokeFunc(start, ground, flying, cancellation.Token);
    }

    // 1.3.4: look for walkable ground at the destination's own height first. A single search
    // 100 yalms tall could pick a cave or ledge above/below the target (same X/Z, wrong floor),
    // leaving the character under it, unable to reach it.
    private Vector3? NearestReachable(Vector3 destination)
    {
        var query = pi.GetIpcSubscriber<Vector3, float, float, Vector3?>(IntegrationIds.NavigationQueryMeshNearestPointReachable);
        foreach (var (horizontal, vertical) in new (float, float)[] { (4, 4), (10, 8), (20, 20), (20, 100) })
            if (query.InvokeFunc(destination, horizontal, vertical) is { } point) return point;
        return null;
    }

    public void Update()
    {
        if (pending is not { IsCompleted: true } task) return;
        pending = null;
        cancellation?.Dispose(); cancellation = null;
        try
        {
            var path = task.GetAwaiter().GetResult();
            if (path.Count == 0) throw new InvalidOperationException(L.T("O módulo de navegação retornou uma rota vazia."));
            ownsPath = true;
            pi.GetIpcSubscriber<List<Vector3>, bool, object>(IntegrationIds.NavigationPathMoveTo).InvokeAction(path, fly);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = ex.Message; }
    }

    public void Stop()
    {
        // Own cancellation token prevents a late pathfinding result from restarting movement after Stop.
        var abandoned = pending;
        pending = null;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        if (abandoned != null)
            _ = abandoned.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        Error = null;
        if (ownsPath)
        {
            pi.GetIpcSubscriber<object>(IntegrationIds.NavigationPathStop).InvokeAction();
            ownsPath = false;
        }
    }
}
