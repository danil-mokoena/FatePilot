namespace FatePilot.Core;

public enum ReturnGoal { None, Fates, Gemstones, Time, CurrencyCap, BagFull, Durability, JobQueueDone, Trader, Purchase }

public static class SessionGoals
{
    // First observed goal is latched by Runner. Ties use this explicit order.
    // freeSlots / durability are -1 when unknown; durability is also -1 while self-repair handles it.
    public static ReturnGoal Evaluate(RunOptions options, int completed, int earned, double elapsed, int balance, int cap,
        int freeSlots = -1, int durability = -1)
    {
        if (options.AvoidGemstoneCap && cap > 0 && balance >= Math.Max(0, cap - options.GemstoneCapReserve)) return ReturnGoal.CurrencyCap;
        if (options.ReturnWhenBagSlotsBelow > 0 && freeSlots >= 0 && freeSlots < options.ReturnWhenBagSlotsBelow) return ReturnGoal.BagFull;
        if (options.ReturnWhenDurabilityBelow > 0 && durability >= 0 && durability < options.ReturnWhenDurabilityBelow) return ReturnGoal.Durability;
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
        ReturnGoal.BagFull => L.T("Bolsa quase cheia"),
        ReturnGoal.Durability => L.T("Durabilidade baixa"),
        ReturnGoal.JobQueueDone => L.T("Fila de classes concluída"),
        ReturnGoal.Trader => L.T("Ida ao trocador de gemstones"),
        ReturnGoal.Purchase => L.T("Gemstones para a próxima compra"),
        _ => "",
    };
}
