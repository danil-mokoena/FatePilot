using System.Numerics;

namespace FatePilot.Core;

public enum GreensShopState { None, Opening, Ready, Busy, Closed, Foreign, Failed }
public enum PurchaseResult { Pending, Confirmed, Mismatch, TimedOut }
public sealed record VendorActor(ulong Id, Vector3 Position, float Hitbox = 0);
public sealed record GreensInventory(bool Ready, int Count, int Capacity, long Gil);
public sealed record GreensPurchase(int Quantity, int BeforeCount, long BeforeGil, double SentAt)
{
    public int Cost => Quantity * GreensRestock.Price;

    public PurchaseResult Observe(GreensInventory inventory, double now)
    {
        if (inventory.Ready)
        {
            var received = inventory.Count - BeforeCount;
            var paid = BeforeGil - inventory.Gil;
            if (received < 0 || received > Quantity || paid < 0 || paid > Cost) return PurchaseResult.Mismatch;
            if (received == Quantity && paid == Cost) return PurchaseResult.Confirmed;
        }
        return now - SentAt >= 15 ? PurchaseResult.TimedOut : PurchaseResult.Pending;
    }
}

public static class GreensRestock
{
    public const uint ItemId = 4868, VendorId = 1001787, ShopId = 0x0004000E;
    public const int Price = 36, StackSize = 999, MaxBatch = 99;
    public static readonly Destination VendorCity = new(129, 8, "Limsa Lominsa");
    // Level sheet row 4143334; only a navigation hint. The actual NPC is revalidated before every action.
    public static readonly Vector3 VendorPosition = new(-62.1024f, 18.0003f, 9.42483f);

    public static int Needed(RunOptions options, GreensInventory inventory, long committed)
    {
        if (!inventory.Ready) throw new InvalidOperationException(L.T("O inventário ainda não está disponível para a reposição."));
        var needed = Math.Max(0, options.GreensTarget - inventory.Count);
        if (needed > inventory.Capacity) throw new InvalidOperationException(L.T("Falta espaço no inventário para o estoque de Gysahl Greens."));
        var cost = (long)needed * Price;
        if (cost > inventory.Gil || committed + cost > options.GreensGilLimit)
            throw new InvalidOperationException(L.T("A reposição excede o gil disponível ou o orçamento de Gysahl Greens."));
        return needed;
    }
}
