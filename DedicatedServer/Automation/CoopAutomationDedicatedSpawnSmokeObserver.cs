using System;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure.Automation
{
    // Runs only from the already profile-gated dedicated control tick.
    internal static class CoopAutomationDedicatedSpawnSmokeObserver
    {
        private static CoopAutomationSmokeLifecycle _lifecycle;
        private static Mission _mission;
        private static DateTime _nextPollUtc;
        private static bool _captured;
        public static string State { get; private set; } = string.Empty;
        public static string Failure { get; private set; } = string.Empty;
        public static bool IsTerminal { get; private set; }
        public static CoopAutomationDedicatedSpawnSmokeEvidence Evidence { get; private set; }

        public static void Begin()
        {
            if (_lifecycle != null || !CoopAutomationSpawnSmokeBridge.IsActive)
                throw new InvalidOperationException("SpawnSmokeObserverAlreadyBound");
            _lifecycle = new CoopAutomationSmokeLifecycle();
            Evidence = new CoopAutomationDedicatedSpawnSmokeEvidence
            { InitialStateWasClean = CoopAutomationSpawnSmokeBridge.InitialStateWasClean };
            State = "WaitingForMissionCommandReady";
        }

        public static void Tick()
        {
            if (_lifecycle == null || IsTerminal || DateTime.UtcNow < _nextPollUtc) return;
            _nextPollUtc = DateTime.UtcNow.AddMilliseconds(100);
            try
            {
                if (!string.IsNullOrEmpty(CoopAutomationSpawnSmokeBridge.Failure))
                { Fail(CoopAutomationSpawnSmokeBridge.Failure); return; }
                Mission current = Mission.Current;
                if (_lifecycle.StartRequests == 0)
                {
                    if (current != null) { Fail("UnexpectedExistingMission"); return; }
                    if (!CoopAutomationDedicatedControlBridge.TryObserveNativeCommandReadiness(opening: true)) return;
                    if (!_lifecycle.TryClaimStart(true, false, out string failure)) { Fail(failure); return; }
                    Evidence.StartMissionRequests = _lifecycle.StartRequests;
                    State = "MissionOpening";
                    GameNetwork.HandleConsoleCommand("start_mission");
                    return;
                }
                if (_mission == null)
                {
                    if (current == null || current.CurrentState != Mission.State.Continuing) return;
                    _mission = current;
                    State = "MissionCurrent";
                    return;
                }
                if (_lifecycle.EndRequests != 0)
                {
                    if (current != null)
                    {
                        if (!ReferenceEquals(current, _mission)) Fail("UnexpectedReplacementMission");
                        return;
                    }
                    Evidence.MissionDisposed = true;
                    Evidence.ProtectedResultUnchanged = CoopAutomationSpawnSmokeBridge.CheckProtectedResult();
                    Evidence.PhaseBeforeEnd = CoopAutomationSpawnSmokeBridge.PhaseBeforeEnd;
                    Evidence.ResultAttempts = CoopAutomationSpawnSmokeBridge.ResultAttempts;
                    Evidence.SuppressedResults = CoopAutomationSpawnSmokeBridge.SuppressedResults;
                    Evidence.ResultEntriesAtAttempt = CoopAutomationSpawnSmokeBridge.ResultEntriesAtAttempt;
                    if (!CoopAutomationSpawnSmokeBridge.MissionEnded || !Evidence.ProtectedResultUnchanged ||
                        Evidence.PhaseBeforeEnd != CoopAutomationSpawnSmokeContract.Stage ||
                        Evidence.ResultAttempts < 1 || Evidence.ResultAttempts != Evidence.SuppressedResults ||
                        Evidence.ResultEntriesAtAttempt != 47)
                    { Fail("EarlyAbortSuppressionEvidenceIncomplete"); return; }
                    State = "SpawnSmokePassed";
                    IsTerminal = true;
                    return;
                }
                if (!ReferenceEquals(current, _mission) || current.CurrentState != Mission.State.Continuing)
                { Fail("MissionLostBeforeAbort"); return; }
                string phase = CoopBattlePhaseRuntimeState.GetPhase().ToString();
                if (phase == "BattleActive" || phase == "BattleEnded")
                { Fail("PreBattleBoundaryExceeded"); return; }
                if (!_captured)
                {
                    State = "Materializing";
                    if (phase != CoopAutomationSpawnSmokeContract.Stage) return;
                    // One potentially native-sensitive scan; failure is terminal, never repaired/retried.
                    _captured = true;
                    Evidence.Observation = CoopMissionSpawnLogic.TryCaptureAutomationSpawnSmokeEvidence(_mission);
                    if (!CoopAutomationSpawnSmokeContract.TryValidateObservation(
                        CoopAutomationSpawnSmokeBridge.Fixture, Evidence.Observation, out string failure))
                    { Fail(failure); return; }
                    if (!CoopAutomationSpawnSmokeBridge.CheckProtectedResult())
                    { Fail(CoopAutomationSpawnSmokeBridge.Failure); return; }
                    State = "PreBattleHold";
                    return; // Publish the observed boundary before requesting the normal abort.
                }
                if (!CoopAutomationDedicatedControlBridge.TryObserveNativeCommandReadiness(opening: false)) return;
                if (!_lifecycle.TryClaimEnd(true, CoopAutomationSpawnSmokeBridge.MatchesMission(current), phase, out string endFailure))
                { Fail(endFailure); return; }
                Evidence.EndMissionRequests = _lifecycle.EndRequests;
                State = "MissionAborting";
                GameNetwork.HandleConsoleCommand("end_mission");
            }
            catch (Exception ex) { Fail("SpawnSmokeObservationFailed:" + ex.GetType().Name + ":" + ex.Message); }
        }

        private static void Fail(string failure)
        {
            Failure = failure;
            State = "Failed";
            IsTerminal = true;
        }

        public static void Reset()
        {
            _lifecycle = null;
            _mission = null;
            _nextPollUtc = DateTime.MinValue;
            _captured = IsTerminal = false;
            State = Failure = string.Empty;
            Evidence = null;
        }
    }
}