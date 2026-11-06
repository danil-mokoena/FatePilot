using System.Numerics;

namespace FatePilot.Core;

public sealed record FateNpcCandidate(ulong Id, uint EntityId, uint BaseId, uint LayoutId, bool Linked,
    bool Targetable, bool Alive, bool Hostile, uint Marker, Vector3 Position, float Hitbox);

public static class FateNpcBinding
{
    public static FateNpcCandidate? Resolve(uint reference, uint marker, Vector3 center, float radius, IReadOnlyList<FateNpcCandidate> candidates)
    {
        var eligible = candidates.Where(c => c.Targetable && c.Alive).ToArray();
        if (reference is not (0 or 0xE0000000))
        {
            // Local event NPC handles can use BaseId in their low 32 bits instead of EntityId.
            var direct = eligible.Where(c => c.EntityId == reference || (uint)c.Id == reference ||
                (c.Linked && (c.BaseId == reference || c.LayoutId == reference))).ToArray();
            if (direct.Length != 0) return direct.Length == 1 ? direct[0] : null;
        }
        // No proximity-only guess: require the selected FATE's native event association.
        var linked = eligible.Where(c => c.Linked && !c.Hostile &&
            Vector3.Distance(c.Position, center) <= Math.Max(30, radius + 20)).ToArray();
        if (marker != 0)
        {
            var marked = linked.Where(c => c.Marker == marker).ToArray();
            if (marked.Length != 0) return marked.Length == 1 ? marked[0] : null;
        }
        return linked.Length == 1 ? linked[0] : null;
    }
}
