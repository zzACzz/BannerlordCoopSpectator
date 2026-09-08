using System;
using System.Collections.Generic;
using System.IO;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.Automation
{
    // Default-off process/run binding. No engine objects are inspected in this shared bridge.
    public static class CoopAutomationSpawnSmokeBridge
    {
        public const string ProfileVariable = "COOPSPECTATOR_AUTOMATION_SPAWN_SMOKE_PROFILE";
        private static CoopAutomationRuntimeConfiguration _configuration;
        private static CoopAutomationSmokeFixture _fixture;
        private static object _mission;
        private static bool _opened;
        private static bool _initialized;
        private static bool _ended;
        private static string _protectedResultHash;
        private static string _protectedResultPath;
        public static string Failure { get; private set; } = string.Empty;
        public static string OpenedShell { get; private set; } = string.Empty;
        public static int ResultAttempts { get; private set; }
        public static int SuppressedResults { get; private set; }
        public static int ResultEntriesAtAttempt { get; private set; }
        public static string PhaseBeforeEnd { get; private set; } = string.Empty;
        public static bool MissionEnded => _ended;
        public static bool InitialStateWasClean { get; private set; }
        public static bool IsRequested => CoopAutomationRuntimeBridge.IsAutomationEnabled &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ProfileVariable));
        public static bool IsActive => _configuration != null && _fixture != null;
        public static CoopAutomationSmokeFixture Fixture => _fixture;

        public static bool TryActivate(CoopAutomationRuntimeConfiguration configuration,
            string profile, string fixtureId, string relativeRoot, out string failure)
        {
            failure = "SpawnSmokeProfileMismatch";
            if (!IsRequested || profile != CoopAutomationSpawnSmokeContract.Profile ||
                Environment.GetEnvironmentVariable(ProfileVariable) != profile)
                return false;
            failure = "SpawnSmokeAlreadyBound";
            if (IsActive || _opened || _initialized || _ended || ResultAttempts != 0) return false;
            failure = "SpawnSmokeConfigurationInvalid";
            if (configuration == null || configuration.ResultPolicy != CoopAutomationRuntimeContract.SuppressResultPolicy)
                return false;
            if (!CoopAutomationSpawnSmokeContract.TryLoadFixture(configuration.RunRoot, relativeRoot, fixtureId,
                out CoopAutomationSmokeFixture fixture, out failure)) return false;
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                _protectedResultPath = Path.Combine(docs, "Mount and Blade II Bannerlord", "CoopSpectator", "battle_result.json");
                _protectedResultHash = ReadProtectedResultIdentity();
                _configuration = configuration;
                _fixture = fixture;
                InitialStateWasClean = true;
                failure = string.Empty;
                return true;
            }
            catch (Exception ex) { failure = "ProtectedResultBaselineFailed:" + ex.GetType().Name; return false; }
        }

        public static string ReadRosterJson()
        {
            RequireActive();
            return _fixture.RosterJson; // Immutable verified bytes, never a later path re-read.
        }

        public static string GetPhaseFolder()
        {
            RequireActive();
            if (!CoopAutomationSpawnSmokeContract.TryResolveContainedPath(_configuration.RunRoot,
                "state/phase", out string path, out string failure))
                throw new InvalidOperationException(failure);
            return path;
        }

        public static void ObserveOpening(string scene, string shell)
        {
            if (!IsRequested) return;
            RequireActive();
            if (_opened || scene != CoopAutomationSpawnSmokeContract.Scene || shell != CoopAutomationSpawnSmokeContract.MissionShell)
                throw new InvalidOperationException("SpawnSmokeDuplicateOrMismatchedMissionOpen");
            _opened = true;
            OpenedShell = shell;
        }

        public static void ObserveInitialized(object mission)
        {
            if (!IsActive) return;
            if (!_opened || _initialized || mission == null)
            { Fail("SpawnSmokeMissionInitializationMismatch"); return; }
            _mission = mission;
            _initialized = true;
        }

        public static void ObserveEnding(object mission, string phase)
        {
            if (!IsActive) return;
            if (!ReferenceEquals(_mission, mission)) { Fail("SpawnSmokeEndMissionMismatch"); return; }
            if (!_ended) PhaseBeforeEnd = phase;
            _ended = true;
        }

        public static void ObserveResultAttempt(object mission, string battleId, int entryCount,
            string phase, bool succeeded, bool suppressed)
        {
            if (!IsActive) return;
            ResultAttempts++;
            ResultEntriesAtAttempt = entryCount;
            if (!ReferenceEquals(_mission, mission) || battleId != CoopAutomationSpawnSmokeContract.BattleId)
                Fail("SpawnSmokeResultIdentityMismatch");
            if (!succeeded || !suppressed) Fail("SpawnSmokeResultNotSuppressed");
            else SuppressedResults++;
            if (phase == "BattleActive") Fail("SpawnSmokeBattleActiveReached");
            CheckProtectedResult();
        }

        public static bool CheckProtectedResult()
        {
            if (!IsActive) return false;
            try
            {
                if (ReadProtectedResultIdentity() != _protectedResultHash)
                    Fail("ProtectedResultChanged");
            }
            catch (Exception ex) { Fail("ProtectedResultCheckFailed:" + ex.GetType().Name); }
            return string.IsNullOrEmpty(Failure);
        }

        public static void Fail(string failure)
        {
            if (string.IsNullOrEmpty(Failure)) Failure = failure;
        }

        public static bool MatchesMission(object mission) => IsActive && ReferenceEquals(_mission, mission);

        public static void Reset()
        {
            _configuration = null;
            _fixture = null;
            _mission = null;
            _opened = _initialized = _ended = false;
            _protectedResultHash = _protectedResultPath = null;
            Failure = OpenedShell = PhaseBeforeEnd = string.Empty;
            ResultAttempts = SuppressedResults = ResultEntriesAtAttempt = 0;
            InitialStateWasClean = false;
        }

        private static void RequireActive()
        {
            if (!IsActive || !string.IsNullOrEmpty(Failure))
                throw new InvalidOperationException("SpawnSmokeRosterNotAdmitted:" + Failure);
        }

        private static string ReadProtectedResultIdentity()
        {
            return File.Exists(_protectedResultPath)
                ? CoopAutomationRuntimeContract.ComputeFileSha256(_protectedResultPath)
                : "Absent";
        }
    }
}