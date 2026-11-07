using System.Numerics;

namespace FatePilot.Core;

// World X/Z ↔ the game's 2048×2048 zone map texture and the coordinates shown in its map window.
// sizeFactor, offsetX and offsetY come from the client's Map sheet row for the zone.
public static class MapProjection
{
    public const float TextureSize = 2048f;

    public static Vector2 ToTexture(Vector3 world, ushort sizeFactor, short offsetX, short offsetY)
    {
        var scale = Math.Max((ushort)1, sizeFactor) / 100f;
        return new((world.X + offsetX) * scale + TextureSize / 2, (world.Z + offsetY) * scale + TextureSize / 2);
    }

    // Same numbers as the in-game map ("X: 21.4 Y: 18.0").
    public static Vector2 ToMapCoordinates(Vector3 world, ushort sizeFactor, short offsetX, short offsetY)
    {
        var size = Math.Max((ushort)1, sizeFactor);
        float Axis(float value, short offset) => 0.02f * (value + offset) + 2048f / size + 1f;
        return new(Axis(world.X, offsetX), Axis(world.Z, offsetY));
    }

    // Screen direction of the player's facing (x right, y down = map south).
    public static Vector2 Facing(float rotation) => new(MathF.Sin(rotation), MathF.Cos(rotation));

    // A round distance for the scale bar that fits in maxYalms (10, 20, 25, 50, 100, 200, 250, 500…).
    public static int ScaleBar(float maxYalms)
    {
        var best = 10;
        foreach (var step in new[] { 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000 })
            if (step <= maxYalms) best = step;
        return best;
    }
}
