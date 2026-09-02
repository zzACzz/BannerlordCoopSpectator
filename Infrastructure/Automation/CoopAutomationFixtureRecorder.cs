using System;
using System.IO;
using System.Reflection;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.Automation
{
    internal static class CoopAutomationFixtureRecorder
    {
        private static readonly object RecordLock = new object();
        private static string _recordedRunId = string.Empty;

        public static bool TryRecordCampaignRoster(
            string rosterPath,
            BattleSnapshotMessage snapshot,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (!CoopAutomationRuntimeBridge.IsFixtureRecordingRequested)
                return true;

            lock (RecordLock)
            {
                return TryRecordCampaignRosterCore(rosterPath, snapshot, out failureCode, out failureMessage);
            }
        }

        private static bool TryRecordCampaignRosterCore(
            string rosterPath,
            BattleSnapshotMessage snapshot,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (!CoopAutomationRuntimeBridge.TryResolveConfiguration(
                    out CoopAutomationRuntimeConfiguration configuration,
                    out failureCode,
                    out failureMessage))
            {
                return false;
            }

            string fixtureId = (Environment.GetEnvironmentVariable(CoopAutomationRuntimeBridge.FixtureIdVariable) ?? string.Empty).Trim();
            if (!CoopAutomationRuntimeContract.IsValidRunId(fixtureId))
                return FailWithStatus(configuration, fixtureId, "FixtureIdInvalid", "The fixture id is missing or invalid.", out failureCode, out failureMessage);

            if (string.Equals(_recordedRunId, configuration.RunId, StringComparison.Ordinal))
                return true;

            if (!CoopAutomationFixtureContract.TryQualifyFirstFieldSlice(
                    snapshot,
                    out CoopAutomationFieldFixtureQualification qualification,
                    out string qualificationFailureCode,
                    out string qualificationFailureMessage))
            {
                TryWriteStatus(
                    configuration,
                    fixtureId,
                    "Skipped",
                    qualificationFailureCode,
                    qualificationFailureMessage,
                    string.Empty);
                return true;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(rosterPath) || !File.Exists(rosterPath))
                    return FailWithStatus(configuration, fixtureId, "CampaignRosterMissing", "The exact campaign roster file does not exist after the production write.", out failureCode, out failureMessage);

                byte[] payloadBytes = File.ReadAllBytes(rosterPath);
                if (payloadBytes.Length <= 0)
                    return FailWithStatus(configuration, fixtureId, "CampaignRosterEmpty", "The exact campaign roster file is empty.", out failureCode, out failureMessage);

                Assembly moduleAssembly = Assembly.GetExecutingAssembly();
                string modulePath = moduleAssembly.Location;
                string moduleHash = CoopAutomationRuntimeContract.ComputeFileSha256(modulePath);
                if (!string.Equals(moduleHash, configuration.ExpectedModuleSha256, StringComparison.Ordinal))
                    return FailWithStatus(configuration, fixtureId, "ModuleHashMismatch", "The loaded module SHA-256 does not match the expected capture identity.", out failureCode, out failureMessage);

                string sourceRevision = (Environment.GetEnvironmentVariable(CoopAutomationRuntimeBridge.SourceRevisionVariable) ?? string.Empty).Trim();
                string gameVersion = (Environment.GetEnvironmentVariable(CoopAutomationRuntimeBridge.GameVersionVariable) ?? string.Empty).Trim();
                var metadata = new CoopAutomationFixtureMetadata
                {
                    SchemaVersion = CoopAutomationFixtureContract.CurrentSchemaVersion,
                    FixtureId = fixtureId,
                    RunId = configuration.RunId,
                    PayloadKind = CoopAutomationFixtureContract.CampaignRosterPayloadKind,
                    Boundary = CoopAutomationFixtureContract.CampaignRosterBoundary,
                    SourceRole = "CampaignHost",
                    TargetRole = "DedicatedServer",
                    Encoding = "UTF-8",
                    Compression = "None",
                    Serializer = CoopAutomationFixtureContract.CampaignRosterSerializer,
                    PayloadSchema = CoopAutomationFixtureContract.CampaignRosterPayloadSchema,
                    TransportSchema = "NotApplicableFileBoundary",
                    CompatibilityStatus = CoopAutomationFixtureContract.CurrentOnlyUnversionedCompatibility,
                    PayloadFile = CoopAutomationFixtureContract.RawPayloadFileName,
                    PayloadLength = payloadBytes.LongLength,
                    PayloadSha256 = CoopAutomationFixtureContract.ComputeSha256Hex(payloadBytes),
                    CampaignId = snapshot.CampaignId ?? string.Empty,
                    BattleId = snapshot.BattleId ?? string.Empty,
                    BattleInstanceId = snapshot.BattleInstanceId ?? string.Empty,
                    BattleStage = "PreMissionCampaignRoster",
                    ScenarioKind = snapshot.ScenarioContext?.ScenarioKind ?? string.Empty,
                    SourceRevision = sourceRevision,
                    ModuleVersion = moduleAssembly.GetName().Version?.ToString() ?? string.Empty,
                    ModuleFileName = Path.GetFileName(modulePath),
                    ModuleSha256 = moduleHash,
                    ExpectedModuleSha256 = configuration.ExpectedModuleSha256,
                    GameVersion = gameVersion,
                    DedicatedServerVersion = "NotObservedAtCampaignBoundary",
                    CapturedUtc = DateTime.UtcNow,
                    CaptureReason = "Milestone3ExactFieldFixture",
                    SanitizationStatus = "UnreviewedPrivateRunArtifact",
                    IndependentOracleStatus = "PendingIndependentReview",
                    Qualification = qualification
                };

                if (!CoopAutomationFixtureContract.TryValidateRecordedPayload(
                        metadata,
                        payloadBytes,
                        out string validationFailureCode,
                        out string validationFailureMessage))
                {
                    return FailWithStatus(configuration, fixtureId, validationFailureCode, validationFailureMessage, out failureCode, out failureMessage);
                }

                string fixtureRoot = CoopAutomationRuntimeContract.CombineRunPath(
                    configuration.RunRoot,
                    CoopAutomationFixtureContract.FixtureRelativeRoot);
                if (!CoopAutomationFixtureContract.TryCombineUnderRoot(
                        fixtureRoot,
                        CoopAutomationFixtureContract.RawPayloadFileName,
                        out string payloadPath,
                        out string pathFailureCode,
                        out string pathFailureMessage))
                {
                    return FailWithStatus(configuration, fixtureId, pathFailureCode, pathFailureMessage, out failureCode, out failureMessage);
                }
                if (!CoopAutomationFixtureContract.TryCombineUnderRoot(
                        fixtureRoot,
                        CoopAutomationFixtureContract.MetadataFileName,
                        out string metadataPath,
                        out pathFailureCode,
                        out pathFailureMessage))
                {
                    return FailWithStatus(configuration, fixtureId, pathFailureCode, pathFailureMessage, out failureCode, out failureMessage);
                }

                EnsureImmutablePayload(payloadPath, payloadBytes, metadata.PayloadSha256);
                EnsureImmutableMetadata(metadataPath, metadata, payloadBytes);
                TryWriteStatus(configuration, fixtureId, "Recorded", string.Empty, string.Empty, metadata.PayloadSha256);
                _recordedRunId = configuration.RunId;
                return true;
            }
            catch (Exception ex)
            {
                return FailWithStatus(configuration, fixtureId, "FixtureRecordFailed", "The exact campaign roster fixture could not be recorded: " + ex.Message, out failureCode, out failureMessage);
            }
        }

        private static void EnsureImmutablePayload(string path, byte[] bytes, string expectedSha256)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(path))
            {
                string existingSha256 = CoopAutomationFixtureContract.ComputeSha256Hex(File.ReadAllBytes(path));
                if (!string.Equals(existingSha256, expectedSha256, StringComparison.Ordinal))
                    throw new IOException("An immutable fixture payload already exists with a different SHA-256.");
                return;
            }

            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporaryPath, path);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        private static void EnsureImmutableMetadata(
            string path,
            CoopAutomationFixtureMetadata metadata,
            byte[] payloadBytes)
        {
            if (File.Exists(path))
            {
                if (!CoopAutomationProtocolFileIO.TryReadJson(
                        path,
                        1024 * 1024,
                        out CoopAutomationFixtureMetadata existing,
                        out string failureCode,
                        out string failureMessage))
                {
                    throw new IOException(
                        "Existing fixture metadata is invalid: " +
                        failureCode +
                        " " +
                        failureMessage);
                }
                if (!CoopAutomationFixtureContract.TryValidateRecordedPayload(
                        existing,
                        payloadBytes,
                        out failureCode,
                        out failureMessage))
                {
                    throw new IOException(
                        "Existing fixture metadata does not validate: " +
                        failureCode +
                        " " +
                        failureMessage);
                }
                if (!string.Equals(existing.RunId, metadata.RunId, StringComparison.Ordinal) ||
                    !string.Equals(existing.FixtureId, metadata.FixtureId, StringComparison.Ordinal) ||
                    !string.Equals(existing.BattleId, metadata.BattleId, StringComparison.Ordinal) ||
                    !string.Equals(existing.BattleInstanceId, metadata.BattleInstanceId, StringComparison.Ordinal) ||
                    !string.Equals(existing.SourceRevision, metadata.SourceRevision, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(existing.ModuleSha256, metadata.ModuleSha256, StringComparison.Ordinal))
                {
                    throw new IOException("Existing fixture metadata belongs to a different immutable capture identity.");
                }
                return;
            }

            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(
                path,
                metadata,
                (temporaryPath, destinationPath, destinationExists) =>
                {
                    if (destinationExists)
                        throw new IOException("Immutable fixture metadata already exists.");
                    File.Move(temporaryPath, destinationPath);
                });
        }

        private static bool FailWithStatus(
            CoopAutomationRuntimeConfiguration configuration,
            string fixtureId,
            string code,
            string message,
            out string failureCode,
            out string failureMessage)
        {
            TryWriteStatus(configuration, fixtureId, "Failed", code, message, string.Empty);
            failureCode = code;
            failureMessage = message;
            return false;
        }

        private static void TryWriteStatus(
            CoopAutomationRuntimeConfiguration configuration,
            string fixtureId,
            string state,
            string failureCode,
            string failureMessage,
            string payloadSha256)
        {
            try
            {
                var status = new CoopAutomationFixtureRecordStatus
                {
                    SchemaVersion = CoopAutomationFixtureContract.CurrentSchemaVersion,
                    RunId = configuration?.RunId ?? string.Empty,
                    FixtureId = fixtureId ?? string.Empty,
                    State = state ?? string.Empty,
                    FailureCode = failureCode ?? string.Empty,
                    FailureMessage = failureMessage ?? string.Empty,
                    PayloadSha256 = payloadSha256 ?? string.Empty,
                    UpdatedUtc = DateTime.UtcNow
                };
                string statusPath = CoopAutomationRuntimeContract.CombineRunPath(
                    configuration.RunRoot,
                    CoopAutomationFixtureContract.RecordStatusRelativePath);
                CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(statusPath, status);
            }
            catch
            {
                // Recording status is supplementary and must not affect production gameplay.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
