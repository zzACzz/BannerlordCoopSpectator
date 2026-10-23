using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Detached prepared MapSiegeVM values only. No campaign objects or commands.
    // Not yet connected to host capture, transport or client rendering.
    public sealed class CoopCampaignMapSiegePoint
    {
        public int Type { get; set; }
        public int MachineIndex { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public uint PrimaryColor { get; set; }
        public uint SecondaryColor { get; set; }
        public int MachineType { get; set; } = -1;
        public int QueueIndex { get; set; } = -1;
        // Native bindings also represent construction/redeployment progress.
        // Never normalize or recompute this pair from campaign machine state.
        public float CurrentHitpoints { get; set; }
        public float MaxHitpoints { get; set; }
        public bool HasItem { get; set; }
        public bool IsConstructing { get; set; }
        public bool IsPlayerSidePOI { get; set; }
        public bool IsFireVersion { get; set; }
        public bool IsSelected { get; set; }

        public bool IsValid() => Type >= 0 && Type <= 4 &&
            MachineIndex >= 0 && MachineIndex < CoopCampaignMapSiegeOverlay.MaximumPoints &&
            Coordinate(X) && Coordinate(Y) && Coordinate(Z) &&
            MachineType >= -1 && MachineType <= 7 && QueueIndex >= -1 &&
            QueueIndex <= CoopCampaignMapSiegeOverlay.MaximumPoints &&
            Nonnegative(CurrentHitpoints) && Nonnegative(MaxHitpoints);

        public CoopCampaignMapSiegePoint Clone() => (CoopCampaignMapSiegePoint)MemberwiseClone();
        private static bool Coordinate(float value) => Finite(value) && Math.Abs(value) <= 100000f;
        private static bool Nonnegative(float value) => Finite(value) && value >= 0 && value <= 1e12f;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class CoopCampaignMapSiegeOverlay
    {
        public const int MaximumPoints = 128;
        public const int MaximumTitleCharacters = 16384;
        public string PreparationTitleText { get; set; } = string.Empty;
        public float PreparationProgress { get; set; }
        public float CameraDistance { get; set; }
        public bool IsPreparationsCompleted { get; set; }
        public CoopCampaignMapSiegeDecals Decals { get; set; }
        public CoopCampaignMapSiegeMachines Machines { get; set; }
        public CoopCampaignMapSiegeProjectiles Projectiles { get; set; }
        public CoopCampaignMapSiegeTime Time { get; set; }
        public List<CoopCampaignMapSiegePoint> Points { get; set; } = new List<CoopCampaignMapSiegePoint>();

        public bool IsValid()
        {
            if (Time != null && !Time.IsValid()) return false;
            if (Decals != null && !Decals.TrySnapshot(out _)) return false;
            if (Machines != null && !Machines.TrySnapshot(out _)) return false;
            if (Projectiles != null && !Projectiles.TrySnapshot(out _)) return false;
            if (PreparationTitleText == null || PreparationTitleText.Length > MaximumTitleCharacters ||
                !CoopCampaignMapSiegePoint.Finite(PreparationProgress) || PreparationProgress < 0 ||
                PreparationProgress > 1 || !CoopCampaignMapSiegePoint.Finite(CameraDistance) ||
                CameraDistance < 0 || CameraDistance > 100000 ||
                Points == null || Points.Count > MaximumPoints) return false;
            var identities = new HashSet<long>();
            foreach (var point in Points)
            {
                if (point == null || !point.IsValid() ||
                    !identities.Add(((long)point.Type << 32) | (uint)point.MachineIndex)) return false;
            }
            return true;
        }

        public bool TrySnapshot(out CoopCampaignMapSiegeOverlay snapshot)
        {
            snapshot = null;
            if (!IsValid()) return false;
            snapshot = (CoopCampaignMapSiegeOverlay)MemberwiseClone();
            snapshot.Time = Time?.Clone();
            snapshot.Points = Points.ConvertAll(point => point.Clone());
            if (Decals != null) { Decals.TrySnapshot(out var decals); snapshot.Decals = decals; }
            if (Machines != null) { Machines.TrySnapshot(out var machines); snapshot.Machines = machines; }
            if (Projectiles != null) { Projectiles.TrySnapshot(out var projectiles); snapshot.Projectiles = projectiles; }
            return true;
        }
    }
}
