using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    public sealed partial class CoopCampaignMapPrototypePublisherBehavior
    {
        private readonly CoopCampaignMapConversationPublicationLease _conversationPublication =
            new CoopCampaignMapConversationPublicationLease();
        private Mission _standaloneConversationMission;
        private bool _clearConversationOnCaptureFailure;

        private static double PublicationTime => System.Diagnostics.Stopwatch.GetTimestamp() /
            (double)System.Diagnostics.Stopwatch.Frequency;

        // OpenNew returns before the expensive native scene loading tick. Bind
        // the exact new mission now, not to a guessed post-loading time window.
        internal static void ObserveNativeOpening(Mission mission, string missionName)
        {
            var publisher = _applicationTickPublisher;
            if (publisher == null) return;
            publisher._standaloneConversationMission = null;
            publisher._clearConversationOnCaptureFailure = true;
            try
            {
                bool admitted = ExperimentalFeatures.EnableCampaignMapPrototype &&
                    publisher._tooltipOwnerThread == System.Threading.Thread.CurrentThread.ManagedThreadId &&
                    CoopCampaignMapMissionConversationContext.IsStandaloneOpening(mission, missionName);
                if (publisher._conversationPublication.TryAcquire(
                        TaleWorlds.CampaignSystem.Campaign.Current, publisher._sessionId,
                        mission, admitted, PublicationTime, out _))
                    publisher._standaloneConversationMission = mission;
            }
            catch (Exception)
            {
                // Optional read-only observation cannot interrupt native opening.
                publisher.RevokeMissionConversation();
            }
        }

        private bool TryCaptureMissionConversation(Mission mission,
            out CoopCampaignMapPrototypeHostSnapshot snapshot, out string reason)
        {
            snapshot = null;
            reason = "mission-dialogue-not-admitted";
            bool admitted = ReferenceEquals(mission, _standaloneConversationMission) &&
                !mission.MissionEnded && mission.CurrentState != Mission.State.Over &&
                _revision < int.MaxValue;
            if (!_conversationPublication.TryAcquire(TaleWorlds.CampaignSystem.Campaign.Current,
                    _sessionId, mission, admitted, PublicationTime, out var anchor)) return false;
            if (!ReferenceEquals(Mission.Current, mission) ||
                mission.CurrentState != Mission.State.Continuing)
            {
                reason = "mission-dialogue-loading";
                return false;
            }
            _revision++;
            _clearConversationOnCaptureFailure = true;
            UpdateConversationSnapshot();
            snapshot = anchor;
            snapshot.Revision = _revision;
            snapshot.SampleTimeMilliseconds = Environment.TickCount & int.MaxValue;
            snapshot.UpdatedUtc = DateTime.UtcNow;
            snapshot.IsMoving = false;
            snapshot.MovementTarget = string.Empty;
            snapshot.Conversation = _cachedConversation.Clone();
            snapshot.ConversationPose = _capturedConversationPose?.Clone();
            reason = null;
            return true;
        }

        private void RevokeMissionConversation()
        {
            CoopCampaignMapMissionConversationVisualCapture.Reset();
            _conversationPublication.Reset();
            _standaloneConversationMission = null;
        }
    }
}
