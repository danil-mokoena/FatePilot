using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace FatePilot;

internal sealed partial class MainWindow
{
    private static uint ItemIcon(uint itemId) => Services.Data.GetExcelSheet<Item>().GetRowOrDefault(itemId)?.Icon ?? 0;
    private static uint AetheryteIcon => (uint)(Services.Data.GetExcelSheet<MapSymbol>().GetRowOrDefault(1)?.Icon ?? 0);
    private static uint FateIcon(ushort fateId)
    {
        var row = Services.Data.GetExcelSheet<Fate>().GetRowOrDefault(fateId);
        return row?.Icon is > 0 ? row.Value.Icon : row?.MapIcon ?? 0;
    }

    private static void GameIcon(uint iconId, float size = 24)
    {
        var pixels = size * ImGuiHelpers.GlobalScale;
        if (iconId != 0 && Services.Textures.TryGetFromGameIcon(new GameIconLookup(iconId), out var shared)
            && shared != null && shared.TryGetWrap(out var texture, out _))
            ImGui.Image(texture.Handle, new Vector2(pixels));
        else
            ImGui.Dummy(new Vector2(pixels));
    }

    private static void IconText(uint iconId, string text)
    {
        GameIcon(iconId); ImGui.SameLine(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted(text);
    }

    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip(); ImGui.PushTextWrapPos(420 * ImGuiHelpers.GlobalScale);
            ImGui.TextUnformatted(text); ImGui.PopTextWrapPos(); ImGui.EndTooltip();
        }
    }
}
