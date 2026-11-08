using System.Numerics;

namespace FatePilot.Core;

public enum PreviewAction { Defend, Return, Restock, Repair, SwitchJob, Eat, Teleport, Summon, GoToFate, WaitHotspot, Wait }
public sealed record PreviewStep(PreviewAction Action, string Text, FateInfo? Fate = null, Vector3? Target = null)
{
    public string Key => Action + ":" + (Fate?.Id ?? 0);
}

// 1.0 simulation: what a session would do right now, in the same order as the runner's selection
// step, from a snapshot only. It never touches the game.
public static class SessionPreview
{
    public static PreviewStep Next(World w, RunOptions o, ZoneHistory history, double now = 0)
    {
        if (w.InCombat) return new(PreviewAction.Defend, L.T("Defender-se do inimigo que está atacando."));
        var goal = SessionGoals.Evaluate(o, 0, 0, 0, w.Gemstones, w.GemstoneCap, w.FreeBagSlots, o.SelfRepair ? -1 : w.MinDurability);
        if (goal != ReturnGoal.None && o.ReturnDestination is { } home)
            return new(PreviewAction.Return, L.T("{0}: voltar para {1}.", SessionGoals.Label(goal), Destination(o, goal) ?? home.Name));
        if (o.RestockGreens && w.GreensInventory.Ready && w.GreensInventory.Count <= o.GreensThreshold)
            return new(PreviewAction.Restock, L.T("Comprar Gysahl Greens em Limsa Lominsa ({0} na bolsa).", w.GreensInventory.Count));
        if (o.SelfRepair && w.MinDurability >= 0 && w.MinDurability < o.RepairBelowPercent)
            return new(PreviewAction.Repair, w.CanSelfRepair
                ? L.T("Reparar o equipamento ({0}%).", w.MinDurability)
                : L.T("Reparo necessário ({0}%), mas o reparo próprio não está disponível agora.", w.MinDurability));
        if (o.UseJobQueue && o.JobQueue.Count > 0)
        {
            var index = o.JobQueue.FindIndex(j => j.Job == w.Job);
            if (index < 0 || w.Level >= o.JobQueue[index].TargetLevel)
            {
                if (index >= 0 && index + 1 >= o.JobQueue.Count)
                    return new(PreviewAction.Return, L.T("{0}: voltar para {1}.", SessionGoals.Label(ReturnGoal.JobQueueDone), o.ReturnDestination?.Name ?? "—"));
                var next = o.JobQueue[index < 0 ? 0 : index + 1];
                if (next.Job != w.Job) return new(PreviewAction.SwitchJob, L.T("Trocar para {0} (meta: nível {1}).", next.Name, next.TargetLevel));
            }
        }
        if (o.UseFood && o.FoodItemId != 0 && w.FoodSecondsLeft is { } left && left < o.FoodRefreshMinutes * 60 && w.FoodCount > 0)
            return new(PreviewAction.Eat, L.T("Comer para renovar o bônus de comida."));
        if (o.Route.Count > 0 && o.Route.All(d => d.TerritoryId != w.Territory))
            return new(PreviewAction.Teleport, L.T("Teleportar para {0}.", o.Route[0].Name));
        if (o.SummonChocobo && w.CompanionSecondsLeft is { } chocobo && chocobo <= 0 && w.GreensInventory.Ready && w.GreensInventory.Count > 0)
            return new(PreviewAction.Summon, L.T("Invocar o Chocobo."));
        var fate = Selection.Choose(w, o, new Dictionary<FateKey, double>(), now);
        if (fate != null)
            return new(PreviewAction.GoToFate, L.T("Ir para {0} ({1} y).", fate.Name, Vector3.Distance(w.Position, fate.Position).ToString("N0")), fate, fate.Position);
        if (o.IdleToHotspot && history.Hotspot(w.Territory) is { } spot && Vector3.Distance(w.Position, spot) >= 60)
            return new(PreviewAction.WaitHotspot, L.T("Nenhum FATE elegível: esperar no ponto onde mais aparecem."), null, spot);
        return new(PreviewAction.Wait, L.T("Nenhum FATE elegível: aguardar {0} s e então trocar de instância ou de zona.", o.IdleSeconds));
    }

    private static string? Destination(RunOptions o, ReturnGoal goal) =>
        goal is ReturnGoal.Gemstones or ReturnGoal.CurrencyCap && o.GemstoneDestination != null ? o.GemstoneDestination.Name : null;
}

// Timeline of the simulated decisions: a new line only when the decision itself changes.
public sealed class SimulationLog
{
    private string? lastKey;
    public List<(DateTime At, string Text)> Entries { get; } = [];

    public bool Observe(PreviewStep step, DateTime at)
    {
        if (step.Key == lastKey) return false;
        lastKey = step.Key;
        Entries.Insert(0, (at, step.Text));
        if (Entries.Count > 30) Entries.RemoveAt(Entries.Count - 1);
        return true;
    }

    public void Clear() { lastKey = null; Entries.Clear(); }
}
