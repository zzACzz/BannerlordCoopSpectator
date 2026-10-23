using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Detached non-settlement events only. IDs identify events, never parties.
    // Not yet connected to capture/transport/rendering.
    public sealed class CoopCampaignMapBattleMarker
    {
        public int EventId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public CoopCampaignMapBattleMarker(int eventId, float x, float y, float z)
        { EventId = eventId; X = x; Y = y; Z = z; }

        public bool IsValid => EventId > 0 && Finite(X) && Finite(Y) && Finite(Z);
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public static class CoopCampaignMapBattleMarkerPolicy
    {
        public const float VerticalOffset = 1.5f;
        public const float CameraHeightCutoff = 200f;
        public const int MaximumEvents = 512;

        // Native MapEventVisualsVM excludes settlement-related events altogether.
        public static bool Admit(bool hostDisclosed, bool settlementRelated) =>
            hostDisclosed && !settlementRelated;

        // Viewport culling is independent from host disclosure, for detached cameras.
        public static bool Show(bool hostDisclosed, float cameraHeight, float projectionW) =>
            hostDisclosed && CoopCampaignMapBattleMarker.Finite(cameraHeight) &&
            CoopCampaignMapBattleMarker.Finite(projectionW) &&
            cameraHeight < CameraHeightCutoff && projectionW > 0f;

        // Full replacement, including an empty list, removes ended/hidden events.
        // Reject the entire invalid snapshot instead of publishing a partial list.
        public static bool TrySnapshot(IReadOnlyList<CoopCampaignMapBattleMarker> source,
            out CoopCampaignMapBattleMarker[] snapshot)
        {
            snapshot = null;
            if (source == null || source.Count > MaximumEvents) return false;
            var ids = new HashSet<int>();
            var result = new CoopCampaignMapBattleMarker[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item == null || !item.IsValid || !ids.Add(item.EventId)) return false;
                result[i] = item; // Entries are immutable; the list itself is detached.
            }
            snapshot = result;
            return true;
        }
    }
}
