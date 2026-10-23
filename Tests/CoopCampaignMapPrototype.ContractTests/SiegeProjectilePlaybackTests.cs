using System;
using CoopSpectator.Infrastructure;

internal static class SiegeProjectilePlaybackTests
{
    internal static void Run()
    {
        var player = new CoopCampaignMapSiegeProjectilePlayback();
        var first = State(10, 0); var second = State(10.2, 10);
        Near(player.Sample(first, 100).Items[0].Frame[9], 0, "first exact");
        Near(player.Sample(second, 100.2).Items[0].Frame[9], 0, "buffer starts at prior point");
        Near(player.Sample(second, 100.3).Items[0].Frame[9], 5, "midpoint");
        Near(second.Projectiles.Items[0].Frame[9], 10, "source immutable");
        Near(player.Sample(second, 101).Items[0].Frame[9], 10, "no extrapolation");
        Near(player.Sample(State(10.1, 999), 101.1).Items[0].Frame[9], 10, "out of order ignored");
        var pause = State(10.4, 20); pause.Time.TimeScale = 0;
        Near(player.Sample(pause, 101.2).Items[0].Frame[9], 20, "pause snaps immediately");
        var resume = State(10.6, 30);
        Near(player.Sample(resume, 101.4).Items[0].Frame[9], 30, "resume boundary not blended");
        var newShot = State(10.8, 500); newShot.Projectiles.Items[0].ShotId = 2;
        Near(player.Sample(newShot, 101.6).Items[0].Frame[9], 500, "new shot cannot inherit motion");
        var changedEpoch = State(11, 700); changedEpoch.Time.Epoch = 2;
        Near(player.Sample(changedEpoch, 101.8).Items[0].Frame[9], 700, "siege replacement exact");
        player.Clear(); player.Sample(first, 0);
        second.Projectiles.Items[0].Index = 99;
        Near(player.Sample(second, 0.2).Items[0].Frame[9], 0, "list index irrelevant");
        Near(player.Sample(second, 0.3).Items[0].Frame[9], 5, "same shot after compaction");
        var gap = State(12, 50);
        Near(player.Sample(gap, 2).Items[0].Frame[9], 50, "large sample gap snaps");
        var hidden = State(12.2, 80); hidden.Projectiles.Items[0].FlightChildVisible = false;
        Near(player.Sample(hidden, 2.2).Items[0].Frame[9], 80, "bone attachment is not flight");
        var empty = State(12.4, 90); empty.Projectiles.Items.Clear();
        Check(player.Sample(empty, 2.4).Items.Count == 0, "removed shots disappear");
        Check(player.Sample(null, 3) == null, "close clears");
        Near(player.Sample(second, 4).Items[0].Frame[9], 10, "reopen has no old history");
        Check(player.Sample(second, double.NaN) == null, "invalid local clock clears");
        var untimed = State(13, 6); untimed.Time = null;
        Near(player.Sample(untimed, 5).Items[0].Frame[9], 6, "untimed exact fallback");
        player.Clear();
        first = State(20, 0); second = State(20.2, 0);
        first.Machines = Engines(0.2f); second.Machines = Engines(0.8f);
        player.Sample(first, 10); player.Sample(second, 10.2); player.Sample(second, 10.3);
        Near(player.Machines.Items[0].Animation.Progress, 0.5f, "machine phase midpoint");
        Near(second.Machines.Items[0].Animation.Progress, 0.8f, "machine source immutable");
        var replaced = State(20.4, 0); replaced.Machines = Engines(0.9f);
        replaced.Machines.Items[0].InstanceId = 2;
        player.Sample(replaced, 10.4);
        Near(player.Machines.Items[0].Animation.Progress, 0.9f, "new machine exact");
        var cycle = State(20.6, 0); cycle.Machines = Engines(0.95f);
        cycle.Machines.Items[0].InstanceId = 2; cycle.Machines.Items[0].AnimationCycleHours = 2;
        player.Sample(cycle, 10.6);
        Near(player.Machines.Items[0].Animation.Progress, 0.95f, "new cycle exact");
        Console.WriteLine("Siege projectile sampled playback checks passed; native smoothness pending.");
        player.Clear();
        first = State(20, 0); first.Projectiles.Items.Clear();
        second = State(20.2, 0);
        second.Projectiles.Items[0].TrajectoryOnly = true;
        second.Projectiles.Items[0].Flight = new CoopCampaignMapSiegeFlight {
            StartHours = 20.05, EndHours = 20.15, Origin = new float[] { 0,0,0 },
            Forward = new float[] { 0,1,0 }, Up = new float[] { 0,0,1 }, Speed = 14, Gravity = 10,
            PresentationScale = new float[] { 2,3,4 }
        };
        player.Sample(first, 0);
        Check(player.Sample(second, 0.2).Items.Count == 0, "retained shot not visible before launch");
        var shortFlight = player.Sample(second, 0.3);
        Check(shortFlight.Items.Count == 1 && shortFlight.Items[0].FlightChildVisible, "missed sample flight reconstructed");
        Near(shortFlight.Items[0].Frame[10], 0.7f, "host trajectory evaluated between samples");
        Near(shortFlight.Items[0].Frame[0], 2, "native lateral scale applied during replay");
        Check(shortFlight.Items[0].Frame[5] < 0 && shortFlight.Items[0].Frame[7] > 0,
            "replayed projectile pitches along descending trajectory");
        Near(second.Projectiles.Items[0].Frame[0], 1, "native attachment source frame unchanged");
        Check(player.Sample(second, 0.41).Items.Count == 0, "flight ends without ghost root");
        player.Clear();
        Check(player.Sample(second, 1).Items.Count == 0, "late join never replays past shot");
        var pausedFlight = State(20.3, 0); pausedFlight.Time.CampaignHours = 20.1; pausedFlight.Time.TimeScale = 0;
        pausedFlight.Projectiles = second.Projectiles;
        player.Sample(pausedFlight, 2);
        Near(player.Sample(pausedFlight, 20).Items[0].Frame[10], 0.7f, "paused trajectory stays fixed");
    }

    private static CoopCampaignMapSiegeOverlay State(double time, float x)
    {
        var state = new CoopCampaignMapSiegeOverlay {
            Time = new CoopCampaignMapSiegeTime { Epoch = 1, SampleSeconds = time, CampaignHours = time, TimeScale = 0.25f },
            Projectiles = new CoopCampaignMapSiegeProjectiles()
        };
        state.Projectiles.Items.Add(new CoopCampaignMapSiegeProjectile { ShotId = 1, Side = 0, Index = 0,
            Prefab = "ballista_mapicon_projectile", FlightChildVisible = true,
            Frame = new float[] { 1,0,0,0,1,0,0,0,1,x,0,0 } });
        return state;
    }
    private static CoopCampaignMapSiegeMachines Engines(float phase)
    {
        var result = new CoopCampaignMapSiegeMachines();
        result.Items.Add(new CoopCampaignMapSiegeMachine { Side = 0, Slot = 0, InstanceId = 1,
            AnimationCycleHours = 1, Prefab = "ballista_b_mapicon",
            Frame = new float[] { 1,0,0,0,1,0,0,0,1,0,0,0 },
            Animation = new CoopCampaignMapSiegeAnimation { ClipName = "ballista_b_mapicon_fire", Progress = phase } });
        return result;
    }
    private static void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < 0.001f, message);
    private static void Check(bool value, string message) { if (!value) throw new Exception("Siege playback: " + message); }
}
