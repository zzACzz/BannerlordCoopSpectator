using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Detached prepared graphics. No build, reserve, attack or campaign authority.
    public sealed class CoopCampaignMapSiegeMachine
    {
        public int Side { get; set; } // Native Defender=0, Attacker=1.
        public int Slot { get; set; }
        // Zero is an untracked sample: render exactly, never interpolate it.
        public long InstanceId { get; set; }
        public double AnimationCycleHours { get; set; }
        public string Prefab { get; set; } = string.Empty;
        public float[] Frame { get; set; }
        public CoopCampaignMapSiegeAnimation Animation { get; set; }
        public bool IsRanged => CoopCampaignMapSiegeResources.IsRanged(Prefab);
        public long Key => ((long)Side << 33) | (IsRanged ? 1L << 32 : 0) | (uint)Slot;

        public bool IsValid()
        {
            if (InstanceId < 0 || double.IsNaN(AnimationCycleHours) || double.IsInfinity(AnimationCycleHours) ||
                AnimationCycleHours < 0 || Side < 0 || Side > 1 || Slot < 0 || Slot >= CoopCampaignMapSiegeMachines.MaximumCount ||
                !CoopCampaignMapSiegeResources.AcceptsAnimation(Prefab, Animation) ||
                (Side == 0 && !IsRanged) || !ValidFrame(Frame)) return false;
            return true;
        }

        public CoopCampaignMapSiegeMachine Clone() => new CoopCampaignMapSiegeMachine {
            Side = Side, Slot = Slot, InstanceId = InstanceId, AnimationCycleHours = AnimationCycleHours,
            Prefab = Prefab, Frame = (float[])Frame?.Clone(),
            Animation = Animation == null ? null : new CoopCampaignMapSiegeAnimation {
                ClipName = Animation.ClipName, Progress = Animation.Progress, Speed = Animation.Speed }
        };

        internal static bool ValidFrame(float[] frame)
        {
            if (frame == null || frame.Length != 12) return false;
            for (int i = 0; i < frame.Length; i++)
                if (float.IsNaN(frame[i]) || float.IsInfinity(frame[i]) ||
                    Math.Abs(frame[i]) > (i < 9 ? 64f : 100000f)) return false;
            double determinant = (double)frame[0] * ((double)frame[4] * frame[8] - (double)frame[5] * frame[7]) -
                (double)frame[1] * ((double)frame[3] * frame[8] - (double)frame[5] * frame[6]) +
                (double)frame[2] * ((double)frame[3] * frame[7] - (double)frame[4] * frame[6]);
            return Math.Abs(determinant) > 0.000001d;
        }
    }

    // Complete replacement set; an empty set removes all previously rendered engines.
    // Future transport must bind it to settlement/session, revision and host sample time.
    public sealed class CoopCampaignMapSiegeMachines
    {
        public const int MaximumCount = 128;
        public List<CoopCampaignMapSiegeMachine> Items { get; set; } = new List<CoopCampaignMapSiegeMachine>();

        public bool TrySnapshot(out CoopCampaignMapSiegeMachines snapshot)
        {
            snapshot = null;
            if (Items == null || Items.Count > MaximumCount) return false;
            var result = new CoopCampaignMapSiegeMachines();
            var keys = new HashSet<long>();
            foreach (var item in Items)
            {
                if (item == null || !item.IsValid() || !keys.Add(item.Key)) return false;
                result.Items.Add(item.Clone());
            }
            snapshot = result;
            return true;
        }
    }
}
