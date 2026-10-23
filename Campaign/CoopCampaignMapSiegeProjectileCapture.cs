using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using ProjectileEntry = System.ValueTuple<TaleWorlds.Engine.GameEntity, TaleWorlds.Core.BattleSideEnum, int>;
using CampaignMissile = TaleWorlds.CampaignSystem.Siege.SiegeEvent.SiegeEngineMissile;

namespace CoopSpectator.Campaign
{
    // Native-sensitive owner-thread reads only. Never retain engine objects in output.
    internal sealed partial class CoopCampaignMapSiegeProjectileCapture
    {
        private readonly int _ownerThread = Thread.CurrentThread.ManagedThreadId;
        private FieldInfo _missiles;
        private readonly CoopSiegeProjectileParticleProbe _particleProbe = new CoopSiegeProjectileParticleProbe();
        private readonly CoopCampaignMapSiegeShotIdentity<CampaignMissile> _shotIds =
            new CoopCampaignMapSiegeShotIdentity<CampaignMissile>(256);
        private double _lastCampaignHours;

        internal bool TryCapture(out CoopCampaignMapSiegeProjectiles result)
        {
            result = null;
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread) return false;
            try
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                var screen = MapScreen.Instance;
                var manager = SettlementVisualManager.Current;
                var siege = PlayerSiege.PlayerSiegeEvent;
                if (campaign == null || screen == null || manager == null || siege == null ||
                    Mission.Current != null || !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                    campaign.ConversationManager?.IsConversationInProgress == true)
                { _shotIds.Clear(); return true; }
                double hours = TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours;
                if (double.IsNaN(hours) || double.IsInfinity(hours)) return false;
                if (hours < _lastCampaignHours) _shotIds.Clear();
                _lastCampaignHours = hours;
                if (_missiles == null)
                {
                    var field = typeof(SettlementVisual).GetField("_siegeMissileEntities", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field?.FieldType != typeof(List<ProjectileEntry>)) return false;
                    _missiles = field;
                }
                var visual = manager.GetSettlementVisual(siege.BesiegedSettlement);
                var root = visual?.StrategicEntity;
                if (root == null || !root.IsVisibleIncludeParents()) return true;
                var entries = _missiles.GetValue(visual) as List<ProjectileEntry>;
                if (entries == null || entries.Count > CoopCampaignMapSiegeProjectiles.MaximumCount) return false;
                int count = entries.Count;
                // Snapshot both complete managed lists: hidden missiles must retain IDs.
                var populations = new CampaignMissile[2][];
                var allMissiles = new List<CampaignMissile>();
                for (int side = 0; side < 2; side++)
                {
                    var list = siege.GetSiegeEventSide((BattleSideEnum)side).SiegeEngineMissiles;
                    if (list.Count > CoopCampaignMapSiegeProjectiles.MaximumCount) return false;
                    populations[side] = new CampaignMissile[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] == null) return false;
                        populations[side][i] = list[i];
                        allMissiles.Add(list[i]);
                    }
                }
                var candidate = new CoopCampaignMapSiegeProjectiles();
                var populationIndices = new List<int>();
                foreach (var entry in entries)
                {
                    var entity = entry.Item1;
                    if (entity == null) return false;
                    if (!entity.IsVisibleIncludeParents()) continue;
                    int side = (int)entry.Item2;
                    if (side < 0 || side > 1 || entry.Item3 < 0 || entry.Item3 >= populations[side].Length) return false;
                    // Native bombardment addresses child zero independently of root.
                    // Reject incompatible roots before indexing native children.
                    if (entity.ChildCount < 1) return false;
                    var child = entity.WeakEntity.GetChild(0);
                    var frame = entity.GetGlobalFrame();
                    var sample = new CoopCampaignMapSiegeProjectile {
                        Side = side, Index = entry.Item3, ShotId = 1, // assigned after owner revalidation
                        Prefab = entity.GetPrefabName(),
                        Frame = CopyFrame(frame),
                        FlightChildVisible = child.IsVisibleIncludeParents()
                    };
                    if (!sample.IsValid()) return false;
                    _particleProbe.Observe("campaign", entity, sample.Prefab, sample.FlightChildVisible);
                    candidate.Items.Add(sample);
                    populationIndices.Add((side == 0 ? 0 : populations[0].Length) + entry.Item3);
                }
                if (!ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                    !ReferenceEquals(screen, MapScreen.Instance) || !ReferenceEquals(manager, SettlementVisualManager.Current) ||
                    !ReferenceEquals(siege, PlayerSiege.PlayerSiegeEvent) || Mission.Current != null ||
                    !ReferenceEquals(visual, manager.GetSettlementVisual(siege.BesiegedSettlement)) ||
                    !ReferenceEquals(root, visual.StrategicEntity) ||
                    !ReferenceEquals(entries, _missiles.GetValue(visual)) || entries.Count != count) return false;
                for (int side = 0; side < 2; side++)
                {
                    var list = siege.GetSiegeEventSide((BattleSideEnum)side).SiegeEngineMissiles;
                    if (list.Count != populations[side].Length) return false;
                    for (int i = 0; i < list.Count; i++)
                        if (!ReferenceEquals(list[i], populations[side][i])) return false;
                }
                if (!_shotIds.TryAssign(siege, allMissiles, out var ids)) return false;
                for (int i = 0; i < candidate.Items.Count; i++)
                    candidate.Items[i].ShotId = ids[populationIndices[i]];
                if (!AttachFlights(siege, candidate)) return false;
                return candidate.TrySnapshot(out result);
            }
            catch (Exception) { return false; }
        }

        private static float[] CopyFrame(MatrixFrame frame) => new[] {
            frame.rotation.s.x, frame.rotation.s.y, frame.rotation.s.z,
            frame.rotation.f.x, frame.rotation.f.y, frame.rotation.f.z,
            frame.rotation.u.x, frame.rotation.u.y, frame.rotation.u.z,
            frame.origin.x, frame.origin.y, frame.origin.z };
    }
}
