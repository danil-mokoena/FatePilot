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
        var ground = pi.GetIpcSubscriber<Vector3, float, float, Vector3?>(IntegrationIds.NavigationQueryMeshNearestPointReachable)
            .InvokeFunc(destination, 20, 100) ?? throw new InvalidOperationException(L.T("Não há ponto navegável próximo ao destino."));
        cancellation = new();
        fly = flying;
        pending = pi.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>(IntegrationIds.NavigationNavPathfindCancelable)
            .InvokeFunc(start, ground, flying, cancellation.Token);
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
