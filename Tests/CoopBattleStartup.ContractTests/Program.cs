using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Relief;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateExactReliefUniqueSettlementResolution();
            ValidateExactReliefResolutionIsSideOrderIndependent();
            ValidateExactReliefAttachedPartyMatchesArmyLeader();
            ValidateExactReliefRejectsUnrelatedActiveSiege();
            ValidateExactReliefRejectsOtherBattleTypes();
            ValidateExactReliefRejectsCompletedSiege();
            ValidateExactReliefRejectsZeroAndAmbiguousCandidates();
            ValidateExactReliefRejectsBothSidesInSameBesiegerArmy();
            ValidateExactReliefResolutionIsIdempotentAndNonMutating();
            ValidateExactReliefSnapshotAndServerSourceFlow();
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
            ValidateWeaponRuntimeWaitsForSnapshot();
            ValidatePossessedHeroEmptyWeaponSlotIsSuppressed();
            ValidateCraftedThrowingAxeUsageIsAllowed();
            ValidateInvalidCraftedWeaponUsageIsSuppressed();
            ValidateWeaponRuntimeContractDoesNotAffectNonCoopTraffic();
            ValidateExactHeroAgentCapabilityFlagMapping();
            ValidateExactHeroAgentCapabilityFlagsPreserveExistingFlags();
            ValidateClientExactHeroCapabilityApplyOrder();
            ValidateStrictExactHeroUsesAuthoritativeCreateAgentMissionEquipment();
            ValidateStrictExactHeroRejectsIncompatibleMissionEquipment();
            ValidatePossessionDoesNotInjectWeaponRuntimeState();
            ValidateMissingItemCatalogExcludesLoadedItems();
            ValidateMissingItemCatalogLoadsEachMissingIdOnce();
            ValidateMissingItemCatalogRejectsInvalidNodes();
            ValidateStandaloneCampaignSkipsCraftedMirrorMaterialization();
            ValidateNetworkRuntimeUsesOnlyReadyPreloadedCraftingCatalog();
            ValidateUnavailableCraftingCatalogIsRejected();
            ValidateCraftingPieceCanonicalityContract();
            ValidateCampaignSmithyCraftingPieceSelectionContract();
            ValidateRejectedCraftedMirrorIsNotRetried();
            ValidateCraftedMirrorFailureUsesSafeWeaponSlotFallback();
            ValidateBundledBannerlord148CraftingCatalog();
            ValidateRuntimeCraftingRegistryDoesNotMutateGlobalCatalogs();
            Console.WriteLine("Coop battle startup contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateExactReliefUniqueSettlementResolution()
    {
        ExactReliefSettlementResolution resolution =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("relief_army") },
                new[] { Party("host_besieger") },
                new[]
                {
                    Siege(
                        "castle_a",
                        isActive: true,
                        Party("host_besieger"))
                });

        Assert(
            resolution.Status == ExactReliefSettlementResolutionStatus.Resolved &&
            resolution.SettlementId == "castle_a" &&
            resolution.MatchingCandidateCount == 1,
            "One active siege explained by exactly one battle side must resolve its settlement.");
        Assert(
            resolution.BesiegerBattleSide == ExactReliefBesiegerBattleSide.Defender,
            "The resolver must report the live defender as the besieger side without remapping it.");
    }

    private static void ValidateExactReliefResolutionIsSideOrderIndependent()
    {
        ExactReliefSiegeCandidateDescriptor candidate = Siege(
            "castle_a",
            isActive: true,
            Party("host_besieger"));
        ExactReliefSettlementResolution original =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("relief_army") },
                new[] { Party("host_besieger") },
                new[] { candidate });
        ExactReliefSettlementResolution swapped =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("host_besieger") },
                new[] { Party("relief_army") },
                new[] { candidate });

        Assert(
            original.Status == ExactReliefSettlementResolutionStatus.Resolved &&
            swapped.Status == ExactReliefSettlementResolutionStatus.Resolved &&
            original.SettlementId == swapped.SettlementId,
            "Swapping attacker and defender inputs must not change the resolved relief settlement.");
        Assert(
            original.BesiegerBattleSide == ExactReliefBesiegerBattleSide.Defender &&
            swapped.BesiegerBattleSide == ExactReliefBesiegerBattleSide.Attacker,
            "Swapping inputs may change the reported live side but must not invent campaign roles.");
    }

    private static void ValidateExactReliefAttachedPartyMatchesArmyLeader()
    {
        ExactReliefSettlementResolution resolution =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("relief_army") },
                new[] { Party("host_attached_party", "host_army_leader") },
                new[]
                {
                    Siege(
                        "castle_attached",
                        isActive: true,
                        Party("host_army_leader", "host_army_leader"))
                });

        Assert(
            resolution.Status == ExactReliefSettlementResolutionStatus.Resolved &&
            resolution.SettlementId == "castle_attached",
            "An attached battle party must match the besieger army through its leader party.");
    }

    private static void ValidateExactReliefRejectsUnrelatedActiveSiege()
    {
        ExactReliefSettlementResolution resolution =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("field_attacker") },
                new[] { Party("field_defender") },
                new[]
                {
                    Siege(
                        "nearby_castle",
                        isActive: true,
                        Party("unrelated_besieger"))
                });

        Assert(
            resolution.Status == ExactReliefSettlementResolutionStatus.NoMatch,
            "An active nearby siege must not match when neither live battle side belongs to it.");
    }

    private static void ValidateExactReliefRejectsOtherBattleTypes()
    {
        ExactReliefSiegeCandidateDescriptor candidate = Siege(
            "castle_a",
            isActive: true,
            Party("host_besieger"));
        foreach (string battleType in new[] { "FieldBattle", "Siege", "SallyOut" })
        {
            ExactReliefSettlementResolution resolution =
                ExactReliefSettlementResolutionPolicy.Resolve(
                    battleType,
                    new[] { Party("relief_army") },
                    new[] { Party("host_besieger") },
                    new[] { candidate });
            Assert(
                resolution.Status == ExactReliefSettlementResolutionStatus.NoMatch,
                battleType + " must never be reclassified as an exact SiegeOutside relief battle.");
        }
    }

    private static void ValidateExactReliefRejectsCompletedSiege()
    {
        ExactReliefSettlementResolution resolution =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("relief_army") },
                new[] { Party("host_besieger") },
                new[]
                {
                    Siege(
                        "completed_castle",
                        isActive: false,
                        Party("host_besieger"))
                });

        Assert(
            resolution.Status == ExactReliefSettlementResolutionStatus.NoMatch,
            "A completed or cancelled siege event must never resolve a relief settlement.");
    }

    private static void ValidateExactReliefRejectsZeroAndAmbiguousCandidates()
    {
        ExactReliefSettlementResolution zero =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("relief_army") },
                new[] { Party("host_besieger") },
                Array.Empty<ExactReliefSiegeCandidateDescriptor>());
        Assert(
            zero.Status == ExactReliefSettlementResolutionStatus.NoMatch &&
            zero.MatchingCandidateCount == 0,
            "Zero matching active sieges must fail without a proximity guess.");

        ExactReliefSettlementResolution missingOpponent =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                Array.Empty<ExactReliefPartyDescriptor>(),
                new[] { Party("host_besieger") },
                new[]
                {
                    Siege("castle_a", true, Party("host_besieger"))
                });
        Assert(
            missingOpponent.Status == ExactReliefSettlementResolutionStatus.NoMatch,
            "A siege side without a proven opposing battle side must fail closed.");

        ExactReliefSettlementResolution ambiguous =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("relief_army") },
                new[] { Party("host_besieger") },
                new[]
                {
                    Siege("castle_a", true, Party("host_besieger")),
                    Siege("castle_b", true, Party("host_besieger"))
                });
        Assert(
            ambiguous.Status == ExactReliefSettlementResolutionStatus.Ambiguous &&
            ambiguous.MatchingCandidateCount == 2 &&
            string.IsNullOrEmpty(ambiguous.SettlementId),
            "Two equally valid active sieges must be rejected as ambiguous.");
    }

    private static void ValidateExactReliefRejectsBothSidesInSameBesiegerArmy()
    {
        ExactReliefSettlementResolution resolution =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                new[] { Party("army_party_a", "shared_army_leader") },
                new[] { Party("army_party_b", "shared_army_leader") },
                new[]
                {
                    Siege(
                        "castle_a",
                        true,
                        Party("shared_army_leader", "shared_army_leader"))
                });

        Assert(
            resolution.Status == ExactReliefSettlementResolutionStatus.NoMatch,
            "A candidate explained by both battle sides must be rejected instead of guessing a besieger side.");
    }

    private static void ValidateExactReliefResolutionIsIdempotentAndNonMutating()
    {
        var attacker = new List<ExactReliefPartyDescriptor>
        {
            Party("relief_army")
        };
        var defender = new List<ExactReliefPartyDescriptor>
        {
            Party("host_attached", "host_leader")
        };
        var candidates = new List<ExactReliefSiegeCandidateDescriptor>
        {
            Siege("castle_a", true, Party("host_leader", "host_leader"))
        };

        ExactReliefSettlementResolution first =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                attacker,
                defender,
                candidates);
        ExactReliefSettlementResolution second =
            ExactReliefSettlementResolutionPolicy.Resolve(
                "SiegeOutside",
                attacker,
                defender,
                candidates);

        Assert(
            first.Status == second.Status &&
            first.SettlementId == second.SettlementId &&
            first.BesiegerBattleSide == second.BesiegerBattleSide,
            "Repeated resolution over the same campaign state must be deterministic and idempotent.");
        Assert(
            attacker.Count == 1 &&
            defender.Count == 1 &&
            candidates.Count == 1 &&
            defender[0].PartyId == "host_attached" &&
            candidates[0].SettlementId == "castle_a",
            "Settlement resolution must not mutate the supplied battle or siege descriptors.");
    }

    private static void ValidateExactReliefSnapshotAndServerSourceFlow()
    {
        string repositoryRoot = FindRepositoryRoot();
        string detectorSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Campaign",
            "BattleDetector.cs"));
        string adapterSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Campaign",
            "Relief",
            "ExactReliefCampaignBattleAdapter.cs"));
        string scenarioSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Infrastructure",
            "Relief",
            "ExactReliefScenarioContract.cs"));
        string dedicatedSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "DedicatedHelper",
            "DedicatedServerCommands.cs"));

        Assert(
            CountOccurrences(
                detectorSource,
                "Settlement encounterSettlement = ResolveEncounterSettlementSafe(battle);") == 3,
            "Mission validation, scenario context, and scene selection must use one relief settlement resolver.");
        Assert(
            detectorSource.Contains(
                "CampaignBattleType = battle?.EventType.ToString()",
                StringComparison.Ordinal) &&
            detectorSource.Contains(
                "SettlementId = encounterSettlement?.StringId ?? string.Empty",
                StringComparison.Ordinal) &&
            detectorSource.Contains(
                "message.Snapshot.ScenarioContext = scenarioContext?.Clone();",
                StringComparison.Ordinal),
            "The resolved SiegeOutside identity and settlement id must reach the battle snapshot.");
        Assert(
            detectorSource.Contains(
                "TryGetPropertyValue(battle, \"AttackerSide\")",
                StringComparison.Ordinal) &&
            detectorSource.Contains(
                "TryGetPropertyValue(battle, \"DefenderSide\")",
                StringComparison.Ordinal),
            "Snapshot construction must preserve Bannerlord's live attacker and defender sides.");
        Assert(
            adapterSource.Contains(
                "ExactReliefSettlementResolutionPolicy.Resolve",
                StringComparison.Ordinal) &&
            adapterSource.Contains(
                "mobileParty.Army?.LeaderParty",
                StringComparison.Ordinal) &&
            adapterSource.Contains(
                "mobileParty.AttachedTo",
                StringComparison.Ordinal) &&
            !adapterSource.Contains("FindNearest", StringComparison.Ordinal),
            "The campaign adapter must use active siege and army membership without a nearest-settlement guess.");
        Assert(
            scenarioSource.Contains(
                "public const string CampaignBattleType = \"SiegeOutside\";",
                StringComparison.Ordinal) &&
            scenarioSource.Contains(
                "!scenarioContext.IsSiegeBattle",
                StringComparison.Ordinal),
            "Exact relief must retain the SiegeOutside campaign identity in the field-battle mission shell.");
        Assert(
            dedicatedSource.Contains(
                "string requestedGameType = snapshot.MultiplayerGameType;",
                StringComparison.Ordinal) &&
            dedicatedSource.Contains(
                "? CoopGameModeIds.OfficialBattle",
                StringComparison.Ordinal),
            "The dedicated helper must continue consuming the snapshot and selecting the official Battle game type.");
    }

    private static ExactReliefPartyDescriptor Party(
        string partyId,
        string armyLeaderPartyId = null)
    {
        return new ExactReliefPartyDescriptor(
            partyId,
            armyLeaderPartyId ?? partyId);
    }

    private static ExactReliefSiegeCandidateDescriptor Siege(
        string settlementId,
        bool isActive,
        params ExactReliefPartyDescriptor[] besiegerParties)
    {
        return new ExactReliefSiegeCandidateDescriptor(
            settlementId,
            isActive,
            besiegerParties);
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void ValidateMissingItemCatalogExcludesLoadedItems()
    {
        XmlDocument source = LoadXml(
            "<Items>" +
            "<CraftedItem id='cs_mirror_imperial_throwing_spear_1_t4_11307d28' />" +
            "<Item id='already_loaded_armor' />" +
            "<Item id='missing_campaign_armor' />" +
            "</Items>");
        var loadedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs_mirror_imperial_throwing_spear_1_t4_11307d28",
            "already_loaded_armor"
        };

        ExactCampaignItemCatalogSelection selection =
            ExactCampaignItemCatalogLoadPolicy.SelectMissingItems(
                source,
                itemId => loadedIds.Contains(itemId));

        Assert(selection.CandidateCount == 3 && selection.SelectedCount == 1,
            "The missing-item catalog must retain only items absent from MBObjectManager.");
        Assert(selection.SkippedExistingCount == 2,
            "The missing-item catalog must exclude an already loaded crafted Pilum and ordinary item.");
        Assert(selection.Document.DocumentElement?.FirstChild?.Attributes?["id"]?.Value == "missing_campaign_armor",
            "The missing-item catalog must preserve the exact missing item node.");
    }

    private static void ValidateMissingItemCatalogLoadsEachMissingIdOnce()
    {
        XmlDocument source = LoadXml(
            "<Items>" +
            "<Item id='missing_weapon' />" +
            "<CraftedItem id='missing_weapon' />" +
            "<CraftedItem id='missing_crafted_weapon' />" +
            "</Items>");

        ExactCampaignItemCatalogSelection selection =
            ExactCampaignItemCatalogLoadPolicy.SelectMissingItems(source, itemId => false);

        Assert(selection.CandidateCount == 3 && selection.SelectedCount == 2,
            "The missing-item catalog must load every distinct missing id exactly once.");
        Assert(selection.SkippedDuplicateCount == 1,
            "The missing-item catalog must reject repeated ids before deserialization.");
    }

    private static void ValidateMissingItemCatalogRejectsInvalidNodes()
    {
        XmlDocument source = LoadXml(
            "<Items>" +
            "<!-- ignored -->" +
            "<EquipmentRoster id='not_an_item' />" +
            "<Item />" +
            "<CraftedItem id='  ' />" +
            "<Item id='valid_item' />" +
            "</Items>");

        ExactCampaignItemCatalogSelection selection =
            ExactCampaignItemCatalogLoadPolicy.SelectMissingItems(source, itemId => false);

        Assert(selection.CandidateCount == 3 && selection.SelectedCount == 1,
            "The missing-item catalog must ignore non-item XML and reject item nodes without a valid id.");
        Assert(selection.SkippedInvalidCount == 2,
            "The missing-item catalog must report invalid item nodes without attempting deserialization.");
    }

    private static XmlDocument LoadXml(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document;
    }

    private static void ValidateStandaloneCampaignSkipsCraftedMirrorMaterialization()
    {
        ExactCampaignCraftingRuntimeDecision decision =
            ExactCampaignCraftingRuntimeSafetyContract.Evaluate(
                isCampaignRuntime: true,
                isNetworkSessionActive: false,
                isPreloadedCatalogReady: false);

        Assert(decision == ExactCampaignCraftingRuntimeDecision.SkipStandaloneCampaign,
            "A standalone campaign host must publish snapshots without materializing multiplayer crafted mirrors.");
    }

    private static void ValidateNetworkRuntimeUsesOnlyReadyPreloadedCraftingCatalog()
    {
        ExactCampaignCraftingRuntimeDecision decision =
            ExactCampaignCraftingRuntimeSafetyContract.Evaluate(
                isCampaignRuntime: false,
                isNetworkSessionActive: true,
                isPreloadedCatalogReady: true);

        Assert(decision == ExactCampaignCraftingRuntimeDecision.UsePreloadedCatalog,
            "A network battle may materialize crafted mirrors only from the catalog loaded before the mission.");
    }

    private static void ValidateUnavailableCraftingCatalogIsRejected()
    {
        ExactCampaignCraftingRuntimeDecision decision =
            ExactCampaignCraftingRuntimeSafetyContract.Evaluate(
                isCampaignRuntime: false,
                isNetworkSessionActive: true,
                isPreloadedCatalogReady: false);

        Assert(decision == ExactCampaignCraftingRuntimeDecision.RejectUnavailableCatalog,
            "An incomplete network crafting catalog must be rejected instead of being reloaded at runtime.");
    }

    private static void ValidateCraftingPieceCanonicalityContract()
    {
        Assert(ExactCampaignCraftingRuntimeSafetyContract.IsCanonicalCraftingPiece(
                belongsToTemplate: true,
                resolvesToSameObject: true,
                isReady: true,
                isValid: true),
            "A ready registered template piece must pass canonical validation.");
        Assert(!ExactCampaignCraftingRuntimeSafetyContract.IsCanonicalCraftingPiece(
                belongsToTemplate: true,
                resolvesToSameObject: false,
                isReady: true,
                isValid: true),
            "A duplicate piece instance with the same id must not enter crafted weapon generation.");
        Assert(!ExactCampaignCraftingRuntimeSafetyContract.IsCanonicalCraftingPiece(
                belongsToTemplate: true,
                resolvesToSameObject: true,
                isReady: false,
                isValid: true),
            "An unready crafting piece must not enter crafted weapon generation.");
    }

    private static void ValidateCampaignSmithyCraftingPieceSelectionContract()
    {
        Assert(ExactCampaignCraftingRuntimeSafetyContract.ShouldAllowCampaignSmithyPieceSelection(
                isCampaignSmithy: true,
                belongsToTemplate: true,
                resolvesToSameObject: true,
                isReady: true,
                isValid: true),
            "A ready canonical campaign smithy piece must remain selectable.");
        Assert(!ExactCampaignCraftingRuntimeSafetyContract.ShouldAllowCampaignSmithyPieceSelection(
                isCampaignSmithy: true,
                belongsToTemplate: true,
                resolvesToSameObject: true,
                isReady: false,
                isValid: false),
            "An unmaterialized multiplayer campaign smithy placeholder must be rejected.");
        Assert(!ExactCampaignCraftingRuntimeSafetyContract.ShouldAllowCampaignSmithyPieceSelection(
                isCampaignSmithy: true,
                belongsToTemplate: true,
                resolvesToSameObject: false,
                isReady: true,
                isValid: true),
            "A duplicate campaign smithy piece instance must be rejected.");
        Assert(!ExactCampaignCraftingRuntimeSafetyContract.ShouldAllowCampaignSmithyPieceSelection(
                isCampaignSmithy: true,
                belongsToTemplate: false,
                resolvesToSameObject: true,
                isReady: true,
                isValid: true),
            "A piece outside the active campaign smithy template must be rejected.");
        Assert(ExactCampaignCraftingRuntimeSafetyContract.ShouldAllowCampaignSmithyPieceSelection(
                isCampaignSmithy: false,
                belongsToTemplate: false,
                resolvesToSameObject: false,
                isReady: false,
                isValid: false),
            "The campaign smithy guard must not intercept non-campaign crafting flows.");
    }

    private static void ValidateRejectedCraftedMirrorIsNotRetried()
    {
        Assert(!ExactCampaignCraftingRuntimeSafetyContract.ShouldRetryRejectedCraftedMirror(wasRejected: true),
            "A rejected crafted mirror key must not repeatedly mutate native-sensitive state.");
        Assert(ExactCampaignCraftingRuntimeSafetyContract.ShouldRetryRejectedCraftedMirror(wasRejected: false),
            "A new crafted mirror key must receive one validation attempt.");
    }

    private static void ValidateCraftedMirrorFailureUsesSafeWeaponSlotFallback()
    {
        Assert(ExactCampaignCraftingRuntimeSafetyContract.ShouldUseSafeWeaponSlotFallback(craftedMirrorResolved: false),
            "An unresolved crafted mirror must degrade to an empty safe weapon slot.");
        Assert(!ExactCampaignCraftingRuntimeSafetyContract.ShouldUseSafeWeaponSlotFallback(craftedMirrorResolved: true),
            "A valid crafted mirror must remain equipped.");
    }

    private static void ValidateBundledBannerlord148CraftingCatalog()
    {
        string repositoryRoot = FindRepositoryRoot();
        string catalogPath = Path.Combine(
            repositoryRoot,
            "Module",
            "CoopSpectator",
            "ModuleData",
            "coopspectator_crafting_pieces.xml");
        var catalogDocument = new XmlDocument();
        catalogDocument.Load(catalogPath);
        string[] pieceIds = catalogDocument
            .SelectNodes("/CraftingPieces/CraftingPiece")
            .Cast<XmlElement>()
            .Select(piece => piece.GetAttribute("id"))
            .ToArray();

        Assert(pieceIds.Length == 805,
            "The bundled Bannerlord 1.4.8 campaign crafting catalog must contain all 805 pieces.");
        Assert(pieceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 805,
            "The bundled campaign crafting catalog must not contain duplicate piece ids.");

        foreach (string subModuleRelativePath in new[]
                 {
                     Path.Combine("Module", "CoopSpectator", "SubModule.xml"),
                     Path.Combine("Module", "CoopSpectatorDedicated", "SubModule.xml")
                 })
        {
            var subModuleDocument = new XmlDocument();
            subModuleDocument.Load(Path.Combine(repositoryRoot, subModuleRelativePath));
            XmlNode craftingPiecesNode = subModuleDocument.SelectSingleNode(
                "/Module/Xmls/XmlNode[XmlName/@path='coopspectator_crafting_pieces']");
            Assert(craftingPiecesNode != null,
                "Each module must declare the bundled crafting piece catalog.");
            Assert(craftingPiecesNode.SelectSingleNode(
                       "IncludedGameTypes/GameType[@value='MultiplayerGame']") != null,
                "The bundled campaign crafting pieces must be loaded only by MultiplayerGame.");
        }
    }

    private static void ValidateRuntimeCraftingRegistryDoesNotMutateGlobalCatalogs()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Infrastructure",
            "ExactCampaignRuntimeItemRegistry.cs"));

        Assert(!source.Contains("LoadSupportXmlDocument", StringComparison.Ordinal),
            "The runtime crafting registry must never bulk-load support XML catalogs.");
        Assert(!source.Contains("TryUnregisterNonReadyObjects", StringComparison.Ordinal),
            "The runtime crafting registry must never globally unregister non-ready objects.");
        Assert(!source.Contains("\"_availablePieces\"", StringComparison.Ordinal),
            "The runtime crafting registry must never rewrite WeaponDescription private piece lists.");
    }

    private static string FindRepositoryRoot()
    {
        string configuredRoot = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string resolvedRoot = Path.GetFullPath(configuredRoot);
            if (File.Exists(Path.Combine(resolvedRoot, "CoopSpectator.csproj")))
                return resolvedRoot;
            throw new InvalidOperationException("COOPSPECTATOR_REPOSITORY_ROOT does not identify this repository.");
        }

        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CoopSpectator.csproj")))
            directory = directory.Parent;

        if (directory == null)
            throw new InvalidOperationException("Could not locate the repository root for source contract checks.");

        return directory.FullName;
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

    private static void ValidateWeaponRuntimeWaitsForSnapshot()
    {
        CoopClientWeaponRuntimeSafetyResult result =
            CoopClientWeaponRuntimeSafetyContract.Evaluate(
                isCoopClientContext: true,
                snapshotReady: false,
                hasDeferredAgentBootstrap: true,
                agentExists: false,
                agentActive: false,
                requestTargetsWeaponSlot: true,
                requestedSlotOccupied: false,
                validateUsageIndex: true,
                usageCatalogReadable: false,
                requestedUsageIndex: 1,
                usageCount: 0);

        Assert(result.Decision == CoopClientWeaponRuntimeSafetyDecision.Defer,
            "A strict hero weapon message must wait while its exact snapshot and agent bootstrap are pending.");
    }

    private static void ValidatePossessedHeroEmptyWeaponSlotIsSuppressed()
    {
        CoopClientWeaponRuntimeSafetyResult result =
            CoopClientWeaponRuntimeSafetyContract.Evaluate(
                isCoopClientContext: true,
                snapshotReady: true,
                hasDeferredAgentBootstrap: false,
                agentExists: true,
                agentActive: true,
                requestTargetsWeaponSlot: true,
                requestedSlotOccupied: false,
                validateUsageIndex: true,
                usageCatalogReadable: false,
                requestedUsageIndex: 1,
                usageCount: 0);

        Assert(result.Decision == CoopClientWeaponRuntimeSafetyDecision.Suppress,
            "A possessed hero must never enter native wield code when the requested crafted weapon slot is empty.");
    }

    private static void ValidateCraftedThrowingAxeUsageIsAllowed()
    {
        CoopClientWeaponRuntimeSafetyResult result =
            CoopClientWeaponRuntimeSafetyContract.Evaluate(
                isCoopClientContext: true,
                snapshotReady: true,
                hasDeferredAgentBootstrap: false,
                agentExists: true,
                agentActive: true,
                requestTargetsWeaponSlot: true,
                requestedSlotOccupied: true,
                validateUsageIndex: true,
                usageCatalogReadable: true,
                requestedUsageIndex: 1,
                usageCount: 2);

        Assert(result.Decision == CoopClientWeaponRuntimeSafetyDecision.Allow,
            "A materialized crafted throwing axe must allow its valid throwing usage before native wield.");
    }

    private static void ValidateInvalidCraftedWeaponUsageIsSuppressed()
    {
        CoopClientWeaponRuntimeSafetyResult result =
            CoopClientWeaponRuntimeSafetyContract.Evaluate(
                isCoopClientContext: true,
                snapshotReady: true,
                hasDeferredAgentBootstrap: false,
                agentExists: true,
                agentActive: true,
                requestTargetsWeaponSlot: true,
                requestedSlotOccupied: true,
                validateUsageIndex: true,
                usageCatalogReadable: true,
                requestedUsageIndex: 1,
                usageCount: 1);

        Assert(result.Decision == CoopClientWeaponRuntimeSafetyDecision.Suppress,
            "An out-of-range crafted weapon usage must be rejected before camera or network native code can read it.");
    }

    private static void ValidateWeaponRuntimeContractDoesNotAffectNonCoopTraffic()
    {
        CoopClientWeaponRuntimeSafetyResult result =
            CoopClientWeaponRuntimeSafetyContract.Evaluate(
                isCoopClientContext: false,
                snapshotReady: false,
                hasDeferredAgentBootstrap: false,
                agentExists: false,
                agentActive: false,
                requestTargetsWeaponSlot: true,
                requestedSlotOccupied: false,
                validateUsageIndex: true,
                usageCatalogReadable: false,
                requestedUsageIndex: 99,
                usageCount: 0);

        Assert(result.Decision == CoopClientWeaponRuntimeSafetyDecision.Allow,
            "The coop weapon safety contract must not intercept unrelated vanilla multiplayer traffic.");
    }

    private static void ValidateExactHeroAgentCapabilityFlagMapping()
    {
        Assert(
            CoopExactHeroAgentCapabilityFlagContract.ResolveDesiredFlagBits(new[] { "BowHorseMaster" }) ==
            CoopExactHeroAgentCapabilityFlagContract.CanUseAllBowsMountedBit,
            "BowHorseMaster must restore the exact hero's mounted bow capability flag.");
        Assert(
            CoopExactHeroAgentCapabilityFlagContract.ResolveDesiredFlagBits(new[] { "CrossbowMountedCrossbowman" }) ==
            CoopExactHeroAgentCapabilityFlagContract.CanReloadAllXBowsMountedBit,
            "CrossbowMountedCrossbowman must restore the exact hero's mounted crossbow reload capability flag.");
        Assert(
            CoopExactHeroAgentCapabilityFlagContract.ResolveDesiredFlagBits(new[] { "TwoHandedProjectileDeflection" }) ==
            CoopExactHeroAgentCapabilityFlagContract.CanDeflectArrowsWithTwoHandedWeaponBit,
            "TwoHandedProjectileDeflection must restore the exact hero's projectile deflection capability flag.");

        int expectedCombinedFlags =
            CoopExactHeroAgentCapabilityFlagContract.CanUseAllBowsMountedBit |
            CoopExactHeroAgentCapabilityFlagContract.CanReloadAllXBowsMountedBit |
            CoopExactHeroAgentCapabilityFlagContract.CanDeflectArrowsWithTwoHandedWeaponBit;
        int combinedFlags = CoopExactHeroAgentCapabilityFlagContract.ResolveDesiredFlagBits(
            new[]
            {
                "BowHorseMaster",
                "CrossbowMountedCrossbowman",
                "TwoHandedProjectileDeflection"
            });

        Assert(combinedFlags == expectedCombinedFlags,
            "The exact hero capability contract must combine all supported campaign perk flags.");
        Assert(
            CoopExactHeroAgentCapabilityFlagContract.ResolveDesiredFlagBits(
                new[] { "crossbowmountedcrossbowman" }) ==
            CoopExactHeroAgentCapabilityFlagContract.CanReloadAllXBowsMountedBit,
            "Exact hero capability perk matching must be case-insensitive.");
        Assert(
            CoopExactHeroAgentCapabilityFlagContract.ResolveDesiredFlagBits(new[] { "UnknownPerk" }) == 0,
            "Unknown campaign perks must not add agent capability flags.");
    }

    private static void ValidateExactHeroAgentCapabilityFlagsPreserveExistingFlags()
    {
        const int existingFlags = 0x8 | 0x200;
        int mergedFlags = CoopExactHeroAgentCapabilityFlagContract.MergeWithCurrentFlagBits(
            existingFlags,
            new[] { "CrossbowMountedCrossbowman" });

        Assert((mergedFlags & existingFlags) == existingFlags,
            "Applying an exact hero capability must preserve every existing agent flag.");
        Assert(
            (mergedFlags & CoopExactHeroAgentCapabilityFlagContract.CanReloadAllXBowsMountedBit) != 0,
            "Applying the mounted crossbow perk must add its missing agent flag.");
    }

    private static void ValidateClientExactHeroCapabilityApplyOrder()
    {
        string source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "Mission", "CoopMissionBehaviors.cs"));

        const string strictMethodSignature =
            "internal static bool TryApplyStrictExactHeroLocalInitialWield";
        int strictMethodStart = source.IndexOf(strictMethodSignature, StringComparison.Ordinal);
        int strictMethodEnd = source.IndexOf(
            "public static bool TryResolveExactDisplayNameForAgent",
            strictMethodStart,
            StringComparison.Ordinal);
        Assert(strictMethodStart >= 0 && strictMethodEnd > strictMethodStart,
            "The strict exact hero local initial wield method must remain available for source-contract validation.");

        string strictMethodBody = source.Substring(strictMethodStart, strictMethodEnd - strictMethodStart);
        int capabilityApplyIndex = strictMethodBody.IndexOf(
            "TryApplyExactCampaignHeroFlags(agent, entryState",
            StringComparison.Ordinal);
        int liveWeaponExitIndex = strictMethodBody.IndexOf(
            "server-authoritative-live-weapons",
            StringComparison.Ordinal);
        Assert(
            capabilityApplyIndex >= 0 &&
            liveWeaponExitIndex >= 0 &&
            capabilityApplyIndex < liveWeaponExitIndex,
            "Client exact hero capability flags must be applied before the live-weapon early exit.");

        const string clientProfileSignature =
            "private static string TryApplyClientLocalCombatProfile";
        int clientProfileStart = source.IndexOf(clientProfileSignature, StringComparison.Ordinal);
        int clientProfileEnd = source.IndexOf(
            "private static int TryAssignExactCampaignCommanders",
            clientProfileStart,
            StringComparison.Ordinal);
        Assert(clientProfileStart >= 0 && clientProfileEnd > clientProfileStart,
            "The client-local combat profile method must remain available for source-contract validation.");

        string clientProfileBody = source.Substring(
            clientProfileStart,
            clientProfileEnd - clientProfileStart);
        Assert(
            clientProfileBody.IndexOf(
                "TryApplyExactCampaignHeroFlags(agent, entryState",
                StringComparison.Ordinal) >= 0,
            "The client-local combat profile must restore exact campaign hero capability flags.");
    }

    private static void ValidateStrictExactHeroUsesAuthoritativeCreateAgentMissionEquipment()
    {
        string source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "Patches", "BattleMapSpawnHandoffPatch.cs"));

        const string strictAdapterSignature =
            "private static bool TryHandleStrictExactHeroCreateAgentViaContract";
        int strictAdapterStart = source.IndexOf(strictAdapterSignature, StringComparison.Ordinal);
        int strictAdapterEnd = source.IndexOf(
            "private static bool TryHandleExactSiegeTroopCreateAgentViaSnapshot",
            strictAdapterStart,
            StringComparison.Ordinal);
        Assert(strictAdapterStart >= 0 && strictAdapterEnd > strictAdapterStart,
            "The strict exact hero CreateAgent adapter must remain available for source-contract validation.");

        string strictAdapterBody = source.Substring(
            strictAdapterStart,
            strictAdapterEnd - strictAdapterStart);
        int authoritativeResolveIndex = strictAdapterBody.IndexOf(
            "TryResolveStrictExactHeroCreateAgentAuthoritativeMissionEquipment(",
            StringComparison.Ordinal);
        int missionEquipmentApplyIndex = strictAdapterBody.IndexOf(
            ".MissionEquipment(createTimeMissionEquipment)",
            StringComparison.Ordinal);
        Assert(
            authoritativeResolveIndex >= 0 &&
            missionEquipmentApplyIndex > authoritativeResolveIndex,
            "The strict exact hero adapter must resolve and apply the authoritative CreateAgent mission equipment.");

        const string resolverSignature =
            "private static bool TryResolveStrictExactHeroCreateAgentAuthoritativeMissionEquipment";
        int resolverStart = source.IndexOf(resolverSignature, StringComparison.Ordinal);
        int resolverEnd = source.IndexOf(
            "private static Banner ResolveStrictExactHeroCreateAgentBanner",
            resolverStart,
            StringComparison.Ordinal);
        Assert(resolverStart >= 0 && resolverEnd > resolverStart,
            "The authoritative mission-equipment resolver must remain available for source-contract validation.");

        string resolverBody = source.Substring(resolverStart, resolverEnd - resolverStart);
        Assert(
            resolverBody.IndexOf(
                "authoritativeMissionEquipment = createAgent?.MissionEquipment;",
                StringComparison.Ordinal) >= 0,
            "Mutable weapon runtime state must come directly from the server-authoritative CreateAgent payload.");
        Assert(
            resolverBody.IndexOf("return exactMissionEquipment", StringComparison.Ordinal) < 0 &&
            resolverBody.IndexOf("authoritativeMissionEquipment = expectedMissionEquipment", StringComparison.Ordinal) < 0,
            "The exact contract mission equipment must never replace the authoritative CreateAgent runtime state.");
    }

    private static void ValidateStrictExactHeroRejectsIncompatibleMissionEquipment()
    {
        string source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "Patches", "BattleMapSpawnHandoffPatch.cs"));

        const string resolverSignature =
            "private static bool TryResolveStrictExactHeroCreateAgentAuthoritativeMissionEquipment";
        int resolverStart = source.IndexOf(resolverSignature, StringComparison.Ordinal);
        int resolverEnd = source.IndexOf(
            "private static Banner ResolveStrictExactHeroCreateAgentBanner",
            resolverStart,
            StringComparison.Ordinal);
        Assert(resolverStart >= 0 && resolverEnd > resolverStart,
            "The authoritative mission-equipment resolver must remain available for compatibility validation.");

        string resolverBody = source.Substring(resolverStart, resolverEnd - resolverStart);
        Assert(
            resolverBody.IndexOf("weapon-slot-presence-mismatch", StringComparison.Ordinal) >= 0 &&
            resolverBody.IndexOf("weapon-slot-item-mismatch", StringComparison.Ordinal) >= 0 &&
            resolverBody.IndexOf("weapon-slot-modifier-mismatch", StringComparison.Ordinal) >= 0 &&
            resolverBody.IndexOf("weapon-slot-usage-mismatch", StringComparison.Ordinal) >= 0,
            "The strict exact adapter must reject slot, item, modifier, and usage mismatches before mixing sources.");
        Assert(
            resolverBody.IndexOf("authoritativeMissionEquipment = null;", StringComparison.Ordinal) >= 0,
            "An incompatible CreateAgent payload must fail closed instead of exposing mixed weapon state.");
    }

    private static void ValidatePossessionDoesNotInjectWeaponRuntimeState()
    {
        string source = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "Mission", "CoopMissionBehaviors.cs"));

        Assert(
            source.IndexOf(
                "TrySendExactHeroPossessionCrossbowRuntimeStateToPeer",
                StringComparison.Ordinal) < 0,
            "Possession must not use a custom crossbow runtime-state synchronization helper.");

        AssertPossessionPathDoesNotInjectWeaponRuntimeState(
            source,
            "private static bool TryReplaceMaterializedBotWithPlayer",
            "private static string ArmMaterializedPossessionProtection",
            "The generic materialized possession path");
        AssertPossessionPathDoesNotInjectWeaponRuntimeState(
            source,
            "private static bool TryBindMaterializedBotWithPlayerForSiegeRespawn",
            "private static RosterEntryState ResolveMaterializedEntryStateForPossessedAgent",
            "The manual siege possession path");
        AssertPossessionPathDoesNotInjectWeaponRuntimeState(
            source,
            "private static bool TryReclaimPeerAgentControlFromAi",
            "private static void TryRollbackFailedAgentReclaim",
            "The AI reclaim possession path");
    }

    private static void AssertPossessionPathDoesNotInjectWeaponRuntimeState(
        string source,
        string methodSignature,
        string followingMethodSignature,
        string pathName)
    {
        int methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        int methodEnd = source.IndexOf(
            followingMethodSignature,
            methodStart,
            StringComparison.Ordinal);
        Assert(methodStart >= 0 && methodEnd > methodStart,
            pathName + " must remain available for source-contract validation.");

        string methodBody = source.Substring(methodStart, methodEnd - methodStart);
        Assert(
            methodBody.IndexOf("SetWeaponNetworkData", StringComparison.Ordinal) < 0 &&
            methodBody.IndexOf("SetWeaponAmmoData", StringComparison.Ordinal) < 0 &&
            methodBody.IndexOf("SetWeaponReloadPhase", StringComparison.Ordinal) < 0,
            pathName + " must preserve Bannerlord's authoritative weapon state without manual injection.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
