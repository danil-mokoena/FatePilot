namespace FatePilot.Core;

public enum Payment { Free, Ticket, Gil }

public static class TravelPolicy
{
    public static Payment Choose(RunOptions options, uint fare, int tickets, uint gil, long reservedGil)
    {
        if (fare == 0) return Payment.Free;
        if (options.UseTickets && fare >= options.TicketMinimumFare)
        {
            if (tickets > 0) return Payment.Ticket;
            if (!options.PayGilWhenTicketsRunOut)
                throw new InvalidOperationException(L.T("Os Aetheryte Tickets acabaram. A sessão foi interrompida antes de gastar gil."));
        }
        if (gil < fare) throw new InvalidOperationException(L.T("Gil insuficiente para este teleporte."));
        if (options.MaximumTeleportGil > 0 && reservedGil + fare > options.MaximumTeleportGil)
            throw new InvalidOperationException(L.T("O próximo teleporte excederia o limite de gil da sessão."));
        return Payment.Gil;
    }
}
