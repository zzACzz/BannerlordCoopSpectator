using System;

namespace CoopSpectator.Infrastructure.Hideout
{
    public enum CoopHideoutAmbushPhase
    {
        WaitingForMaterialization = 0,
        Stealth = 1,
        CallTroops = 2,
        MainCampBattle = 3,
        BossBattle = 4,
        Completed = 5,
        Faulted = 6
    }

    public enum CoopHideoutAmbushClientCommandKind
    {
        UseCallTroopsPoint = 0,
        CinematicReady = 1
    }

    public sealed class CoopHideoutAmbushState
    {
        public string BattleInstanceId { get; set; } = string.Empty;

        public int Revision { get; set; }

        public CoopHideoutAmbushPhase Phase { get; set; }

        public int GuardAgentIndex { get; set; } = -1;

        public int ObservedAgentIndex { get; set; } = -1;

        public int SuspicionPermille { get; set; }

        public bool IsAlarmed { get; set; }

        public bool HasGlobalAlarm { get; set; }

        public bool IsAlarmFailureCounterActive { get; set; }

        public int AlarmFailureRemainingMilliseconds { get; set; }

        public bool IsUsePointAvailable { get; set; }

        public string Reason { get; set; } = string.Empty;

        public CoopHideoutAmbushState Clone()
        {
            return (CoopHideoutAmbushState)MemberwiseClone();
        }
    }

    public sealed class CoopHideoutAmbushAwarenessSnapshot
    {
        public int GuardAgentIndex { get; set; } = -1;

        public int ObservedAgentIndex { get; set; } = -1;

        public float Suspicion01 { get; set; }

        public bool IsAlarmed { get; set; }
    }

    public static class CoopHideoutAmbushContract
    {
        public const string ScenarioKind = "HideoutAmbush";
        public const string GameModeId = "CoopHideoutNight";
        public const string NativeControllerTypeName =
            "SandBox.Missions.MissionLogics.Hideout.HideoutAmbushMissionController";
        public const string StealthAreaUsePointTypeName =
            "SandBox.Objects.Usables.StealthAreaUsePoint";
        public const string ReinforcementSpawnPointTag =
            "reinforcement_ally_group_spawn_point_tag";
        public const string ReinforcementWaitPointTag = "wait_point_tag";
        public const string ForcedSentrySpawnGroupTag = "stealth_agent_forced";
        public const string OptionalSentrySpawnGroupTag = "stealth_agent";
        public const string TorchMirrorItemId = "cs_mirror_torch_ae349521";
        public const string CallTroopsCameraTag = "hideout_ambush_cutscene_camera";
        public const string CallTroopsArrowBarrelTag = "hideout_ambush_cutscene_arrow_barrel";
        public const string CallTroopsArrowPathTag = "hideout_ambush_cutscene_arrow_path";
        public const string CallTroopsRequestResponseReasonPrefix = "call-troops-response:";
        public const string AlarmFailureCompletionReason =
            "night-hideout-main-hero-compromised";
        public const string MainHeroDefeatCompletionReason =
            "night-hideout-main-hero-defeated";
        public const int ProtocolVersion = 2;
        public const int MaximumBattleInstanceIdCharacters = 96;
        public const int MaximumReasonCharacters = 128;
        public const int AlarmFailureSeconds = 15;
        public const int AlarmFailureMilliseconds = AlarmFailureSeconds * 1000;
        public const int CallTroopsTransitionSeconds = 10;
        public const float HostUsePointFallbackRadius = 2.75f;
        public const float CautiousAwarenessThreshold = 1f;
        public const float AlarmedAwarenessThreshold = 2f;
        public const float MinimumDetectionFrontDot = 0.15f;
        public const float CautiousSneakAttackBackDot = 0.174f;

        public static bool IsHideoutAmbushScenario(string scenarioKind)
        {
            return string.Equals(
                (scenarioKind ?? string.Empty).Trim(),
                ScenarioKind,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMatchingNightHideoutMissionContract(
            string requestedSceneName,
            string contractSceneName,
            string scenarioKind)
        {
            if (!CoopHideoutBossPhaseContract.TryNormalizeDayHideoutSceneName(
                    requestedSceneName,
                    out string normalizedRequestedScene) ||
                !CoopHideoutBossPhaseContract.TryNormalizeDayHideoutSceneName(
                    contractSceneName,
                    out string normalizedContractScene))
            {
                return false;
            }

            return string.Equals(
                       normalizedRequestedScene,
                       normalizedContractScene,
                       StringComparison.OrdinalIgnoreCase) &&
                   IsHideoutAmbushScenario(scenarioKind);
        }

        public static bool CanEnterNightHideoutCampaignBridge(
            bool hasDayController,
            bool hasAmbushController,
            bool hasSelectedRosterContract)
        {
            return !hasDayController &&
                   hasAmbushController &&
                   hasSelectedRosterContract;
        }

        public static bool ShouldCountHostedNightReinforcementSelectionAsReady(
            bool isHostedPeer,
            bool hasActiveControlledAgent,
            bool hasPendingSpawnRequest,
            bool pendingEntryIsReservedReinforcement)
        {
            return isHostedPeer &&
                   !hasActiveControlledAgent &&
                   hasPendingSpawnRequest &&
                   pendingEntryIsReservedReinforcement;
        }

        public static bool AreNightHideoutAssignedPeersReadyForBattleStart(
            int assignedPeerCount,
            int controlledPeerCount,
            bool hasHostedPendingReinforcementSelection)
        {
            if (assignedPeerCount <= 0)
                return false;

            if (controlledPeerCount >= assignedPeerCount)
                return true;

            return hasHostedPendingReinforcementSelection &&
                   controlledPeerCount > 0 &&
                   controlledPeerCount + 1 >= assignedPeerCount;
        }

        public static bool ShouldAllowDeferredHostStartHotkey(
            bool hasLocalControlledAgent,
            bool canStartBattle,
            bool snapshotHasAgent,
            bool isSpawnQueued)
        {
            return !hasLocalControlledAgent &&
                   canStartBattle &&
                   !snapshotHasAgent &&
                   isSpawnQueued;
        }

        public static bool ShouldUseMissingSpawnComponentFallback(
            bool isServer,
            bool hasIsolatedHideoutController,
            bool hasSpawnComponent)
        {
            return isServer &&
                   hasIsolatedHideoutController &&
                   !hasSpawnComponent;
        }

        public static bool ShouldKeepAlarmFailureCounterRunning(
            int activeAlarmedDefenderCount)
        {
            return activeAlarmedDefenderCount > 0;
        }

        public static bool ShouldRunMainHeroAlarmFailureCounter(
            bool isStealthPhase,
            bool mainHeroIsActive,
            bool hasAlarmedDefenderForMainHero)
        {
            return isStealthPhase &&
                   mainHeroIsActive &&
                   hasAlarmedDefenderForMainHero;
        }

        public static int ResolveAlarmFailureRemainingMilliseconds(
            float currentTime,
            float alarmStartedAt)
        {
            if (float.IsNaN(currentTime) ||
                float.IsInfinity(currentTime) ||
                float.IsNaN(alarmStartedAt) ||
                float.IsInfinity(alarmStartedAt) ||
                alarmStartedAt < 0f)
            {
                return 0;
            }

            double elapsedSeconds = Math.Max(0d, currentTime - alarmStartedAt);
            double remainingMilliseconds =
                AlarmFailureMilliseconds - elapsedSeconds * 1000d;
            return Math.Max(
                0,
                Math.Min(
                    AlarmFailureMilliseconds,
                    (int)Math.Ceiling(remainingMilliseconds)));
        }

        public static bool HasAlarmFailureCounterExpired(
            bool isCounterActive,
            int remainingMilliseconds)
        {
            return isCounterActive && remainingMilliseconds <= 0;
        }

        public static bool ShouldFailNightHideoutAfterMainHeroDefeated(
            CoopHideoutAmbushPhase phase,
            bool mainHeroIsDefeated,
            bool reinforcementsSpawned,
            int activePlayerAgentCount)
        {
            if (!mainHeroIsDefeated)
                return false;

            if (phase == CoopHideoutAmbushPhase.Stealth)
                return true;

            return reinforcementsSpawned &&
                   activePlayerAgentCount <= 0 &&
                   (phase == CoopHideoutAmbushPhase.CallTroops ||
                    phase == CoopHideoutAmbushPhase.MainCampBattle);
        }

        public static float AdvanceNightAwareness(
            float currentAwareness,
            float awarenessIncrease,
            bool isCautious)
        {
            if (float.IsNaN(currentAwareness) || float.IsInfinity(currentAwareness))
                currentAwareness = 0f;
            if (float.IsNaN(awarenessIncrease) || float.IsInfinity(awarenessIncrease))
                awarenessIncrease = 0f;

            float ceiling = isCautious
                ? AlarmedAwarenessThreshold
                : CautiousAwarenessThreshold;
            return Math.Max(
                0f,
                Math.Min(ceiling, currentAwareness + Math.Max(0f, awarenessIncrease)));
        }

        public static bool ShouldEnterNightCautiousState(
            bool isCautious,
            bool isAlarmed,
            float awareness)
        {
            return !isCautious &&
                   !isAlarmed &&
                   awareness >= CautiousAwarenessThreshold;
        }

        public static bool ShouldEnterNightAlarmedState(
            bool isCautious,
            bool isAlarmed,
            float awareness)
        {
            return isCautious &&
                   !isAlarmed &&
                   awareness >= AlarmedAwarenessThreshold;
        }

        public static bool ShouldAlarmNightDefenderAfterHit(
            bool defenderIsActive,
            float remainingHealth)
        {
            return defenderIsActive &&
                   !float.IsNaN(remainingHealth) &&
                   !float.IsInfinity(remainingHealth) &&
                   remainingHealth >= 1f;
        }

        public static float NormalizeNightAwarenessForUi(float awareness)
        {
            if (float.IsNaN(awareness) || float.IsInfinity(awareness))
                return 0f;

            return Math.Max(
                0f,
                Math.Min(1f, awareness / CautiousAwarenessThreshold));
        }

        public static bool IsInsideNightGuardVisionCone(float frontDot)
        {
            return !float.IsNaN(frontDot) &&
                   !float.IsInfinity(frontDot) &&
                   frontDot >= MinimumDetectionFrontDot;
        }

        public static bool CanDealNightSneakAttack(
            bool isEligibleWeapon,
            bool victimIsHuman,
            bool victimIsPlayer,
            bool victimCanGetAlarmed,
            int victimAlarmState,
            bool attackerExists,
            float attackerDirectionDotVictimForward)
        {
            if (!isEligibleWeapon || !victimIsHuman || victimIsPlayer)
                return false;

            int alarmState = victimAlarmState & 3;
            if (alarmState == 0 && victimCanGetAlarmed)
                return true;

            return alarmState != 3 &&
                   attackerExists &&
                   !float.IsNaN(attackerDirectionDotVictimForward) &&
                   !float.IsInfinity(attackerDirectionDotVictimForward) &&
                   attackerDirectionDotVictimForward < CautiousSneakAttackBackDot;
        }

        public static float ResolveCampaignSneakAttackMultiplier(
            int effectiveRoguery,
            bool isDaggerOrThrowingKnife)
        {
            float multiplier = 1.5f + Math.Max(0, effectiveRoguery) * 0.002f;
            if (isDaggerOrThrowingKnife)
                multiplier += 2f;
            return multiplier;
        }

        public static int ResolveBossIdentityPriority(params string[] identifiers)
        {
            int priority = 0;
            foreach (string identifier in identifiers ?? Array.Empty<string>())
            {
                if (ContainsIdentityToken(identifier, "boss"))
                    priority = Math.Max(priority, 300);
                else if (ContainsIdentityToken(identifier, "leader"))
                    priority = Math.Max(priority, 200);
                else if (ContainsIdentityToken(identifier, "chief"))
                    priority = Math.Max(priority, 100);
            }
            return priority;
        }

        public static bool ShouldDeferReservedBossVisualOverlayAssignment(
            string scenarioKind,
            string entryId,
            string reservedBossEntryId)
        {
            return IsHideoutAmbushScenario(scenarioKind) &&
                   !string.IsNullOrWhiteSpace(entryId) &&
                   !string.IsNullOrWhiteSpace(reservedBossEntryId) &&
                   string.Equals(
                       entryId,
                       reservedBossEntryId,
                       StringComparison.Ordinal);
        }

        public static string ResolveBossConversationDisplayName(
            string exactDisplayName,
            string nativeDisplayName)
        {
            return !string.IsNullOrWhiteSpace(exactDisplayName)
                ? exactDisplayName
                : nativeDisplayName;
        }

        public static bool ShouldReplaceExactDisplayNameCache(
            string cachedEntryId,
            string currentEntryId)
        {
            return !string.IsNullOrWhiteSpace(currentEntryId) &&
                   (string.IsNullOrWhiteSpace(cachedEntryId) ||
                    !string.Equals(cachedEntryId, currentEntryId, StringComparison.OrdinalIgnoreCase));
        }

        public static bool ShouldReleaseUsePointRequestPending(
            CoopHideoutAmbushPhase phase,
            string reason)
        {
            return phase != CoopHideoutAmbushPhase.Stealth ||
                   (reason ?? string.Empty).StartsWith(
                       CallTroopsRequestResponseReasonPrefix,
                       StringComparison.Ordinal);
        }

        public static int ResolveBossBodyguardCount(int initialHideoutPopulation)
        {
            if (initialHideoutPopulation <= 1)
                return 0;

            int half = initialHideoutPopulation / 2;
            return Math.Max(4, Math.Min(20, half));
        }

        public static int ResolveBossGroupCount(
            int initialHideoutPopulation,
            bool hasSeparateBossOrigin)
        {
            int bodyguardCount = ResolveBossBodyguardCount(initialHideoutPopulation);
            if (bodyguardCount <= 0 && !hasSeparateBossOrigin)
                return 0;

            return bodyguardCount + (hasSeparateBossOrigin ? 1 : 0);
        }

        public static bool IsValidNativeInitialEnemyContract(
            int initialHideoutPopulation,
            int liveInitialEnemyCount,
            int nativeSentryCount)
        {
            return initialHideoutPopulation > 0 &&
                   liveInitialEnemyCount > 0 &&
                   nativeSentryCount > 0 &&
                   nativeSentryCount <= liveInitialEnemyCount;
        }

        public static bool IsValidNightFirstPhaseParticipantCount(
            int totalTroopCount,
            int liveInitialEnemyCount)
        {
            return totalTroopCount > 0 && liveInitialEnemyCount > 0;
        }

        public static bool CanUseSyntheticInitialEnemyTroop(
            string candidateTroopId,
            string reservedBossTroopId)
        {
            return !string.IsNullOrWhiteSpace(candidateTroopId) &&
                   !string.IsNullOrWhiteSpace(reservedBossTroopId) &&
                   !string.Equals(
                       candidateTroopId.Trim(),
                       reservedBossTroopId.Trim(),
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool AreNightInitialParticipantOrdersReady(
            int attackerEntryOrderCount,
            int defenderEntryOrderCount)
        {
            return attackerEntryOrderCount > 0 && defenderEntryOrderCount > 0;
        }

        public static bool ShouldStartCallTroopsCinematic(
            CoopHideoutAmbushPhase phase,
            string incomingBattleInstanceId,
            string startedBattleInstanceId)
        {
            if (phase != CoopHideoutAmbushPhase.CallTroops ||
                string.IsNullOrWhiteSpace(incomingBattleInstanceId))
            {
                return false;
            }

            return !string.Equals(
                incomingBattleInstanceId.Trim(),
                (startedBattleInstanceId ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        public static int ResolveSyntheticInitialEnemyCount(
            int initialHideoutPopulation,
            int liveInitialEnemyCount,
            bool hasSeparateBossOrigin)
        {
            if (initialHideoutPopulation <= 0 || liveInitialEnemyCount <= 0)
                return 0;

            int originalInitialEnemyOrigins = Math.Max(
                0,
                initialHideoutPopulation - (hasSeparateBossOrigin ? 1 : 0));
            return Math.Max(0, liveInitialEnemyCount - originalInitialEnemyOrigins);
        }

        public static bool IsSentrySpawnGroup(string spawnGroupTag)
        {
            return string.Equals(
                       spawnGroupTag,
                       ForcedSentrySpawnGroupTag,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       spawnGroupTag,
                       OptionalSentrySpawnGroupTag,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsForcedSentrySpawnGroup(string spawnGroupTag)
        {
            return string.Equals(
                spawnGroupTag,
                ForcedSentrySpawnGroupTag,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOptionalSentrySpawnGroup(string spawnGroupTag)
        {
            return string.Equals(
                spawnGroupTag,
                OptionalSentrySpawnGroupTag,
                StringComparison.OrdinalIgnoreCase);
        }

        public static int ResolveOptionalSentryRouteCount(
            int initialHideoutPopulation,
            int availableOptionalRoutes)
        {
            if (initialHideoutPopulation <= 0 || availableOptionalRoutes <= 0)
                return 0;

            return Math.Min(availableOptionalRoutes, initialHideoutPopulation / 8);
        }

        public static int CompressSuspicion(float suspicion01)
        {
            if (float.IsNaN(suspicion01) || float.IsInfinity(suspicion01))
                return 0;
            return (int)Math.Round(Math.Max(0f, Math.Min(1f, suspicion01)) * 1000f);
        }

        private static bool ContainsIdentityToken(string value, string token)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrEmpty(token))
                return false;

            int searchFrom = 0;
            while (searchFrom < value.Length)
            {
                int index = value.IndexOf(
                    token,
                    searchFrom,
                    StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return false;

                int end = index + token.Length;
                bool leftBoundary = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
                bool rightBoundary = end == value.Length || !char.IsLetterOrDigit(value[end]);
                if (leftBoundary && rightBoundary)
                    return true;

                searchFrom = end;
            }
            return false;
        }

        public static bool IsMainHeroEntry(
            string originalCharacterId,
            string heroRole)
        {
            return string.Equals(
                       originalCharacterId,
                       "main_hero",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       heroRole,
                       "player",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasMainHeroUseAuthority(
            bool hasActiveControlledAgent,
            string originalCharacterId,
            string heroRole)
        {
            return hasActiveControlledAgent &&
                   IsMainHeroEntry(originalCharacterId, heroRole);
        }

        public static bool TryValidateMainHeroUseRequest(
            bool senderControlsMainHero,
            CoopHideoutAmbushPhase phase,
            int requestRevision,
            int currentRevision,
            out bool idempotent,
            out string rejection)
        {
            idempotent = false;
            rejection = string.Empty;
            if (!senderControlsMainHero)
            {
                rejection = "call-troops-sender-not-main-hero-controller";
                return false;
            }

            if (phase >= CoopHideoutAmbushPhase.CallTroops &&
                phase < CoopHideoutAmbushPhase.Faulted)
            {
                idempotent = true;
                return true;
            }

            if (phase != CoopHideoutAmbushPhase.Stealth)
            {
                rejection = "call-troops-phase-invalid:" + phase;
                return false;
            }

            if (requestRevision != currentRevision)
            {
                rejection = "call-troops-revision-stale";
                return false;
            }

            return true;
        }

        public static string Bound(string value, int maximumCharacters)
        {
            string normalized = value ?? string.Empty;
            if (maximumCharacters <= 0)
                return string.Empty;
            return normalized.Length <= maximumCharacters
                ? normalized
                : normalized.Substring(0, maximumCharacters);
        }
    }
}
