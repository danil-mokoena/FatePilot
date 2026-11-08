using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace FatePilot;

// 1.3: reads the same numbers the game shows in its own experience bar and currency window.
internal static unsafe class ProgressReader
{
    public const uint ExpIcon = 65001;

    public static ExpSnapshot? Exp()
    {
        if (!Services.Client.IsLoggedIn) return null;
        var hud = AgentHUD.Instance();
        if (hud == null || hud->ExpClassJobId == 0 || hud->ExpLevel == 0) return null;
        var max = hud->ExpFlags.HasFlag(AgentHudExpFlag.MaxLevel);
        return new(hud->ExpClassJobId, hud->ExpLevel, hud->ExpCurrentExperience, hud->ExpNeededExperience, max);
    }

    public static SealSnapshot? Seals()
    {
        if (!Services.Client.IsLoggedIn) return null;
        var player = PlayerState.Instance();
        var inventory = InventoryManager.Instance();
        if (player == null || inventory == null) return null;
        var company = player->GrandCompany;
        if (company is < 1 or > 3) return new(0, 0, 0);
        var rank = player->GetGrandCompanyRank();
        var cap = (int)(Services.Data.GetExcelSheet<GrandCompanyRank>().GetRowOrDefault(rank)?.MaxSeals ?? 0);
        return new(company, (int)inventory->GetCompanySeals(company), cap);
    }

    // Storm, Serpent and Flame Seals are items 20, 21 and 22.
    public static uint SealItem(int company) => company is >= 1 and <= 3 ? (uint)(19 + company) : 0;
}
