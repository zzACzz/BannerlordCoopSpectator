using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Prepared host-visible decals, not build commands or inferred siege state.
    // Integration must bind this complete replacement set to the enclosing siege/session.
    public sealed class CoopCampaignMapSiegeDecal
    {
        // 0 defender ranged, 1 attacker ram, 2 attacker tower, 3 attacker ranged.
        public int Group { get; set; }
        public int Index { get; set; }
        public float[] Frame { get; set; }
        // Raw Decal.GetFactor1 value; renderer must use SetFactor1, not linear conversion.
        public uint Color { get; set; }
        public string MaterialName => Group == 0 ? "decal_defender_ranged_siege" :
            Group == 1 ? "decal_siege_ram" : Group == 2 ? "decal_siege_tower" :
            Group == 3 ? "decal_siege_ranged" : null;

        public bool IsValid()
        {
            if (Group < 0 || Group > 3 || Index < 0 || Index >= CoopCampaignMapSiegeDecals.MaximumCount ||
                Frame == null || Frame.Length != 12) return false;
            for (int i = 0; i < Frame.Length; i++)
                if (float.IsNaN(Frame[i]) || float.IsInfinity(Frame[i]) ||
                    Math.Abs(Frame[i]) > (i < 9 ? 64f : 100000f)) return false;
            double determinant = (double)Frame[0] * ((double)Frame[4] * Frame[8] - (double)Frame[5] * Frame[7]) -
                (double)Frame[1] * ((double)Frame[3] * Frame[8] - (double)Frame[5] * Frame[6]) +
                (double)Frame[2] * ((double)Frame[3] * Frame[7] - (double)Frame[4] * Frame[6]);
            return Math.Abs(determinant) > 0.000001d;
        }

        public CoopCampaignMapSiegeDecal Clone() => new CoopCampaignMapSiegeDecal
        { Group = Group, Index = Index, Color = Color, Frame = (float[])Frame?.Clone() };
    }

    public sealed class CoopCampaignMapSiegeDecals
    {
        public const int MaximumCount = 128;
        public List<CoopCampaignMapSiegeDecal> Items { get; set; } = new List<CoopCampaignMapSiegeDecal>();

        public bool TrySnapshot(out CoopCampaignMapSiegeDecals snapshot)
        {
            snapshot = null;
            if (Items == null || Items.Count > MaximumCount) return false;
            var copy = new CoopCampaignMapSiegeDecals();
            var identities = new HashSet<long>();
            foreach (var item in Items)
            {
                if (item == null || !item.IsValid() ||
                    !identities.Add(((long)item.Group << 32) | (uint)item.Index)) return false;
                copy.Items.Add(item.Clone());
            }
            snapshot = copy;
            return true;
        }
    }
}
