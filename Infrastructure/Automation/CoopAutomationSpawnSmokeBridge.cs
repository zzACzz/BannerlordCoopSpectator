using System;
using System.Collections.Generic;
using System.IO;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.Automation
{
    // Default-off process/run binding. No engine objects are inspected in this shared bridge.
    public static class CoopAutomationSpawnSmokeBridge
    {
        public const string ProfileVariable = CoopAutomationRuntimeContract.SpawnSmokeProfileVariable;
        private static CoopAutomationRuntimeConfiguration _configuration;
        private static CoopAutomationSmokeFixture _fixture;
        private static object _mission;
        private static object _nativeModeInitializationMission;
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
                string folder = CoopAutomationRuntimeContract.ResolveCoopFolderPath(
                    true, profile, configuration, () => throw new InvalidOperationException("ProductionFolderForbidden"));
                if (!CoopAutomationRuntimeContract.TryResolveContainedPath(folder, "battle_result.json",
                    out string protectedPath, out failure)) return false;
                string expectedHash = CoopAutomationRuntimeContract.ComputeSha256Hex(
                    CoopAutomationRuntimeContract.LocalResultSentinelText(configuration.RunId));
                if (!File.Exists(protectedPath) ||
                    CoopAutomationRuntimeContract.ComputeFileSha256(protectedPath) != expectedHash)
                { failure = "LocalResultSentinelMissingOrChanged"; return false; }
                _protectedResultPath = protectedPath;
                _protectedResultHash = expectedHash;
                _configuration = configuration;
                _fixture = fixture;
                InitialStateWasClean = true;
                failure = string.Empty;
                return true;
            }
            catch (Exception ex) { failure = "LocalResultBaselineFailed:" + ex.GetType().Name; return false; }
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

        public static bool ClaimNativeModeInitialization(object mission, string scene, bool isDedicatedServer)
        {
            if (!IsRequested) return false;
            RequireActive();
            if (Environment.GetEnvironmentVariable(ProfileVariable) != CoopAutomationSpawnSmokeContract.Profile ||
                !isDedicatedServer || !_opened || OpenedShell != CoopAutomationSpawnSmokeContract.MissionShell ||
                _initialized || _ended || mission == null || scene != CoopAutomationSpawnSmokeContract.Scene ||
                _nativeModeInitializationMission != null)
            {
                Fail("SpawnSmokeNativeModeInitializationMismatch");
                throw new InvalidOperationException(Failure);
            }

            // Claim the factory's mission once; the later observer must bind the same instance.
            _nativeModeInitializationMission = mission;
            return true;
        }

        public static void ObserveInitialized(object mission)
        {
            if (!IsActive) return;
            if (!_opened || _initialized || mission == null || !string.IsNullOrEmpty(Failure) ||
                !ReferenceEquals(_nativeModeInitializationMission, mission))
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

        public static bool CanUseZeroClientPreBattle(object mission, string scene, bool isDedicatedServer,
            bool continuing, string mode, string phase, bool noConnectedClients, bool snapshotMatches)
        {
            if (!IsRequested || !IsActive || !_opened || !_initialized || _ended ||
                !string.IsNullOrEmpty(Failure) || mission == null ||
                Environment.GetEnvironmentVariable(ProfileVariable) != CoopAutomationSpawnSmokeContract.Profile ||
                !ReferenceEquals(_mission, mission) || !ReferenceEquals(_nativeModeInitializationMission, mission) ||
                OpenedShell != CoopAutomationSpawnSmokeContract.MissionShell ||
                scene != CoopAutomationSpawnSmokeContract.Scene || !isDedicatedServer || !continuing ||
                mode != "Battle" || !noConnectedClients || !snapshotMatches)
                return false;

            return phase == "Loading" || phase == "SideSelection" || phase == "UnitSelection" ||
                phase == "Deployment" || phase == "PreBattleHold";
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
            _nativeModeInitializationMission = null;
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
            if (!CoopAutomationRuntimeContract.TryResolveContainedPath(_configuration.RunRoot,
                CoopAutomationRuntimeContract.LocalResultRelativePath, out string currentPath, out string failure) ||
                !string.Equals(currentPath, _protectedResultPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(failure);
            return File.Exists(_protectedResultPath)
                ? CoopAutomationRuntimeContract.ComputeFileSha256(_protectedResultPath)
                : "Absent";
        }
    }
}
