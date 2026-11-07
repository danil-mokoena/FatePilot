namespace FatePilot.Core;

// Local NPC conversations may not populate server EventState fields. Accept only
// a verified speaker/actor, the pages that follow it, and the Yes/No prompt that
// comes right after — never a prompt the player opened on their own.
public sealed class LocalDialogueGuard
{
    // How long after our last verified page (or our own interaction) a Yes/No still belongs to us.
    public const double ConfirmWindow = 5;
    private ulong callback;
    private double lastTalk = double.NegativeInfinity;

    // recentInteraction: we sent the interaction to this FATE NPC within ConfirmWindow seconds
    // (some NPCs open the Yes/No prompt directly, without Talk pages).
    public bool Observe(bool globalsEmpty, bool actorValid, bool talk, bool speakerMatches, bool yesNo, ulong receiver, double now,
        bool recentInteraction = false)
    {
        if (!globalsEmpty || !actorValid) { Reset(); return false; }
        if (!talk && !yesNo) return false; // A brief gap between pages is not a different conversation.
        if (receiver == 0) { Reset(); return false; }
        if (talk && !yesNo && speakerMatches)
        { callback = receiver; lastTalk = now; return true; }
        // Later pages of an already verified conversation may show narration, the
        // player's own line or a second speaker. They stay ours only while the same
        // native callback keeps driving the Talk window without a gap.
        if (talk && !yesNo && callback != 0 && receiver == callback && now - lastTalk <= ConfirmWindow)
        { lastTalk = now; return true; }
        if (talk) { Reset(); return false; }
        // The FATE start prompt ("Are you curious?") is a separate Yes/No window with its own
        // callback. It is ours only right after our verified conversation or our own interaction,
        // with the bound FATE NPC still targeted and in range (actorValid). A prompt the player
        // opens later (discarding an item, etc.) falls outside this window and is never accepted.
        return yesNo && (callback != 0 && now - lastTalk <= ConfirmWindow || recentInteraction);
    }

    public void Reset() { callback = 0; lastTalk = double.NegativeInfinity; }
}
