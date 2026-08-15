using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateCompleteApplication();
            ValidatePartialRejection();
            ValidateEntryCountMismatch();
            ValidateSuccessfulRetry();
            ValidateInitialBarrierRejectsPartialApplication();
            ValidateActiveBattleAcknowledgesStaleLifecycleRejection();
            ValidateActiveBattleRejectsTransportCountMismatch();
            ValidateBattleEndedRejectsPartialApplication();
            ValidateRetryWaitsForDelay();
            ValidateRetryRunsAfterDelay();
            ValidateSnapshotRefreshPreservesDeferredBootstrap();
            ValidateSnapshotReadyReplaysDeferredBootstrap();
            ValidateMissionBoundaryClearsDeferredBootstrap();
            ValidateAlreadyMaterializedAgentIsNotReplayed();
            ValidateFacingEnemyDoesNotRequireWorldPosition();
            ValidateFacingDirectionRequiresValidWorldPosition();
            ValidateFacingDirectionRejectsMissingWorldPosition();
            ValidateFacingDirectionRejectsInvalidWorldPosition();
            ValidateRemovedGeneralReleasesOwnership();
            ValidateRemovedCaptainReleasesOwnedFormation();
            ValidateRemovedUnownedAgentDoesNotReleaseOwnership();
            ValidateSpectatorAgentDoesNotReleaseOwnership();
            ValidateClientRemovalDoesNotReleaseOwnership();
            ValidateFieldBattleReleaseIssuesCharge();
            ValidateExactSiegeReleasePreservesNativeAi();
            ValidateEmptyFormationDoesNotReceiveCharge();
            ValidateRemovedGeneralPreservesLivingCaptainFormation();
            ValidateRemovedGeneralReleasesUnownedFormation();
            ValidateRemovedGeneralReleasesDisconnectedCaptainFormation();
            ValidateRemovedCaptainReleasesItsEmptyFormation();
            ValidateUnrelatedRemovalPreservesUnownedFormation();
            ValidateMountedHeroInitialMissingLinkRequestsRepair();
            ValidateMountedHeroVerifiedLinkPreservesRuntimeDismount();
            ValidateMountedHeroMountDeathPreservesRuntimeDismount();
            ValidateMountedHeroRemountVerifiesLiveLink();
            ValidateMountedHeroMountSwapVerifiesNewLiveLink();
            ValidateMountedHeroWithoutTrackedMountDoesNotRepair();
            ValidateMissingCommanderAgentDefersPresentationReplacement();
            ValidateMissingCommanderEntryDefersPresentationReplacement();
            ValidateReadyCommanderAllowsPresentationReplacement();
            ValidateIncompleteCommanderArmyDefersPresentationReplacement();
            ValidateCommanderReadinessRetryCanSucceed();
            ValidateMissingRequiredCommanderBehaviorIsHardFailure();
            ValidateUnitSelectionSearchContract();
            Console.WriteLine("Coop battle startup contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateCompleteApplication()
    {
        CoopMaterializedAgentEntrySnapshotApplyResult result =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 3,
                transportedMappingCount: 3,
                appliedMappingCount: 3);

        Assert(result.AppliedSuccessfully,
            "A snapshot must succeed when every expected mapping is transported and applied.");
        Assert(result.AppliedMappingCount == 3,
            "The result must retain the number of actually applied mappings.");
    }

    private static void ValidatePartialRejection()
    {
        CoopMaterializedAgentEntrySnapshotApplyResult result =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 3,
                transportedMappingCount: 3,
                appliedMappingCount: 2);

        Assert(!result.AppliedSuccessfully,
            "A snapshot must fail when the current agent lifecycle rejects any mapping.");
        Assert(result.TransportedMappingCount == 3 && result.AppliedMappingCount == 2,
            "A partial result must distinguish transport completeness from application completeness.");
    }

    private static void ValidateEntryCountMismatch()
    {
        CoopMaterializedAgentEntrySnapshotApplyResult result =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 3,
                transportedMappingCount: 2,
                appliedMappingCount: 2);

        Assert(!result.AppliedSuccessfully,
            "A snapshot must fail when its parsed mapping count differs from the authoritative entry count.");
    }

    private static void ValidateSuccessfulRetry()
    {
        CoopMaterializedAgentEntrySnapshotApplyResult firstAttempt =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 3,
                transportedMappingCount: 3,
                appliedMappingCount: 2);
        CoopMaterializedAgentEntrySnapshotApplyResult retryAttempt =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 3,
                transportedMappingCount: 3,
                appliedMappingCount: 3);

        Assert(!firstAttempt.AppliedSuccessfully && retryAttempt.AppliedSuccessfully,
            "A later retry must succeed after all agent lifecycles stabilize and accept their mappings.");
    }

    private static void ValidateInitialBarrierRejectsPartialApplication()
    {
        CoopMaterializedAgentEntrySnapshotApplyResult result =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 177,
                transportedMappingCount: 177,
                appliedMappingCount: 166);

        Assert(!CoopMaterializedAgentEntrySnapshotContract.ShouldAcknowledgeSuccess(
                payloadMatched: true,
                phase: CoopMaterializedAgentEntrySnapshotAcknowledgementPhase.InitialBarrier,
                applyResult: result),
            "The initial materialization barrier must reject a partially applied snapshot.");
    }

    private static void ValidateActiveBattleAcknowledgesStaleLifecycleRejection()
    {
        CoopMaterializedAgentEntrySnapshotApplyResult result =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 177,
                transportedMappingCount: 177,
                appliedMappingCount: 166);

        Assert(CoopMaterializedAgentEntrySnapshotContract.ShouldAcknowledgeSuccess(
                payloadMatched: true,
                phase: CoopMaterializedAgentEntrySnapshotAcknowledgementPhase.BattleActive,
                applyResult: result),
            "An active battle must acknowledge a complete snapshot after stale lifecycle mappings are rejected.");
    }

    private static void ValidateActiveBattleRejectsTransportCountMismatch()
    {
        CoopMaterializedAgentEntrySnapshotApplyResult result =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 177,
                transportedMappingCount: 176,
                appliedMappingCount: 176);

        Assert(!CoopMaterializedAgentEntrySnapshotContract.ShouldAcknowledgeSuccess(
                payloadMatched: true,
                phase: CoopMaterializedAgentEntrySnapshotAcknowledgementPhase.BattleActive,
                applyResult: result),
            "An active battle must reject a snapshot whose transported mapping count is incomplete.");
    }

    private static void ValidateBattleEndedRejectsPartialApplication()
    {
        CoopMaterializedAgentEntrySnapshotApplyResult result =
            CoopMaterializedAgentEntrySnapshotContract.Evaluate(
                expectedEntryCount: 177,
                transportedMappingCount: 177,
                appliedMappingCount: 166);

        Assert(!CoopMaterializedAgentEntrySnapshotContract.ShouldAcknowledgeSuccess(
                payloadMatched: true,
                phase: CoopMaterializedAgentEntrySnapshotAcknowledgementPhase.BattleEnded,
                applyResult: result),
            "A completed battle must not acknowledge a late partially applied snapshot.");
    }

    private static void ValidateRetryWaitsForDelay()
    {
        DateTime completedUtc = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert(!CoopMaterializedAgentEntrySnapshotContract.IsRetryDue(
                completedUtc,
                completedUtc.AddSeconds(2.999),
                TimeSpan.FromSeconds(3)),
            "A rejected snapshot must not bypass its retry delay.");
    }

    private static void ValidateRetryRunsAfterDelay()
    {
        DateTime completedUtc = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert(CoopMaterializedAgentEntrySnapshotContract.IsRetryDue(
                completedUtc,
                completedUtc.AddSeconds(3),
                TimeSpan.FromSeconds(3)),
            "A rejected snapshot must become retryable when its retry delay expires.");
    }

    private static void ValidateSnapshotRefreshPreservesDeferredBootstrap()
    {
        CoopClientBootstrapTransitionResult result =
            CoopClientBootstrapResetContract.Evaluate(
                CoopClientBootstrapTransitionKind.BattleSnapshotRefresh,
                pendingPayloadCount: 2);

        Assert(result.PreserveDeferredPayloads && result.PendingPayloadCount == 2,
            "Refreshing the current battle snapshot must preserve deferred hero CreateAgent payloads.");
        Assert(result.ReplayedPayloadCount == 0 && result.ClearedPayloadCount == 0,
            "A snapshot refresh must neither replay nor clear deferred payloads before readiness.");
    }

    private static void ValidateSnapshotReadyReplaysDeferredBootstrap()
    {
        CoopClientBootstrapTransitionResult refreshed =
            CoopClientBootstrapResetContract.Evaluate(
                CoopClientBootstrapTransitionKind.BattleSnapshotRefresh,
                pendingPayloadCount: 2);
        CoopClientBootstrapTransitionResult ready =
            CoopClientBootstrapResetContract.Evaluate(
                CoopClientBootstrapTransitionKind.AuthoritativeSnapshotReady,
                pendingPayloadCount: refreshed.PendingPayloadCount);

        Assert(ready.PendingPayloadCount == 0 && ready.ReplayedPayloadCount == 2,
            "Both deferred hero CreateAgent payloads must be replayed after the authoritative snapshot is ready.");
    }

    private static void ValidateMissionBoundaryClearsDeferredBootstrap()
    {
        CoopClientBootstrapTransitionResult result =
            CoopClientBootstrapResetContract.Evaluate(
                CoopClientBootstrapTransitionKind.MissionBoundary,
                pendingPayloadCount: 2);

        Assert(!result.PreserveDeferredPayloads,
            "A real mission boundary must not preserve deferred client bootstrap payloads.");
        Assert(result.PendingPayloadCount == 0 && result.ClearedPayloadCount == 2,
            "A real mission boundary must clear every deferred payload from the previous mission.");
    }

    private static void ValidateAlreadyMaterializedAgentIsNotReplayed()
    {
        CoopClientBootstrapTransitionResult result =
            CoopClientBootstrapResetContract.Evaluate(
                CoopClientBootstrapTransitionKind.AuthoritativeSnapshotReady,
                pendingPayloadCount: 2,
                alreadyMaterializedPayloadCount: 1);

        Assert(result.ReplayedPayloadCount == 1,
            "An already materialized agent must not be created a second time during deferred replay.");
        Assert(result.SkippedAlreadyMaterializedPayloadCount == 1,
            "The transition result must report the payload skipped because its agent already exists.");
    }

    private static void ValidateFacingEnemyDoesNotRequireWorldPosition()
    {
        CoopCommanderFacingOrderDecision decision =
            CoopCommanderFacingOrderContract.Evaluate(
                isFacingEnemyActive: false,
                hasWorldPosition: false,
                isWorldPositionValid: false);

        Assert(decision == CoopCommanderFacingOrderDecision.FaceEnemy,
            "Switching to face the enemy must not require a world position.");
    }

    private static void ValidateFacingDirectionRequiresValidWorldPosition()
    {
        CoopCommanderFacingOrderDecision decision =
            CoopCommanderFacingOrderContract.Evaluate(
                isFacingEnemyActive: true,
                hasWorldPosition: true,
                isWorldPositionValid: true);

        Assert(decision == CoopCommanderFacingOrderDecision.FaceDirection,
            "Switching from face-enemy to face-direction must use a valid world position.");
    }

    private static void ValidateFacingDirectionRejectsMissingWorldPosition()
    {
        CoopCommanderFacingOrderDecision decision =
            CoopCommanderFacingOrderContract.Evaluate(
                isFacingEnemyActive: true,
                hasWorldPosition: false,
                isWorldPositionValid: false);

        Assert(decision == CoopCommanderFacingOrderDecision.Suppress,
            "A face-direction order without a world position must be suppressed.");
    }

    private static void ValidateFacingDirectionRejectsInvalidWorldPosition()
    {
        CoopCommanderFacingOrderDecision decision =
            CoopCommanderFacingOrderContract.Evaluate(
                isFacingEnemyActive: true,
                hasWorldPosition: true,
                isWorldPositionValid: false);

        Assert(decision == CoopCommanderFacingOrderDecision.Suppress,
            "A face-direction order with an invalid world position must be suppressed.");
    }

    private static void ValidateRemovedGeneralReleasesOwnership()
    {
        Assert(CoopCommanderDeathHandoffContract.ShouldReleaseRemovedAgentOwnership(
                isServer: true,
                hasMission: true,
                hasAgent: true,
                hasPlayableTeam: true,
                ownsGeneralRole: true,
                ownsPlayerOrderController: false,
                ownedFormationCount: 0),
            "A removed general must release team order ownership while its exact agent reference is still available.");
    }

    private static void ValidateRemovedCaptainReleasesOwnedFormation()
    {
        Assert(CoopCommanderDeathHandoffContract.ShouldReleaseRemovedAgentOwnership(
                isServer: true,
                hasMission: true,
                hasAgent: true,
                hasPlayableTeam: true,
                ownsGeneralRole: false,
                ownsPlayerOrderController: false,
                ownedFormationCount: 1),
            "A removed captain must release every formation still owned by that agent.");
    }

    private static void ValidateRemovedUnownedAgentDoesNotReleaseOwnership()
    {
        Assert(!CoopCommanderDeathHandoffContract.ShouldReleaseRemovedAgentOwnership(
                isServer: true,
                hasMission: true,
                hasAgent: true,
                hasPlayableTeam: true,
                ownsGeneralRole: false,
                ownsPlayerOrderController: false,
                ownedFormationCount: 0),
            "Removing an ordinary unowned troop must not change team order ownership.");
    }

    private static void ValidateSpectatorAgentDoesNotReleaseOwnership()
    {
        Assert(!CoopCommanderDeathHandoffContract.ShouldReleaseRemovedAgentOwnership(
                isServer: true,
                hasMission: true,
                hasAgent: true,
                hasPlayableTeam: false,
                ownsGeneralRole: true,
                ownsPlayerOrderController: true,
                ownedFormationCount: 1),
            "A spectator-side removal must not mutate a playable team's order ownership.");
    }

    private static void ValidateClientRemovalDoesNotReleaseOwnership()
    {
        Assert(!CoopCommanderDeathHandoffContract.ShouldReleaseRemovedAgentOwnership(
                isServer: false,
                hasMission: true,
                hasAgent: true,
                hasPlayableTeam: true,
                ownsGeneralRole: true,
                ownsPlayerOrderController: true,
                ownedFormationCount: 4),
            "Only the authoritative server may release commander ownership after agent removal.");
    }

    private static void ValidateFieldBattleReleaseIssuesCharge()
    {
        Assert(CoopCommanderDeathHandoffContract.ShouldIssueChargeOrder(
                useNativeExactSiegeFormationAi: false,
                activeFormationUnitCount: 35),
            "A released non-empty field-battle formation must receive the charge order.");
    }

    private static void ValidateExactSiegeReleasePreservesNativeAi()
    {
        Assert(!CoopCommanderDeathHandoffContract.ShouldIssueChargeOrder(
                useNativeExactSiegeFormationAi: true,
                activeFormationUnitCount: 35),
            "An exact siege formation must preserve native siege AI instead of receiving a forced charge order.");
    }

    private static void ValidateEmptyFormationDoesNotReceiveCharge()
    {
        Assert(!CoopCommanderDeathHandoffContract.ShouldIssueChargeOrder(
                useNativeExactSiegeFormationAi: false,
                activeFormationUnitCount: 0),
            "An empty released formation must not receive a charge order.");
    }

    private static void ValidateRemovedGeneralPreservesLivingCaptainFormation()
    {
        Assert(!CoopCommanderDeathHandoffContract.ShouldReleaseFormationOwnership(
                ownedByRemovedAgent: false,
                releasedGeneralOwnership: true,
                activeFormationUnitCount: 35,
                hasDifferentActivePlayerOwner: true),
            "Removing the general must preserve a populated formation owned by a different connected living captain.");
    }

    private static void ValidateRemovedGeneralReleasesUnownedFormation()
    {
        Assert(CoopCommanderDeathHandoffContract.ShouldReleaseFormationOwnership(
                ownedByRemovedAgent: false,
                releasedGeneralOwnership: true,
                activeFormationUnitCount: 35,
                hasDifferentActivePlayerOwner: false),
            "Removing the general must release every populated formation that has no active player owner.");
    }

    private static void ValidateRemovedGeneralReleasesDisconnectedCaptainFormation()
    {
        Assert(CoopCommanderDeathHandoffContract.ShouldReleaseFormationOwnership(
                ownedByRemovedAgent: false,
                releasedGeneralOwnership: true,
                activeFormationUnitCount: 35,
                hasDifferentActivePlayerOwner: false),
            "A disconnected or inactive captain must not leave its populated formation blocked after the general is removed.");
    }

    private static void ValidateRemovedCaptainReleasesItsEmptyFormation()
    {
        Assert(CoopCommanderDeathHandoffContract.ShouldReleaseFormationOwnership(
                ownedByRemovedAgent: true,
                releasedGeneralOwnership: false,
                activeFormationUnitCount: 0,
                hasDifferentActivePlayerOwner: false),
            "Removing a captain must clear its stale ownership even when the owned formation is empty.");
    }

    private static void ValidateUnrelatedRemovalPreservesUnownedFormation()
    {
        Assert(!CoopCommanderDeathHandoffContract.ShouldReleaseFormationOwnership(
                ownedByRemovedAgent: false,
                releasedGeneralOwnership: false,
                activeFormationUnitCount: 35,
                hasDifferentActivePlayerOwner: false),
            "Removing an unrelated agent must not mutate an unowned formation.");
    }

    private static void ValidateMountedHeroInitialMissingLinkRequestsRepair()
    {
        Assert(CoopMountedHeroMountLinkContract.Evaluate(
                isClient: true,
                snapshotExpectsMount: true,
                trackedMountAgentIndex: 11,
                liveMountAgentIndex: -1,
                hasVerifiedLiveMountLink: false) == CoopMountedHeroMountLinkDecision.RepairInitialLink,
            "A mounted hero whose initial live mount link has never appeared must remain eligible for initial repair.");
    }

    private static void ValidateMountedHeroVerifiedLinkPreservesRuntimeDismount()
    {
        Assert(CoopMountedHeroMountLinkContract.Evaluate(
                isClient: true,
                snapshotExpectsMount: true,
                trackedMountAgentIndex: 11,
                liveMountAgentIndex: -1,
                hasVerifiedLiveMountLink: true) == CoopMountedHeroMountLinkDecision.PreserveRuntimeDismount,
            "A hero who had a verified mount link must remain dismounted when the authoritative runtime removes that link.");
    }

    private static void ValidateMountedHeroMountDeathPreservesRuntimeDismount()
    {
        Assert(CoopMountedHeroMountLinkContract.Evaluate(
                isClient: true,
                snapshotExpectsMount: true,
                trackedMountAgentIndex: 67,
                liveMountAgentIndex: -1,
                hasVerifiedLiveMountLink: true) == CoopMountedHeroMountLinkDecision.PreserveRuntimeDismount,
            "Losing a previously verified mount after its death must not reattach the rider through the initial repair path.");
    }

    private static void ValidateMountedHeroRemountVerifiesLiveLink()
    {
        Assert(CoopMountedHeroMountLinkContract.Evaluate(
                isClient: true,
                snapshotExpectsMount: true,
                trackedMountAgentIndex: -1,
                liveMountAgentIndex: 11,
                hasVerifiedLiveMountLink: true) == CoopMountedHeroMountLinkDecision.LinkVerified,
            "Remounting must verify the current live mount even after the previous tracked link was cleared.");
    }

    private static void ValidateMountedHeroMountSwapVerifiesNewLiveLink()
    {
        Assert(CoopMountedHeroMountLinkContract.Evaluate(
                isClient: true,
                snapshotExpectsMount: true,
                trackedMountAgentIndex: 11,
                liveMountAgentIndex: 42,
                hasVerifiedLiveMountLink: true) == CoopMountedHeroMountLinkDecision.LinkVerified,
            "Switching horses must accept the new authoritative live mount instead of restoring the old tracked mount.");
    }

    private static void ValidateMountedHeroWithoutTrackedMountDoesNotRepair()
    {
        Assert(CoopMountedHeroMountLinkContract.Evaluate(
                isClient: true,
                snapshotExpectsMount: true,
                trackedMountAgentIndex: -1,
                liveMountAgentIndex: -1,
                hasVerifiedLiveMountLink: false) == CoopMountedHeroMountLinkDecision.NotRequired,
            "A mount repair must not run before the authoritative payload identifies the expected mount agent.");
    }

    private static void ValidateMissingCommanderAgentDefersPresentationReplacement()
    {
        CoopCommanderDeploymentReadiness readiness =
            CoopCommanderDeploymentReadinessContract.EvaluatePrerequisites(
                hasMission: true,
                hasSide: true,
                isVillageBoundaryReady: true,
                hasTeam: true,
                hasBannerBearerLogic: true,
                hasOrderController: true,
                hasCommanderEntry: true,
                hasCommanderAgent: false);

        Assert(readiness == CoopCommanderDeploymentReadiness.WaitingForCommanderAgent,
            "A commander entry whose live agent has not arrived must remain a transient wait.");
        Assert(CoopCommanderDeploymentReadinessContract.IsTransientWait(readiness),
            "A missing live commander agent must be retried on a later refresh.");
        Assert(!CoopCommanderDeploymentReadinessContract.ShouldReplaceCurrentPresentation(readiness),
            "A transient commander-agent wait must preserve the current usable selection presentation.");
    }

    private static void ValidateMissingCommanderEntryDefersPresentationReplacement()
    {
        CoopCommanderDeploymentReadiness readiness =
            CoopCommanderDeploymentReadinessContract.EvaluatePrerequisites(
                hasMission: true,
                hasSide: true,
                isVillageBoundaryReady: true,
                hasTeam: true,
                hasBannerBearerLogic: true,
                hasOrderController: true,
                hasCommanderEntry: false,
                hasCommanderAgent: false);

        Assert(readiness == CoopCommanderDeploymentReadiness.WaitingForCommanderEntry,
            "The UI must wait for the authoritative commander entry before looking for its agent.");
        Assert(!CoopCommanderDeploymentReadinessContract.ShouldReplaceCurrentPresentation(readiness),
            "A missing commander entry must not destroy the current selection presentation.");
    }

    private static void ValidateReadyCommanderAllowsPresentationReplacement()
    {
        CoopCommanderDeploymentReadiness readiness =
            CoopCommanderDeploymentReadinessContract.EvaluatePrerequisites(
                hasMission: true,
                hasSide: true,
                isVillageBoundaryReady: true,
                hasTeam: true,
                hasBannerBearerLogic: true,
                hasOrderController: true,
                hasCommanderEntry: true,
                hasCommanderAgent: true);

        Assert(readiness == CoopCommanderDeploymentReadiness.Ready,
            "All commander deployment prerequisites must produce the ready state.");
        Assert(CoopCommanderDeploymentReadinessContract.ShouldReplaceCurrentPresentation(readiness),
            "The current selection presentation may be replaced only after commander readiness is complete.");
    }

    private static void ValidateIncompleteCommanderArmyDefersPresentationReplacement()
    {
        CoopCommanderDeploymentReadiness readiness =
            CoopCommanderDeploymentReadinessContract.EvaluateArmy(
                formationsWithUnits: 8,
                selectableFormationsWithUnits: 0,
                physicalClassUnitCount: 0);

        Assert(readiness == CoopCommanderDeploymentReadiness.WaitingForCommanderArmy,
            "The commander UI must wait while formations exist but their selectable agents are not materialized.");
        Assert(CoopCommanderDeploymentReadinessContract.IsTransientWait(readiness),
            "An incomplete commander army must be retried after materialization advances.");
        Assert(!CoopCommanderDeploymentReadinessContract.ShouldReplaceCurrentPresentation(readiness),
            "An incomplete commander army must preserve the current selection presentation.");
    }

    private static void ValidateCommanderReadinessRetryCanSucceed()
    {
        CoopCommanderDeploymentReadiness firstAttempt =
            CoopCommanderDeploymentReadinessContract.EvaluatePrerequisites(
                hasMission: true,
                hasSide: true,
                isVillageBoundaryReady: true,
                hasTeam: true,
                hasBannerBearerLogic: true,
                hasOrderController: true,
                hasCommanderEntry: true,
                hasCommanderAgent: false);
        CoopCommanderDeploymentReadiness retryAttempt =
            CoopCommanderDeploymentReadinessContract.EvaluatePrerequisites(
                hasMission: true,
                hasSide: true,
                isVillageBoundaryReady: true,
                hasTeam: true,
                hasBannerBearerLogic: true,
                hasOrderController: true,
                hasCommanderEntry: true,
                hasCommanderAgent: true);

        Assert(!CoopCommanderDeploymentReadinessContract.ShouldReplaceCurrentPresentation(firstAttempt) &&
               CoopCommanderDeploymentReadinessContract.ShouldReplaceCurrentPresentation(retryAttempt),
            "A later refresh must enter commander deployment after the authoritative agent arrives.");
    }

    private static void ValidateMissingRequiredCommanderBehaviorIsHardFailure()
    {
        CoopCommanderDeploymentReadiness readiness =
            CoopCommanderDeploymentReadinessContract.EvaluatePrerequisites(
                hasMission: true,
                hasSide: true,
                isVillageBoundaryReady: true,
                hasTeam: true,
                hasBannerBearerLogic: false,
                hasOrderController: true,
                hasCommanderEntry: true,
                hasCommanderAgent: true);

        Assert(readiness == CoopCommanderDeploymentReadiness.MissingRequiredBannerBearerLogic,
            "A missing required commander mission behavior must retain its precise failure reason.");
        Assert(!CoopCommanderDeploymentReadinessContract.IsTransientWait(readiness),
            "A required mission behavior missing after battle readiness must remain a hard configuration failure.");
    }

    private static void ValidateUnitSelectionSearchContract()
    {
        Assert(CoopUnitSelectionSearchContract.MatchesDisplayName("VVS", string.Empty),
            "An empty unit search must keep every fighter visible.");
        Assert(CoopUnitSelectionSearchContract.MatchesDisplayName("Battanian Fian Champion", "fian"),
            "A unit search must match a partial display name.");
        Assert(CoopUnitSelectionSearchContract.MatchesDisplayName("VVS Hero", "vvs HERO"),
            "A unit search must ignore letter casing.");
        Assert(CoopUnitSelectionSearchContract.MatchesDisplayName("Князь Альдар", "альд"),
            "A unit search must support partial Cyrillic hero names.");
        Assert(CoopUnitSelectionSearchContract.MatchesDisplayName("Player AC", "  player ac  "),
            "A unit search must trim surrounding whitespace.");
        Assert(!CoopUnitSelectionSearchContract.MatchesDisplayName("VVS Hero", "XCTwnik"),
            "A non-matching fighter must be hidden.");
        Assert(CoopUnitSelectionSearchContract.MatchesDisplayName("VVS Hero", "   ") &&
               CoopUnitSelectionSearchContract.MatchesDisplayName("Player AC", "   "),
            "Clearing the search must restore the complete unit list.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
