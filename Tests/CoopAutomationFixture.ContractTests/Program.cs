using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CoopSpectator.Infrastructure.Automation;
using CoopSpectator.Network.Messages;
using Newtonsoft.Json;

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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
