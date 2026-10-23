using System;
using System.Threading.Tasks;
using CoopSpectator.Campaign;
using SandBox.View.Map.Visuals;
using Missile = TaleWorlds.CampaignSystem.Siege.SiegeEvent.SiegeEngineMissile;

internal static class SiegeFlightObservationTests
{
    internal static void Run()
    {
        var observation = new CoopCampaignMapSiegeFlightObservation();
        var visual = new SettlementVisual();
        var shot = new Missile { CollisionTime = new TaleWorlds.CampaignSystem.CampaignTime { ToHours = 10 } };
        var data = SettlementVisual.Prepared();
        Check(!Task.Run(() => observation.Begin(visual, new[] { shot }, new long[] { 1 })).Result, "worker cannot admit");
        Check(observation.Begin(visual, new[] { shot }, new long[] { 7 }), "owner admits");
        Task.Run(() => {
            observation.Observe(new SettlementVisual(), shot, data);
            observation.Observe(visual, new Missile(), data);
            observation.Observe(visual, shot, data);
        }).Wait();
        Check(observation.Finish(out var flights) && flights.Count == 1 && flights.ContainsKey(7), "only exact reference pair");
        Check(flights[7].StartHours == 9 && flights[7].EndHours == 10, "host collision and duration");
        observation.Observe(visual, shot, data);
        Check(!observation.Finish(out _), "finished batch cannot repeat");
        Check(observation.Begin(visual, new[] { shot }, new long[] { 7 }), "next batch");
        observation.Observe(visual, shot, new object());
        Check(!observation.Finish(out _), "unknown prepared layout invalidates batch");
        Check(observation.Begin(visual, new[] { shot }, new long[] { 7 }), "clear setup");
        observation.Clear(); observation.Observe(visual, shot, data);
        Check(!observation.Finish(out _), "clear revokes observations");
        Check(!observation.Begin(visual, new[] { shot, shot }, new long[] { 1, 2 }), "duplicate object rejected");
        Console.WriteLine("Siege flight observation ownership/parallel substitute checks passed.");
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Flight observation: " + message); }
}

namespace TaleWorlds.Library
{
    public struct Vec3 { public float x, y, z; }
    public struct Mat3 { public Vec3 f, u; }
    public struct MatrixFrame { public Mat3 rotation; }
}
namespace TaleWorlds.CampaignSystem
{
    public struct CampaignTime { public double ToHours; }
}
namespace TaleWorlds.CampaignSystem.Siege
{
    public class SiegeEvent
    {
        public class SiegeEngineMissile { public TaleWorlds.CampaignSystem.CampaignTime CollisionTime; }
    }
}
namespace SandBox.View.Map.Visuals
{
    public class SettlementVisual
    {
        private struct SiegeBombardmentData
        {
            public TaleWorlds.Library.Vec3 LaunchGlobalPosition;
            public TaleWorlds.Library.MatrixFrame TargetAlignedShooterGlobalFrame;
            public float MissileSpeed, LaunchAngle, Gravity, FlightDuration;
        }
        public static object Prepared() => new SiegeBombardmentData {
            LaunchGlobalPosition = new TaleWorlds.Library.Vec3 { x = 1, y = 2, z = 3 },
            TargetAlignedShooterGlobalFrame = new TaleWorlds.Library.MatrixFrame {
                rotation = new TaleWorlds.Library.Mat3 {
                    f = new TaleWorlds.Library.Vec3 { y = 2 }, u = new TaleWorlds.Library.Vec3 { z = 3 } } },
            MissileSpeed = 14, LaunchAngle = 0, Gravity = 10, FlightDuration = 1
        };
    }
}
