using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using FatePilot.Core;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace FatePilot;

// 0.9 Map page: the zone's own map texture with a live radar on top — FATE areas and progress,
// the current FATE and the path to it, where FATEs usually appear, the aetheryte, enemies and
// the player arrow. Everything is drawn locally from game data already read by the plugin.
internal sealed partial class MainWindow
{
    private sealed record ZoneMap(uint RowId, uint Territory, string Texture, ushort SizeFactor, short OffsetX, short OffsetY, string Name);
    private ZoneMap? zoneMap;
    private static readonly Vector4 FateTone = new(.80f, .86f, .96f, 1);
    private const float MinMapRadius = 40, MaxMapRadius = 1200, CompactMapRadius = 160;

    private unsafe ZoneMap? CurrentZoneMap()
    {
        var territory = World.Territory;
        if (territory == 0) return null;
        uint mapId = 0;
        var agent = AgentMap.Instance();
        if (agent != null) mapId = agent->CurrentMapId;
        if (zoneMap is { } cached && cached.Territory == territory && (mapId == 0 || cached.RowId == mapId)) return cached;
        var row = mapId != 0 ? Services.Data.GetExcelSheet<Map>().GetRowOrDefault(mapId) : null;
        if (row is not { } current || current.TerritoryType.RowId != territory)
            row = Services.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory)?.Map.ValueNullable;
        if (row is not { } m) return zoneMap = null;
        var id = m.Id.ExtractText();
        var texture = id.Length == 0 ? "" : $"ui/map/{id}/{id.Replace("/", "")}_m.tex";
        if (texture.Length > 0 && !Services.Data.FileExists(texture)) texture = "";
        var name = m.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        var sub = m.PlaceNameSub.ValueNullable?.Name.ExtractText() ?? "";
        if (sub.Length > 0) name = name.Length > 0 ? name + " — " + sub : sub;
        return zoneMap = new(m.RowId, territory, texture, m.SizeFactor, m.OffsetX, m.OffsetY, name);
    }

    // Position and facing read live while drawing, so the arrow moves smoothly.
    private (Vector3 Position, float Rotation) LivePlayer()
    {
        var player = Services.Objects.LocalPlayer;
        return player != null ? (player.Position, player.Rotation) : (World.Position, World.Rotation);
    }

    private void DrawMap()
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (!World.LoggedIn || World.Loading || World.Territory == 0)
        {
            BeginCard("mapEmpty"); EmptyState(FontAwesomeIcon.MapMarkedAlt, L.T("O mapa aparece quando você está no mundo aberto.")); EndCard();
            return;
        }
        BeginCard("zoneMap");
        var map = CurrentZoneMap();
        var start = ImGui.GetCursorPosX();
        var width = CardWidth();
        // Header: zone and coordinates on the left, zoom on the right.
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, Gold);
        DrawSymbol(FontAwesomeIcon.MapMarkedAlt); ImGui.SameLine(); ImGui.TextUnformatted(map?.Name is { Length: > 0 } n ? n : L.T("Mapa"));
        ImGui.PopStyleColor();
        if (map != null)
        {
            var coords = MapProjection.ToMapCoordinates(LivePlayer().Position, map.SizeFactor, map.OffsetX, map.OffsetY);
            ImGui.SameLine(); ImGui.TextDisabled($"X {coords.X:0.0}  Y {coords.Y:0.0}");
        }
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var slider = 170 * scale;
        var zoomWidth = IconButtonWidth(FontAwesomeIcon.SearchMinus) + IconButtonWidth(FontAwesomeIcon.SearchPlus) + slider + spacing * 2;
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), start + width - zoomWidth));
        var radius = Math.Clamp(config.MapRadius, MinMapRadius, MaxMapRadius);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.SearchPlus)) SetMapRadius(radius * .75f);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Aproximar"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(slider);
        if (ImGui.SliderFloat("###mapRadius", ref radius, MinMapRadius, MaxMapRadius, L.T("raio de {0} y", "%.0f"))) SetMapRadius(radius);
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.SearchMinus)) SetMapRadius(radius / .75f);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(L.T("Afastar"));
        ImGui.Spacing();

        // The canvas fills the card, leaving room for the legend and the option below it.
        var footer = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetTextLineHeightWithSpacing() + CardPadding * scale * 2;
        var height = Math.Clamp(ImGui.GetContentRegionAvail().Y - footer, 260 * scale, Math.Max(260 * scale, width));
        DrawZoneMap("###zoneMapCanvas", new Vector2(width, height), Math.Clamp(config.MapRadius, MinMapRadius, MaxMapRadius), true);

        // Legend.
        ImGui.Spacing();
        LegendDot(Gold, L.T("FATE atual ou priorizado"), false); ImGui.SameLine();
        LegendDot(FateTone, "FATE", false); ImGui.SameLine();
        LegendDot(Muted, L.T("Ignorado"), false); ImGui.SameLine();
        LegendDot(Gold, L.T("Onde mais aparecem"), true); ImGui.SameLine();
        LegendDot(Danger, L.T("Inimigo atacando você"), false);
        var compactMap = config.CompactMap;
        if (ImGui.Checkbox(L.T("Mostrar o mapa no painel compacto"), ref compactMap)) { config.CompactMap = compactMap; config.Save(); }
        Help(L.T("Passe o mouse sobre um FATE para ver os detalhes. A roda do mouse sobre o mapa aproxima ou afasta. O mapa é desenhado só com dados do próprio jogo."));
        EndCard();
    }

    private void SetMapRadius(float value)
    {
        config.MapRadius = Math.Clamp(value, MinMapRadius, MaxMapRadius);
        config.Save();
    }

    private static void LegendDot(Vector4 color, string label, bool ring)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var h = ImGui.GetTextLineHeight();
        var center = ImGui.GetCursorScreenPos() + new Vector2(h / 2, h / 2);
        var draw = ImGui.GetWindowDrawList();
        if (ring) { draw.AddCircle(center, h * .36f, U32(color), 20, 1.6f * scale); draw.AddCircleFilled(center, h * .12f, U32(color)); }
        else { draw.AddCircleFilled(center, h * .32f, U32(color with { W = .3f })); draw.AddCircle(center, h * .32f, U32(color), 20, 1.4f * scale); }
        ImGui.Dummy(new Vector2(h, h));
        ImGui.SameLine(0, 4 * scale);
        ImGui.TextDisabled(label);
    }

    // Draws the map in a rectangle at the cursor: `radius` yalms from the player to the nearest edge.
    private void DrawZoneMap(string id, Vector2 size, float radius, bool detailed)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = p0 + size;
        var center = p0 + size / 2;
        var rounding = 10 * scale;
        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        if (detailed && hovered && ImGui.GetIO().MouseWheel is var wheel and not 0)
            SetMapRadius(radius * (wheel > 0 ? .85f : 1 / .85f));

        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p0, p1, U32(theme.Frame), rounding);
        var (me, facing) = LivePlayer();
        var map = CurrentZoneMap();
        var perYalm = Math.Min(size.X, size.Y) / 2 / radius;
        Vector2 Screen(Vector3 v) => center + new Vector2(v.X - me.X, v.Z - me.Z) * perYalm;

        // The game's own map, cropped around the player (only the part inside the texture).
        if (map is { Texture.Length: > 0 } && Services.Textures.GetFromGame(map.Texture).TryGetWrap(out var texture, out _))
        {
            var texturePerPixel = map.SizeFactor / 100f / perYalm;
            var middle = MapProjection.ToTexture(me, map.SizeFactor, map.OffsetX, map.OffsetY);
            var uv0 = (middle - size / 2 * texturePerPixel) / MapProjection.TextureSize;
            var uv1 = (middle + size / 2 * texturePerPixel) / MapProjection.TextureSize;
            var c0 = Vector2.Clamp(uv0, Vector2.Zero, Vector2.One);
            var c1 = Vector2.Clamp(uv1, Vector2.Zero, Vector2.One);
            if (c1.X > c0.X && c1.Y > c0.Y)
                draw.AddImageRounded(texture.Handle, p0 + (c0 - uv0) / (uv1 - uv0) * size, p0 + (c1 - uv0) / (uv1 - uv0) * size,
                    c0, c1, U32(new Vector4(1, 1, 1, .92f)), rounding);
            // A light veil so the markers stand out on busy map art.
            draw.AddRectFilled(p0, p1, U32(theme.Window with { W = .18f }), rounding);
        }
        else
        {
            var ringStep = Math.Min(size.X, size.Y) / 6;
            for (var ring = 1; ring <= 3; ring++) draw.AddCircle(center, ringStep * ring, U32(theme.Border), 64, scale);
        }

        ImGui.PushClipRect(p0, p1, true);
        // Where FATEs usually appear in this zone (learned from the local history).
        if (runner.History.Hotspot(World.Territory) is { } hot)
        {
            var h = Screen(hot);
            draw.AddCircle(h, 16 * scale, U32(Gold with { W = .85f }), 28, 1.6f * scale);
            draw.AddCircleFilled(h, 3.5f * scale, U32(Gold));
        }
        // Path to the current FATE.
        // The running session's FATE, or the one the simulation would pick.
        var active = runner.Selected ?? (Simulating ? simulationStep?.Fate : null);
        var selected = active is { } s && s.TerritoryId == World.Territory ? s : null;
        if (selected != null) draw.AddLine(Screen(me), Screen(selected.Position), U32(Gold with { W = .75f }), 2 * scale);
        // Aetheryte.
        if (World.NearbyAetheryte is { } aetheryte) DrawIconAt(draw, AetheryteIcon, Screen(aetheryte), (detailed ? 24 : 18) * scale);
        // Enemies: red when they target you, gold for the current FATE, faint for the rest.
        foreach (var e in World.Enemies)
        {
            var at = Screen(e.Position);
            if (e.TargetingPlayer) draw.AddCircleFilled(at, 4 * scale, U32(Danger));
            else if (selected != null && e.FateId == selected.Id) draw.AddCircleFilled(at, 3 * scale, U32(Gold with { W = .95f }));
            else if (detailed) draw.AddCircleFilled(at, 2.2f * scale, U32(Muted with { W = .5f }));
        }
        // FATE areas with a progress ring and the FATE icon.
        FateInfo? tip = null;
        var tipDistance = float.MaxValue;
        var mouse = ImGui.GetIO().MousePos;
        var iconSize = (detailed ? 26 : 20) * scale;
        foreach (var f in World.Fates.Where(f => f.TerritoryId == World.Territory))
        {
            var at = Screen(f.Position);
            var r = Math.Max(8 * scale, f.Radius * perYalm);
            var current = selected?.Id == f.Id;
            var priority = config.Options.PriorityFates.Contains(f.Id);
            var ignored = config.Options.ExcludedFates.Contains(f.Id);
            var tone = current || priority ? Gold : ignored ? Muted : FateTone;
            draw.AddCircleFilled(at, r, U32(tone with { W = current ? .22f : .10f }));
            draw.AddCircle(at, r, U32(tone with { W = current ? .95f : .5f }), 48, (current ? 2.4f : 1.2f) * scale);
            if (f.Progress > 0) ProgressArc(draw, at, r, Math.Clamp(f.Progress / 100f, 0, 1), U32(tone), 3 * scale);
            DrawIconAt(draw, FateIcon(f.Id), at, iconSize);
            if (detailed && f.Progress > 0)
            {
                var text = f.Progress + "%";
                var textSize = ImGui.CalcTextSize(text);
                var textAt = at + new Vector2(-textSize.X / 2, iconSize / 2 + 2 * scale);
                draw.AddRectFilled(textAt - new Vector2(4, 1) * scale, textAt + textSize + new Vector2(4, 1) * scale, U32(theme.Window with { W = .8f }), 4 * scale);
                draw.AddText(textAt, U32(tone), text);
            }
            var distance = Vector2.Distance(mouse, at);
            if (hovered && distance <= Math.Max(r, iconSize / 2) && distance < tipDistance) { tip = f; tipDistance = distance; }
        }
        // Player arrow, pointing where the character faces.
        var forward = MapProjection.Facing(facing);
        var side = new Vector2(-forward.Y, forward.X);
        var me2 = Screen(me);
        draw.AddCircleFilled(me2, 14 * scale, U32(Gold with { W = .16f }));
        var nose = me2 + forward * 11 * scale;
        var back = me2 - forward * 7 * scale;
        var notch = me2 - forward * 3 * scale;
        draw.AddTriangleFilled(nose, back + side * 7 * scale, notch, U32(Gold));
        draw.AddTriangleFilled(nose, notch, back - side * 7 * scale, U32(Gold));
        draw.AddLine(nose, back + side * 7 * scale, U32(theme.Window), 1.2f * scale);
        draw.AddLine(nose, back - side * 7 * scale, U32(theme.Window), 1.2f * scale);
        // North and, on the full map, a scale bar.
        var north = ImGui.CalcTextSize("N");
        draw.AddText(new Vector2(center.X - north.X / 2, p0.Y + 6 * scale), U32(Muted), "N");
        if (detailed)
        {
            var yalms = MapProjection.ScaleBar(radius * .5f);
            var length = yalms * perYalm;
            var bar0 = new Vector2(p0.X + 12 * scale, p1.Y - 14 * scale);
            draw.AddLine(bar0, bar0 + new Vector2(length, 0), U32(new Vector4(.91f, .91f, .89f, .9f)), 2 * scale);
            draw.AddLine(bar0 - new Vector2(0, 4 * scale), bar0 + new Vector2(0, 4 * scale), U32(new Vector4(.91f, .91f, .89f, .9f)), 2 * scale);
            draw.AddLine(bar0 + new Vector2(length, -4 * scale), bar0 + new Vector2(length, 4 * scale), U32(new Vector4(.91f, .91f, .89f, .9f)), 2 * scale);
            draw.AddText(bar0 + new Vector2(length + 6 * scale, -ImGui.GetTextLineHeight() / 2), U32(new Vector4(.91f, .91f, .89f, .9f)), yalms + " y");
        }
        ImGui.PopClipRect();
        draw.AddRect(p0, p1, U32(theme.Border), rounding, ImDrawFlags.None, 1.2f * scale);

        if (tip != null)
        {
            ImGui.BeginTooltip();
            GameIcon(FateIcon(tip.Id), 28); ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextColored(Gold, tip.Name);
            var left = tip.State == EventState.Preparing ? L.T("aguardando início") : TimeSpan.FromSeconds(tip.SecondsLeft).ToString(@"m\:ss");
            ImGui.TextDisabled(L.T("Nível {0} · {1}% · {2}", tip.Level, tip.Progress, left));
            if (map != null)
            {
                var c = MapProjection.ToMapCoordinates(tip.Position, map.SizeFactor, map.OffsetX, map.OffsetY);
                ImGui.TextDisabled($"X {c.X:0.0}  Y {c.Y:0.0}  ·  {Vector3.Distance(me, tip.Position):0} y");
            }
            if (selected?.Id == tip.Id) ImGui.TextColored(Gold, L.T("FATE atual"));
            else if (config.Options.PriorityFates.Contains(tip.Id)) ImGui.TextColored(Gold, L.T("Priorizado"));
            else if (config.Options.ExcludedFates.Contains(tip.Id)) ImGui.TextDisabled(L.T("Ignorado"));
            ImGui.EndGroup();
            ImGui.EndTooltip();
        }
    }

    private static void ProgressArc(ImDrawListPtr draw, Vector2 center, float radius, float fraction, uint color, float thickness)
    {
        var segments = Math.Max(4, (int)(56 * fraction));
        var start = -MathF.PI / 2;
        var previous = center + new Vector2(MathF.Cos(start), MathF.Sin(start)) * radius;
        for (var i = 1; i <= segments; i++)
        {
            var angle = start + 2 * MathF.PI * fraction * i / segments;
            var next = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            draw.AddLine(previous, next, color, thickness);
            previous = next;
        }
    }

    private static void DrawIconAt(ImDrawListPtr draw, uint icon, Vector2 center, float size)
    {
        if (icon != 0 && Services.Textures.TryGetFromGameIcon(new GameIconLookup(icon), out var shared)
            && shared != null && shared.TryGetWrap(out var texture, out _))
            draw.AddImage(texture.Handle, center - new Vector2(size / 2), center + new Vector2(size / 2));
    }
}
