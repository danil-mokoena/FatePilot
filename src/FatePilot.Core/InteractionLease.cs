namespace FatePilot.Core;

public enum InteractionStatus { None, Waiting, Owned, Foreign, Expired }
public readonly record struct EventSignal(ulong ObjectId, uint EventId)
{
    public bool IsEmpty => EventId == 0 && ObjectId is 0 or 0xE0000000;
}

// Occupied can be raised before the native event context is populated. During that
// short window we wait without issuing callbacks; ownership still requires both IDs.
public sealed class InteractionLease
{
    private ulong actor;
    private uint[] events = [];
    private double started;
    private bool observed;
    private double lastOwned;
    // Long pre-FATE conversations (8+ pages, two clicks per page while text types)
    // exceeded the old 15 s cap and were dropped mid-dialogue. The cap bounds a
    // stuck window, not a healthy conversation.
    public const double MaxSeconds = 60;
    public bool Active => actor != 0;

    public void Begin(ulong actorId, IEnumerable<uint> eventIds, double now)
    {
        actor = actorId;
        events = eventIds.Where(id => id != 0).Distinct().ToArray();
        started = now;
        observed = false;
        lastOwned = double.NegativeInfinity;
    }

    public InteractionStatus Observe(double now, bool occupiedOrDialog, EventSignal first, EventSignal second, bool matchingSelector, ulong sceneObject = 0, bool matchingLocalDialog = false)
    {
        if (!Active) return InteractionStatus.None;
        if (now - started >= MaxSeconds) { Cancel(); return InteractionStatus.Expired; }
        if (!occupiedOrDialog && !matchingSelector)
        {
            if (observed && now - lastOwned < 1) return InteractionStatus.Waiting;
            if (observed || now - started >= 3) { Cancel(); return InteractionStatus.None; }
            return InteractionStatus.Waiting;
        }
        if (Matches(first, sceneObject) || Matches(second, sceneObject) || matchingSelector ||
            (matchingLocalDialog && first.IsEmpty && second.IsEmpty && sceneObject is 0 or 0xE0000000))
        {
            observed = true;
            lastOwned = now;
            return InteractionStatus.Owned;
        }
        if (!observed && now - started < 3) return InteractionStatus.Waiting;
        // Hand-over between windows of our own conversation (last Talk page → Yes/No):
        // the character stays occupied for a moment with neither window recognisable yet.
        if (observed && now - lastOwned < 1.5) return InteractionStatus.Waiting;
        Cancel();
        return InteractionStatus.Foreign;
    }

    public bool Matches(EventSignal signal, ulong sceneObject = 0) => Active &&
        (signal.ObjectId == actor || sceneObject == actor) && events.Contains(signal.EventId);
    public void Cancel() { actor = 0; events = []; observed = false; }
}
