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
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using MachineEntry = System.ValueTuple<TaleWorlds.Engine.GameEntity, TaleWorlds.Core.BattleSideEnum, int, TaleWorlds.Library.MatrixFrame, TaleWorlds.Engine.GameEntity>;
using CampaignMachine = TaleWorlds.CampaignSystem.Siege.SiegeEvent.SiegeEngineConstructionProgress;

namespace CoopSpectator.Campaign
{
    // Read-only prepared visual capture for the published siege overlay.
    // Native objects remain local to the owner thread and never enter the payload.
    internal sealed class CoopCampaignMapSiegeMachineCapture
    {
        private readonly int _ownerThread = Thread.CurrentThread.ManagedThreadId;
        private FieldInfo _ranged, _melee;
        private readonly CoopCampaignMapSiegeShotIdentity<CampaignMachine> _identities =
            new CoopCampaignMapSiegeShotIdentity<CampaignMachine>(128);

        internal bool TryCapture(out CoopCampaignMapSiegeMachines result)
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
                    campaign.ConversationManager?.IsConversationInProgress == true) return true;
                if (!Resolve()) return false;
                var visual = manager.GetSettlementVisual(siege.BesiegedSettlement);
                var root = visual?.StrategicEntity;
                if (root == null || !root.IsVisibleIncludeParents()) return true;
                var ranged = _ranged.GetValue(visual) as List<MachineEntry>;
                var melee = _melee.GetValue(visual) as List<MachineEntry>;
                if (ranged == null || melee == null || ranged.Count > CoopCampaignMapSiegeMachines.MaximumCount ||
                    melee.Count > CoopCampaignMapSiegeMachines.MaximumCount - ranged.Count) return false;
                var candidate = new CoopCampaignMapSiegeMachines();
                int rangedCount = ranged.Count, meleeCount = melee.Count;
                if (!Read(ranged, true, candidate) || !Read(melee, false, candidate)) return false;
                var machines = new List<CampaignMachine>();
                foreach (var item in candidate.Items)
                {
                    var container = siege.GetSiegeEventSide((BattleSideEnum)item.Side).SiegeEngines;
                    var deployed = item.IsRanged ? container.DeployedRangedSiegeEngines : container.DeployedMeleeSiegeEngines;
                    if (item.Slot >= deployed.Length || deployed[item.Slot] == null) return false;
                    var machine = deployed[item.Slot];
                    machines.Add(machine);
                    double cycle = machine.RangedSiegeEngine?.LastBombardTime.ToHours ?? 0;
                    item.AnimationCycleHours = Math.Max(0, cycle);
                }
                if (!ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                    !ReferenceEquals(screen, MapScreen.Instance) || !ReferenceEquals(manager, SettlementVisualManager.Current) ||
                    !ReferenceEquals(siege, PlayerSiege.PlayerSiegeEvent) || Mission.Current != null ||
                    !ReferenceEquals(visual, manager.GetSettlementVisual(siege.BesiegedSettlement)) ||
                    !ReferenceEquals(root, visual.StrategicEntity) ||
                    !ReferenceEquals(ranged, _ranged.GetValue(visual)) || !ReferenceEquals(melee, _melee.GetValue(visual)) ||
                    ranged.Count != rangedCount || melee.Count != meleeCount) return false;
                for (int i = 0; i < candidate.Items.Count; i++)
                {
                    var item = candidate.Items[i];
                    var container = siege.GetSiegeEventSide((BattleSideEnum)item.Side).SiegeEngines;
                    var deployed = item.IsRanged ? container.DeployedRangedSiegeEngines : container.DeployedMeleeSiegeEngines;
                    if (item.Slot >= deployed.Length || !ReferenceEquals(machines[i], deployed[item.Slot])) return false;
                }
                if (!_identities.TryAssign(siege, machines, out var ids)) return false;
                for (int i = 0; i < candidate.Items.Count; i++) candidate.Items[i].InstanceId = ids[i];
                return candidate.TrySnapshot(out result);
            }
            catch (Exception) { return false; }
        }

        private static bool Read(List<MachineEntry> entries, bool ranged, CoopCampaignMapSiegeMachines candidate)
        {
            foreach (var entry in entries)
            {
                var entity = entry.Item1;
                if (entity == null) return false;
                if (!entity.IsVisibleIncludeParents()) continue;
                string prefab = entity.GetPrefabName();
                if (!CoopCampaignMapSiegeResources.IsMachine(prefab) ||
                    CoopCampaignMapSiegeResources.IsRanged(prefab) != ranged) return false;
                var sample = new CoopCampaignMapSiegeMachine {
                    Side = (int)entry.Item2, Slot = entry.Item3, Prefab = prefab,
                    Frame = CopyFrame(entity.GetGlobalFrame()) };
                if (ranged)
                {
                    var child = entry.Item5;
                    var skeleton = child?.Skeleton;
                    if (child == null || !child.IsVisibleIncludeParents() || skeleton == null || !skeleton.IsValid) return false;
                    sample.Animation = new CoopCampaignMapSiegeAnimation {
                        ClipName = skeleton.GetAnimationAtChannel(0),
                        Progress = skeleton.GetAnimationParameterAtChannel(0),
                        Speed = skeleton.GetAnimationSpeedAtChannel(0) };
                }
                if (!sample.IsValid()) return false;
                candidate.Items.Add(sample);
            }
            return true;
        }

        private bool Resolve()
        {
            if (_ranged != null && _melee != null) return true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var ranged = typeof(SettlementVisual).GetField("_siegeRangedMachineEntities", flags);
            var melee = typeof(SettlementVisual).GetField("_siegeMeleeMachineEntities", flags);
            if (ranged?.FieldType != typeof(List<MachineEntry>) || melee?.FieldType != typeof(List<MachineEntry>)) return false;
            _ranged = ranged; _melee = melee; return true;
        }

        private static float[] CopyFrame(MatrixFrame frame) => new[] {
            frame.rotation.s.x, frame.rotation.s.y, frame.rotation.s.z,
            frame.rotation.f.x, frame.rotation.f.y, frame.rotation.f.z,
            frame.rotation.u.x, frame.rotation.u.y, frame.rotation.u.z,
            frame.origin.x, frame.origin.y, frame.origin.z };
    }
}
