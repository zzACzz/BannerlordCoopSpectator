using System;
using System.Threading;
using CoopSpectator.Infrastructure;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    // Read-only owner-thread sampler. Caller must additionally apply current body disclosure.
    internal sealed class CoopCampaignMapSiegeTentCapture
    {
        private readonly int _ownerThread = Thread.CurrentThread.ManagedThreadId;

        internal static bool IsCampParticipant(PartyBase party)
        {
            var siege = party?.MobileParty?.BesiegedSettlement?.SiegeEvent;
            return siege != null && siege.BesiegerCamp.HasInvolvedPartyForEventType(
                party, MapEvent.BattleTypes.Siege);
        }

        internal bool TryCapture(PartyBase party, MobilePartyVisual source, bool hostDisclosed,
            out CoopCampaignMapSiegeTent value)
        {
            value = null;
            if (!hostDisclosed || party == null || source == null ||
                Thread.CurrentThread.ManagedThreadId != _ownerThread) return false;
            try
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                var manager = MobilePartyVisualManager.Current;
                if (campaign == null || manager == null || Mission.Current != null ||
                    !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                    campaign.ConversationManager?.IsConversationInProgress == true ||
                    party.IsVisualDirty || !party.IsVisible ||
                    !ReferenceEquals(source, manager.GetVisualOfEntity(party)) ||
                    source.HumanAgentVisuals != null || source.MountAgentVisuals != null ||
                    source.CaravanMountAgentVisuals != null) return false;
                if (!IsCampParticipant(party)) return false;
                GameEntity root = source.StrategicEntity;
                if (root == null || !root.IsVisibleIncludeParents()) return false;
                int children = root.ChildCount;
                if (children < 1 || children > 16) return false;
                CoopCampaignMapSiegeTent found = null;
                for (int i = 0; i < children; i++)
                {
                    GameEntity child = root.GetChild(i);
                    if (child == null || !child.IsVisibleIncludeParents()) continue;
                    int count = child.MultiMeshComponentCount;
                    if (count > 16) return false;
                    bool tent = false;
                    MetaMesh flag = null;
                    for (int j = 0; j < count; j++)
                    {
                        MetaMesh mesh = child.GetMetaMesh(j);
                        string name = mesh?.GetName();
                        if (name == CoopCampaignMapSiegeTent.TentMesh)
                        {
                            if (tent) return false;
                            tent = true;
                        }
                        else if (name == CoopCampaignMapSiegeTent.FlagMesh)
                        {
                            if (flag != null) return false;
                            flag = mesh;
                        }
                    }
                    if (!tent) continue;
                    int plainFlagCount = flag == null ? 0 : 1;
                    // Native AddTentEntityForParty stores campaign_flag as a cloth
                    // component (including its cached-component reuse branch).
                    // MultiMeshComponentCount deliberately excludes those components.
                    int clothCount = child.ClothSimulatorComponentCount;
                    if (clothCount > 1) return false;
                    if (clothCount == 1)
                    {
                        MetaMesh clothFlag = child.GetClothSimulator(0)?.GetFirstMetaMesh();
                        if (flag != null || clothFlag?.GetName() != CoopCampaignMapSiegeTent.FlagMesh)
                            return false;
                        flag = clothFlag;
                    }
                    if (found != null || count != 1 + plainFlagCount) return false;
                    found = new CoopCampaignMapSiegeTent {
                        TentFrame = CopyFrame(child.GetFrame()),
                        BannerCode = flag == null ? string.Empty : party.LeaderHero?.ClanBanner?.BannerCode,
                        FlagFrame = flag == null ? null : CopyFrame(flag.Frame)
                    };
                }
                if (!CoopCampaignMapSiegeTent.IsValid(found) || party.IsVisualDirty || !party.IsVisible ||
                    Mission.Current != null || !ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                    !ReferenceEquals(manager, MobilePartyVisualManager.Current) ||
                    !ReferenceEquals(source, manager.GetVisualOfEntity(party)) || source.StrategicEntity != root ||
                    root.ChildCount != children) return false;
                value = found;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static float[] CopyFrame(MatrixFrame frame) => new[] {
            frame.rotation.s.x, frame.rotation.s.y, frame.rotation.s.z,
            frame.rotation.f.x, frame.rotation.f.y, frame.rotation.f.z,
            frame.rotation.u.x, frame.rotation.u.y, frame.rotation.u.z,
            frame.origin.x, frame.origin.y, frame.origin.z
        };
    }
}
