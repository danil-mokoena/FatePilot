using System.Text.Json;

namespace FatePilot.Core;

public sealed partial class Runner
{
    private RunOptions? pendingOptions;
    public bool HasPendingOptions => pendingOptions != null;
    public string? OptionsError { get; private set; }

    public void QueueOptions(RunOptions config)
    {
        pendingOptions = null;
        OptionsError = null;
        if (!IsRunning) return;
        try
        {
            var copy = JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(config))!;
            copy.Validate();
            pendingOptions = copy;
        }
        catch (Exception ex) { OptionsError = ex.Message; }
    }

    private void ApplyPendingOptions(World w)
    {
        if (pendingOptions == null || PendingReturnGoal != ReturnGoal.None ||
            State is not (RunState.Selecting or RunState.Waiting) || Selected != null ||
            w.Loading || !w.LoggedIn || w.InCombat || w.Casting || w.Occupied || w.DialogueOpen || w.TravelBusy) return;
        var candidate = pendingOptions;
        pendingOptions = null;
        try
        {
            if (candidate.AvoidGemstoneCap && (w.GemstoneCap <= 0 || candidate.GemstoneCapReserve >= w.GemstoneCap))
                throw new InvalidOperationException(L.T("O limite de gemstones não está disponível ou a reserva é maior que o limite."));
            // Validation precedes all runtime changes; counters and in-flight payment records are preserved.
            runtime.UpdateOptions(candidate);
            options = candidate;
            OptionsError = null;
            runtime.Log(L.T("Configurações atualizadas sem reiniciar a sessão."));
        }
        catch (Exception ex)
        {
            OptionsError = ex.Message;
            runtime.Log(L.T("Alterações não aplicadas: {0}", ex.Message));
        }
    }
}
