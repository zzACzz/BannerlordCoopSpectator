using System;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Patches;
using SandBox.View.Map;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using Missile = TaleWorlds.CampaignSystem.Siege.SiegeEvent.SiegeEngineMissile;
using ProjectileEntry = System.ValueTuple<TaleWorlds.Engine.GameEntity, TaleWorlds.Core.BattleSideEnum, int>;

namespace CoopSpectator.Campaign
{
    internal sealed partial class CoopCampaignMapSiegeProjectileCapture
    {
        private readonly CoopCampaignMapSiegeFlightObservation _flightObservation = new CoopCampaignMapSiegeFlightObservation();
        private bool _flightInstalled;
        private SiegeEvent _flightSiege;
        private SettlementVisualManager _flightManager;
        private SettlementVisual _flightVisual;
        private MapScreen _flightScreen;
        private readonly CoopCampaignMapSiegeFlightJournal _flightJournal = new CoopCampaignMapSiegeFlightJournal();
        private readonly Dictionary<long, CoopCampaignMapSiegeProjectile> _flightAppearances = new Dictionary<long, CoopCampaignMapSiegeProjectile>();
        private readonly Dictionary<long, float[]> _flightScales = new Dictionary<long, float[]>();
        private static readonly PropertyInfo PrefabCacheProperty = typeof(MapScreen).GetProperty("PrefabEntityCache");
        private static readonly MethodInfo PrefabScaleMethod = PrefabCacheProperty?.PropertyType.GetMethod(
            "GetScaleForSiegeEngine", new[] { typeof(SiegeEngineType), typeof(BattleSideEnum) });

        internal CoopCampaignMapSiegeProjectileCapture()
        {
            _flightInstalled = CoopSiegeFlightObservationPatch.TryInstall(BeginFlightTick, FinishFlightTick,
                _flightObservation.Clear, _flightObservation.Observe);
        }

        private void BeginFlightTick(SettlementVisualManager manager)
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread) return;
            try { BeginFlightTickCore(manager); }
            finally { if (_flightSiege == null) { _flightJournal.Clear(); _flightAppearances.Clear(); _flightScales.Clear(); } }
        }

        private void BeginFlightTickCore(SettlementVisualManager manager)
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread) return;
            ClearFlightBatch();
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var siege = PlayerSiege.PlayerSiegeEvent;
            if (campaign == null || siege == null || manager != SettlementVisualManager.Current ||
                MapScreen.Instance == null || Mission.Current != null ||
                !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                campaign.ConversationManager?.IsConversationInProgress == true) return;
            var visual = manager.GetSettlementVisual(siege.BesiegedSettlement);
            if (visual?.StrategicEntity == null || !visual.StrategicEntity.IsVisibleIncludeParents()) return;
            double hours = CampaignTime.Now.ToHours;
            if (double.IsNaN(hours) || double.IsInfinity(hours)) return;
            if (hours < _lastCampaignHours) { _shotIds.Clear(); _flightJournal.Clear(); _flightAppearances.Clear(); _flightScales.Clear(); }
            _lastCampaignHours = hours;
            var missiles = new List<Missile>();
            int defenders = 0;
            for (int side = 0; side < 2; side++)
            {
                var population = siege.GetSiegeEventSide((BattleSideEnum)side).SiegeEngineMissiles;
                if (population.Count > CoopCampaignMapSiegeProjectiles.MaximumCount) return;
                foreach (var missile in population) missiles.Add(missile);
                if (side == 0) defenders = population.Count;
            }
            if (!_shotIds.TryAssign(siege, missiles, out var ids)) return;
            if (_missiles == null)
            {
                _missiles = typeof(SettlementVisual).GetField("_siegeMissileEntities", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_missiles?.FieldType != typeof(List<ProjectileEntry>)) { _missiles = null; return; }
            }
            var roots = _missiles.GetValue(visual) as List<ProjectileEntry>;
            if (roots == null || roots.Count > CoopCampaignMapSiegeProjectiles.MaximumCount) return;
            var recent = _flightJournal.Snapshot(siege, System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
            foreach (long key in new List<long>(_flightAppearances.Keys))
                if (!recent.ContainsKey(key)) { _flightAppearances.Remove(key); _flightScales.Remove(key); }
            foreach (var entry in roots)
            {
                int side = (int)entry.Item2;
                int populationCount = side == 0 ? defenders : missiles.Count - defenders;
                if (side < 0 || side > 1 || entry.Item3 < 0 || entry.Item1 == null) return;
                // Native retains hidden roots after the managed list shrinks.
                if (entry.Item3 >= populationCount) continue;
                int index = (side == 0 ? 0 : defenders) + entry.Item3;
                long id = ids[index];
                var cache = PrefabCacheProperty?.GetValue(MapScreen.Instance);
                if (cache == null || PrefabScaleMethod?.ReturnType != typeof(TaleWorlds.Library.Vec3)) return;
                var scale = (TaleWorlds.Library.Vec3)PrefabScaleMethod.Invoke(cache,
                    new object[] { missiles[index].ShooterSiegeEngineType, entry.Item2 });
                if (float.IsNaN(scale.x) || float.IsNaN(scale.y) || float.IsNaN(scale.z) ||
                    scale.x <= 0 || scale.y <= 0 || scale.z <= 0 ||
                    scale.x > 1000 || scale.y > 1000 || scale.z > 1000) return;
                var sample = new CoopCampaignMapSiegeProjectile {
                    Side = side, Index = entry.Item3, ShotId = id, Prefab = entry.Item1.GetPrefabName(),
                    Frame = CopyFrame(entry.Item1.GetGlobalFrame()), FlightChildVisible = false
                };
                if (!sample.IsValid()) return;
                _flightAppearances[id] = sample;
                _flightScales[id] = new[] { scale.x, scale.y, scale.z };
            }
            if (!_flightObservation.Begin(visual, missiles, ids)) return;
            _flightSiege = siege; _flightManager = manager; _flightVisual = visual; _flightScreen = MapScreen.Instance;
        }

        private void FinishFlightTick()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread) return;
            bool accepted = false;
            try
            {
                if (_flightSiege == null || !ReferenceEquals(_flightSiege, PlayerSiege.PlayerSiegeEvent) ||
                    !ReferenceEquals(_flightManager, SettlementVisualManager.Current) ||
                    !ReferenceEquals(_flightScreen, MapScreen.Instance) || Mission.Current != null ||
                    !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                    TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.IsConversationInProgress == true ||
                    !ReferenceEquals(_flightVisual, _flightManager.GetSettlementVisual(_flightSiege.BesiegedSettlement)) ||
                    _flightVisual.StrategicEntity == null || !_flightVisual.StrategicEntity.IsVisibleIncludeParents()) return;
                if (_flightObservation.Finish(out var flights))
                    accepted = _flightJournal.Merge(_flightSiege,
                        System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency, flights);
            }
            finally
            {
                if (!accepted) { _flightJournal.Clear(); _flightAppearances.Clear(); _flightScales.Clear(); }
                _flightObservation.Clear();
                _flightSiege = null; _flightManager = null; _flightVisual = null; _flightScreen = null;
            }
        }

        private void ClearFlightBatch()
        {
            _flightObservation.Clear();
            _flightSiege = null; _flightManager = null; _flightVisual = null; _flightScreen = null;
        }

        internal void ReleaseFlights()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread) return;
            ClearFlightBatch();
            _flightJournal.Clear();
            _flightAppearances.Clear();
            _flightScales.Clear();
            if (_flightInstalled) { CoopSiegeFlightObservationPatch.Reset(); _flightInstalled = false; }
        }

        private bool AttachFlights(SiegeEvent siege, CoopCampaignMapSiegeProjectiles candidate)
        {
            if (!_flightInstalled) return true;
            var flights = _flightJournal.Snapshot(siege,
                System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
            var present = new HashSet<long>();
            foreach (var pair in flights)
            {
                if (!_flightScales.TryGetValue(pair.Key, out var scale)) return false;
                pair.Value.PresentationScale = (float[])scale.Clone();
            }
            foreach (var item in candidate.Items)
            {
                present.Add(item.ShotId);
                if (flights.TryGetValue(item.ShotId, out var flight)) item.Flight = flight;
            }
            foreach (var pair in flights)
            {
                if (present.Contains(pair.Key)) continue;
                if (!_flightAppearances.TryGetValue(pair.Key, out var appearance)) return false;
                var item = appearance.Clone(); item.Flight = pair.Value; item.TrajectoryOnly = true;
                candidate.Items.Add(item);
                if (candidate.Items.Count > CoopCampaignMapSiegeProjectiles.MaximumCount) return false;
            }
            return true;
        }
    }
}
