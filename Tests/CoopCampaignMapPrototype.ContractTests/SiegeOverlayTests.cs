using System;
using CoopSpectator.Infrastructure;

internal static class SiegeOverlayTests
{
    internal static void Run()
    {
        var state = new CoopCampaignMapSiegeOverlay { PreparationProgress = 0.25f, CameraDistance = 18.5f,
            PreparationTitleText = "Building Siege Camp" };
        state.Decals = new CoopCampaignMapSiegeDecals();
        state.Machines = new CoopCampaignMapSiegeMachines();
        state.Projectiles = new CoopCampaignMapSiegeProjectiles();
        state.Time = new CoopCampaignMapSiegeTime { Epoch = 3, SampleSeconds = 12.5, CampaignHours = 1084.75, TimeScale = 0.25f };
        Check(state.TrySnapshot(out var timedCopy), "timed snapshot");
        timedCopy.Time.Epoch = 9;
        Check(state.Time.Epoch == 3, "time snapshot detached");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1d })
        {
            var time = state.Time.Clone(); time.SampleSeconds = invalid;
            Check(!time.IsValid(), "invalid sample clock");
            time = state.Time.Clone(); time.CampaignHours = invalid;
            Check(!time.IsValid(), "invalid campaign clock");
        }
        var paused = state.Time.Clone(); paused.TimeScale = 0;
        Check(paused.IsValid(), "paused host admitted");
        paused.TimeScale = 17;
        Check(!paused.IsValid(), "time scale bound");
        paused = state.Time.Clone(); paused.Epoch = 0;
        Check(!paused.IsValid(), "missing time epoch rejected");
        state.Projectiles.Items.Add(new CoopCampaignMapSiegeProjectile { Side = 0, Index = 2, ShotId = 7,
            Prefab = "ballista_mapicon_projectile", FlightChildVisible = true,
            Frame = new float[] { 1,0,0,0,1,0,0,0,1,7,8,9 } });
        state.Machines.Items.Add(new CoopCampaignMapSiegeMachine { Side = 1, Slot = 0,
            Prefab = "batteringram_a_mapicon", Frame = new float[] { 1,0,0,0,1,0,0,0,1,12,34,5 } });
        state.Decals.Items.Add(new CoopCampaignMapSiegeDecal { Group = 3, Index = 0, Color = 0xff123456,
            Frame = new float[] { 1,0,0,0,1,0,0,0,1,12,34,5 } });
        for (int type = 0; type <= 4; type++)
            state.Points.Add(new CoopCampaignMapSiegePoint { Type = type, MachineIndex = 0,
                MachineType = type == 0 ? 0 : -1, X = 20, Y = 30, Z = 4 });
        Check(state.IsValid(), "Same index in distinct native point groups is valid");
        Check(state.Points[0].QueueIndex == -1, "Native absent-queue sentinel preserved");
        state.Points.Add(state.Points[0].Clone());
        Check(!state.IsValid(), "Duplicate composite identity rejected");
        state.Points.RemoveAt(5);
        state.Points[1].HasItem = true;
        state.Points[1].IsConstructing = true;
        state.Points[1].MachineType = 3;
        state.Points[1].CurrentHitpoints = 0.4f;
        state.Points[1].MaxHitpoints = 1;
        Check(state.TrySnapshot(out var copy), "Construction progress captured");
        Check(copy.Points[1].CurrentHitpoints == 0.4f && copy.Points[1].MaxHitpoints == 1,
            "Progress pair preserved without health conversion");
        copy.Points[1].CurrentHitpoints = 700;
        copy.Points.Clear();
        Check(state.Points.Count == 5 && state.Points[1].CurrentHitpoints == 0.4f, "Snapshot owns list and entries");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            state.TrySnapshot(out copy); copy.PreparationProgress = invalid;
            Check(!copy.IsValid(), "Invalid progress rejected");
            state.TrySnapshot(out copy); copy.Points[0].CurrentHitpoints = invalid;
            Check(!copy.IsValid(), "Invalid indicator value rejected");
        }
        state.TrySnapshot(out copy); copy.Points[0].X = float.NaN;
        Check(!copy.IsValid(), "Invalid world anchor rejected");
        state.TrySnapshot(out copy); copy.Points[0].Type = 5;
        Check(!copy.IsValid(), "Unknown group rejected");
        state.TrySnapshot(out copy); copy.Points[0].MachineType = 8;
        Check(!copy.IsValid(), "Unknown native icon rejected");
        state.TrySnapshot(out copy); copy.Points[0] = null;
        Check(!copy.TrySnapshot(out var rejected) && rejected == null, "Malformed snapshot fails atomically");
        state.TrySnapshot(out copy); copy.PreparationProgress = 1; copy.IsPreparationsCompleted = true;
        Check(copy.IsValid(), "Completed camp admitted");
        copy.Points[0].CurrentHitpoints = 0; copy.Points[0].MaxHitpoints = 1500;
        copy.Points[0].MachineType = 1;
        Check(copy.IsValid(), "Destroyed wall preserves zero HP and broken icon");
        copy.Points.Clear();
        for (int i = 0; i < CoopCampaignMapSiegeOverlay.MaximumPoints; i++)
            copy.Points.Add(new CoopCampaignMapSiegePoint { MachineIndex = i });
        Check(copy.IsValid(), "Bounded maximum admitted");
        copy.Points.Add(new CoopCampaignMapSiegePoint());
        Check(!copy.IsValid(), "Oversized list rejected");
        foreach (var original in new[] { state, new CoopCampaignMapSiegeOverlay() })
        {
            Check(CoopCampaignMapSiegeOverlayCodec.TryEncode(original, out var bytes), "Encode valid overlay");
            Check(CoopCampaignMapSiegeOverlayCodec.TryDecode(bytes, out var decoded) &&
                decoded.PreparationTitleText == original.PreparationTitleText &&
                decoded.PreparationProgress == original.PreparationProgress && decoded.Points.Count == original.Points.Count,
                "Overlay round trip");
            Check(decoded.CameraDistance == original.CameraDistance, "Native camera distance retained");
            Check((decoded.Time == null) == (original.Time == null), "time presence retained");
            if (original.Time != null)
                Check(decoded.Time.Epoch == 3 && decoded.Time.SampleSeconds == 12.5 &&
                    decoded.Time.CampaignHours == 1084.75 && decoded.Time.TimeScale == 0.25f, "time fields exact");
            Check((decoded.Projectiles == null) == (original.Projectiles == null), "Projectile presence retained");
            if (original.Projectiles != null)
                Check(decoded.Projectiles.Items.Count == 1 && decoded.Projectiles.Items[0].Index == 2 &&
                    decoded.Projectiles.Items[0].FlightChildVisible && decoded.Projectiles.Items[0].Frame[10] == 8,
                    "Nested projectile data retained");
            Check((decoded.Machines == null) == (original.Machines == null), "Machine presence retained");
            if (original.Machines != null)
                Check(decoded.Machines.Items.Count == 1 && decoded.Machines.Items[0].Prefab == "batteringram_a_mapicon" &&
                    decoded.Machines.Items[0].Frame[10] == 34, "Nested machine data retained");
            Check(CoopCampaignMapSiegeOverlayCodec.TryEncode(decoded, out var encoded) &&
                Convert.ToBase64String(bytes) == Convert.ToBase64String(encoded), "Every field retained canonically");
            for (int n = 0; n < bytes.Length; n++)
            {
                var prefix = new byte[n]; Array.Copy(bytes, prefix, n);
                Check(!CoopCampaignMapSiegeOverlayCodec.TryDecode(prefix, out _), "Every prefix rejected");
            }
            var extra = new byte[bytes.Length + 1]; Array.Copy(bytes, extra, bytes.Length);
            Check(!CoopCampaignMapSiegeOverlayCodec.TryDecode(extra, out _), "Trailing data rejected");
            var corrupt = (byte[])bytes.Clone(); corrupt[0] ^= 1;
            Check(!CoopCampaignMapSiegeOverlayCodec.TryDecode(corrupt, out _), "Unknown version rejected");
            corrupt = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, corrupt, 4, 4);
            Check(!CoopCampaignMapSiegeOverlayCodec.TryDecode(corrupt, out _), "Hostile text length rejected");
            int titleSize = BitConverter.ToInt32(bytes, 4);
            corrupt = (byte[])bytes.Clone(); corrupt[12 + titleSize] = 2;
            Check(!CoopCampaignMapSiegeOverlayCodec.TryDecode(corrupt, out _), "Noncanonical boolean rejected");
            corrupt = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(-1), 0, corrupt, 13 + titleSize, 4);
            Check(!CoopCampaignMapSiegeOverlayCodec.TryDecode(corrupt, out _), "Negative point count rejected");
            corrupt = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, corrupt, 17 + titleSize, 4);
            Check(!CoopCampaignMapSiegeOverlayCodec.TryDecode(corrupt, out _), "Invalid camera distance rejected");
        }
        var menu = new CoopCampaignMapMenuPresentation { MenuId = "siege", IsInSiegeMode = true, SiegeOverlay = state };
        Check(CoopCampaignMapMenuCodec.TrySerialize(23, menu, out var menuBytes) &&
            CoopCampaignMapMenuCodec.TryDeserialize(menuBytes, out var revision, out var received) &&
            revision == 23 && received.SiegeOverlay.Points.Count == 5 &&
            received.SiegeOverlay.PreparationProgress == 0.25f, "Menu transport preserves siege");
        var clonedMenu = menu.Clone(); clonedMenu.SiegeOverlay.Points[0].X = 999;
        Check(menu.SiegeOverlay.Points[0].X == 20, "Menu clone isolates siege points");
        Check(CoopCampaignMapInterfaceCodec.TryEncode(24, menu, null, out var interfaceBytes) &&
            CoopCampaignMapInterfaceCodec.TryDecode(interfaceBytes, 24, out var interfaceMenu, out _) &&
            interfaceMenu.SiegeOverlay.Points[1].CurrentHitpoints == 0.4f, "Atomic interface preserves siege");
        Check(CoopCampaignMapMenuCodec.TrySerialize(25, null, out var closed) &&
            CoopCampaignMapMenuCodec.TryDeserialize(closed, out _, out var closedMenu) && closedMenu == null,
            "Closing menu clears siege atomically");
        menu.IsInSiegeMode = false;
        Check(!menu.IsValid() && !CoopCampaignMapMenuCodec.TrySerialize(26, menu, out _), "Siege data outside siege menu rejected");
        menu.SiegeOverlay = null;
        Check(CoopCampaignMapMenuCodec.TrySerialize(27, menu, out _), "Ordinary menu remains supported");
        Console.WriteLine("Siege overlay model, codec and menu/interface transport checks passed; rendering pending.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
