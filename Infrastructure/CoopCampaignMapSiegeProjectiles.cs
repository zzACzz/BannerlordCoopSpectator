using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Prepared host graphics only; no local ballistic simulation or damage authority.
    public sealed class CoopCampaignMapSiegeProjectile
    {
        public int Side { get; set; }
        public int Index { get; set; }
        public long ShotId { get; set; }
        public string Prefab { get; set; } = string.Empty;
        public float[] Frame { get; set; }
        // Native root can be visible while attached to the shooting machine.
        // Its first child is enabled separately during flight.
        public bool FlightChildVisible { get; set; }
        public CoopCampaignMapSiegeFlight Flight { get; set; }
        public bool TrajectoryOnly { get; set; }
        public long Key => ShotId;

        public bool IsValid() => ShotId > 0 && Side >= 0 && Side <= 1 && Index >= 0 &&
            Index < CoopCampaignMapSiegeProjectiles.MaximumCount &&
            CoopCampaignMapSiegeResources.IsProjectile(Prefab) &&
            CoopCampaignMapSiegeMachine.ValidFrame(Frame) && (Flight == null || Flight.IsValid()) &&
            (!TrajectoryOnly || Flight != null);

        public CoopCampaignMapSiegeProjectile Clone() => new CoopCampaignMapSiegeProjectile {
            Side = Side, Index = Index, ShotId = ShotId, Prefab = Prefab,
            Frame = (float[])Frame?.Clone(), FlightChildVisible = FlightChildVisible, Flight = Flight?.Clone(),
            TrajectoryOnly = TrajectoryOnly
        };
    }

    // Contains visible roots only. Replacement/empty sets remove disappeared roots.
    // Index is the current list position; ShotId is the host-assigned identity.
    // Enclosing session admission is still required; disappearance is not an impact.
    public sealed class CoopCampaignMapSiegeProjectiles
    {
        public const int MaximumCount = 128;
        public List<CoopCampaignMapSiegeProjectile> Items { get; set; } =
            new List<CoopCampaignMapSiegeProjectile>();

        public bool TrySnapshot(out CoopCampaignMapSiegeProjectiles snapshot)
        {
            snapshot = null;
            if (Items == null || Items.Count > MaximumCount) return false;
            var result = new CoopCampaignMapSiegeProjectiles();
            var keys = new HashSet<long>();
            var positions = new HashSet<long>();
            foreach (var item in Items)
            {
                if (item == null || !item.IsValid() || !keys.Add(item.Key) ||
                    (item.Flight == null && !positions.Add(((long)item.Side << 32) | (uint)item.Index))) return false;
                result.Items.Add(item.Clone());
            }
            snapshot = result;
            return true;
        }
    }
}
