using System;
using System.Threading;
using CoopSpectator.Infrastructure;
using SandBox.View.Map.Visuals;
using SandBox.View.Map.Managers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

namespace CoopSpectator.Campaign
{
    // Construct and call on the publishing campaign thread. The caller must first
    // admit the party through the same host-disclosure policy as its body.
    // Reads native skeleton state for bounded map publication, never mutates it.
    internal sealed class CoopCampaignMapPartyAnimationCapture
    {
        private readonly int _ownerThread = Thread.CurrentThread.ManagedThreadId;

        internal bool TryCapture(PartyBase party, MobilePartyVisual source, bool hostDisclosed,
            out CoopCampaignMapPartyAnimation human,
            out CoopCampaignMapPartyAnimation mount,
            out CoopCampaignMapPartyAnimation caravan)
        {
            human = mount = caravan = null;
            if (!hostDisclosed || party == null || source == null || Mission.Current != null ||
                Thread.CurrentThread.ManagedThreadId != _ownerThread) return false;
            try
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                var manager = MobilePartyVisualManager.Current;
                if (campaign == null || manager == null ||
                    !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                    campaign.ConversationManager?.IsConversationInProgress == true ||
                    !ReferenceEquals(source, manager.GetVisualOfEntity(party))) return false;
                var h = source.HumanAgentVisuals;
                var m = source.MountAgentVisuals;
                var c = source.CaravanMountAgentVisuals;
                // An absent human can be an intentional tent presentation. Do not
                // fabricate an idle character or apply a previously captured pose.
                if (h == null || !TryRead(h, out var hp) ||
                    !TryRead(m, out var mp) || !TryRead(c, out var cp)) return false;
                if (Mission.Current != null ||
                    !ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                    !ReferenceEquals(manager, MobilePartyVisualManager.Current) ||
                    !ReferenceEquals(source, manager.GetVisualOfEntity(party)) ||
                    !ReferenceEquals(h, source.HumanAgentVisuals) ||
                    !ReferenceEquals(m, source.MountAgentVisuals) ||
                    !ReferenceEquals(c, source.CaravanMountAgentVisuals)) return false;
                human = hp; mount = mp; caravan = cp; return true;
            }
            catch (Exception) { return false; }
        }

        private static bool TryRead(AgentVisuals visual, out CoopCampaignMapPartyAnimation sample)
        {
            sample = null;
            if (visual == null) return true;
            var native = visual.GetVisuals();
            var skeleton = native?.GetSkeleton();
            if (skeleton == null) return false;
            var value = new CoopCampaignMapPartyAnimation {
                ActionName = skeleton.GetActionAtChannel(0).GetName(),
                Progress = skeleton.GetAnimationParameterAtChannel(0),
                Speed = skeleton.GetAnimationSpeedAtChannel(0) };
            if (!value.IsValid()) return false;
            sample = value; return true;
        }
    }
}
