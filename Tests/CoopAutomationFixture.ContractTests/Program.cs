using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CoopSpectator.Infrastructure.Automation;
using CoopSpectator.Network.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static readonly string[] AutomationVariables =
    {
        CoopAutomationRuntimeBridge.TestAutomationVariable,
        CoopAutomationRuntimeBridge.RunIdVariable,
        CoopAutomationRuntimeBridge.RunRootVariable,
        CoopAutomationRuntimeBridge.RunTokenVariable,
        CoopAutomationRuntimeBridge.ExpectedModuleSha256Variable,
        CoopAutomationRuntimeBridge.ResultPolicyVariable,
        CoopAutomationRuntimeBridge.FixtureRecordVariable,
        CoopAutomationRuntimeBridge.FixtureIdVariable,
        CoopAutomationRuntimeBridge.SourceRevisionVariable,
        CoopAutomationRuntimeBridge.GameVersionVariable
    };

    private static int Main()
    {
        var previousEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < AutomationVariables.Length; i++)
            previousEnvironment[AutomationVariables[i]] = Environment.GetEnvironmentVariable(AutomationVariables[i]);

        string repositoryRoot = ResolveRepositoryRoot();
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "CoopSpectator",
            "ContractTests",
            "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        try
        {
            ValidateSourceIntegration(repositoryRoot);
            ValidateCommittedSanitizedFixture(repositoryRoot);
            ValidateFieldAdmissionAcrossScenarioTypes();
            ValidateDisabledRecorderDoesNoIo(testRoot);
            ValidateExactRecordingAndRejectionContracts(testRoot);
            Console.WriteLine("Coop automation exact field-fixture contract tests passed.");
            return 0;
        }
        finally
        {
            foreach (KeyValuePair<string, string> entry in previousEnvironment)
                Environment.SetEnvironmentVariable(entry.Key, entry.Value, EnvironmentVariableTarget.Process);
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void ValidateSourceIntegration(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "Campaign", "BattleRosterFile.cs");
        string source = File.ReadAllText(path);
        int serialize = source.IndexOf("JsonConvert.SerializeObject(dto, Formatting.Indented)", StringComparison.Ordinal);
        int productionWrite = source.IndexOf("File.WriteAllText(path, json)", serialize, StringComparison.Ordinal);
        int recorder = source.IndexOf("CoopAutomationFixtureRecorder.TryRecordCampaignRoster", productionWrite, StringComparison.Ordinal);
        Assert(serialize >= 0, "The existing campaign-roster serializer must remain present.");
        Assert(productionWrite > serialize, "The existing File.WriteAllText production boundary must remain present.");
        Assert(recorder > productionWrite, "Exact recording must occur only after the production roster write.");
        Assert(
            !source.Substring(serialize, recorder - serialize).Contains("WriteAllBytes", StringComparison.Ordinal),
            "The production serializer boundary must not be replaced by recorder-specific byte serialization.");
    }

    private static void ValidateCommittedSanitizedFixture(string repositoryRoot)
    {
        const string expectedPrivatePayloadSha256 = "ECF29661E44B64C1AEE77EC2B44E61F63926287A3A05A9BFD6DC545EC073B9C7";
        const string canonicalHeroBodyProperties = "<BodyProperties version=\"4\" age=\"20\" weight=\"0\" build=\"0\" key=\"00000000000000000000000000000000\" />";
        var canonicalBannerCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "11.163.166.1528.1528.764.764.1.0.0.133.171.171.483.483.764.764.0.0.0",
            "35.116.116.1528.1528.766.740.1.0.0.510.19.171.1528.353.758.658.0.0.0.510.19.171.1528.398.760.845.0.0.0"
        };

        string fixtureRoot = Path.Combine(repositoryRoot, "Tests", "Fixtures", "Automation", "field-current");
        string payloadPath = Path.Combine(fixtureRoot, "battle_roster.sanitized.json");
        string metadataPath = Path.Combine(fixtureRoot, "fixture.sanitized.metadata.json");
        string oraclePath = Path.Combine(fixtureRoot, "fixture.oracle.json");
        string[] expectedFiles =
        {
            Path.GetFileName(payloadPath),
            Path.GetFileName(metadataPath),
            Path.GetFileName(oraclePath)
        };

        Assert(Directory.Exists(fixtureRoot), "The reviewed sanitized field fixture directory must exist.");
        string[] committedFiles = Directory.GetFiles(fixtureRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert(
            committedFiles.SequenceEqual(expectedFiles.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal),
            "Only the sanitized payload, sanitized metadata, and independent oracle may be committed.");
        Assert(
            !File.Exists(Path.Combine(fixtureRoot, CoopAutomationFixtureContract.RawPayloadFileName)) &&
            !File.Exists(Path.Combine(fixtureRoot, CoopAutomationFixtureContract.MetadataFileName)),
            "Raw private capture files must never be committed with the sanitized fixture.");

        byte[] payloadBytes = File.ReadAllBytes(payloadPath);
        byte[] metadataBytes = File.ReadAllBytes(metadataPath);
        byte[] oracleBytes = File.ReadAllBytes(oraclePath);
        string payloadText = new UTF8Encoding(false, true).GetString(payloadBytes);
        string metadataText = new UTF8Encoding(false, true).GetString(metadataBytes);
        string oracleText = new UTF8Encoding(false, true).GetString(oracleBytes);
        Assert(!HasUtf8Bom(payloadBytes) && !HasUtf8Bom(metadataBytes) && !HasUtf8Bom(oracleBytes), "All committed fixture files must remain UTF-8 without a BOM.");
        Assert(
            !payloadText.Contains("\r", StringComparison.Ordinal) &&
            !metadataText.Contains("\r", StringComparison.Ordinal) &&
            !oracleText.Contains("\r", StringComparison.Ordinal),
            "All committed fixture files must use LF line endings only.");

        JObject payload = JObject.Parse(payloadText);
        JObject metadata = JObject.Parse(metadataText);
        JObject oracle = JObject.Parse(oracleText);
        JObject sourceReview = RequireObject(oracle, "SourceReview");
        JObject oraclePayload = RequireObject(oracle, "Payload");
        JObject expected = RequireObject(oracle, "Expected");
        JObject expectedEquipment = RequireObject(expected, "Equipment");
        JObject evidenceBoundary = RequireObject(oracle, "EvidenceBoundary");

        string payloadSha256 = ComputeSha256(payloadBytes);
        Assert(oracle.Value<int>("SchemaVersion") == 1, "The independent oracle schema must remain explicit and supported.");
        Assert(oracle.Value<string>("FixtureId") == metadata.Value<string>("FixtureId"), "Sanitized metadata and the independent oracle must identify the same derivative.");
        Assert(oracle.Value<string>("OracleBasis") == "IndependentReadOnlyJsonAuditV1", "The oracle must retain its independent audit basis.");
        Assert(!oracle.Value<bool>("RecorderQualificationUsedAsOracle"), "Recorder qualification must never become the independent oracle.");
        Assert(oraclePayload.Value<string>("File") == Path.GetFileName(payloadPath), "The independent oracle must name only the sanitized payload.");
        Assert(payloadSha256 == metadata.Value<string>("PayloadSha256"), "Sanitized metadata must pin the payload SHA-256.");
        Assert(payloadSha256 == oraclePayload.Value<string>("Sha256"), "The independent oracle must pin the payload SHA-256.");
        Assert(payloadBytes.LongLength == metadata.Value<long>("PayloadLength"), "Sanitized metadata must pin the payload length.");
        Assert(payloadBytes.LongLength == oraclePayload.Value<long>("Length"), "The independent oracle must pin the payload length.");
        Assert(metadata.Value<string>("PayloadFile") == Path.GetFileName(payloadPath), "Sanitized metadata must name only the sanitized payload.");
        Assert(metadata.Value<string>("OracleFile") == Path.GetFileName(oraclePath), "Sanitized metadata must name the independent oracle.");
        Assert(metadata.Value<string>("SanitizationStatus") == "ReviewedSanitizedDerivative", "The derivative must retain its privacy-review status.");
        Assert(metadata.Value<string>("PrivacyReviewStatus") == "NoRawCampaignAccountPathOrCredentialValues", "The derivative must retain the explicit privacy boundary.");
        Assert(metadata.Value<string>("IndependentOracleStatus") == "IndependentAuditRequired", "The sanitizer must not claim to generate its own independent oracle.");
        Assert(metadata.Value<string>("SourcePrivatePayloadSha256") == expectedPrivatePayloadSha256, "Sanitized metadata must pin the independently reviewed private source hash.");
        Assert(sourceReview.Value<string>("PrivatePayloadSha256") == expectedPrivatePayloadSha256, "The oracle must pin the independently reviewed private source hash.");
        Assert(metadata.Value<long>("SourcePrivatePayloadLength") == sourceReview.Value<long>("PrivatePayloadLength"), "Sanitized metadata and the independent oracle must agree on the private source length.");
        Assert(!sourceReview.Value<bool>("RawPayloadCommitted"), "The oracle must explicitly reject committing the private source payload.");
        Assert(!metadata.Value<bool>("FullBattleCompleted") && !metadata.Value<bool>("L2OrL3PassClaimed"), "A pre-mission fixture must not claim full-battle or higher-level evidence.");
        Assert(evidenceBoundary.Value<string>("BattleStage") == "PreMissionCampaignRoster", "The oracle must retain the pre-mission evidence boundary.");
        Assert(!evidenceBoundary.Value<bool>("FullBattleCompleted") && !evidenceBoundary.Value<bool>("L2OrL3PassClaimed"), "The oracle must not promote the fixture beyond its evidence level.");

        string combinedShareableText = payloadText + "\n" + metadataText + "\n" + oracleText;
        var forbiddenPatterns = new[]
        {
            @"(?i)(?:[A-Z]:[\\/]|\\\\[^\\])",
            @"(?i)(?:^|[\\/])Users[\\/][^\\/]+",
            @"(?i)[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
            @"(?<!\d)7656\d{13}(?!\d)",
            @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+",
            @"COOPSPECTATOR_(?:AUTOMATION_RUN_TOKEN|SERVER_PASSWORD)"
        };
        for (int i = 0; i < forbiddenPatterns.Length; i++)
            Assert(!Regex.IsMatch(combinedShareableText, forbiddenPatterns[i]), "Committed fixture content must not contain path, account, or credential patterns; pattern index=" + i + ".");

        Assert(
            payload.Properties().Select(property => property.Name).SequenceEqual(new[] { "TroopIds", "Snapshot" }, StringComparer.Ordinal),
            "The committed payload must retain the exact BattleRosterFileDto top-level schema.");
        JArray topLevelTroopIds = RequireArray(payload, "TroopIds");
        JObject snapshot = RequireObject(payload, "Snapshot");
        JObject scenario = RequireObject(snapshot, "ScenarioContext");
        List<JObject> sides = RequireArray(snapshot, "Sides").Values<JObject>().ToList();
        List<JObject> parties = sides.SelectMany(side => RequireArray(side, "Parties").Values<JObject>()).ToList();
        List<JObject> sideTroops = sides.SelectMany(side => RequireArray(side, "Troops").Values<JObject>()).ToList();
        List<JObject> positiveTroops = sideTroops.Where(troop => troop.Value<int>("Count") > 0).ToList();
        List<JObject> partyTroops = parties.SelectMany(party => RequireArray(party, "Troops").Values<JObject>()).ToList();

        Assert(snapshot.Value<string>("CampaignId") == "fixture-campaign-001", "The campaign identity must be deterministic and sanitized.");
        Assert(snapshot.Value<string>("BattleId") == "fixture-battle-001", "The battle identity must be deterministic and sanitized.");
        Assert(snapshot.Value<string>("BattleInstanceId") == "fixture-battle-instance-001", "The battle-instance identity must be deterministic and sanitized.");
        Assert(scenario.Value<string>("ScenarioKind") == expected.Value<string>("ScenarioKind"), "The scenario kind must match the independent oracle.");
        Assert(scenario.Value<string>("CampaignBattleType") == expected.Value<string>("CampaignBattleType"), "The campaign battle type must match the independent oracle.");
        Assert(scenario.Value<bool>("IsSiegeBattle") == expected.Value<bool>("IsSiegeBattle"), "The siege flag must match the independent oracle.");
        Assert(sides.Count == expected.Value<int>("SideCount"), "The side count must match the independent oracle.");
        Assert(parties.Count == expected.Value<int>("PartyCount"), "The party count must match the independent oracle.");
        Assert(positiveTroops.Count == expected.Value<int>("PositiveStackCount"), "The positive stack count must match the independent oracle.");
        Assert(positiveTroops.Sum(troop => troop.Value<int>("Count")) == expected.Value<int>("PositiveUnitCount"), "The unit count must match the independent oracle.");
        Assert(positiveTroops.Sum(troop => troop.Value<int>("WoundedCount")) == expected.Value<int>("WoundedUnitCount"), "The wounded count must match the independent oracle.");
        Assert(positiveTroops.Count(troop => !troop.Value<bool>("IsMounted")) == expected.Value<int>("UnmountedStackCount"), "The unmounted composition must match the independent oracle.");
        Assert(positiveTroops.Count(troop => troop.Value<bool>("IsMounted")) == expected.Value<int>("MountedStackCount"), "The mounted composition must match the independent oracle.");
        Assert(positiveTroops.Count(troop => troop.Value<bool>("IsRanged")) == expected.Value<int>("RangedStackCount"), "The ranged composition must match the independent oracle.");
        Assert(positiveTroops.Count(troop => troop.Value<bool>("IsHero")) == expected.Value<int>("HeroStackCount"), "The hero composition must match the independent oracle.");
        Assert(positiveTroops.Count(troop => troop.Value<bool>("IsPlayerCharacter")) == expected.Value<int>("PlayerCharacterStackCount"), "The player-character count must match the independent oracle.");
        Assert(positiveTroops.Count(troop => troop.Value<bool>("IsPlayerClanHero")) == expected.Value<int>("PlayerClanHeroStackCount"), "The player-clan hero count must match the independent oracle.");
        Assert(topLevelTroopIds.Count == expected.Value<int>("TopLevelTroopIdCount"), "The selectable top-level troop count must match the independent oracle.");
        Assert(topLevelTroopIds.Values<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count() == expected.Value<int>("TopLevelDistinctTroopIdCount"), "The distinct selectable troop count must match the independent oracle.");
        Assert(RequireArray(snapshot, "CraftedWeapons").Count == expected.Value<int>("CraftedWeaponCount"), "The crafted-weapon count must match the independent oracle.");
        Assert(RequireArray(snapshot, "FrozenCaptainEntryIds").Count == expected.Value<int>("FrozenCaptainEntryCount"), "The frozen-captain entry count must match the independent oracle.");
        Assert(RequireArray(snapshot, "FrozenCaptainCombatGroups").Count == expected.Value<int>("FrozenCaptainCombatGroupCount"), "The frozen captain-group count must match the independent oracle.");

        var sideIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        int referentialFailures = 0;
        foreach (JObject side in sides)
        {
            string sideId = side.Value<string>("SideId");
            if (!Regex.IsMatch(sideId ?? string.Empty, @"^fixture-side-\d{3}$") || !sideIds.Add(sideId))
                referentialFailures++;
            Assert(side.Value<string>("SideText").StartsWith("Fixture ", StringComparison.Ordinal), "Side display text must be sanitized.");
            Assert(side.Value<string>("AppearanceSource") == "CoopFieldFixtureSanitizationV1", "Side appearance provenance must identify the sanitization policy.");
            Assert(canonicalBannerCodes.Contains(side.Value<string>("BannerCode")), "Side banners must use reviewed canonical fixture values.");

            List<JObject> sideParties = RequireArray(side, "Parties").Values<JObject>().ToList();
            var sidePartyIds = new HashSet<string>(sideParties.Select(party => party.Value<string>("PartyId")), StringComparer.OrdinalIgnoreCase);
            if (!sidePartyIds.Contains(side.Value<string>("LeaderPartyId")))
                referentialFailures++;
            foreach (JObject party in sideParties)
            {
                string partyId = party.Value<string>("PartyId");
                if (!Regex.IsMatch(partyId ?? string.Empty, @"^fixture-party-\d{3}$") || !partyIds.Add(partyId))
                    referentialFailures++;
                Assert(party.Value<string>("PartyName").StartsWith("Fixture Party ", StringComparison.Ordinal), "Party display names must be sanitized.");
                Assert(Regex.IsMatch(party.Value<string>("CombatGroupId") ?? string.Empty, @"^fixture-combat-group-\d{3}$"), "Combat-group identities must be sanitized.");
                List<JObject> currentPartyTroops = RequireArray(party, "Troops").Values<JObject>().Where(troop => troop.Value<int>("Count") > 0).ToList();
                if (currentPartyTroops.Sum(troop => troop.Value<int>("Count")) != party.Value<int>("TotalManCount"))
                    referentialFailures++;
                foreach (JObject troop in currentPartyTroops)
                {
                    if (!string.Equals(troop.Value<string>("SideId"), sideId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(troop.Value<string>("PartyId"), partyId, StringComparison.OrdinalIgnoreCase))
                    {
                        referentialFailures++;
                    }
                }
            }

            List<JObject> currentSideTroops = RequireArray(side, "Troops").Values<JObject>().Where(troop => troop.Value<int>("Count") > 0).ToList();
            foreach (JObject troop in currentSideTroops)
            {
                string entryId = troop.Value<string>("EntryId");
                if (!Regex.IsMatch(entryId ?? string.Empty, @"^fixture-entry-\d{3}$") || entries.ContainsKey(entryId))
                    referentialFailures++;
                else
                    entries.Add(entryId, troop);
                if (!string.Equals(troop.Value<string>("SideId"), sideId, StringComparison.OrdinalIgnoreCase) ||
                    !sidePartyIds.Contains(troop.Value<string>("PartyId")))
                {
                    referentialFailures++;
                }
                Assert(troop.Value<string>("TroopName").StartsWith("Fixture Troop ", StringComparison.Ordinal), "Troop display names must be sanitized.");
                Assert(IsEmptyOrFixtureIdentity(troop.Value<string>("HeroId"), "fixture-hero-"), "Hero identities must be sanitized.");
                Assert(IsEmptyOrFixtureIdentity(troop.Value<string>("HeroClanId"), "fixture-clan-"), "Clan identities must be sanitized.");
                string bodyProperties = troop.Value<string>("HeroBodyProperties");
                Assert(string.IsNullOrWhiteSpace(bodyProperties) || bodyProperties == canonicalHeroBodyProperties, "Hero body properties must use the reviewed canonical value.");
            }

            JArray missionOrder = RequireArray(side, "MissionReadyEntryOrder");
            if (missionOrder.Count != side.Value<int>("TotalManCount") ||
                sideParties.Sum(party => party.Value<int>("TotalManCount")) != side.Value<int>("TotalManCount"))
            {
                referentialFailures++;
            }
            var missionCounts = missionOrder.Values<string>()
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (JObject troop in currentSideTroops)
            {
                if (!missionCounts.TryGetValue(troop.Value<string>("EntryId"), out int missionCount) ||
                    missionCount != troop.Value<int>("Count"))
                {
                    referentialFailures++;
                }
            }

            JObject expectedSide = RequireArray(expected, "Sides").Values<JObject>()
                .Single(candidate => candidate.Value<bool>("IsPlayerSide") == side.Value<bool>("IsPlayerSide"));
            Assert(sideParties.Count == expectedSide.Value<int>("PartyCount"), "Per-side party counts must match the independent oracle.");
            Assert(currentSideTroops.Count == expectedSide.Value<int>("PositiveStackCount"), "Per-side stack counts must match the independent oracle.");
            Assert(currentSideTroops.Sum(troop => troop.Value<int>("Count")) == expectedSide.Value<int>("PositiveUnitCount"), "Per-side unit counts must match the independent oracle.");
            Assert(missionOrder.Count == expectedSide.Value<int>("MissionReadyEntryCount"), "Per-side mission order counts must match the independent oracle.");
        }

        Assert(partyTroops.Count(troop => troop.Value<int>("Count") > 0) == positiveTroops.Count, "Side and party projections must contain the same number of positive stacks.");
        foreach (JObject partyTroop in partyTroops.Where(troop => troop.Value<int>("Count") > 0))
        {
            if (!entries.TryGetValue(partyTroop.Value<string>("EntryId"), out JObject sideTroop) || !JToken.DeepEquals(sideTroop, partyTroop))
                referentialFailures++;
        }
        Assert(referentialFailures == expected.Value<int>("ReferentialFailureCount"), "All committed fixture references and duplicate projections must match the independent oracle.");

        Assert(positiveTroops.Count(HasAnyWeapon) == expectedEquipment.Value<int>("AnyWeaponStackCount"), "Weapon-bearing stack count must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatItem0Id")) == expectedEquipment.Value<int>("WeaponSlot0StackCount"), "Weapon slot 0 must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatItem1Id")) == expectedEquipment.Value<int>("WeaponSlot1StackCount"), "Weapon slot 1 must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatItem2Id")) == expectedEquipment.Value<int>("WeaponSlot2StackCount"), "Weapon slot 2 must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatItem3Id")) == expectedEquipment.Value<int>("WeaponSlot3StackCount"), "Weapon slot 3 must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatHeadId")) == expectedEquipment.Value<int>("HeadArmorStackCount"), "Head armor must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatBodyId")) == expectedEquipment.Value<int>("BodyArmorStackCount"), "Body armor must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatLegId")) == expectedEquipment.Value<int>("LegArmorStackCount"), "Leg armor must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatGlovesId")) == expectedEquipment.Value<int>("GlovesStackCount"), "Gloves must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatCapeId")) == expectedEquipment.Value<int>("CapeStackCount"), "Capes must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatHorseId")) == expectedEquipment.Value<int>("HorseStackCount"), "Horses must match the independent oracle.");
        Assert(positiveTroops.Count(troop => HasText(troop, "CombatHorseHarnessId")) == expectedEquipment.Value<int>("HarnessStackCount"), "Harnesses must match the independent oracle.");
        Assert(positiveTroops.Count(troop => troop.Value<bool>("IsMounted") && !HasText(troop, "CombatHorseId")) == expectedEquipment.Value<int>("MountedWithoutHorseCount"), "Mounted stacks must retain horses.");
        Assert(positiveTroops.Count(troop => !troop.Value<bool>("IsMounted") && HasText(troop, "CombatHorseId")) == expectedEquipment.Value<int>("UnmountedWithHorseCount"), "Unmounted stacks must not gain horses.");

        CommittedBattleRosterFile committed = JsonConvert.DeserializeObject<CommittedBattleRosterFile>(payloadText);
        Assert(committed?.Snapshot != null, "The committed fixture must deserialize through the production message schema.");
        Assert(
            CoopAutomationFixtureContract.TryQualifyFirstFieldSlice(
                committed.Snapshot,
                out CoopAutomationFieldFixtureQualification qualification,
                out string failureCode,
                out string failureMessage),
            "The committed sanitized field fixture must satisfy the production admission contract: " + failureCode + " " + failureMessage);
        Assert(qualification.SideCount == expected.Value<int>("SideCount"), "Production qualification must retain the oracle side count.");

        committed.Snapshot.ScenarioContext = new BattleScenarioContextMessage
        {
            ScenarioKind = "Siege",
            CampaignBattleType = "Siege",
            IsSiegeBattle = true
        };
        Assert(
            !CoopAutomationFixtureContract.TryQualifyFirstFieldSlice(committed.Snapshot, out _, out failureCode, out _) &&
            failureCode == "ScenarioNotFirstFieldSlice",
            "The committed fixture must remain rejected when mutated into another battle type.");

        string sanitizerPath = Path.Combine(repositoryRoot, "scripts", "New-CoopSanitizedFieldFixture.ps1");
        string sanitizerSource = File.ReadAllText(sanitizerPath);
        Assert(sanitizerSource.Contains(expectedPrivatePayloadSha256, StringComparison.Ordinal), "The sanitizer must pin the independently reviewed private payload hash.");
        Assert(sanitizerSource.Contains("AllowRepositoryOutput", StringComparison.Ordinal), "Repository output must remain behind an explicit sanitizer switch.");
        Assert(sanitizerSource.Contains("The sanitized output directory must be absent or empty.", StringComparison.Ordinal), "The sanitizer must refuse nonempty output directories.");
        Assert(sanitizerSource.Contains("RawPayloadCopied = $false", StringComparison.Ordinal), "The sanitizer result must explicitly deny copying the raw payload.");
        Assert(sanitizerSource.Contains("LogsCopied = $false", StringComparison.Ordinal), "The sanitizer result must explicitly deny copying run logs.");
    }

    private static void ValidateFieldAdmissionAcrossScenarioTypes()
    {
        BattleSnapshotMessage eligible = CreateEligibleFieldSnapshot();
        Assert(
            CoopAutomationFixtureContract.TryQualifyFirstFieldSlice(
                eligible,
                out CoopAutomationFieldFixtureQualification qualification,
                out string failureCode,
                out string failureMessage),
            "The exact mixed field slice must qualify: " + failureCode + " " + failureMessage);
        Assert(qualification.SideCount == 2, "The field qualification must retain two populated sides.");
        Assert(qualification.InfantryStackCount == 1, "The field qualification must retain the infantry stack.");
        Assert(qualification.MountedStackCount == 1, "The field qualification must retain the mounted stack.");
        Assert(qualification.HeroOrCaptainStackCount == 1, "The field qualification must retain the hero/captain evidence.");

        var rejectedScenarios = new[]
        {
            new BattleScenarioContextMessage { ScenarioKind = "VillageBattle", CampaignBattleType = "VillageBattle", IsSiegeBattle = false },
            new BattleScenarioContextMessage { ScenarioKind = "Siege", CampaignBattleType = "Siege", IsSiegeBattle = true },
            new BattleScenarioContextMessage { ScenarioKind = "SallyOut", CampaignBattleType = "SallyOut", IsSiegeBattle = true },
            new BattleScenarioContextMessage { ScenarioKind = "SiegeAmbush", CampaignBattleType = "SiegeAmbush", IsSiegeBattle = true },
            new BattleScenarioContextMessage { ScenarioKind = "FieldBattle", CampaignBattleType = "SiegeOutside", IsSiegeBattle = false },
            new BattleScenarioContextMessage { ScenarioKind = "LordsHall", CampaignBattleType = "Siege", IsSiegeBattle = true },
            new BattleScenarioContextMessage { ScenarioKind = "Hideout", CampaignBattleType = "Hideout", IsSiegeBattle = false },
            new BattleScenarioContextMessage { ScenarioKind = "HideoutAmbush", CampaignBattleType = "Hideout", IsSiegeBattle = false }
        };
        for (int i = 0; i < rejectedScenarios.Length; i++)
        {
            BattleSnapshotMessage rejected = CreateEligibleFieldSnapshot();
            rejected.ScenarioContext = rejectedScenarios[i];
            Assert(
                !CoopAutomationFixtureContract.TryQualifyFirstFieldSlice(
                    rejected,
                    out _,
                    out failureCode,
                    out _),
                "Only the ordinary SCN-001 field scenario may qualify; rejected scenario index=" + i + ".");
            Assert(failureCode == "ScenarioNotFirstFieldSlice", "Other battle types must have the stable scenario rejection code.");
        }

        BattleSnapshotMessage noCavalry = CreateEligibleFieldSnapshot();
        noCavalry.Sides[1].Troops[0].IsMounted = false;
        Assert(
            !CoopAutomationFixtureContract.TryQualifyFirstFieldSlice(noCavalry, out _, out failureCode, out _) &&
            failureCode == "FieldCavalryMissing",
            "A field sample without cavalry must be rejected.");

        BattleSnapshotMessage noHero = CreateEligibleFieldSnapshot();
        TroopStackInfo hero = noHero.Sides[0].Troops[0];
        hero.IsHero = false;
        hero.HeroId = string.Empty;
        hero.HeroRole = string.Empty;
        Assert(
            !CoopAutomationFixtureContract.TryQualifyFirstFieldSlice(noHero, out _, out failureCode, out _) &&
            failureCode == "FieldHeroOrCaptainMissing",
            "A field sample without a hero or captain must be rejected.");
    }

    private static void ValidateDisabledRecorderDoesNoIo(string testRoot)
    {
        ClearAutomationEnvironment();
        string nonexistentRoster = Path.Combine(testRoot, "disabled", "battle_roster.json");
        Assert(
            CoopAutomationFixtureRecorder.TryRecordCampaignRoster(
                nonexistentRoster,
                CreateEligibleFieldSnapshot(),
                out string failureCode,
                out string failureMessage),
            "Disabled recording must return without inspecting the roster path: " + failureCode + " " + failureMessage);
        Assert(!Directory.Exists(Path.GetDirectoryName(nonexistentRoster)), "Disabled recording must perform no file-system writes.");
    }

    private static void ValidateExactRecordingAndRejectionContracts(string testRoot)
    {
        string runId = "m3a-fixture-contract-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        string runRoot = Path.Combine(Path.GetTempPath(), "CoopSpectator", "Automation", runId);
        try
        {
            Directory.CreateDirectory(runRoot);
            string sourceRoot = Path.Combine(testRoot, "source");
            Directory.CreateDirectory(sourceRoot);
            string rosterPath = Path.Combine(sourceRoot, "battle_roster.json");
            byte[] exactBytes = new UTF8Encoding(false).GetBytes("{\r\n  \"ExactBoundary\": \"preserve-crlf-and-spacing\"\r\n}");
            File.WriteAllBytes(rosterPath, exactBytes);

            string modulePath = Assembly.GetExecutingAssembly().Location;
            string moduleHash = CoopAutomationRuntimeContract.ComputeFileSha256(modulePath);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.TestAutomationVariable, "1");
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.RunIdVariable, runId);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.RunRootVariable, runRoot);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.RunTokenVariable, new string('T', 64));
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.ExpectedModuleSha256Variable, moduleHash);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.ResultPolicyVariable, CoopAutomationRuntimeContract.SuppressResultPolicy);
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.FixtureRecordVariable, "1");
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.FixtureIdVariable, "field-current-mixed-v1");
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.SourceRevisionVariable, new string('A', 40));
            Environment.SetEnvironmentVariable(CoopAutomationRuntimeBridge.GameVersionVariable, "e1.3.14");

            Assert(
                CoopAutomationFixtureRecorder.TryRecordCampaignRoster(
                    rosterPath,
                    CreateEligibleFieldSnapshot(),
                    out string failureCode,
                    out string failureMessage),
                "The exact current field fixture must record: " + failureCode + " " + failureMessage);

            string fixtureRoot = Path.Combine(runRoot, "artifacts", "fixtures", "field-current");
            string recordedPayloadPath = Path.Combine(fixtureRoot, CoopAutomationFixtureContract.RawPayloadFileName);
            string metadataPath = Path.Combine(fixtureRoot, CoopAutomationFixtureContract.MetadataFileName);
            string statusPath = Path.Combine(runRoot, "state", "fixture-record.status.json");
            Assert(File.Exists(recordedPayloadPath), "The exact raw payload must be retained.");
            Assert(File.Exists(metadataPath), "The fixture metadata must be retained.");
            Assert(File.Exists(statusPath), "The fixture recording status must be retained.");
            byte[] recordedBytes = File.ReadAllBytes(recordedPayloadPath);
            Assert(ByteArraysEqual(exactBytes, recordedBytes), "Recorded fixture bytes must exactly match the post-write campaign roster bytes.");

            byte[] metadataBytes = File.ReadAllBytes(metadataPath);
            Assert(
                CoopAutomationFixtureRecorder.TryRecordCampaignRoster(
                    rosterPath,
                    CreateEligibleFieldSnapshot(),
                    out failureCode,
                    out failureMessage),
                "An identical repeated capture must remain idempotent: " + failureCode + " " + failureMessage);
            Assert(ByteArraysEqual(metadataBytes, File.ReadAllBytes(metadataPath)), "Repeated capture must not rewrite immutable metadata.");

            CoopAutomationFixtureMetadata metadata = JsonConvert.DeserializeObject<CoopAutomationFixtureMetadata>(File.ReadAllText(metadataPath));
            Assert(
                CoopAutomationFixtureContract.TryValidateRecordedPayload(
                    metadata,
                    recordedBytes,
                    out failureCode,
                    out failureMessage),
                "The immutable fixture and provenance must validate: " + failureCode + " " + failureMessage);

            byte[] corrupted = (byte[])recordedBytes.Clone();
            corrupted[corrupted.Length - 1] ^= 0x01;
            Assert(
                !CoopAutomationFixtureContract.TryValidateRecordedPayload(metadata, corrupted, out failureCode, out _) &&
                failureCode == "PayloadHashMismatch",
                "A same-length byte corruption must have the stable payload-hash rejection code.");

            long originalLength = metadata.PayloadLength;
            metadata.PayloadLength++;
            Assert(
                !CoopAutomationFixtureContract.TryValidateRecordedPayload(metadata, recordedBytes, out failureCode, out _) &&
                failureCode == "PayloadLengthMismatch",
                "A payload length mismatch must be rejected before replay.");
            metadata.PayloadLength = originalLength;

            int originalSchema = metadata.SchemaVersion;
            metadata.SchemaVersion = originalSchema + 1;
            Assert(
                !CoopAutomationFixtureContract.TryValidateRecordedPayload(metadata, recordedBytes, out failureCode, out _) &&
                failureCode == "FixtureSchemaUnsupported",
                "An unsupported fixture schema must be rejected.");
            metadata.SchemaVersion = originalSchema;

            string originalPayloadSchema = metadata.PayloadSchema;
            metadata.PayloadSchema = "BattleRosterFileDto.FutureV999";
            Assert(
                !CoopAutomationFixtureContract.TryValidateRecordedPayload(metadata, recordedBytes, out failureCode, out _) &&
                failureCode == "PayloadSchemaUnsupported",
                "An unsupported payload schema must be rejected.");
            metadata.PayloadSchema = originalPayloadSchema;

            Assert(
                !CoopAutomationFixtureContract.TryCombineUnderRoot(
                    fixtureRoot,
                    "..\\escaped.json",
                    out _,
                    out failureCode,
                    out _) &&
                failureCode == "FixturePathEscapesRoot",
                "A fixture path must not escape its run-scoped root.");
        }
        finally
        {
            if (Directory.Exists(runRoot))
                Directory.Delete(runRoot, recursive: true);
        }
    }

    private static BattleSnapshotMessage CreateEligibleFieldSnapshot()
    {
        return new BattleSnapshotMessage
        {
            CampaignId = "contract-campaign",
            BattleId = "contract-battle",
            BattleInstanceId = "contract-instance",
            BattleType = "FieldBattle",
            ScenarioContext = new BattleScenarioContextMessage
            {
                ScenarioKind = "FieldBattle",
                CampaignBattleType = "FieldBattle",
                IsSiegeBattle = false,
                Source = "contract-test"
            },
            Sides = new List<BattleSideSnapshotMessage>
            {
                new BattleSideSnapshotMessage
                {
                    SideId = "Attacker",
                    Troops = new List<TroopStackInfo>
                    {
                        new TroopStackInfo
                        {
                            EntryId = "attacker-hero-infantry",
                            CharacterId = "contract_infantry",
                            Count = 3,
                            IsMounted = false,
                            IsHero = true,
                            HeroId = "contract_hero",
                            HeroRole = "Captain"
                        }
                    }
                },
                new BattleSideSnapshotMessage
                {
                    SideId = "Defender",
                    Troops = new List<TroopStackInfo>
                    {
                        new TroopStackInfo
                        {
                            EntryId = "defender-cavalry",
                            CharacterId = "contract_cavalry",
                            Count = 2,
                            IsMounted = true
                        }
                    }
                }
            }
        };
    }

    private static string ResolveRepositoryRoot()
    {
        string configured = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        DirectoryInfo current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Tests", "contract-tests.manifest.json")) &&
                File.Exists(Path.Combine(current.FullName, "Campaign", "BattleRosterFile.cs")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not resolve the repository root.");
    }

    private static void ClearAutomationEnvironment()
    {
        for (int i = 0; i < AutomationVariables.Length; i++)
            Environment.SetEnvironmentVariable(AutomationVariables[i], null, EnvironmentVariableTarget.Process);
    }

    private static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }
        return true;
    }

    private static JObject RequireObject(JObject parent, string propertyName)
    {
        JObject value = parent[propertyName] as JObject;
        Assert(value != null, "Expected JSON object property: " + propertyName + ".");
        return value;
    }

    private static JArray RequireArray(JObject parent, string propertyName)
    {
        JArray value = parent[propertyName] as JArray;
        Assert(value != null, "Expected JSON array property: " + propertyName + ".");
        return value;
    }

    private static bool HasText(JObject value, string propertyName)
    {
        return !string.IsNullOrWhiteSpace(value.Value<string>(propertyName));
    }

    private static bool HasAnyWeapon(JObject troop)
    {
        return HasText(troop, "CombatItem0Id") ||
               HasText(troop, "CombatItem1Id") ||
               HasText(troop, "CombatItem2Id") ||
               HasText(troop, "CombatItem3Id");
    }

    private static bool IsEmptyOrFixtureIdentity(string value, string prefix)
    {
        return string.IsNullOrWhiteSpace(value) ||
               (value.StartsWith(prefix, StringComparison.Ordinal) &&
                Regex.IsMatch(value.Substring(prefix.Length), @"^\d{3}$"));
    }

    private static bool HasUtf8Bom(byte[] bytes)
    {
        return bytes != null &&
               bytes.Length >= 3 &&
               bytes[0] == 0xEF &&
               bytes[1] == 0xBB &&
               bytes[2] == 0xBF;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using (SHA256 algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty);
    }

    private sealed class CommittedBattleRosterFile
    {
        public List<string> TroopIds { get; set; }
        public BattleSnapshotMessage Snapshot { get; set; }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
