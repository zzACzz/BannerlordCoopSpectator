using System;
using System.Reflection;
using System.Threading;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using SandBox.View.Map.Managers;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    // Owner-thread, read-only prepared graphics capture for the published siege overlay.
    internal sealed class CoopCampaignMapSiegeDecalCapture
    {
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private static readonly string[] Names = { "_defenderMachinesCircleEntities",
            "_attackerRamMachinesCircleEntities", "_attackerTowerMachinesCircleEntities",
            "_attackerRangedMachinesCircleEntities" };
        private FieldInfo[] _fields;
        private FieldInfo _added;

        internal bool TryCapture(out CoopCampaignMapSiegeDecals value)
        {
            value = null;
            if (Thread.CurrentThread.ManagedThreadId != _thread) return false;
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
                if (!(bool)_added.GetValue(manager)) return true;
                var visual = manager.GetSettlementVisual(siege.BesiegedSettlement);
                if (visual?.StrategicEntity == null || !visual.StrategicEntity.IsVisibleIncludeParents()) return true;
                var arrays = new GameEntity[4][];
                var candidate = new CoopCampaignMapSiegeDecals();
                int inspected = 0;
                for (int group = 0; group < arrays.Length; group++)
                {
                    arrays[group] = _fields[group].GetValue(manager) as GameEntity[];
                    if (arrays[group] == null || arrays[group].Length > CoopCampaignMapSiegeDecals.MaximumCount) return false;
                    for (int index = 0; index < arrays[group].Length; index++)
                    {
                        if (++inspected > CoopCampaignMapSiegeDecals.MaximumCount) return false;
                        var entity = arrays[group][index];
                        if (entity == null) return false;
                        if (!entity.IsVisibleIncludeParents()) continue;
                        var decal = entity.GetComponentAtIndex(0, (GameEntity.ComponentType)7) as Decal;
                        if (decal == null) return false;
                        var item = new CoopCampaignMapSiegeDecal { Group = group, Index = index,
                            Color = decal.GetFactor1(), Frame = CopyFrame(entity.GetGlobalFrame()) };
                        if (decal.GetMaterial()?.Name != item.MaterialName) return false;
                        candidate.Items.Add(item);
                    }
                }
                if (!ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                    !ReferenceEquals(screen, MapScreen.Instance) || !ReferenceEquals(manager, SettlementVisualManager.Current) ||
                    !ReferenceEquals(siege, PlayerSiege.PlayerSiegeEvent) || !(bool)_added.GetValue(manager) ||
                    !ReferenceEquals(visual, manager.GetSettlementVisual(siege.BesiegedSettlement))) return false;
                for (int i = 0; i < arrays.Length; i++)
                    if (!ReferenceEquals(arrays[i], _fields[i].GetValue(manager))) return false;
                return candidate.TrySnapshot(out value);
            }
            catch (Exception) { return false; }
        }

        private bool Resolve()
        {
            if (_fields != null) return true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var owner = typeof(SettlementVisualManager);
            var added = owner.GetField("_playerSiegeMachineSlotMeshesAdded", flags);
            if (added?.FieldType != typeof(bool)) return false;
            var fields = new FieldInfo[Names.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = owner.GetField(Names[i], flags);
                if (fields[i]?.FieldType != typeof(GameEntity[])) return false;
            }
            _added = added; _fields = fields; return true;
        }

        private static float[] CopyFrame(MatrixFrame frame) => new[] {
            frame.rotation.s.x, frame.rotation.s.y, frame.rotation.s.z,
            frame.rotation.f.x, frame.rotation.f.y, frame.rotation.f.z,
            frame.rotation.u.x, frame.rotation.u.y, frame.rotation.u.z,
            frame.origin.x, frame.origin.y, frame.origin.z };
    }
}
