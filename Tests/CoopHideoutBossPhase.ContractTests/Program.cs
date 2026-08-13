using System;
using CoopSpectator.Infrastructure.Hideout;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateCampaignBridgePolicy();
            ValidateScenePolicy();
            ValidatePreOpenMissionContractPolicy();
            ValidateNightAmbushContractPolicy();
            ValidateNightDeferredHostBattleStartPolicy();
            ValidateNightAlarmFailureCounterPolicy();
            ValidateNightMainHeroDefeatPolicy();
            ValidateCallTroopsCinematicPolicy();
            ValidateCampaignBossAgentChoreographyPolicy();
            ValidateNightUseAuthorityPolicy();
            ValidateNightBossIdentityAndPlacementPolicy();
            ValidateMissionObjectivePolicy();
            ValidateNativeTimerStartupPolicy();
            ValidateCommanderIdentityFallbackPolicy();
            ValidateCommanderOrderInputPolicy();
            ValidateCommanderOrderAuthorityGuardPolicy();
            ValidateDeferredBossPossessionPolicy();
            ValidateSceneManifestParsing();
            ValidateNightSceneManifestParsing();
            ValidateTriggerPolicy();
            ValidateCooperativeMainHeroFallbackPolicy();
            ValidateMaterializationPolicy();
            ValidatePhaseTransitions();
            ValidateHostChoiceAuthority();
            ValidateFallbackTransitions();
            Console.WriteLine("Coop hideout boss phase contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void ValidateScenePolicy()
    {
        string[] supportedScenes =
        {
            "bandit_forest",
            "bandit_forest_sv",
            "desert_hideout_004_sv",
            "forest_hideout_003",
            "hideout_steppe_001",
            "mountain_hideout_004_sv",
            "sea_bandit_d_sv"
        };
        foreach (string scene in supportedScenes)
        {
            Assert(
                CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(scene),
                "Expected supported vanilla hideout scene: " + scene);
        }

        Assert(
            CoopHideoutBossPhaseContract.TryNormalizeDayHideoutSceneName(
                "  BANDIT_FOREST_SV  ",
                out string normalizedScene) &&
            normalizedScene == "BANDIT_FOREST_SV",
            "A supported hideout scene must be trimmed without changing its identifier casing.");
        Assert(
            CoopHideoutBossPhaseContract.DefenderGuardPatrolEntityTag == "sp_guard_patrol" &&
            CoopHideoutBossPhaseContract.DefenderDynamicPatrolAreaEntityTag == "dynamic_patrol_area_tag",
            "The dedicated hideout fallback must use the vanilla engine scene tags.");

        string[] rejectedScenes =
        {
            null,
            string.Empty,
            "battle_terrain_n",
            "mp_battle_map_001",
            "village_battania_a",
            "bandit_forest_variant"
        };
        foreach (string scene in rejectedScenes)
        {
            Assert(
                !CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(scene),
                "Expected non-hideout scene rejection: " + (scene ?? "null"));
        }
    }

    private static void ValidatePreOpenMissionContractPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "BANDIT_FOREST_SV",
                CoopHideoutBossPhaseContract.ScenarioKind),
            "A matching exact hideout scene and hideout scenario must be accepted.");
        Assert(
            !CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "bandit_forest_sv",
                null),
            "A pre-open contract with a missing scenario kind must be rejected.");
        Assert(
            !CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "battle_terrain_n",
                CoopHideoutBossPhaseContract.ScenarioKind),
            "A pre-open contract with a field battle scene must be rejected.");
        Assert(
            !CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "forest_hideout_003",
                CoopHideoutBossPhaseContract.ScenarioKind),
            "A different supported hideout scene must be rejected.");
        Assert(
            !CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "bandit_forest_sv",
                "FieldBattle"),
            "A non-hideout scenario kind must be rejected.");
    }

    private static void ValidateCampaignBridgePolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.CanEnterDayHideoutCampaignBridge(
                hasDayController: true,
                hasAmbushController: false,
                hasSelectedRosterContract: true),
            "A vanilla day assault must enter the isolated campaign bridge before scene entities finish loading.");
        Assert(
            !CoopHideoutBossPhaseContract.CanEnterDayHideoutCampaignBridge(
                hasDayController: false,
                hasAmbushController: false,
                hasSelectedRosterContract: true),
            "A mission without the vanilla day controller must remain unsupported.");
        Assert(
            !CoopHideoutBossPhaseContract.CanEnterDayHideoutCampaignBridge(
                hasDayController: true,
                hasAmbushController: true,
                hasSelectedRosterContract: true),
            "A night ambush must not enter the day-assault bridge.");
        Assert(
            !CoopHideoutBossPhaseContract.CanEnterDayHideoutCampaignBridge(
                hasDayController: true,
                hasAmbushController: false,
                hasSelectedRosterContract: false),
            "A day assault without the selected-roster contract must remain blocked.");
    }

        private static void ValidateNightAmbushContractPolicy()
    {
        Assert(
            CoopHideoutAmbushContract.IsMatchingNightHideoutMissionContract(
                "bandit_forest_sv",
                "BANDIT_FOREST_SV",
                CoopHideoutAmbushContract.ScenarioKind),
            "A matching nighttime hideout scene and ambush scenario must be accepted.");
        Assert(
            !CoopHideoutAmbushContract.IsMatchingNightHideoutMissionContract(
                "bandit_forest_sv",
                "bandit_forest_sv",
                CoopHideoutBossPhaseContract.ScenarioKind),
            "The nighttime mode must reject the daytime hideout scenario.");
        Assert(
            CoopHideoutAmbushContract.CanEnterNightHideoutCampaignBridge(
                hasDayController: false,
                hasAmbushController: true,
                hasSelectedRosterContract: true),
            "A native nighttime ambush with a selected roster must enter its isolated bridge.");
        Assert(
            !CoopHideoutAmbushContract.CanEnterNightHideoutCampaignBridge(
                hasDayController: true,
                hasAmbushController: true,
                hasSelectedRosterContract: true),
            "A mixed day/night native controller stack must fail closed.");
        Assert(
            !CoopHideoutAmbushContract.CanEnterNightHideoutCampaignBridge(
                hasDayController: false,
                hasAmbushController: true,
                hasSelectedRosterContract: false),
            "A nighttime ambush without the current native roster fields must fail closed.");
        Assert(
            CoopHideoutAmbushContract.ShouldUseMissingSpawnComponentFallback(
                isServer: true,
                hasIsolatedHideoutController: true,
                hasSpawnComponent: false) &&
            !CoopHideoutAmbushContract.ShouldUseMissingSpawnComponentFallback(
                isServer: false,
                hasIsolatedHideoutController: true,
                hasSpawnComponent: false) &&
            !CoopHideoutAmbushContract.ShouldUseMissingSpawnComponentFallback(
                isServer: true,
                hasIsolatedHideoutController: false,
                hasSpawnComponent: false) &&
            !CoopHideoutAmbushContract.ShouldUseMissingSpawnComponentFallback(
                isServer: true,
                hasIsolatedHideoutController: true,
                hasSpawnComponent: true),
            "Only an isolated server-side day or night hideout without SpawnComponent may use the zero-period fallback.");
        Assert(
            !CoopHideoutAmbushContract.ShouldKeepAlarmFailureCounterRunning(0) &&
            CoopHideoutAmbushContract.ShouldKeepAlarmFailureCounterRunning(1) &&
            CoopHideoutAmbushContract.ShouldKeepAlarmFailureCounterRunning(2),
            "The stealth failure counter must run only while at least one active alarmed defender remains.");

        Assert(
            CoopHideoutAmbushContract.ResolveBossBodyguardCount(29) == 14 &&
            CoopHideoutAmbushContract.ResolveBossGroupCount(
                29,
                hasSeparateBossOrigin: true) == 15 &&
            CoopHideoutAmbushContract.ResolveSyntheticInitialEnemyCount(
                29,
                liveInitialEnemyCount: 29,
                hasSeparateBossOrigin: true) == 1,
            "A twenty-nine-bandit ambush must create twenty-nine initial enemies and a separate boss plus fourteen bodyguards.");
        Assert(
            CoopHideoutAmbushContract.ResolveBossBodyguardCount(5) == 4 &&
            CoopHideoutAmbushContract.ResolveBossGroupCount(
                5,
                hasSeparateBossOrigin: true) == 5 &&
            CoopHideoutAmbushContract.ResolveSyntheticInitialEnemyCount(
                5,
                liveInitialEnemyCount: 26,
                hasSeparateBossOrigin: true) == 22,
            "A small ambush must synthesize authored initial enemies without consuming its separate boss origin.");
        Assert(
            CoopHideoutAmbushContract.IsValidNativeInitialEnemyContract(
                initialHideoutPopulation: 29,
                liveInitialEnemyCount: 29,
                nativeSentryCount: 8) &&
            !CoopHideoutAmbushContract.IsValidNativeInitialEnemyContract(
                initialHideoutPopulation: 29,
                liveInitialEnemyCount: 7,
                nativeSentryCount: 8),
            "The native sentry count must be treated as a subset of all live initial enemies.");
        Assert(
            CoopHideoutAmbushContract.IsValidNightFirstPhaseParticipantCount(
                totalTroopCount: 5,
                liveInitialEnemyCount: 26),
            "A nighttime hideout must allow native-generated initial enemies beyond the persistent roster.");
        Assert(
            CoopHideoutAmbushContract.CanUseSyntheticInitialEnemyTroop(
                "forest_bandits_chief",
                "forest_bandits_boss") &&
            !CoopHideoutAmbushContract.CanUseSyntheticInitialEnemyTroop(
                "forest_bandits_boss",
                "forest_bandits_boss") &&
            !CoopHideoutAmbushContract.CanUseSyntheticInitialEnemyTroop(
                "forest_bandits_chief",
                null),
            "A native-generated bandit chief may be reused, but the exact reserved boss and an unresolved boss identity must fail closed.");
        Assert(
            CoopHideoutAmbushContract.AreNightInitialParticipantOrdersReady(
                attackerEntryOrderCount: 28,
                defenderEntryOrderCount: 29) &&
            !CoopHideoutAmbushContract.AreNightInitialParticipantOrdersReady(
                attackerEntryOrderCount: 28,
                defenderEntryOrderCount: 0) &&
            !CoopHideoutAmbushContract.AreNightInitialParticipantOrdersReady(
                attackerEntryOrderCount: 0,
                defenderEntryOrderCount: 29),
            "A nighttime hideout may start only after both exact initial participant orders are available.");
        Assert(
            CoopHideoutAmbushContract.ResolveBossBodyguardCount(1) == 0 &&
            CoopHideoutAmbushContract.ResolveBossGroupCount(
                1,
                hasSeparateBossOrigin: true) == 1,
            "A one-bandit edge case must retain its separate boss without inventing bodyguard templates.");
        Assert(
            CoopHideoutAmbushContract.IsSentrySpawnGroup("stealth_agent") &&
            CoopHideoutAmbushContract.IsSentrySpawnGroup("STEALTH_AGENT_FORCED") &&
            !CoopHideoutAmbushContract.IsSentrySpawnGroup("bandit"),
            "Only the two native stealth spawn-group tags may classify sentries.");

        float cautiousAwareness = CoopHideoutAmbushContract.AdvanceNightAwareness(
            currentAwareness: 0.9f,
            awarenessIncrease: 5f,
            isCautious: false);
        float alarmedAwareness = CoopHideoutAmbushContract.AdvanceNightAwareness(
            currentAwareness: cautiousAwareness,
            awarenessIncrease: 5f,
            isCautious: true);
        Assert(
            Math.Abs(
                cautiousAwareness -
                CoopHideoutAmbushContract.CautiousAwarenessThreshold) < 0.001f &&
            CoopHideoutAmbushContract.ShouldEnterNightCautiousState(
                isCautious: false,
                isAlarmed: false,
                awareness: cautiousAwareness) &&
            !CoopHideoutAmbushContract.ShouldEnterNightAlarmedState(
                isCautious: false,
                isAlarmed: false,
                awareness: cautiousAwareness),
            "One awareness update may fill the indicator and enter cautious state, but it must not skip directly to alarmed state.");
        Assert(
            Math.Abs(
                alarmedAwareness -
                CoopHideoutAmbushContract.AlarmedAwarenessThreshold) < 0.001f &&
            CoopHideoutAmbushContract.ShouldEnterNightAlarmedState(
                isCautious: true,
                isAlarmed: false,
                awareness: alarmedAwareness),
            "Continued visual detection after the cautious threshold must be required to enter alarmed state.");
        Assert(
            CoopHideoutAmbushContract.ShouldAlarmNightDefenderAfterHit(
                defenderIsActive: true,
                remainingHealth: 1f) &&
            !CoopHideoutAmbushContract.ShouldAlarmNightDefenderAfterHit(
                defenderIsActive: true,
                remainingHealth: 0f) &&
            !CoopHideoutAmbushContract.ShouldAlarmNightDefenderAfterHit(
                defenderIsActive: false,
                remainingHealth: 100f) &&
            !CoopHideoutAmbushContract.ShouldAlarmNightDefenderAfterHit(
                defenderIsActive: true,
                remainingHealth: float.NaN),
            "Only an active nighttime defender that survived the hit may enter combat AI from OnScoreHit.");
        Assert(
            Math.Abs(CoopHideoutAmbushContract.NormalizeNightAwarenessForUi(0.5f) - 0.5f) < 0.001f &&
            Math.Abs(CoopHideoutAmbushContract.NormalizeNightAwarenessForUi(1f) - 1f) < 0.001f &&
            Math.Abs(CoopHideoutAmbushContract.NormalizeNightAwarenessForUi(2f) - 1f) < 0.001f,
            "The indicator must remain full while the defender is in the hidden second awareness stage.");
        Assert(
            CoopHideoutAmbushContract.IsInsideNightGuardVisionCone(0.2f) &&
            !CoopHideoutAmbushContract.IsInsideNightGuardVisionCone(-0.2f),
            "A nighttime guard must not detect a target behind its authored viewing direction.");

        Assert(
            CoopHideoutAmbushContract.CanDealNightSneakAttack(
                isEligibleWeapon: true,
                victimIsHuman: true,
                victimIsPlayer: false,
                victimCanGetAlarmed: true,
                victimAlarmState: 0,
                attackerExists: true,
                attackerDirectionDotVictimForward: 1f) &&
            CoopHideoutAmbushContract.CanDealNightSneakAttack(
                isEligibleWeapon: true,
                victimIsHuman: true,
                victimIsPlayer: false,
                victimCanGetAlarmed: true,
                victimAlarmState: 1,
                attackerExists: true,
                attackerDirectionDotVictimForward: -1f) &&
            !CoopHideoutAmbushContract.CanDealNightSneakAttack(
                isEligibleWeapon: true,
                victimIsHuman: true,
                victimIsPlayer: false,
                victimCanGetAlarmed: true,
                victimAlarmState: 1,
                attackerExists: true,
                attackerDirectionDotVictimForward: 1f) &&
            !CoopHideoutAmbushContract.CanDealNightSneakAttack(
                isEligibleWeapon: true,
                victimIsHuman: true,
                victimIsPlayer: false,
                victimCanGetAlarmed: true,
                victimAlarmState: 3,
                attackerExists: true,
                attackerDirectionDotVictimForward: -1f),
            "Nighttime sneak attacks must match campaign normal, cautious-behind, and alarmed victim rules.");
        Assert(
            Math.Abs(
                CoopHideoutAmbushContract.ResolveCampaignSneakAttackMultiplier(
                    effectiveRoguery: 100,
                    isDaggerOrThrowingKnife: false) - 1.7f) < 0.001f &&
            Math.Abs(
                CoopHideoutAmbushContract.ResolveCampaignSneakAttackMultiplier(
                    effectiveRoguery: 100,
                    isDaggerOrThrowingKnife: true) - 3.7f) < 0.001f,
            "Nighttime sneak-attack multipliers must match the Bannerlord 1.4.8 campaign formula.");
        }

        private static void ValidateNightDeferredHostBattleStartPolicy()
        {
            Assert(
                CoopHideoutAmbushContract.ShouldCountHostedNightReinforcementSelectionAsReady(
                    isHostedPeer: true,
                    hasActiveControlledAgent: false,
                    hasPendingSpawnRequest: true,
                    pendingEntryIsReservedReinforcement: true),
                "A hosted peer with an exact pending night reinforcement must be ready without an active agent.");
            Assert(
                !CoopHideoutAmbushContract.ShouldCountHostedNightReinforcementSelectionAsReady(
                    isHostedPeer: false,
                    hasActiveControlledAgent: false,
                    hasPendingSpawnRequest: true,
                    pendingEntryIsReservedReinforcement: true) &&
                !CoopHideoutAmbushContract.ShouldCountHostedNightReinforcementSelectionAsReady(
                    isHostedPeer: true,
                    hasActiveControlledAgent: false,
                    hasPendingSpawnRequest: false,
                    pendingEntryIsReservedReinforcement: true) &&
                !CoopHideoutAmbushContract.ShouldCountHostedNightReinforcementSelectionAsReady(
                    isHostedPeer: true,
                    hasActiveControlledAgent: false,
                    hasPendingSpawnRequest: true,
                    pendingEntryIsReservedReinforcement: false),
                "A remote peer, missing request, or non-reinforcement entry must not receive deferred host readiness.");
            Assert(
                CoopHideoutAmbushContract.AreNightHideoutAssignedPeersReadyForBattleStart(
                    assignedPeerCount: 2,
                    controlledPeerCount: 1,
                    hasHostedPendingReinforcementSelection: true),
                "One controlled infiltrator plus the hosted pending reinforcement must allow the two-player ambush to start.");
            Assert(
                CoopHideoutAmbushContract.AreNightHideoutAssignedPeersReadyForBattleStart(
                    assignedPeerCount: 2,
                    controlledPeerCount: 2,
                    hasHostedPendingReinforcementSelection: false),
                "The ordinary all-peers-controlled path must remain ready.");
            Assert(
                !CoopHideoutAmbushContract.AreNightHideoutAssignedPeersReadyForBattleStart(
                    assignedPeerCount: 1,
                    controlledPeerCount: 0,
                    hasHostedPendingReinforcementSelection: true) &&
                !CoopHideoutAmbushContract.AreNightHideoutAssignedPeersReadyForBattleStart(
                    assignedPeerCount: 3,
                    controlledPeerCount: 1,
                    hasHostedPendingReinforcementSelection: true) &&
                !CoopHideoutAmbushContract.AreNightHideoutAssignedPeersReadyForBattleStart(
                    assignedPeerCount: 2,
                    controlledPeerCount: 1,
                    hasHostedPendingReinforcementSelection: false),
                "Deferred host readiness must not start without an infiltrator or while another assigned peer is still unready.");
            Assert(
                CoopHideoutAmbushContract.ShouldAllowDeferredHostStartHotkey(
                    hasLocalControlledAgent: false,
                    canStartBattle: true,
                    snapshotHasAgent: false,
                    isSpawnQueued: true),
                "A server-authorized host must be able to press H while its exact reinforcement remains queued.");
            Assert(
                !CoopHideoutAmbushContract.ShouldAllowDeferredHostStartHotkey(
                    hasLocalControlledAgent: false,
                    canStartBattle: false,
                    snapshotHasAgent: false,
                    isSpawnQueued: true) &&
                !CoopHideoutAmbushContract.ShouldAllowDeferredHostStartHotkey(
                    hasLocalControlledAgent: false,
                    canStartBattle: true,
                    snapshotHasAgent: false,
                    isSpawnQueued: false) &&
                !CoopHideoutAmbushContract.ShouldAllowDeferredHostStartHotkey(
                    hasLocalControlledAgent: false,
                    canStartBattle: true,
                    snapshotHasAgent: true,
                    isSpawnQueued: true),
                "The no-agent H path must require server authority, queued lifecycle, and an absent snapshot agent.");
        }

        private static void ValidateNightAlarmFailureCounterPolicy()
        {
            Assert(
                CoopHideoutAmbushContract.ProtocolVersion == 2,
                "The timer fields require the second night-hideout network protocol version.");
            Assert(
                CoopHideoutAmbushContract.ShouldRunMainHeroAlarmFailureCounter(
                    isStealthPhase: true,
                    mainHeroIsActive: true,
                    hasAlarmedDefenderForMainHero: true) &&
                !CoopHideoutAmbushContract.ShouldRunMainHeroAlarmFailureCounter(
                    isStealthPhase: false,
                    mainHeroIsActive: true,
                    hasAlarmedDefenderForMainHero: true) &&
                !CoopHideoutAmbushContract.ShouldRunMainHeroAlarmFailureCounter(
                    isStealthPhase: true,
                    mainHeroIsActive: false,
                    hasAlarmedDefenderForMainHero: true) &&
                !CoopHideoutAmbushContract.ShouldRunMainHeroAlarmFailureCounter(
                    isStealthPhase: true,
                    mainHeroIsActive: true,
                    hasAlarmedDefenderForMainHero: false),
                "Only a live main hero compromised during stealth may arm the failure counter.");

            Assert(
                CoopHideoutAmbushContract.ResolveAlarmFailureRemainingMilliseconds(
                    currentTime: 10f,
                    alarmStartedAt: 10f) == 15000 &&
                CoopHideoutAmbushContract.ResolveAlarmFailureRemainingMilliseconds(
                    currentTime: 17.25f,
                    alarmStartedAt: 10f) == 7750 &&
                CoopHideoutAmbushContract.ResolveAlarmFailureRemainingMilliseconds(
                    currentTime: 25f,
                    alarmStartedAt: 10f) == 0 &&
                CoopHideoutAmbushContract.ResolveAlarmFailureRemainingMilliseconds(
                    currentTime: 9f,
                    alarmStartedAt: 10f) == 15000,
                "The authoritative counter must begin at fifteen seconds, decrease monotonically, and clamp at both boundaries.");
            Assert(
                CoopHideoutAmbushContract.ResolveAlarmFailureRemainingMilliseconds(
                    float.NaN,
                    alarmStartedAt: 10f) == 0 &&
                CoopHideoutAmbushContract.ResolveAlarmFailureRemainingMilliseconds(
                    currentTime: 10f,
                    float.PositiveInfinity) == 0 &&
                CoopHideoutAmbushContract.ResolveAlarmFailureRemainingMilliseconds(
                    currentTime: 10f,
                    alarmStartedAt: -1f) == 0,
                "Invalid counter timestamps must fail closed without creating an infinite or negative timer.");
            Assert(
                CoopHideoutAmbushContract.HasAlarmFailureCounterExpired(
                    isCounterActive: true,
                    remainingMilliseconds: 0) &&
                CoopHideoutAmbushContract.HasAlarmFailureCounterExpired(
                    isCounterActive: true,
                    remainingMilliseconds: -1) &&
                !CoopHideoutAmbushContract.HasAlarmFailureCounterExpired(
                    isCounterActive: true,
                    remainingMilliseconds: 1) &&
                !CoopHideoutAmbushContract.HasAlarmFailureCounterExpired(
                    isCounterActive: false,
                    remainingMilliseconds: 0),
                "Only an active counter at or below zero may fail the battle.");
            Assert(
                CoopHideoutAmbushContract.AlarmFailureCompletionReason ==
                    "night-hideout-main-hero-compromised",
                "The campaign bridge requires a stable main-hero-compromised completion reason.");
        }

        private static void ValidateNightMainHeroDefeatPolicy()
        {
            Assert(
                CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.Stealth,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: false,
                    activePlayerAgentCount: 1),
                "A defeated main hero must fail the night hideout before reinforcements arrive.");
            Assert(
                !CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.CallTroops,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: true,
                    activePlayerAgentCount: 1) &&
                !CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.MainCampBattle,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: true,
                    activePlayerAgentCount: 3),
                "Surviving reinforcements must continue after the main hero is defeated.");
            Assert(
                CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.CallTroops,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: true,
                    activePlayerAgentCount: 0) &&
                CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.MainCampBattle,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: true,
                    activePlayerAgentCount: 0),
                "After reinforcements arrive, defeat requires elimination of the whole player side.");
            Assert(
                !CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.Stealth,
                    mainHeroIsDefeated: false,
                    reinforcementsSpawned: false,
                    activePlayerAgentCount: 0) &&
                !CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.CallTroops,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: false,
                    activePlayerAgentCount: 0),
                "A live main hero or an unmaterialized reinforcement group must not use the post-signal loss rule.");
            Assert(
                !CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.WaitingForMaterialization,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: true,
                    activePlayerAgentCount: 0) &&
                !CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.BossBattle,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: true,
                    activePlayerAgentCount: 0) &&
                !CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.Completed,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: true,
                    activePlayerAgentCount: 0) &&
                !CoopHideoutAmbushContract.ShouldFailNightHideoutAfterMainHeroDefeated(
                    CoopHideoutAmbushPhase.Faulted,
                    mainHeroIsDefeated: true,
                    reinforcementsSpawned: true,
                    activePlayerAgentCount: 0),
                "Main-hero defeat must not override materialization, boss-fight, completed, or faulted phase rules.");
            Assert(
                CoopHideoutAmbushContract.MainHeroDefeatCompletionReason ==
                    "night-hideout-main-hero-defeated",
                "The campaign bridge requires a stable main-hero-defeated completion reason.");
        }

        private static void ValidateNightUseAuthorityPolicy()
        {
            Assert(
                CoopHideoutAmbushContract.ResolveOptionalSentryRouteCount(29, 11) == 3 &&
                CoopHideoutAmbushContract.ResolveOptionalSentryRouteCount(7, 11) == 0 &&
                CoopHideoutAmbushContract.ResolveOptionalSentryRouteCount(24, 2) == 2,
                "Optional night routes must follow the native one-per-eight population rule and available-route cap.");
            Assert(
                CoopHideoutAmbushContract.CompressSuspicion(-1f) == 0 &&
                CoopHideoutAmbushContract.CompressSuspicion(0.5f) == 500 &&
                CoopHideoutAmbushContract.CompressSuspicion(2f) == 1000,
                "Awareness compression must clamp the authoritative value to a stable permille contract.");

            Assert(
                CoopHideoutAmbushContract.IsMainHeroEntry("main_hero", null) &&
                CoopHideoutAmbushContract.IsMainHeroEntry(null, "player") &&
                !CoopHideoutAmbushContract.IsMainHeroEntry("companion_character", "companion") &&
                !CoopHideoutAmbushContract.IsMainHeroEntry(null, null),
                "Only the exact campaign main-hero entry may own night call-troops authority.");
            Assert(
                CoopHideoutAmbushContract.HasMainHeroUseAuthority(
                    hasActiveControlledAgent: true,
                    originalCharacterId: "main_hero",
                    heroRole: "player") &&
                !CoopHideoutAmbushContract.HasMainHeroUseAuthority(
                    hasActiveControlledAgent: false,
                    originalCharacterId: "main_hero",
                    heroRole: "player") &&
                !CoopHideoutAmbushContract.HasMainHeroUseAuthority(
                    hasActiveControlledAgent: true,
                    originalCharacterId: "companion_character",
                    heroRole: "companion") &&
                !CoopHideoutAmbushContract.HasMainHeroUseAuthority(
                    hasActiveControlledAgent: true,
                    originalCharacterId: null,
                    heroRole: null),
                "Main-hero use authority must fail closed for inactive, companion, and unresolved controlled agents.");
            Assert(
                CoopHideoutAmbushContract.TryValidateMainHeroUseRequest(
                    senderControlsMainHero: true,
                    CoopHideoutAmbushPhase.Stealth,
                    requestRevision: 4,
                    currentRevision: 4,
                    out bool idempotent,
                    out string rejection) &&
                !idempotent,
                "A current request from the peer controlling the main hero must be allowed to call troops: " + rejection);
            Assert(
                !CoopHideoutAmbushContract.TryValidateMainHeroUseRequest(
                    senderControlsMainHero: false,
                    CoopHideoutAmbushPhase.Stealth,
                    requestRevision: 4,
                    currentRevision: 4,
                    out _,
                    out rejection) &&
                rejection == "call-troops-sender-not-main-hero-controller",
                "A host or client controlling a companion must not trigger the main-hero-authoritative transition.");
            Assert(
                !CoopHideoutAmbushContract.TryValidateMainHeroUseRequest(
                    senderControlsMainHero: true,
                    CoopHideoutAmbushPhase.Stealth,
                    requestRevision: 3,
                    currentRevision: 4,
                    out _,
                    out rejection) &&
                rejection == "call-troops-revision-stale",
                "A stale use command must fail closed.");
            Assert(
                CoopHideoutAmbushContract.TryValidateMainHeroUseRequest(
                    senderControlsMainHero: true,
                    CoopHideoutAmbushPhase.CallTroops,
                    requestRevision: 3,
                    currentRevision: 4,
                    out idempotent,
                    out rejection) &&
                idempotent,
                "A duplicated command after the committed transition must be accepted idempotently.");
        }

    private static void ValidateNativeTimerStartupPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: true,
                CoopHideoutBossPhaseContract.NativeTimerStartAsServerSource),
            "The isolated hideout runtime must allow the native server timer initialization call.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: true,
                CoopHideoutBossPhaseContract.NativeTimerStartAsClientSource),
            "The isolated hideout runtime must allow the native client timer initialization call.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: false,
                CoopHideoutBossPhaseContract.NativeTimerStartAsServerSource),
            "A non-hideout runtime must retain native server timer suppression.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: true,
                "MultiplayerWarmupComponent.AfterStart"),
            "The hideout timer exception must not allow another native battle shell method.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: true,
                null),
            "A missing native battle shell source must remain suppressed.");
    }

    private static void ValidateNightBossIdentityAndPlacementPolicy()
    {
        const string bossEntryId =
            "defender|forest_bandits_1051|forest_bandits_boss|mp_light_ranged_battania_troop";
        int bossPriority = CoopHideoutAmbushContract.ResolveBossIdentityPriority(bossEntryId);
        int leaderPriority = CoopHideoutAmbushContract.ResolveBossIdentityPriority(
            "forest_bandits_leader");
        int chiefPriority = CoopHideoutAmbushContract.ResolveBossIdentityPriority(
            "forest_bandits_chief");
        Assert(
            bossPriority > leaderPriority &&
            leaderPriority > chiefPriority &&
            chiefPriority > 0,
            "A true boss identity must outrank leader and chief fallback identities.");
        Assert(
            CoopHideoutAmbushContract.ResolveBossIdentityPriority("bossman") == 0,
            "Boss identity matching must require a complete identifier token.");
        Assert(
            CoopHideoutAmbushContract.ShouldDeferReservedBossVisualOverlayAssignment(
                CoopHideoutAmbushContract.ScenarioKind,
                bossEntryId,
                bossEntryId),
            "The reserved night boss must not be consumed by the initial visual-overlay assignment queue.");
        Assert(
            !CoopHideoutAmbushContract.ShouldDeferReservedBossVisualOverlayAssignment(
                CoopHideoutAmbushContract.ScenarioKind,
                "defender|forest_bandits_1051|forest_bandits_bandit|mp_light_ranged_battania_troop",
                bossEntryId),
            "A normal initial bandit must remain eligible for visual-overlay queue assignment.");
        Assert(
            !CoopHideoutAmbushContract.ShouldDeferReservedBossVisualOverlayAssignment(
                CoopHideoutBossPhaseContract.ScenarioKind,
                bossEntryId,
                bossEntryId),
            "The reserved night-boss queue policy must not alter day hideout battles.");
        Assert(
            string.Equals(
                CoopHideoutAmbushContract.ResolveBossConversationDisplayName(
                    "Forest Bandit Boss",
                    "Ranger"),
                "Forest Bandit Boss",
                StringComparison.Ordinal),
            "The exact campaign boss name must override the native multiplayer fallback name.");
        Assert(
            string.Equals(
                CoopHideoutAmbushContract.ResolveBossConversationDisplayName(
                    null,
                    "Ranger"),
                "Ranger",
                StringComparison.Ordinal),
            "The native name must remain available until an authoritative exact name is known.");
        Assert(
            CoopHideoutAmbushContract.ShouldReplaceExactDisplayNameCache(
                "defender|forest_bandits_159|forest_bandits_bandit|mp_light_ranged_battania_troop",
                bossEntryId) &&
            !CoopHideoutAmbushContract.ShouldReplaceExactDisplayNameCache(
                bossEntryId,
                bossEntryId) &&
            !CoopHideoutAmbushContract.ShouldReplaceExactDisplayNameCache(
                bossEntryId,
                null),
            "A reused agent instance must replace a stale initial-bandit name only after a different authoritative entry is known.");

        CoopHideoutBossPrincipalPlacement placement =
            CoopHideoutBossPhaseContract.ResolvePrincipalPlacement(
                innerRadius: 1.5f,
                walkDistance: 3f);
        Assert(
            Math.Abs(placement.PlayerInitialForwardOffset - -4.5f) < 0.001f &&
            Math.Abs(placement.PlayerTargetForwardOffset - -1.5f) < 0.001f &&
            Math.Abs(placement.BossInitialForwardOffset - 4.5f) < 0.001f &&
            Math.Abs(placement.BossTargetForwardOffset - 1.5f) < 0.001f,
            "Player and boss approach frames must begin on opposite outer sides and end at the inner radius.");

        Assert(
            CoopHideoutBossPhaseContract.ResolveNativeCompanionSpineTroopCount(27) == 5,
            "The native triangular layout must allocate five spine rows for 27 companions.");
        CoopHideoutBossCompanionPlacement playerCenter =
            CoopHideoutBossPhaseContract.ResolveNativeCompanionPlacement(
                isPlayerSide: true,
                totalTroopCount: 27,
                zeroBasedIndex: 0);
        CoopHideoutBossCompanionPlacement playerLeft =
            CoopHideoutBossPhaseContract.ResolveNativeCompanionPlacement(
                isPlayerSide: true,
                totalTroopCount: 27,
                zeroBasedIndex: 1);
        CoopHideoutBossCompanionPlacement playerRight =
            CoopHideoutBossPhaseContract.ResolveNativeCompanionPlacement(
                isPlayerSide: true,
                totalTroopCount: 27,
                zeroBasedIndex: 2);
        Assert(
            playerCenter != null &&
            Math.Abs(playerCenter.InitialOffsetX) < 0.001f &&
            Math.Abs(playerCenter.InitialOffsetY - -1.3f) < 0.001f &&
            Math.Abs(playerCenter.TargetOffsetY - -1.8f) < 0.001f &&
            playerLeft != null &&
            Math.Abs(playerLeft.InitialOffsetX - -1f) < 0.001f &&
            playerRight != null &&
            Math.Abs(playerRight.InitialOffsetX - 1f) < 0.001f,
            "The first native player row must contain its center, left, and right slots with the authored half-meter target shift.");

        for (int i = 0; i < 27; i++)
        {
            CoopHideoutBossCompanionPlacement playerRow =
                CoopHideoutBossPhaseContract.ResolveNativeCompanionPlacement(
                    isPlayerSide: true,
                    totalTroopCount: 27,
                    zeroBasedIndex: i);
            CoopHideoutBossCompanionPlacement bossRow =
                CoopHideoutBossPhaseContract.ResolveNativeCompanionPlacement(
                    isPlayerSide: false,
                    totalTroopCount: 27,
                    zeroBasedIndex: i);
            Assert(
                playerRow != null &&
                bossRow != null &&
                playerRow.InitialOffsetY < 0f &&
                playerRow.TargetOffsetY < 0f &&
                bossRow.InitialOffsetY > 0f &&
                bossRow.TargetOffsetY > 0f,
                "Every generated day or night slot must remain on its own side of the encounter center.");
        }

        Assert(
            !CoopHideoutAmbushContract.ShouldReleaseUsePointRequestPending(
                CoopHideoutAmbushPhase.Stealth,
                "global-alarm-changed") &&
            CoopHideoutAmbushContract.ShouldReleaseUsePointRequestPending(
                CoopHideoutAmbushPhase.CallTroops,
                "phase:CallTroops") &&
            CoopHideoutAmbushContract.ShouldReleaseUsePointRequestPending(
                CoopHideoutAmbushPhase.Stealth,
                CoopHideoutAmbushContract.CallTroopsRequestResponseReasonPrefix +
                "call-troops-revision-stale"),
            "A pending use request must survive unrelated updates but clear on acceptance or an explicit response.");
    }

    private static void ValidateCallTroopsCinematicPolicy()
    {
        Assert(
            CoopHideoutAmbushContract.ShouldStartCallTroopsCinematic(
                CoopHideoutAmbushPhase.CallTroops,
                "battle-a",
                null),
            "The first call-troops state for a battle must start its cinematic.");
        Assert(
            !CoopHideoutAmbushContract.ShouldStartCallTroopsCinematic(
                CoopHideoutAmbushPhase.CallTroops,
                " battle-a ",
                "battle-a"),
            "A later state revision for the same battle must not restart its cinematic.");
        Assert(
            CoopHideoutAmbushContract.ShouldStartCallTroopsCinematic(
                CoopHideoutAmbushPhase.CallTroops,
                "battle-b",
                "battle-a"),
            "A different battle instance must be allowed to start its own cinematic.");
        Assert(
            !CoopHideoutAmbushContract.ShouldStartCallTroopsCinematic(
                CoopHideoutAmbushPhase.Stealth,
                "battle-a",
                null) &&
            !CoopHideoutAmbushContract.ShouldStartCallTroopsCinematic(
                CoopHideoutAmbushPhase.CallTroops,
                " ",
                null),
            "A non-call-troops phase or missing battle identity must not start the cinematic.");
    }

    private static void ValidateCampaignBossAgentChoreographyPolicy()
    {
        int authoredArrivalMilliseconds =
            CoopHideoutBossPhaseContract.ResolveCampaignBossApproachHoldMilliseconds(3f);
        Assert(
            authoredArrivalMilliseconds == 4616 &&
            authoredArrivalMilliseconds <
                CoopHideoutBossPhaseContract.CampaignBossCinematicDurationMilliseconds &&
            CoopHideoutBossPhaseContract.ResolveCampaignBossApproachHoldMilliseconds(0f) == 0,
            "Campaign hideout agents must enter their synchronized hold when the authored three-meter approach completes at the native cinematic speed.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldApplyAgentChoreographyMessage(
                "battle-a",
                "battle-a",
                lastAppliedSequence: 1,
                messageSequence: 2) &&
            !CoopHideoutBossPhaseContract.ShouldApplyAgentChoreographyMessage(
                "battle-a",
                "battle-b",
                lastAppliedSequence: 1,
                messageSequence: 2) &&
            !CoopHideoutBossPhaseContract.ShouldApplyAgentChoreographyMessage(
                "battle-a",
                "battle-a",
                lastAppliedSequence: 2,
                messageSequence: 2) &&
            !CoopHideoutBossPhaseContract.ShouldApplyAgentChoreographyMessage(
                "battle-a",
                "battle-a",
                lastAppliedSequence: 3,
                messageSequence: 2),
            "Client choreography must accept only a newer sequence for the active battle instance.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldReleaseAgentForBossChoice(
                CoopHideoutBossPhase.Duel,
                isBossAgent: true) &&
            !CoopHideoutBossPhaseContract.ShouldReleaseAgentForBossChoice(
                CoopHideoutBossPhase.Duel,
                isBossAgent: false) &&
            CoopHideoutBossPhaseContract.ShouldReleaseAgentForBossChoice(
                CoopHideoutBossPhase.AllBattle,
                isBossAgent: false) &&
            !CoopHideoutBossPhaseContract.ShouldReleaseAgentForBossChoice(
                CoopHideoutBossPhase.AwaitingHostChoice,
                isBossAgent: true),
            "A duel may release only the boss while a full battle releases every staged AI participant.");

        float authoredCampaignRadius = CoopHideoutBossPhaseContract.ResolveBossDialogueInnerRadius(
            authoredInnerRadius: 1.5f,
            isCampaignStagedPlacementActive: true);
        CoopHideoutBossPrincipalPlacement authoredCampaignPlacement =
            CoopHideoutBossPhaseContract.ResolvePrincipalPlacement(
                authoredCampaignRadius,
                walkDistance: 3f);
        Assert(
            Math.Abs(authoredCampaignRadius - 1.5f) < 0.001f &&
            Math.Abs(authoredCampaignPlacement.PlayerInitialForwardOffset - -4.5f) < 0.001f &&
            Math.Abs(authoredCampaignPlacement.PlayerTargetForwardOffset - -1.5f) < 0.001f &&
            Math.Abs(authoredCampaignPlacement.BossInitialForwardOffset - 4.5f) < 0.001f,
            "Both day and night campaign boss staging must preserve the authored radius, initial frames, and player placement.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveCinematicDurationMilliseconds(
                isCampaignStagedPlacementActive: true) == 6000 &&
            CoopHideoutBossPhaseContract.ResolveCinematicDurationMilliseconds(
                isCampaignStagedPlacementActive: false) == 8000,
            "Both day and night campaign boss cinematics must use the validated six-second duration while a non-campaign fallback retains its legacy duration.");
        Assert(
            Math.Abs(CoopHideoutBossPhaseContract.ResolveCampaignBossApproachDistance(3f) - 3f) < 0.001f &&
            Math.Abs(CoopHideoutBossPhaseContract.ResolveCampaignBossApproachDistance(0.4f) - 0.4f) < 0.001f,
            "Both day and night campaign boss approaches must preserve the authored walk distance.");
        CoopHideoutBossPlanarOffset rotatedEnemyApproach =
            CoopHideoutBossPhaseContract.ResolveCampaignBossApproachOffset(
                directionX: -3f,
                directionY: 4f,
                authoredWalkDistance: 3f);
        CoopHideoutBossPlanarOffset forwardFallbackApproach =
            CoopHideoutBossPhaseContract.ResolveCampaignBossApproachOffset(
                directionX: 0f,
                directionY: 0f,
                authoredWalkDistance: 3f);
        Assert(
            Math.Abs(rotatedEnemyApproach.X - -1.8f) < 0.001f &&
            Math.Abs(rotatedEnemyApproach.Y - 2.4f) < 0.001f &&
            Math.Abs(
                Math.Sqrt(
                    rotatedEnemyApproach.X * rotatedEnemyApproach.X +
                    rotatedEnemyApproach.Y * rotatedEnemyApproach.Y) - 3f) < 0.001f &&
            Math.Abs(forwardFallbackApproach.X) < 0.001f &&
            Math.Abs(forwardFallbackApproach.Y - 3f) < 0.001f,
            "Every campaign boss target offset must follow its normalized facing direction for the full authored distance.");
        Assert(
            Math.Abs(CoopHideoutBossPhaseContract.ResolveBossDialogueInnerRadius(
                authoredInnerRadius: -1f,
                isCampaignStagedPlacementActive: true)) < 0.001f &&
            Math.Abs(CoopHideoutBossPhaseContract.ResolveBossDialogueInnerRadius(
                authoredInnerRadius: 1.5f,
                isCampaignStagedPlacementActive: false) - 1.5f) < 0.001f,
            "Boss dialogue placement must preserve non-negative authored radii without a scenario-only distance floor.");

        CoopHideoutBossPrincipalPerturbation playerPerturbation =
            CoopHideoutBossPhaseContract.ResolveNativePrincipalPerturbation(
                seedOffset: 0,
                perturbAmount:
                    CoopHideoutBossPhaseContract.NativePrincipalPlacementPerturbation);
        CoopHideoutBossPrincipalPerturbation repeatedPlayerPerturbation =
            CoopHideoutBossPhaseContract.ResolveNativePrincipalPerturbation(
                seedOffset: 0,
                perturbAmount:
                    CoopHideoutBossPhaseContract.NativePrincipalPlacementPerturbation);
        CoopHideoutBossPrincipalPerturbation bossPerturbation =
            CoopHideoutBossPhaseContract.ResolveNativePrincipalPerturbation(
                seedOffset: 1,
                perturbAmount:
                    CoopHideoutBossPhaseContract.NativePrincipalPlacementPerturbation);
        Assert(
            Math.Abs(playerPerturbation.SideOffset - repeatedPlayerPerturbation.SideOffset) < 0.0001f &&
            Math.Abs(playerPerturbation.ForwardOffset - repeatedPlayerPerturbation.ForwardOffset) < 0.0001f &&
            Math.Abs(
                playerPerturbation.SideOffset * playerPerturbation.SideOffset +
                playerPerturbation.ForwardOffset * playerPerturbation.ForwardOffset -
                0.0625f) < 0.0001f &&
            Math.Abs(
                bossPerturbation.SideOffset * bossPerturbation.SideOffset +
                bossPerturbation.ForwardOffset * bossPerturbation.ForwardOffset -
                0.0625f) < 0.0001f &&
            (Math.Abs(playerPerturbation.SideOffset - bossPerturbation.SideOffset) > 0.0001f ||
             Math.Abs(playerPerturbation.ForwardOffset - bossPerturbation.ForwardOffset) > 0.0001f),
            "Campaign principal perturbation must be deterministic, use the native quarter-meter radius, and differ by seed offset.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldStopFormationsForCampaignBossCinematic(
                isCampaignStagedPlacementActive: true) &&
            !CoopHideoutBossPhaseContract.ShouldStopFormationsForCampaignBossCinematic(
                isCampaignStagedPlacementActive: false),
            "Both day and night staged campaign boss cinematics must stop the existing formation movement orders.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldLockFormationAiForCampaignBossCinematic(
                isCampaignStagedPlacementActive: true) &&
            !CoopHideoutBossPhaseContract.ShouldLockFormationAiForCampaignBossCinematic(
                isCampaignStagedPlacementActive: false),
            "Both day and night staged campaign boss choreography must suspend formation AI while its stop order remains authoritative.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldRestoreFormationAiForBossPhase(
                CoopHideoutBossPhase.AllBattle) &&
            !CoopHideoutBossPhaseContract.ShouldRestoreFormationAiForBossPhase(
                CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldRestoreFormationAiForBossPhase(
                CoopHideoutBossPhase.Duel),
            "Formation AI control must return for the all-battle branch but remain suspended during the conversation and duel isolation.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldAttachUnformedBossFightAgentForAllBattle(
                isCampaignStagedPlacementActive: true,
                targetPhase: CoopHideoutBossPhase.AllBattle,
                isAgentActive: true,
                isAiControlled: true,
                isBossSideParticipant: true,
                hasFormation: false),
            "An active unformed day or night boss-side AI participant must join a combat formation before the all-battle charge order.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAttachUnformedBossFightAgentForAllBattle(
                isCampaignStagedPlacementActive: false,
                targetPhase: CoopHideoutBossPhase.AllBattle,
                isAgentActive: true,
                isAiControlled: true,
                isBossSideParticipant: true,
                hasFormation: false) &&
            !CoopHideoutBossPhaseContract.ShouldAttachUnformedBossFightAgentForAllBattle(
                isCampaignStagedPlacementActive: true,
                targetPhase: CoopHideoutBossPhase.Duel,
                isAgentActive: true,
                isAiControlled: true,
                isBossSideParticipant: true,
                hasFormation: false) &&
            !CoopHideoutBossPhaseContract.ShouldAttachUnformedBossFightAgentForAllBattle(
                isCampaignStagedPlacementActive: true,
                targetPhase: CoopHideoutBossPhase.AllBattle,
                isAgentActive: false,
                isAiControlled: true,
                isBossSideParticipant: true,
                hasFormation: false) &&
            !CoopHideoutBossPhaseContract.ShouldAttachUnformedBossFightAgentForAllBattle(
                isCampaignStagedPlacementActive: true,
                targetPhase: CoopHideoutBossPhase.AllBattle,
                isAgentActive: true,
                isAiControlled: false,
                isBossSideParticipant: true,
                hasFormation: false) &&
            !CoopHideoutBossPhaseContract.ShouldAttachUnformedBossFightAgentForAllBattle(
                isCampaignStagedPlacementActive: true,
                targetPhase: CoopHideoutBossPhase.AllBattle,
                isAgentActive: true,
                isAiControlled: true,
                isBossSideParticipant: false,
                hasFormation: false) &&
            !CoopHideoutBossPhaseContract.ShouldAttachUnformedBossFightAgentForAllBattle(
                isCampaignStagedPlacementActive: true,
                targetPhase: CoopHideoutBossPhase.AllBattle,
                isAgentActive: true,
                isAiControlled: true,
                isBossSideParticipant: true,
                hasFormation: true),
            "Formation attachment must not alter fallbacks, duels, inactive agents, players, the opposing side, or agents already assigned to a formation.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldDetachAgentForCampaignBossCinematic(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true) &&
            !CoopHideoutBossPhaseContract.ShouldDetachAgentForCampaignBossCinematic(
                isCampaignStagedPlacementActive: true,
                isAiControlled: false) &&
            !CoopHideoutBossPhaseContract.ShouldDetachAgentForCampaignBossCinematic(
                isCampaignStagedPlacementActive: false,
                isAiControlled: true),
            "Campaign choreography must detach AI participants without touching player-controlled or non-campaign agents.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldPauseAiForCampaignBossChoreography(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true,
                kind: CoopHideoutBossAgentChoreographyKind.HoldAtTarget) &&
            !CoopHideoutBossPhaseContract.ShouldPauseAiForCampaignBossChoreography(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true,
                kind: CoopHideoutBossAgentChoreographyKind.StartApproach) &&
            !CoopHideoutBossPhaseContract.ShouldPauseAiForCampaignBossChoreography(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true,
                kind: CoopHideoutBossAgentChoreographyKind.Release) &&
            !CoopHideoutBossPhaseContract.ShouldPauseAiForCampaignBossChoreography(
                isCampaignStagedPlacementActive: false,
                isAiControlled: true,
                kind: CoopHideoutBossAgentChoreographyKind.HoldAtTarget) &&
            !CoopHideoutBossPhaseContract.ShouldPauseAiForCampaignBossChoreography(
                isCampaignStagedPlacementActive: true,
                isAiControlled: false,
                kind: CoopHideoutBossAgentChoreographyKind.HoldAtTarget),
            "Only staged campaign AI participants must be paused after reaching the synchronized hold target.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldDetachNativeControllerForCampaignBossHold(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true,
                hasMissionPeer: false,
                isHostAgent: false) &&
            !CoopHideoutBossPhaseContract.ShouldDetachNativeControllerForCampaignBossHold(
                isCampaignStagedPlacementActive: false,
                isAiControlled: true,
                hasMissionPeer: false,
                isHostAgent: false) &&
            !CoopHideoutBossPhaseContract.ShouldDetachNativeControllerForCampaignBossHold(
                isCampaignStagedPlacementActive: true,
                isAiControlled: false,
                hasMissionPeer: false,
                isHostAgent: false) &&
            !CoopHideoutBossPhaseContract.ShouldDetachNativeControllerForCampaignBossHold(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true,
                hasMissionPeer: true,
                isHostAgent: false) &&
            !CoopHideoutBossPhaseContract.ShouldDetachNativeControllerForCampaignBossHold(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true,
                hasMissionPeer: false,
                isHostAgent: true),
            "Native AI controller detachment must target only unowned campaign-hideout bots and must never touch a peer-owned or host agent.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldRestoreDetachedNativeControllerForChoreography(
                wasNativeControllerDetached: true,
                kind: CoopHideoutBossAgentChoreographyKind.Release) &&
            !CoopHideoutBossPhaseContract.ShouldRestoreDetachedNativeControllerForChoreography(
                wasNativeControllerDetached: true,
                kind: CoopHideoutBossAgentChoreographyKind.HoldAtTarget) &&
            !CoopHideoutBossPhaseContract.ShouldRestoreDetachedNativeControllerForChoreography(
                wasNativeControllerDetached: true,
                kind: CoopHideoutBossAgentChoreographyKind.StartApproach) &&
            !CoopHideoutBossPhaseContract.ShouldRestoreDetachedNativeControllerForChoreography(
                wasNativeControllerDetached: false,
                kind: CoopHideoutBossAgentChoreographyKind.Release),
            "Only an explicit release may restore a native AI controller that this choreography detached.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                isCampaignStagedPlacementActive: true,
                phase: CoopHideoutBossPhase.PreparingCinematic,
                isAiControlled: true,
                isBossFightParticipant: true) &&
            CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                isCampaignStagedPlacementActive: true,
                phase: CoopHideoutBossPhase.Cinematic,
                isAiControlled: true,
                isBossFightParticipant: true),
            "Day and night AI participants must remain detached only while the cinematic movement is active.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                isCampaignStagedPlacementActive: false,
                phase: CoopHideoutBossPhase.Cinematic,
                isAiControlled: true,
                isBossFightParticipant: true) &&
            !CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                isCampaignStagedPlacementActive: true,
                phase: CoopHideoutBossPhase.Cinematic,
                isAiControlled: false,
                isBossFightParticipant: true) &&
            !CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                isCampaignStagedPlacementActive: true,
                phase: CoopHideoutBossPhase.Cinematic,
                isAiControlled: true,
                isBossFightParticipant: false) &&
            !CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                isCampaignStagedPlacementActive: true,
                phase: CoopHideoutBossPhase.AwaitingHostChoice,
                isAiControlled: true,
                isBossFightParticipant: true) &&
            !CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                isCampaignStagedPlacementActive: true,
                phase: CoopHideoutBossPhase.Duel,
                isAiControlled: true,
                isBossFightParticipant: true) &&
            !CoopHideoutBossPhaseContract.ShouldPreserveCampaignBossFormationDetachment(
                isCampaignStagedPlacementActive: true,
                phase: CoopHideoutBossPhase.AllBattle,
                isAiControlled: true,
                isBossFightParticipant: true),
            "Campaign formation detachment must end when the authored movement is finalized, before the host conversation.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldFallbackFromAwaitingHostChoice(
                isHostAvailable: true) &&
            CoopHideoutBossPhaseContract.ShouldFallbackFromAwaitingHostChoice(
                isHostAvailable: false),
            "The campaign-style host conversation must wait indefinitely and fall back only when the host is unavailable.");
        Assert(
            Math.Abs(
                CoopHideoutBossPhaseContract.NativeCompanionApproachDistance - 0.5f) < 0.001f,
            "Day and night companions must use the native half-meter approach instead of the removed one-meter clamp.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldMaintainLocalHostFacingBoss(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.Cinematic) &&
            CoopHideoutBossPhaseContract.ShouldMaintainLocalHostFacingBoss(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldMaintainLocalHostFacingBoss(
                isLocalHost: false,
                phase: CoopHideoutBossPhase.Cinematic) &&
            !CoopHideoutBossPhaseContract.ShouldMaintainLocalHostFacingBoss(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.Duel),
            "Only the local host visual must face the boss during the cinematic and host choice.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldClearBossConversationLookDirection(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.Duel) &&
            CoopHideoutBossPhaseContract.ShouldClearBossConversationLookDirection(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.AllBattle) &&
            !CoopHideoutBossPhaseContract.ShouldClearBossConversationLookDirection(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldClearBossConversationLookDirection(
                isLocalHost: false,
                phase: CoopHideoutBossPhase.Duel),
            "The local host must clear the forced conversation look direction only when leaving the choice phase.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldAlignLocalHostCombatCameraWithBoss(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.Duel) &&
            CoopHideoutBossPhaseContract.ShouldAlignLocalHostCombatCameraWithBoss(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.AllBattle) &&
            !CoopHideoutBossPhaseContract.ShouldAlignLocalHostCombatCameraWithBoss(
                isLocalHost: false,
                phase: CoopHideoutBossPhase.Duel) &&
            !CoopHideoutBossPhaseContract.ShouldAlignLocalHostCombatCameraWithBoss(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.AwaitingHostChoice),
            "Only the local host combat camera must align with the boss after either fight choice.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldPrimeBossPreferredTargetForDuel(
                CoopHideoutBossPhase.Duel,
                isBossAgent: true,
                isAiControlled: true,
                hostAgentActive: true,
                bossAgentActive: true) &&
            !CoopHideoutBossPhaseContract.ShouldPrimeBossPreferredTargetForDuel(
                CoopHideoutBossPhase.AllBattle,
                isBossAgent: true,
                isAiControlled: true,
                hostAgentActive: true,
                bossAgentActive: true) &&
            !CoopHideoutBossPhaseContract.ShouldPrimeBossPreferredTargetForDuel(
                CoopHideoutBossPhase.Duel,
                isBossAgent: false,
                isAiControlled: true,
                hostAgentActive: true,
                bossAgentActive: true) &&
            !CoopHideoutBossPhaseContract.ShouldPrimeBossPreferredTargetForDuel(
                CoopHideoutBossPhase.Duel,
                isBossAgent: true,
                isAiControlled: true,
                hostAgentActive: false,
                bossAgentActive: true),
            "Only the active AI boss may receive the active host as its preferred duel target.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldClearBossPreferredTarget(
                CoopHideoutBossPhase.AllBattle) &&
            CoopHideoutBossPhaseContract.ShouldClearBossPreferredTarget(
                CoopHideoutBossPhase.Completed) &&
            !CoopHideoutBossPhaseContract.ShouldClearBossPreferredTarget(
                CoopHideoutBossPhase.Duel),
            "The preferred duel target must be cleared for the full battle and completed phases.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldShowBossConversation(
                CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldShowBossConversation(
                CoopHideoutBossPhase.Cinematic) &&
            !CoopHideoutBossPhaseContract.ShouldShowBossConversation(
                CoopHideoutBossPhase.Duel),
            "Every client must show the synchronized boss conversation only while the host choice is pending.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldEnableBossConversationChoices(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldEnableBossConversationChoices(
                isLocalHost: false,
                phase: CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldEnableBossConversationChoices(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.Duel),
            "Only the local campaign host may submit a choice from the synchronized boss conversation.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldReleaseCinematicCameraForBossConversation(
                CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldReleaseCinematicCameraForBossConversation(
                CoopHideoutBossPhase.Cinematic),
            "The moving cinematic camera must be released before the synchronized campaign conversation opens.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldUseObserverCameraForBossConversation(
                isLocalHost: false,
                phase: CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldUseObserverCameraForBossConversation(
                isLocalHost: true,
                phase: CoopHideoutBossPhase.AwaitingHostChoice) &&
            !CoopHideoutBossPhaseContract.ShouldUseObserverCameraForBossConversation(
                isLocalHost: false,
                phase: CoopHideoutBossPhase.Duel),
            "Only non-host clients must use the vanilla-style two-agent observer camera while the host choice is pending.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldCorrectCampaignBossCinematicTarget(
                isCampaignStagedPlacementActive: true,
                distanceSquared: 0.25f) &&
            CoopHideoutBossPhaseContract.ShouldCorrectCampaignBossCinematicTarget(
                isCampaignStagedPlacementActive: true,
                distanceSquared: 0.251f) &&
            !CoopHideoutBossPhaseContract.ShouldCorrectCampaignBossCinematicTarget(
                isCampaignStagedPlacementActive: false,
                distanceSquared: 100f),
            "The campaign-style final snap may run once only beyond the half-meter target threshold in a staged day or night cinematic.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldReactivateAgentAfterCampaignBossChoice(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true,
                isBossFightParticipant: true) &&
            !CoopHideoutBossPhaseContract.ShouldReactivateAgentAfterCampaignBossChoice(
                isCampaignStagedPlacementActive: true,
                isAiControlled: false,
                isBossFightParticipant: true) &&
            !CoopHideoutBossPhaseContract.ShouldReactivateAgentAfterCampaignBossChoice(
                isCampaignStagedPlacementActive: true,
                isAiControlled: true,
                isBossFightParticipant: false) &&
            !CoopHideoutBossPhaseContract.ShouldReactivateAgentAfterCampaignBossChoice(
                isCampaignStagedPlacementActive: false,
                isAiControlled: true,
                isBossFightParticipant: true),
            "Forced AI behavior selection must remain isolated to staged day or night boss-fight AI participants.");
    }

    private static void ValidateCommanderIdentityFallbackPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.ShouldAllowCommanderIdentityFallback(
                botAliveCount: 0,
                botTotalCount: 0,
                isValidatedDayHideoutScenario: true),
            "A validated day hideout may restore commander identity before bot counters arrive.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldAllowCommanderIdentityFallback(
                botAliveCount: 0,
                botTotalCount: 1,
                isValidatedDayHideoutScenario: true),
            "The local commander agent must not be mistaken for a controlled bot.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowCommanderIdentityFallback(
                botAliveCount: 1,
                botTotalCount: 0,
                isValidatedDayHideoutScenario: true),
            "A live controlled bot must use the normal synchronized commander path.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowCommanderIdentityFallback(
                botAliveCount: 0,
                botTotalCount: 2,
                isValidatedDayHideoutScenario: true),
            "Multiple assigned agents must use the normal synchronized commander path.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowCommanderIdentityFallback(
                botAliveCount: 0,
                botTotalCount: 0,
                isValidatedDayHideoutScenario: false),
            "The identity fallback must remain isolated from non-hideout battles.");
    }

    private static void ValidateCommanderOrderInputPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.ShouldUseSingleNativeCommanderOrderInput(
                isExactLandBattleScenario: true,
                isValidatedDayHideoutScenario: false),
            "An exact land battle must retain its established single native order-input path.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldUseSingleNativeCommanderOrderInput(
                isExactLandBattleScenario: false,
                isValidatedDayHideoutScenario: true),
            "A validated day hideout must use one native order-input path instead of a second order view model.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldUseSingleNativeCommanderOrderInput(
                isExactLandBattleScenario: false,
                isValidatedDayHideoutScenario: false),
            "An unrelated commander scenario must preserve its existing dedicated order-input path.");
    }

        private static void ValidateCommanderOrderAuthorityGuardPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.ShouldApplyCommanderOrderAuthorityGuards(
                isExactCampaignBattleScene: true,
                isValidatedHideoutScenario: false),
            "Exact campaign battles must retain commander order-authority guards.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldApplyCommanderOrderAuthorityGuards(
                isExactCampaignBattleScene: false,
                isValidatedHideoutScenario: true),
            "Validated day and night hideouts must suppress non-commander order controls.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldApplyCommanderOrderAuthorityGuards(
                isExactCampaignBattleScene: false,
                isValidatedHideoutScenario: false),
            "Unrelated multiplayer scenes must not inherit campaign commander guards.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldSuppressCommanderOrderControls(
                authorityGuardsApply: true,
                isExactCommander: false,
                hasDelegatedOrderAuthority: false),
            "A regular companion in a validated hideout must not retain order flags.");
            Assert(
                !CoopHideoutBossPhaseContract.ShouldSuppressCommanderOrderControls(
                authorityGuardsApply: true,
                isExactCommander: true,
                hasDelegatedOrderAuthority: false),
            "The exact campaign commander must retain order controls.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldSuppressCommanderOrderControls(
                authorityGuardsApply: true,
                isExactCommander: false,
                hasDelegatedOrderAuthority: true),
            "An explicitly delegated captain must retain authorized order controls.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldSuppressCommanderOrderControls(
                authorityGuardsApply: false,
                isExactCommander: false,
                hasDelegatedOrderAuthority: false),
                "Unrelated multiplayer missions must preserve their native order controls.");

            Assert(
                CoopHideoutBossPhaseContract.ShouldBypassSpawnHandshakeSelectAllSuppression(
                    isExactCommander: true,
                    hasAuthorizedFormations: true),
                "The exact commander must be able to select all formations even while synchronized bot counters remain zero.");
            Assert(
                !CoopHideoutBossPhaseContract.ShouldBypassSpawnHandshakeSelectAllSuppression(
                    isExactCommander: true,
                    hasAuthorizedFormations: false) &&
                !CoopHideoutBossPhaseContract.ShouldBypassSpawnHandshakeSelectAllSuppression(
                    isExactCommander: false,
                    hasAuthorizedFormations: true),
                "Spawn-handshake suppression may only be bypassed by the exact commander with resolved formation authority.");
        }

        private static void ValidateDeferredBossPossessionPolicy()
        {
            Assert(
                CoopHideoutBossPhaseContract.AreAssignedPeersReadyWithDeferredSelections(
                    assignedPeerCount: 3,
                    controlledPeerCount: 2,
                    deferredReadyPeerCount: 1),
                "A reserved boss selection must count as ready while two other peers are already materialized.");
            Assert(
                !CoopHideoutBossPhaseContract.AreAssignedPeersReadyWithDeferredSelections(
                    assignedPeerCount: 1,
                    controlledPeerCount: 0,
                    deferredReadyPeerCount: 1) &&
                !CoopHideoutBossPhaseContract.AreAssignedPeersReadyWithDeferredSelections(
                    assignedPeerCount: 4,
                    controlledPeerCount: 2,
                    deferredReadyPeerCount: 1),
                "A deferred boss selection must neither start an empty battle nor hide another unready peer.");

            Assert(
                CoopHideoutBossPhaseContract.ShouldDeferReservedBossPossession(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.InitialAssault) &&
                CoopHideoutBossPhaseContract.ShouldDeferReservedBossPossession(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.PreparingCinematic) &&
                CoopHideoutBossPhaseContract.ShouldDeferReservedBossPossession(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.Cinematic) &&
                CoopHideoutBossPhaseContract.ShouldDeferReservedBossPossession(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.AwaitingHostChoice),
                "The boss peer must remain unpossessed through the initial assault, cutscene, and dialogue choice.");
            Assert(
                !CoopHideoutBossPhaseContract.ShouldDeferReservedBossPossession(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.Duel) &&
                !CoopHideoutBossPhaseContract.ShouldDeferReservedBossPossession(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.AllBattle) &&
                !CoopHideoutBossPhaseContract.ShouldDeferReservedBossPossession(
                    isReservedBossEntry: false,
                    phase: CoopHideoutBossPhase.InitialAssault),
                "Exact boss possession must release only for the duel or all-battle phases and never affect a regular entry.");
            Assert(
                CoopHideoutBossPhaseContract.ShouldPreservePendingReservedBossSelection(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.AllBattle) &&
                !CoopHideoutBossPhaseContract.ShouldPreservePendingReservedBossSelection(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.Completed),
                "The exact pending boss selection must survive until the boss phase completes.");
            Assert(
                CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.Duel,
                    isExactEntryMatch: true,
                    hasFormation: false) &&
                CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.AllBattle,
                    isExactEntryMatch: true,
                    hasFormation: false),
                "An exact reserved boss may receive its campaign formation only after the duel or all-battle release.");
            Assert(
                !CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.Cinematic,
                    isExactEntryMatch: true,
                    hasFormation: false) &&
                !CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.AwaitingHostChoice,
                    isExactEntryMatch: true,
                    hasFormation: false) &&
                !CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.Completed,
                    isExactEntryMatch: true,
                    hasFormation: false) &&
                !CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                    isReservedBossEntry: false,
                    phase: CoopHideoutBossPhase.AllBattle,
                    isExactEntryMatch: true,
                    hasFormation: false) &&
                !CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.AllBattle,
                    isExactEntryMatch: false,
                    hasFormation: false) &&
                !CoopHideoutBossPhaseContract.ShouldRepairReservedBossPossessionFormation(
                    isReservedBossEntry: true,
                    phase: CoopHideoutBossPhase.AllBattle,
                    isExactEntryMatch: true,
                    hasFormation: true),
                "Formation repair must not alter the cinematic, a completed phase, a regular or inexact entry, or an already formed boss.");
        }

    private static void ValidateSceneManifestParsing()
    {
        const string xml = @"
<scene>
  <entities>
    <game_entity name=""dynamic_patrol_area"">
      <transform position=""1.25, 2.5, 3.75"" />
      <children>
        <game_entity name=""patrol_point"">
          <scripts>
            <script name=""PatrolPoint""><variables>
              <variable name=""WaitDuration"" value=""7"" />
              <variable name=""WaitDeviation"" value=""2"" />
              <variable name=""Index"" value=""1"" />
              <variable name=""IsInfiniteWaitPoint"" value=""true"" />
              <variable name=""PatrollingSpeed"" value=""0.65"" />
              <variable name=""LoopAction"" value=""act_hideout_wait"" />
            </variables></script>
          </scripts>
        </game_entity>
        <game_entity name=""patrol_point"">
          <scripts>
            <script name=""PatrolPoint""><variables>
              <variable name=""Index"" value=""0"" />
            </variables></script>
          </scripts>
        </game_entity>
      </children>
    </game_entity>
    <game_entity name=""dynamic_patrol_area"">
      <transform position=""-4, 5, 6"" />
      <children>
        <game_entity name=""patrol_point""><scripts>
          <script name=""PatrolPoint""><variables>
            <variable name=""Index"" value=""0"" />
            <variable name=""LoopAction"" value=""act_hideout_sit"" />
          </variables></script>
        </scripts></game_entity>
      </children>
    </game_entity>
  </entities>
</scene>";

        Assert(
            CoopHideoutSceneManifest.TryParse(
                xml,
                "bandit_forest_sv",
                out CoopHideoutSceneManifest manifest,
                out string diagnostics),
            "A valid hideout scene manifest must parse: " + diagnostics);
        Assert(manifest.PatrolAreas.Count == 2, "Both dynamic patrol areas must be retained.");
        Assert(manifest.PatrolPointCount == 3, "All scripted patrol points must be retained.");
        Assert(manifest.IdleActionCount == 2, "Both non-empty idle actions must be retained.");
        Assert(
            Math.Abs(manifest.PatrolAreas[0].PositionX - 1.25f) < 0.001f &&
            Math.Abs(manifest.PatrolAreas[0].PositionY - 2.5f) < 0.001f &&
            Math.Abs(manifest.PatrolAreas[0].PositionZ - 3.75f) < 0.001f,
            "The patrol-area anchor must use invariant xscene coordinates.");

        CoopHideoutPatrolPointManifest defaultPoint = manifest.PatrolAreas[0].PatrolPoints[0];
        CoopHideoutPatrolPointManifest idlePoint = manifest.PatrolAreas[0].PatrolPoints[1];
        Assert(defaultPoint.Index == 0, "Patrol points must be sorted by their native index.");
        Assert(
            defaultPoint.WaitDurationSeconds == 1 &&
            defaultPoint.WaitDeviationSeconds == 0 &&
            !defaultPoint.IsInfiniteWaitPoint &&
            Math.Abs(defaultPoint.PatrollingSpeed - -1f) < 0.001f &&
            defaultPoint.LoopAction == string.Empty,
            "Missing optional patrol variables must keep safe vanilla-compatible defaults.");
        Assert(
            idlePoint.Index == 1 &&
            idlePoint.WaitDurationSeconds == 7 &&
            idlePoint.WaitDeviationSeconds == 2 &&
            idlePoint.IsInfiniteWaitPoint &&
            Math.Abs(idlePoint.PatrollingSpeed - 0.65f) < 0.001f &&
            idlePoint.LoopAction == "act_hideout_wait",
            "Explicit wait and idle-action metadata must survive parsing.");

        Assert(
            !CoopHideoutSceneManifest.TryParse(
                "<scene><entities /></scene>",
                "bandit_forest_sv",
                out _,
                out diagnostics) &&
            diagnostics == "scene-manifest-dynamic-patrol-areas-missing",
            "A scene without dynamic patrol areas must fail closed.");
    }

    private static void ValidateNightSceneManifestParsing()
    {
        const string xml = @"
<scene>
  <entities>
    <game_entity name=""dynamic_patrol_area"">
      <transform position=""1, 2, 3"" />
      <children><game_entity name=""torch_group"">
        <tags><tag name=""Torch"" /></tags>
        <children><game_entity name=""route_wrapper""><children>
          <game_entity name=""night_spawn_wrapper""><children>
            <game_entity name=""patrol_point""><scripts>
              <script name=""PatrolPoint""><variables>
                <variable name=""Index"" value=""0"" />
                <variable name=""SpawnGroupTag"" value=""stealth_agent_forced"" />
              </variables></script>
            </scripts></game_entity>
          </children></game_entity>
        </children></game_entity></children>
      </game_entity></children>
    </game_entity>
    <game_entity name=""stealth_area_use_point"">
      <transform position=""10, 20, 2"" rotation_euler=""0, 0, 1.57079632679"" />
      <children>
        <game_entity name=""stealth_area_marker"">
          <transform position=""2, 0, 1"" rotation_euler=""0, 0, 0"" />
          <scripts><script name=""StealthAreaMarker""><variables>
            <variable name=""ReinforcementAllyGroupId"" value=""allies_a"" />
            <variable name=""AreaRadius"" value=""6"" />
          </variables></script></scripts>
          <children>
            <game_entity name=""reinforcement"">
              <transform position=""1, 0, 0"" />
              <tags><tag name=""reinforcement_ally_group_spawn_point_tag"" /></tags>
            </game_entity>
            <game_entity name=""wait"">
              <transform position=""0, 1, 0"" />
              <tags><tag name=""wait_point_tag"" /></tags>
            </game_entity>
          </children>
        </game_entity>
      </children>
    </game_entity>
    <game_entity name=""call_camera""><transform position=""30, 40, 5"" /><tags><tag name=""hideout_ambush_cutscene_camera"" /></tags></game_entity>
    <game_entity name=""call_barrel""><transform position=""31, 41, 5"" /><tags><tag name=""hideout_ambush_cutscene_arrow_barrel"" /></tags></game_entity>
    <game_entity name=""call_arrow""><transform position=""32, 42, 5"" /><tags><tag name=""hideout_ambush_cutscene_arrow_path"" /></tags></game_entity>
    <game_entity prefab=""hideout_boss_fight"">
      <transform position=""50, 60, 7"" rotation_euler=""0, 0, 0.75"" />
      <scripts><script name=""HideoutBossFightBehavior""><variables>
        <variable name=""InnerRadius"" value=""1.5"" />
        <variable name=""OuterRadius"" value=""6.5"" />
        <variable name=""WalkDistance"" value=""3.25"" />
      </variables></script></scripts>
    </game_entity>
  </entities>
</scene>";

        Assert(
            CoopHideoutSceneManifest.TryParse(
                xml,
                "bandit_forest_sv",
                out CoopHideoutSceneManifest manifest,
                out string diagnostics),
            "A nighttime hideout scene manifest must parse: " + diagnostics);
        Assert(manifest.HasNightAmbushContract,
            "A use point, marker, reinforcement frame, and wait frame must form a complete nighttime contract.");
        Assert(
            manifest.PatrolAreas[0].PatrolPoints[0].SpawnGroupTag ==
            CoopHideoutAmbushContract.ForcedSentrySpawnGroupTag,
            "The native sentry spawn-group tag must survive scene parsing.");
        Assert(
            manifest.PatrolAreas[0].PatrolPoints[0].HasTorchTag,
            "The authored Torch wrapper tag must be projected onto the patrol point.");
        Assert(
            manifest.CallTroopsCameraFrame != null &&
            manifest.CallTroopsArrowBarrelFrame != null &&
            manifest.CallTroopsArrowPathFrame != null,
            "All three authored call-troops cinematic resources must survive scene parsing.");
        Assert(
            manifest.BossFight?.Frame != null &&
            Math.Abs(manifest.BossFight.Frame.PositionX - 50f) < 0.001f &&
            Math.Abs(manifest.BossFight.Frame.YawRadians - 0.75f) < 0.001f &&
            Math.Abs(manifest.BossFight.InnerRadius - 1.5f) < 0.001f &&
            Math.Abs(manifest.BossFight.OuterRadius - 6.5f) < 0.001f &&
            Math.Abs(manifest.BossFight.WalkDistance - 3.25f) < 0.001f,
            "The authored boss-fight frame and approach dimensions must survive scene parsing.");

        CoopHideoutStealthAreaMarkerManifest marker = manifest.StealthAreaMarkers[0];
        Assert(
            Math.Abs(marker.MarkerFrame.PositionX - 10f) < 0.001f &&
            Math.Abs(marker.MarkerFrame.PositionY - 22f) < 0.001f &&
            Math.Abs(marker.MarkerFrame.PositionZ - 3f) < 0.001f,
            "The nested marker frame must be composed into global scene coordinates.");
        Assert(
            Math.Abs(marker.ReinforcementSpawnFrame.PositionX - 10f) < 0.001f &&
            Math.Abs(marker.ReinforcementSpawnFrame.PositionY - 23f) < 0.001f,
            "The reinforcement frame must inherit both parent transforms.");
        Assert(
            Math.Abs(marker.WaitFrame.PositionX - 9f) < 0.001f &&
            Math.Abs(marker.WaitFrame.PositionY - 22f) < 0.001f,
            "The wait frame must inherit the marker's global yaw.");
        Assert(marker.Contains(10f, 22f) && !marker.Contains(30f, 22f),
            "The marker radius must classify positions in global coordinates.");
    }

    private static void ValidateTriggerPolicy()
    {
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(0) == 0, "Empty enemy roster must not trigger a boss phase.");
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(1) == 1, "A single enemy must trigger at one.");
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(5) == 2, "Five enemies must trigger at the rounded-up quarter.");
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(20) == 5, "Twenty enemies must trigger at five.");
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(100) == 5, "The trigger must be capped at five.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveReservedBossTriggerCount(20) == 0,
            "A reserved boss group must wait until every initial defender is depleted.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldSpawnReservedBossGroup(
                20,
                activeInitialEnemyCount: 0,
                cinematicPrincipalActive: true,
                bossFightEntityAvailable: true),
            "A reserved boss group must start after the initial group is depleted.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldSpawnReservedBossGroup(
                20,
                activeInitialEnemyCount: 1,
                cinematicPrincipalActive: true,
                bossFightEntityAvailable: true),
            "A reserved boss group must not overlap the last initial defender.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(20, 5, cinematicPrincipalActive: true, bossFightEntityAvailable: true),
            "A valid five-enemy boss threshold must start preparation.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(20, 6, cinematicPrincipalActive: true, bossFightEntityAvailable: true),
            "Preparation must not start above the threshold.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(20, 5, cinematicPrincipalActive: false, bossFightEntityAvailable: true),
            "Preparation must require an active cinematic principal.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(20, 5, cinematicPrincipalActive: true, bossFightEntityAvailable: false),
            "Preparation must require the scene anchor.");
    }

    private static void ValidateCooperativeMainHeroFallbackPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.ShouldAutoStartAllBattleAfterBossCinematic(
                mainHeroActive: false,
                cinematicPrincipalActive: true) &&
            !CoopHideoutBossPhaseContract.ShouldAutoStartAllBattleAfterBossCinematic(
                mainHeroActive: true,
                cinematicPrincipalActive: true) &&
            !CoopHideoutBossPhaseContract.ShouldAutoStartAllBattleAfterBossCinematic(
                mainHeroActive: false,
                cinematicPrincipalActive: false),
            "Only a surviving substitute principal with a defeated main hero may skip the choice and start the full battle.");

        int aiHero = CoopHideoutBossPhaseContract.ResolveBossCinematicPrincipalPriority(
            isHero: true,
            isPlayerControlled: false,
            characterLevel: 10);
        int playerHero = CoopHideoutBossPhaseContract.ResolveBossCinematicPrincipalPriority(
            isHero: true,
            isPlayerControlled: true,
            characterLevel: 50);
        int aiTroop = CoopHideoutBossPhaseContract.ResolveBossCinematicPrincipalPriority(
            isHero: false,
            isPlayerControlled: false,
            characterLevel: 30);
        int playerTroop = CoopHideoutBossPhaseContract.ResolveBossCinematicPrincipalPriority(
            isHero: false,
            isPlayerControlled: true,
            characterLevel: 50);
        Assert(
            aiHero > playerHero &&
            playerHero > aiTroop &&
            aiTroop > playerTroop,
            "The cinematic substitute must prefer an AI hero, then another hero, then an AI troop, without transferring the choice.");

        Assert(
            CoopHideoutBossPhaseContract.ShouldFailHideoutWhenPlayerSideEliminated(
                initialAssaultMaterialized: true,
                activePlayerAgentCount: 0) &&
            !CoopHideoutBossPhaseContract.ShouldFailHideoutWhenPlayerSideEliminated(
                initialAssaultMaterialized: true,
                activePlayerAgentCount: 1) &&
            !CoopHideoutBossPhaseContract.ShouldFailHideoutWhenPlayerSideEliminated(
                initialAssaultMaterialized: false,
                activePlayerAgentCount: 0) &&
            CoopHideoutBossPhaseContract.PlayerSideEliminatedCompletionReason ==
                "hideout-player-side-eliminated",
            "A materialized hideout must fail only after the complete player side is eliminated.");
    }

    private static void ValidateMaterializationPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.ResolveVanillaFirstPhaseDefenderCount(29, 22) == 22,
            "The exact native first-phase count must be retained when a boss reserve exists.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveVanillaFirstPhaseDefenderCount(20, 20) == 14,
            "The native seventy-percent fallback must be mirrored when the requested phase consumes the full roster.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveVanillaFirstPhaseDefenderCount(0, 5) == 0,
            "An empty defender roster must not produce an initial phase.");
        Assert(
            CoopHideoutBossPhaseContract.IsValidFirstPhaseParticipantCount(28, 16),
            "A selected sixteen-unit player group must remain valid inside a larger campaign army.");
        Assert(
            !CoopHideoutBossPhaseContract.IsValidFirstPhaseParticipantCount(28, 29),
            "A first-phase count larger than the available roster must be rejected.");
        Assert(
            !CoopHideoutBossPhaseContract.IsValidFirstPhaseParticipantCount(29, 0),
            "A missing exact first-phase count must fail closed instead of materializing the full roster.");
    }

    private static void ValidateMissionObjectivePolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                isNight: true,
                CoopHideoutAmbushPhase.Stealth,
                hasBossState: true,
                CoopHideoutBossPhase.InitialAssault) ==
                CoopHideoutObjectiveStage.LocateMainCamp,
            "The nighttime infiltration must show the native locate-main-camp objective.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                isNight: true,
                CoopHideoutAmbushPhase.CallTroops,
                hasBossState: true,
                CoopHideoutBossPhase.InitialAssault) ==
                CoopHideoutObjectiveStage.Hidden,
            "The objective must be hidden during the call-troops cinematic.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                isNight: true,
                CoopHideoutAmbushPhase.MainCampBattle,
                hasBossState: true,
                CoopHideoutBossPhase.InitialAssault) ==
                CoopHideoutObjectiveStage.ClearMainCamp,
            "The nighttime main-camp battle must show the clear-main-camp objective.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                isNight: false,
                CoopHideoutAmbushPhase.WaitingForMaterialization,
                hasBossState: true,
                CoopHideoutBossPhase.InitialAssault) ==
                CoopHideoutObjectiveStage.ClearMainCamp,
            "The daytime initial assault must show the clear-main-camp objective.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                isNight: false,
                CoopHideoutAmbushPhase.WaitingForMaterialization,
                hasBossState: true,
                CoopHideoutBossPhase.Cinematic) ==
                CoopHideoutObjectiveStage.Hidden,
            "The objective must be hidden during the boss cinematic.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                isNight: false,
                CoopHideoutAmbushPhase.WaitingForMaterialization,
                hasBossState: true,
                CoopHideoutBossPhase.Duel) ==
                CoopHideoutObjectiveStage.WinDuel,
            "The accepted duel must show the native win-the-duel objective.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                isNight: true,
                CoopHideoutAmbushPhase.MainCampBattle,
                hasBossState: true,
                CoopHideoutBossPhase.AllBattle) ==
                CoopHideoutObjectiveStage.WinFight,
            "The cooperative boss battle must show the native win-the-fight objective.");
        Assert(
            CoopHideoutBossPhaseContract.ResolveHideoutObjectiveStage(
                isNight: true,
                CoopHideoutAmbushPhase.Completed,
                hasBossState: true,
                CoopHideoutBossPhase.Completed) ==
                CoopHideoutObjectiveStage.Hidden,
            "A completed hideout must not retain an objective panel.");
    }

    private static void ValidatePhaseTransitions()
    {
        CoopHideoutBossPhaseSession session = NewSession();
        Assert(
            !CoopHideoutBossPhaseContract.TryTransition(
                session,
                CoopHideoutBossPhase.Cinematic,
                DateTime.UtcNow,
                "skip",
                out string rejection) && rejection.StartsWith("transition_invalid", StringComparison.Ordinal),
            "The initial assault must not skip preparation.");

        int initialRevision = session.Revision;
        AssertTransition(session, CoopHideoutBossPhase.PreparingCinematic);
        Assert(session.Revision == initialRevision + 1, "A successful transition must advance the revision once.");
        AssertTransition(session, CoopHideoutBossPhase.Cinematic);
        AssertTransition(session, CoopHideoutBossPhase.AwaitingHostChoice);
    }

    private static void ValidateHostChoiceAuthority()
    {
        CoopHideoutBossPhaseSession session = NewSession();
        session.HostPeerIndex = 7;
        AssertTransition(session, CoopHideoutBossPhase.PreparingCinematic);
        AssertTransition(session, CoopHideoutBossPhase.Cinematic);
        AssertTransition(session, CoopHideoutBossPhase.AwaitingHostChoice);

        Assert(
            !CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                session,
                senderPeerIndex: 8,
                expectedRevision: session.Revision,
                CoopHideoutBossClientCommandKind.ChooseDuel,
                out _,
                out string rejection) && rejection == "choice_sender_not_host",
            "A non-host peer must not choose the boss fight mode.");
        Assert(
            !CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                session,
                senderPeerIndex: 7,
                expectedRevision: session.Revision - 1,
                CoopHideoutBossClientCommandKind.ChooseDuel,
                out _,
                out rejection) && rejection == "choice_revision_stale",
            "A stale host command must be rejected.");
        Assert(
            CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                session,
                senderPeerIndex: 7,
                expectedRevision: session.Revision,
                CoopHideoutBossClientCommandKind.ChooseDuel,
                out CoopHideoutBossChoice choice,
                out rejection) && choice == CoopHideoutBossChoice.Duel,
            "The current host must be able to accept the duel.");
        Assert(
            !CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                session,
                senderPeerIndex: 7,
                expectedRevision: session.Revision,
                CoopHideoutBossClientCommandKind.ChooseAllBattle,
                out _,
                out rejection) && rejection == "choice_already_committed",
            "A committed choice must be idempotent.");
        AssertTransition(session, CoopHideoutBossPhase.Duel);

        CoopHideoutBossPhaseSession allBattleSession = NewSession();
        allBattleSession.HostPeerIndex = 7;
        AssertTransition(allBattleSession, CoopHideoutBossPhase.PreparingCinematic);
        AssertTransition(allBattleSession, CoopHideoutBossPhase.Cinematic);
        AssertTransition(allBattleSession, CoopHideoutBossPhase.AwaitingHostChoice);
        Assert(
            CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                allBattleSession,
                senderPeerIndex: 7,
                expectedRevision: allBattleSession.Revision,
                CoopHideoutBossClientCommandKind.ChooseAllBattle,
                out choice,
                out rejection) && choice == CoopHideoutBossChoice.AllBattle,
            "The explicit fight-together command must resolve to an all-participants battle.");
    }

    private static void ValidateFallbackTransitions()
    {
        CoopHideoutBossPhaseSession duel = NewSession();
        AssertTransition(duel, CoopHideoutBossPhase.PreparingCinematic);
        AssertTransition(duel, CoopHideoutBossPhase.Cinematic);
        AssertTransition(duel, CoopHideoutBossPhase.AwaitingHostChoice);
        AssertTransition(duel, CoopHideoutBossPhase.Duel);
        AssertTransition(duel, CoopHideoutBossPhase.AllBattle);
        AssertTransition(duel, CoopHideoutBossPhase.Completed);

        CoopHideoutBossPhaseSession timeout = NewSession();
        AssertTransition(timeout, CoopHideoutBossPhase.PreparingCinematic);
        AssertTransition(timeout, CoopHideoutBossPhase.Cinematic);
        AssertTransition(timeout, CoopHideoutBossPhase.AwaitingHostChoice);
        AssertTransition(timeout, CoopHideoutBossPhase.AllBattle);
        AssertTransition(timeout, CoopHideoutBossPhase.Completed);
    }

    private static CoopHideoutBossPhaseSession NewSession()
    {
        return new CoopHideoutBossPhaseSession
        {
            BattleInstanceId = "hideout-contract-test",
            Revision = 1,
            Phase = CoopHideoutBossPhase.InitialAssault,
            Choice = CoopHideoutBossChoice.None
        };
    }

    private static void AssertTransition(
        CoopHideoutBossPhaseSession session,
        CoopHideoutBossPhase phase)
    {
        Assert(
            CoopHideoutBossPhaseContract.TryTransition(
                session,
                phase,
                DateTime.UtcNow,
                "test-" + phase,
                out string rejection),
            "Transition to " + phase + " failed: " + rejection);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
