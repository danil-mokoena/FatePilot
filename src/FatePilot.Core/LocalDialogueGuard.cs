namespace FatePilot.Core;

// Local NPC conversations may not populate server EventState fields. Accept only
// a verified speaker/actor and a subsequent confirmation from that same callback.
public sealed class LocalDialogueGuard
{
    private ulong callback;
    private double lastTalk = double.NegativeInfinity;

    public bool Observe(bool globalsEmpty, bool actorValid, bool talk, bool speakerMatches, bool yesNo, ulong receiver, double now)
    {
        if (!globalsEmpty || !actorValid) { Reset(); return false; }
        if (!talk && !yesNo) return false; // A brief gap between pages is not a different conversation.
        if (receiver == 0) { Reset(); return false; }
        if (talk && !yesNo && speakerMatches)
        { callback = receiver; lastTalk = now; return true; }
        if (talk) { Reset(); return false; }
        return yesNo && receiver == callback && now - lastTalk <= 5;
    }

    public void Reset() { callback = 0; lastTalk = double.NegativeInfinity; }
}
