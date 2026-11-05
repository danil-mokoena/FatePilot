namespace FatePilot.Core;

public enum InteractionStatus { None, Waiting, Owned, Foreign, Expired }
public readonly record struct EventSignal(ulong ObjectId, uint EventId);

// Occupied can be raised before the native event context is populated. During that
// short window we wait without issuing callbacks; ownership still requires both IDs.
public sealed class InteractionLease
{
    private ulong actor;
    private uint[] events = [];
    private double started;
    private bool observed;
    public bool Active => actor != 0;

    public void Begin(ulong actorId, IEnumerable<uint> eventIds, double now)
    {
        actor = actorId;
        events = eventIds.Where(id => id != 0).Distinct().ToArray();
        started = now;
        observed = false;
    }

    public InteractionStatus Observe(double now, bool occupiedOrDialog, EventSignal first, EventSignal second, bool matchingSelector, ulong sceneObject = 0)
    {
        if (!Active) return InteractionStatus.None;
        if (now - started >= 15) { Cancel(); return InteractionStatus.Expired; }
        if (!occupiedOrDialog && !matchingSelector)
        {
            if (observed || now - started >= 3) { Cancel(); return InteractionStatus.None; }
            return InteractionStatus.Waiting;
        }
        if (Matches(first, sceneObject) || Matches(second, sceneObject) || matchingSelector)
        {
            observed = true;
            return InteractionStatus.Owned;
        }
        if (!observed && now - started < 3) return InteractionStatus.Waiting;
        Cancel();
        return InteractionStatus.Foreign;
    }

    public bool Matches(EventSignal signal, ulong sceneObject = 0) => Active &&
        (signal.ObjectId == actor || sceneObject == actor) && events.Contains(signal.EventId);
    public void Cancel() { actor = 0; events = []; observed = false; }
}
