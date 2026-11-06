namespace FatePilot.Core;

public enum ReturnGoal { None, Fates, Gemstones, Time, CurrencyCap }

public static class SessionGoals
{
    // First observed goal is latched by Runner. Ties use this explicit order.
    public static ReturnGoal Evaluate(RunOptions options, int completed, int earned, double elapsed, int balance, int cap)
    {
        if (options.AvoidGemstoneCap && cap > 0 && balance >= Math.Max(0, cap - options.GemstoneCapReserve)) return ReturnGoal.CurrencyCap;
        if (options.ReturnAfterFates > 0 && completed >= options.ReturnAfterFates) return ReturnGoal.Fates;
        if (options.ReturnAfterGemstones > 0 && earned >= options.ReturnAfterGemstones) return ReturnGoal.Gemstones;
        if (options.ReturnAfterMinutes > 0 && elapsed >= options.ReturnAfterMinutes * 60d) return ReturnGoal.Time;
        return ReturnGoal.None;
    }

    public static string Label(ReturnGoal goal) => goal switch
    {
        ReturnGoal.Fates => L.T("Meta de FATEs"),
        ReturnGoal.Gemstones => L.T("Meta de gemstones"),
        ReturnGoal.Time => L.T("Meta de tempo"),
        ReturnGoal.CurrencyCap => L.T("Reserva de gemstones"),
        _ => "",
    };
}
