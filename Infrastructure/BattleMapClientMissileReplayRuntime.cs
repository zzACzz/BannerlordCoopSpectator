using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMessages.FromServer;

namespace CoopSpectator.Infrastructure
{
    internal static class BattleMapClientMissileReplayRuntime
    {
        internal enum DeferredHandleMissileCollisionReplayGate
        {
            Ready = 0,
            Wait = 1,
            DropBecauseMissileIndexReused = 2
        }

        internal static readonly TimeSpan DeferredReplayAttemptThrottleWindow = TimeSpan.FromMilliseconds(100);
        internal static readonly TimeSpan DeferredHandleMissileCollisionBindWindow = TimeSpan.FromSeconds(1);

        private static readonly List<DeferredCreateMissilePayload> DeferredCreateMissilePayloads =
            new List<DeferredCreateMissilePayload>();
        private static readonly List<DeferredHandleMissileCollisionReactionPayload> DeferredHandleMissileCollisionReactionPayloads =
            new List<DeferredHandleMissileCollisionReactionPayload>();
        private static readonly Dictionary<int, ObservedCreateMissileState> LatestObservedCreateMissilesByIndex =
            new Dictionary<int, ObservedCreateMissileState>();

        private static long _nextDeferredCreateMissileSequence;
        private static long _nextDeferredHandleMissileCollisionReactionSequence;
        private static long _nextObservedCreateMissileSequence;

        internal sealed class DeferredCreateMissilePayload
        {
            public long Sequence;
            public CreateMissile Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        internal sealed class ObservedCreateMissileState
        {
            public long Sequence;
            public int MissileIndex;
            public int AgentIndex;
            public DateTime ObservedUtc;
            public string Source;
        }

        internal sealed class DeferredHandleMissileCollisionReactionPayload
        {
            public long Sequence;
            public HandleMissileCollisionReaction Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
            public long ExpectedCreateMissileObservationSequence;
        }

        internal static void Reset()
        {
            lock (DeferredCreateMissilePayloads)
            {
                DeferredCreateMissilePayloads.Clear();
            }

            lock (DeferredHandleMissileCollisionReactionPayloads)
            {
                DeferredHandleMissileCollisionReactionPayloads.Clear();
            }

            lock (LatestObservedCreateMissilesByIndex)
            {
                LatestObservedCreateMissilesByIndex.Clear();
            }

            _nextDeferredCreateMissileSequence = 0;
            _nextDeferredHandleMissileCollisionReactionSequence = 0;
            _nextObservedCreateMissileSequence = 0;
        }

        internal static int GetDeferredCreateMissilePayloadCount()
        {
            lock (DeferredCreateMissilePayloads)
            {
                return DeferredCreateMissilePayloads.Count;
            }
        }

        internal static int GetDeferredHandleMissileCollisionReactionPayloadCount()
        {
            lock (DeferredHandleMissileCollisionReactionPayloads)
            {
                return DeferredHandleMissileCollisionReactionPayloads.Count;
            }
        }

        internal static bool HasDeferredCreateMissilePayloadForAgent(int agentIndex)
        {
            if (agentIndex < 0)
                return false;

            lock (DeferredCreateMissilePayloads)
            {
                return DeferredCreateMissilePayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex);
            }
        }

        internal static List<DeferredCreateMissilePayload> GetDeferredCreateMissilePayloadSnapshot()
        {
            lock (DeferredCreateMissilePayloads)
            {
                if (DeferredCreateMissilePayloads.Count <= 0)
                    return null;

                return DeferredCreateMissilePayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }
        }

        internal static List<DeferredHandleMissileCollisionReactionPayload> GetDeferredHandleMissileCollisionReactionPayloadSnapshot()
        {
            lock (DeferredHandleMissileCollisionReactionPayloads)
            {
                if (DeferredHandleMissileCollisionReactionPayloads.Count <= 0)
                    return null;

                return DeferredHandleMissileCollisionReactionPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }
        }

        internal static void RegisterDeferredCreateMissilePayload(
            CreateMissile createMissile,
            string deferralReason)
        {
            if (createMissile == null)
                return;

            lock (DeferredCreateMissilePayloads)
            {
                DeferredCreateMissilePayload existingPayload = DeferredCreateMissilePayloads
                    .FirstOrDefault(candidate => AreDeferredCreateMissileMessagesEquivalent(candidate?.Message, createMissile));
                if (existingPayload != null)
                {
                    existingPayload.Message = createMissile;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredCreateMissilePayloads.Add(
                    new DeferredCreateMissilePayload
                    {
                        Sequence = ++_nextDeferredCreateMissileSequence,
                        Message = createMissile,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        internal static void RegisterDeferredHandleMissileCollisionReactionPayload(
            HandleMissileCollisionReaction handleMissileCollisionReaction,
            string deferralReason)
        {
            if (handleMissileCollisionReaction == null)
                return;

            long expectedCreateMissileObservationSequence =
                ResolveDeferredHandleMissileCollisionReactionExpectedCreateSequence(handleMissileCollisionReaction);
            lock (DeferredHandleMissileCollisionReactionPayloads)
            {
                DeferredHandleMissileCollisionReactionPayload existingPayload =
                    DeferredHandleMissileCollisionReactionPayloads.FirstOrDefault(
                        candidate => AreDeferredHandleMissileCollisionReactionMessagesEquivalent(
                            candidate?.Message,
                            handleMissileCollisionReaction));
                if (existingPayload != null)
                {
                    existingPayload.Message = handleMissileCollisionReaction;
                    existingPayload.DeferralReason = deferralReason;
                    if (existingPayload.ExpectedCreateMissileObservationSequence <= 0 &&
                        expectedCreateMissileObservationSequence > 0)
                    {
                        existingPayload.ExpectedCreateMissileObservationSequence =
                            expectedCreateMissileObservationSequence;
                    }

                    return;
                }

                DeferredHandleMissileCollisionReactionPayloads.Add(
                    new DeferredHandleMissileCollisionReactionPayload
                    {
                        Sequence = ++_nextDeferredHandleMissileCollisionReactionSequence,
                        Message = handleMissileCollisionReaction,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason,
                        ExpectedCreateMissileObservationSequence = expectedCreateMissileObservationSequence
                    });
            }
        }

        internal static bool HasDeferredCreateMissilePayload(int missileIndex, int shooterAgentIndex = int.MinValue)
        {
            if (missileIndex < 0)
                return false;

            lock (DeferredCreateMissilePayloads)
            {
                return DeferredCreateMissilePayloads.Any(candidate =>
                    candidate?.Message != null &&
                    candidate.Message.MissileIndex == missileIndex &&
                    (shooterAgentIndex == int.MinValue || candidate.Message.AgentIndex == shooterAgentIndex));
            }
        }

        internal static void RecordObservedCreateMissile(CreateMissile createMissile, string source)
        {
            if (createMissile == null || createMissile.MissileIndex < 0)
                return;

            lock (LatestObservedCreateMissilesByIndex)
            {
                LatestObservedCreateMissilesByIndex[createMissile.MissileIndex] =
                    new ObservedCreateMissileState
                    {
                        Sequence = ++_nextObservedCreateMissileSequence,
                        MissileIndex = createMissile.MissileIndex,
                        AgentIndex = createMissile.AgentIndex,
                        ObservedUtc = DateTime.UtcNow,
                        Source = source
                    };
            }
        }

        internal static bool TryGetLatestObservedCreateMissileState(
            int missileIndex,
            out ObservedCreateMissileState observedState)
        {
            observedState = null;
            if (missileIndex < 0)
                return false;

            lock (LatestObservedCreateMissilesByIndex)
            {
                if (!LatestObservedCreateMissilesByIndex.TryGetValue(
                        missileIndex,
                        out ObservedCreateMissileState existingState) ||
                    existingState == null)
                {
                    return false;
                }

                observedState = new ObservedCreateMissileState
                {
                    Sequence = existingState.Sequence,
                    MissileIndex = existingState.MissileIndex,
                    AgentIndex = existingState.AgentIndex,
                    ObservedUtc = existingState.ObservedUtc,
                    Source = existingState.Source
                };
                return true;
            }
        }

        internal static long ResolveDeferredHandleMissileCollisionReactionExpectedCreateSequence(
            HandleMissileCollisionReaction handleMissileCollisionReaction)
        {
            if (handleMissileCollisionReaction == null ||
                !TryGetLatestObservedCreateMissileState(
                    handleMissileCollisionReaction.MissileIndex,
                    out ObservedCreateMissileState observedState) ||
                observedState.AgentIndex != handleMissileCollisionReaction.AttackerAgentIndex)
            {
                return 0;
            }

            return observedState.Sequence;
        }

        internal static bool ShouldThrottleDeferredReplayAttempt(DateTime lastAttemptUtc, DateTime nowUtc)
        {
            return lastAttemptUtc != DateTime.MinValue &&
                   nowUtc - lastAttemptUtc < DeferredReplayAttemptThrottleWindow;
        }

        internal static void MarkDeferredCreateMissileReplayAttempt(
            DeferredCreateMissilePayload payload,
            DateTime nowUtc)
        {
            if (payload == null)
                return;

            payload.LastAttemptUtc = nowUtc;
            payload.Attempts++;
        }

        internal static void MarkDeferredHandleMissileCollisionReactionReplayAttempt(
            DeferredHandleMissileCollisionReactionPayload payload,
            DateTime nowUtc)
        {
            if (payload == null)
                return;

            payload.LastAttemptUtc = nowUtc;
            payload.Attempts++;
        }

        internal static DeferredHandleMissileCollisionReplayGate EvaluateDeferredHandleMissileCollisionReactionReplay(
            DeferredHandleMissileCollisionReactionPayload payload,
            out ObservedCreateMissileState latestObservedCreateMissileState)
        {
            latestObservedCreateMissileState = null;
            HandleMissileCollisionReaction handleMissileCollisionReaction = payload?.Message;
            if (handleMissileCollisionReaction == null)
                return DeferredHandleMissileCollisionReplayGate.Wait;

            if (payload.ExpectedCreateMissileObservationSequence <= 0 &&
                TryGetLatestObservedCreateMissileState(
                    handleMissileCollisionReaction.MissileIndex,
                    out ObservedCreateMissileState bindCandidateState) &&
                bindCandidateState.AgentIndex == handleMissileCollisionReaction.AttackerAgentIndex &&
                bindCandidateState.ObservedUtc >= payload.DeferredUtc &&
                bindCandidateState.ObservedUtc - payload.DeferredUtc <= DeferredHandleMissileCollisionBindWindow)
            {
                payload.ExpectedCreateMissileObservationSequence = bindCandidateState.Sequence;
            }

            if (payload.ExpectedCreateMissileObservationSequence > 0 &&
                TryGetLatestObservedCreateMissileState(
                    handleMissileCollisionReaction.MissileIndex,
                    out latestObservedCreateMissileState) &&
                latestObservedCreateMissileState.Sequence > payload.ExpectedCreateMissileObservationSequence)
            {
                return DeferredHandleMissileCollisionReplayGate.DropBecauseMissileIndexReused;
            }

            if (HasDeferredCreateMissilePayload(
                    handleMissileCollisionReaction.MissileIndex,
                    handleMissileCollisionReaction.AttackerAgentIndex))
            {
                return DeferredHandleMissileCollisionReplayGate.Wait;
            }

            if (payload.ExpectedCreateMissileObservationSequence > 0)
            {
                if (!TryGetLatestObservedCreateMissileState(
                        handleMissileCollisionReaction.MissileIndex,
                        out latestObservedCreateMissileState) ||
                    latestObservedCreateMissileState.AgentIndex != handleMissileCollisionReaction.AttackerAgentIndex ||
                    latestObservedCreateMissileState.Sequence != payload.ExpectedCreateMissileObservationSequence)
                {
                    latestObservedCreateMissileState = null;
                    return DeferredHandleMissileCollisionReplayGate.Wait;
                }
            }
            else if (TryGetLatestObservedCreateMissileState(
                         handleMissileCollisionReaction.MissileIndex,
                         out latestObservedCreateMissileState) &&
                     latestObservedCreateMissileState.AgentIndex != handleMissileCollisionReaction.AttackerAgentIndex)
            {
                latestObservedCreateMissileState = null;
                return DeferredHandleMissileCollisionReplayGate.Wait;
            }

            return DeferredHandleMissileCollisionReplayGate.Ready;
        }

        internal static void RemoveDeferredCreateMissilePayload(int missileIndex, int shooterAgentIndex = int.MinValue)
        {
            if (missileIndex < 0)
                return;

            lock (DeferredCreateMissilePayloads)
            {
                DeferredCreateMissilePayloads.RemoveAll(candidate =>
                    candidate?.Message != null &&
                    candidate.Message.MissileIndex == missileIndex &&
                    (shooterAgentIndex == int.MinValue || candidate.Message.AgentIndex == shooterAgentIndex));
            }
        }

        internal static void RemoveDeferredHandleMissileCollisionReactionPayload(
            HandleMissileCollisionReaction referenceMessage)
        {
            if (referenceMessage == null || referenceMessage.MissileIndex < 0)
                return;

            lock (DeferredHandleMissileCollisionReactionPayloads)
            {
                DeferredHandleMissileCollisionReactionPayloads.RemoveAll(candidate =>
                    AreDeferredHandleMissileCollisionReactionMessagesEquivalent(
                        candidate?.Message,
                        referenceMessage));
            }
        }

        private static bool AreDeferredCreateMissileMessagesEquivalent(
            CreateMissile left,
            CreateMissile right)
        {
            return left != null &&
                   right != null &&
                   left.MissileIndex == right.MissileIndex &&
                   left.AgentIndex == right.AgentIndex;
        }

        private static bool AreDeferredHandleMissileCollisionReactionMessagesEquivalent(
            HandleMissileCollisionReaction left,
            HandleMissileCollisionReaction right)
        {
            return left != null &&
                   right != null &&
                   left.MissileIndex == right.MissileIndex &&
                   left.AttackerAgentIndex == right.AttackerAgentIndex &&
                   left.AttachedAgentIndex == right.AttachedAgentIndex &&
                   left.CollisionReaction == right.CollisionReaction &&
                   left.AttachedToShield == right.AttachedToShield &&
                   left.AttachedBoneIndex == right.AttachedBoneIndex &&
                   GetMissionObjectIdValue(left.AttachedMissionObjectId) == GetMissionObjectIdValue(right.AttachedMissionObjectId) &&
                   left.ForcedSpawnIndex == right.ForcedSpawnIndex;
        }

        private static int GetMissionObjectIdValue(TaleWorlds.MountAndBlade.MissionObjectId missionObjectId)
        {
            return missionObjectId.Id;
        }
    }
}
