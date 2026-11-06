using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FatePilot;

internal sealed class Travel(Action<string> log)
{
    private RunOptions options = new();
    private bool pending;
    private int ticketsBefore;
    private uint gilBefore;
    private Payment payment;
    public long ReservedGil { get; private set; }
    public int UsedTickets { get; private set; }
    public unsafe int Tickets => InventoryManager.Instance() == null ? 0 : InventoryManager.Instance()->GetInventoryItemCount(7569, false, false, false);

    public void Begin(RunOptions settings)
    {
        options = settings;
        ReservedGil = 0; UsedTickets = 0; pending = false;
    }

    public void UpdateOptions(RunOptions settings)
    {
        if (pending) throw new InvalidOperationException(L.T("Já existe um teleporte pendente."));
        options = settings;
    }

    public unsafe void Teleport(Destination destination)
    {
        if (pending) throw new InvalidOperationException(L.T("Já existe um teleporte pendente."));
        var telepo = Telepo.Instance();
        var inventory = InventoryManager.Instance();
        var actions = ActionManager.Instance();
        if (telepo == null || inventory == null || actions == null || actions->GetActionStatus(ActionType.Action, 5) != 0)
            throw new InvalidOperationException(L.T("Teleporte indisponível no estado atual do personagem."));
        telepo->UpdateAetheryteList();
        var quote = Services.Aetherytes.FirstOrDefault(a => a.AetheryteId == destination.AetheryteId && a.SubIndex == 0)
            ?? throw new InvalidOperationException(L.T("Aetheryte não desbloqueado."));
        payment = TravelPolicy.Choose(options, quote.GilCost, Tickets, inventory->GetGil(), ReservedGil);
        ticketsBefore = Tickets;
        gilBefore = inventory->GetGil();
        // Ticket invocation and ordinary Teleport are separate client entry points.
        // Use client structs instead of accepting arbitrary Yes/No dialogs.
        bool accepted;
        if (payment == Payment.Ticket)
        {
            if (telepo->UseTicketInvoker.Telepo != telepo)
                throw new InvalidOperationException(L.T("O cliente ainda não inicializou o uso de tickets. Abra a janela de Teleport uma vez."));
            accepted = telepo->UseTicketInvoker.TeleportWithTickets(destination.AetheryteId, 0);
        }
        else accepted = telepo->Teleport(destination.AetheryteId, 0);
        if (!accepted) throw new InvalidOperationException(L.T("O cliente recusou o teleporte."));
        if (payment == Payment.Gil) ReservedGil += quote.GilCost;
        pending = true;
        log(L.T("Teleporte aceito: {0}. Pagamento: {1}.", destination.Name,
            payment == Payment.Ticket ? "Aetheryte Ticket" : payment == Payment.Free ? L.T("gratuito") : quote.GilCost + " gil"));
    }

    public unsafe void ConfirmArrival()
    {
        if (!pending) return;
        pending = false;
        var consumed = ticketsBefore - Tickets;
        if (payment == Payment.Ticket && (consumed <= 0 || InventoryManager.Instance() == null || InventoryManager.Instance()->GetGil() < gilBefore))
            throw new InvalidOperationException(L.T("O consumo do ticket não foi confirmado. Confira o inventário e as opções de Teleport antes de continuar."));
        if (payment != Payment.Ticket && consumed > 0)
            throw new InvalidOperationException(L.T("O cliente consumiu um ticket fora da opção escolhida. A sessão foi interrompida para revisão."));
        UsedTickets += Math.Max(0, consumed);
    }

    public unsafe void End()
    {
        // Cancel only the Teleport action accepted by this session; never unrelated casts.
        if (pending && Services.Objects.LocalPlayer is { IsCasting: true, CastActionId: 5, CastActionType: 1 })
        {
            var ui = UIState.Instance();
            if (ui != null) ui->Hotbar.CancelCast();
        }
        pending = false;
    }
}
