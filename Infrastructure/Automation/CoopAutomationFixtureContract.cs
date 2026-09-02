using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.Automation
{
    public sealed class CoopAutomationFieldFixtureQualification
    {
        public int SideCount { get; set; }
        public int InfantryStackCount { get; set; }
        public int MountedStackCount { get; set; }
        public int HeroOrCaptainStackCount { get; set; }
    }

    public sealed class CoopAutomationFixtureMetadata
    {
        public int SchemaVersion { get; set; }
        public string FixtureId { get; set; }
        public string RunId { get; set; }
        public string PayloadKind { get; set; }
        public string Boundary { get; set; }
        public string SourceRole { get; set; }
        public string TargetRole { get; set; }
        public string Encoding { get; set; }
        public string Compression { get; set; }
        public string Serializer { get; set; }
        public string PayloadSchema { get; set; }
        public string TransportSchema { get; set; }
        public string CompatibilityStatus { get; set; }
        public string PayloadFile { get; set; }
        public long PayloadLength { get; set; }
        public string PayloadSha256 { get; set; }
        public string CampaignId { get; set; }
        public string BattleId { get; set; }
        public string BattleInstanceId { get; set; }
        public string BattleStage { get; set; }
        public string ScenarioKind { get; set; }
        public string SourceRevision { get; set; }
        public string ModuleVersion { get; set; }
        public string ModuleFileName { get; set; }
        public string ModuleSha256 { get; set; }
        public string ExpectedModuleSha256 { get; set; }
        public string GameVersion { get; set; }
        public string DedicatedServerVersion { get; set; }
        public DateTime CapturedUtc { get; set; }
        public string CaptureReason { get; set; }
        public string SanitizationStatus { get; set; }
        public string IndependentOracleStatus { get; set; }
        public CoopAutomationFieldFixtureQualification Qualification { get; set; }
    }

    public sealed class CoopAutomationFixtureRecordStatus
    {
        public int SchemaVersion { get; set; }
        public string RunId { get; set; }
        public string FixtureId { get; set; }
        public string State { get; set; }
        public string FailureCode { get; set; }
        public string FailureMessage { get; set; }
        public string PayloadSha256 { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public static class CoopAutomationFixtureContract
    {
        public const int CurrentSchemaVersion = 1;
        public const string CampaignRosterPayloadKind = "CampaignRoster";
        public const string CampaignRosterBoundary = "Campaign/BattleRosterFileHelper.WriteRoster/post-write";
        public const string CampaignRosterSerializer = "Newtonsoft.Json.JsonConvert.SerializeObject(Formatting.Indented)+System.IO.File.WriteAllText";
        public const string CampaignRosterPayloadSchema = "BattleRosterFileDto.CurrentUnversioned";
        public const string CurrentOnlyUnversionedCompatibility = "CurrentOnlyUnversioned";
        public const string FixtureRelativeRoot = "artifacts/fixtures/field-current";
        public const string RawPayloadFileName = "battle_roster.raw.json";
        public const string MetadataFileName = "fixture.metadata.json";
        public const string RecordStatusRelativePath = "state/fixture-record.status.json";
        public const string FieldBattleScenarioKind = "FieldBattle";
        public const string FieldBattleCampaignBattleType = "FieldBattle";

        public static bool TryQualifyFirstFieldSlice(
            BattleSnapshotMessage snapshot,
            out CoopAutomationFieldFixtureQualification qualification,
            out string failureCode,
            out string failureMessage)
        {
            qualification = new CoopAutomationFieldFixtureQualification();
            failureCode = string.Empty;
            failureMessage = string.Empty;

            if (snapshot == null)
                return Fail("SnapshotMissing", "The campaign roster snapshot is missing.", out failureCode, out failureMessage);

            BattleScenarioContextMessage scenario = snapshot.ScenarioContext;
            if (scenario == null ||
                scenario.IsSiegeBattle ||
                !string.Equals(scenario.ScenarioKind, FieldBattleScenarioKind, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(scenario.CampaignBattleType, FieldBattleCampaignBattleType, StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    "ScenarioNotFirstFieldSlice",
                    "The snapshot is not the ordinary SCN-001 field-battle scenario.",
                    out failureCode,
                    out failureMessage);
            }

            if (snapshot.Sides == null)
                return Fail("SnapshotSidesMissing", "The snapshot has no battle sides.", out failureCode, out failureMessage);

            var populatedSideIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int sideIndex = 0; sideIndex < snapshot.Sides.Count; sideIndex++)
            {
                BattleSideSnapshotMessage side = snapshot.Sides[sideIndex];
                if (side?.Troops == null)
                    continue;

                bool sidePopulated = false;
                for (int troopIndex = 0; troopIndex < side.Troops.Count; troopIndex++)
                {
                    TroopStackInfo troop = side.Troops[troopIndex];
                    if (troop == null || troop.Count <= 0)
                        continue;

                    sidePopulated = true;
                    if (troop.IsMounted)
                        qualification.MountedStackCount++;
                    else
                        qualification.InfantryStackCount++;

                    if (troop.IsHero ||
                        !string.IsNullOrWhiteSpace(troop.HeroId) ||
                        string.Equals(troop.HeroRole, "Captain", StringComparison.OrdinalIgnoreCase) ||
                        ContainsOrdinalIgnoreCase(snapshot.FrozenCaptainEntryIds, troop.EntryId))
                    {
                        qualification.HeroOrCaptainStackCount++;
                    }
                }

                if (sidePopulated)
                {
                    string sideIdentity = !string.IsNullOrWhiteSpace(side.SideId)
                        ? side.SideId
                        : "side-index-" + sideIndex;
                    populatedSideIds.Add(sideIdentity);
                }
            }

            qualification.SideCount = populatedSideIds.Count;
            if (qualification.SideCount < 2)
                return Fail("FieldSidesInsufficient", "The field fixture requires two populated sides.", out failureCode, out failureMessage);
            if (qualification.InfantryStackCount <= 0)
                return Fail("FieldInfantryMissing", "The field fixture requires at least one infantry stack.", out failureCode, out failureMessage);
            if (qualification.MountedStackCount <= 0)
                return Fail("FieldCavalryMissing", "The field fixture requires at least one mounted stack.", out failureCode, out failureMessage);
            if (qualification.HeroOrCaptainStackCount <= 0)
                return Fail("FieldHeroOrCaptainMissing", "The field fixture requires at least one hero or captain stack.", out failureCode, out failureMessage);

            return true;
        }

        public static bool TryValidateRecordedPayload(
            CoopAutomationFixtureMetadata metadata,
            byte[] payloadBytes,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (metadata == null)
                return Fail("FixtureMetadataMissing", "The fixture metadata is missing.", out failureCode, out failureMessage);
            if (metadata.SchemaVersion != CurrentSchemaVersion)
                return Fail("FixtureSchemaUnsupported", "The fixture metadata schema is unsupported.", out failureCode, out failureMessage);
            if (!string.Equals(metadata.PayloadKind, CampaignRosterPayloadKind, StringComparison.Ordinal) ||
                !string.Equals(metadata.Boundary, CampaignRosterBoundary, StringComparison.Ordinal) ||
                !string.Equals(metadata.Serializer, CampaignRosterSerializer, StringComparison.Ordinal))
            {
                return Fail("FixtureBoundaryUnsupported", "The fixture payload boundary or serializer is unsupported.", out failureCode, out failureMessage);
            }
            if (!string.Equals(metadata.PayloadSchema, CampaignRosterPayloadSchema, StringComparison.Ordinal) ||
                !string.Equals(metadata.CompatibilityStatus, CurrentOnlyUnversionedCompatibility, StringComparison.Ordinal))
            {
                return Fail("PayloadSchemaUnsupported", "The campaign-roster payload schema or compatibility status is unsupported.", out failureCode, out failureMessage);
            }
            if (!string.Equals(metadata.PayloadFile, RawPayloadFileName, StringComparison.Ordinal))
                return Fail("PayloadPathUnsupported", "The fixture payload path is not the canonical immutable file name.", out failureCode, out failureMessage);
            if (!string.Equals(metadata.Encoding, "UTF-8", StringComparison.Ordinal) ||
                !string.Equals(metadata.Compression, "None", StringComparison.Ordinal))
            {
                return Fail("PayloadEncodingUnsupported", "The fixture encoding or compression is unsupported.", out failureCode, out failureMessage);
            }
            if (payloadBytes == null)
                return Fail("PayloadMissing", "The fixture payload bytes are missing.", out failureCode, out failureMessage);
            if (metadata.PayloadLength != payloadBytes.LongLength)
                return Fail("PayloadLengthMismatch", "The fixture payload length does not match the metadata.", out failureCode, out failureMessage);

            string actualSha256 = ComputeSha256Hex(payloadBytes);
            if (!string.Equals(metadata.PayloadSha256, actualSha256, StringComparison.Ordinal))
                return Fail("PayloadHashMismatch", "The fixture payload SHA-256 does not match the metadata.", out failureCode, out failureMessage);
            if (!CoopAutomationRuntimeContract.IsValidRunId(metadata.RunId) ||
                !CoopAutomationRuntimeContract.IsValidRunId(metadata.FixtureId))
            {
                return Fail("FixtureIdentityInvalid", "The fixture or run identity is invalid.", out failureCode, out failureMessage);
            }
            if (!IsGitRevision(metadata.SourceRevision))
                return Fail("SourceRevisionInvalid", "The fixture source revision is not a full Git object identity.", out failureCode, out failureMessage);
            if (!CoopAutomationRuntimeContract.IsSha256(metadata.ModuleSha256) ||
                !string.Equals(metadata.ModuleSha256, metadata.ExpectedModuleSha256, StringComparison.Ordinal))
            {
                return Fail("ModuleIdentityMismatch", "The recorded and expected module identities do not match.", out failureCode, out failureMessage);
            }
            if (string.IsNullOrWhiteSpace(metadata.GameVersion) ||
                string.IsNullOrWhiteSpace(metadata.ModuleVersion) ||
                string.IsNullOrWhiteSpace(metadata.BattleId) ||
                string.IsNullOrWhiteSpace(metadata.BattleInstanceId) ||
                metadata.Qualification == null)
            {
                return Fail("FixtureProvenanceIncomplete", "The fixture provenance or qualification is incomplete.", out failureCode, out failureMessage);
            }

            return true;
        }

        public static bool TryCombineUnderRoot(
            string root,
            string relativePath,
            out string fullPath,
            out string failureCode,
            out string failureMessage)
        {
            fullPath = string.Empty;
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                return Fail("FixturePathInvalid", "The fixture root and relative path must be provided and the relative path must not be rooted.", out failureCode, out failureMessage);

            try
            {
                string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
                string rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    return Fail("FixturePathEscapesRoot", "The fixture path escapes the run-scoped fixture root.", out failureCode, out failureMessage);

                fullPath = candidate;
                return true;
            }
            catch (Exception ex)
            {
                return Fail("FixturePathInvalid", "The fixture path is invalid: " + ex.Message, out failureCode, out failureMessage);
            }
        }

        public static string ComputeSha256Hex(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes ?? Array.Empty<byte>());
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static bool ContainsOrdinalIgnoreCase(List<string> values, string expected)
        {
            if (values == null || string.IsNullOrWhiteSpace(expected))
                return false;
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsGitRevision(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || (value.Length != 40 && value.Length != 64))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') ||
                      (c >= 'a' && c <= 'f') ||
                      (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool Fail(
            string code,
            string message,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = code;
            failureMessage = message;
            return false;
        }
    }
}
