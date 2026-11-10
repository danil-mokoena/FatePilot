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

    // 1.3.6: the event state can carry the actor's 32-bit handle (local event NPCs) instead of the
    // full object ID; the low 32 bits identify the same actor.
    // The actor itself was verified as this FATE's NPC/object before we interacted with it, so any
    // event it runs right after our interaction (opening pages, Item Request, a pickup's gathering)
    // is ours. Another actor's event is ours only through the FATE's own handlers.
    public bool Matches(EventSignal signal, ulong sceneObject = 0) => Active &&
        (SameActor(signal.ObjectId) || (SameActor(sceneObject) && events.Contains(signal.EventId)));

    // Menu choices stay strict: only the FATE's own handlers are ever picked from a selector.
    public bool MatchesHandler(uint eventId) => Active && events.Contains(eventId);

    private bool SameActor(ulong id) => id != 0 && (id == actor || ((uint)id == (uint)actor && (uint)id is not (0 or 0xE0000000)));
    public void Cancel() { actor = 0; events = []; observed = false; }
}
