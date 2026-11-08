namespace FatePilot.Core;

// The game's own experience bar for the current job (real level, never the FATE-synced one).
public sealed record ExpSnapshot(uint Job, int Level, long Current, long Needed, bool MaxLevel)
{
    public double Fraction => MaxLevel || Needed <= 0 ? 1 : Math.Clamp((double)Current / Needed, 0, 1);
}

// Grand Company seals of the player's own company; Company 0 means none joined.
public sealed record SealSnapshot(int Company, int Count, int Cap);

// 1.3: what the session earned in experience and seals. Spending (seals at the GC, a job
// change) moves the baseline instead of counting as a loss.
public sealed class SessionProgress
{
    private ExpSnapshot? lastExp;
    private SealSnapshot? lastSeals;

    public long ExpEarned { get; private set; }
    public int LevelsGained { get; private set; }
    public int SealsEarned { get; private set; }
    public ExpSnapshot? Exp => lastExp;
    public SealSnapshot? Seals => lastSeals;

    public void Start(ExpSnapshot? exp, SealSnapshot? seals)
    {
        ExpEarned = 0; LevelsGained = 0; SealsEarned = 0;
        lastExp = exp; lastSeals = seals;
    }

    // Outside a session the bars stay live but nothing is counted.
    public void Update(ExpSnapshot? exp, SealSnapshot? seals, bool counting = true)
    {
        if (exp != null)
        {
            if (counting && lastExp != null && lastExp.Job == exp.Job && exp.Job != 0)
            {
                if (exp.Level == lastExp.Level && exp.Current > lastExp.Current) ExpEarned += exp.Current - lastExp.Current;
                else if (exp.Level > lastExp.Level)
                {
                    // Level up: the rest of the old bar plus what is already in the new one.
                    ExpEarned += Math.Max(0, lastExp.Needed - lastExp.Current) + exp.Current;
                    LevelsGained += exp.Level - lastExp.Level;
                }
            }
            lastExp = exp;
        }
        if (seals != null)
        {
            if (counting && lastSeals != null && lastSeals.Company == seals.Company && seals.Count > lastSeals.Count)
                SealsEarned += seals.Count - lastSeals.Count;
            lastSeals = seals;
        }
    }
}
