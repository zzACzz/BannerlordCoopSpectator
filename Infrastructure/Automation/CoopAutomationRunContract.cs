using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure.Automation
{
    public sealed class CoopAutomationRoleIdentity
    {
        public string RoleType { get; set; }
        public string RoleInstanceId { get; set; }
        public List<string> Capabilities { get; set; } = new List<string>();
        public string ExecutablePath { get; set; }
        public string ExecutableSha256 { get; set; }
        public int ProcessId { get; set; }
        public int ParentProcessId { get; set; }
        public DateTime ProcessStartUtc { get; set; }
    }

    public sealed class CoopAutomationPortIdentity
    {
        public int Port { get; set; }
        public string Protocol { get; set; }
        public string LocalAddress { get; set; }
        public int OwnerProcessId { get; set; }
        public string OwnerProcessName { get; set; }
        public string OwnerExecutablePath { get; set; }
        public DateTime? OwnerProcessStartUtc { get; set; }
    }

    public sealed class CoopAutomationFixtureIdentity
    {
        public string FixtureId { get; set; }
        public string Path { get; set; }
        public string Sha256 { get; set; }
    }

    public sealed class CoopAutomationRunManifest
    {
        public int ManifestSchemaVersion { get; set; }
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string RequestedCommand { get; set; }
        public string RequestedLevel { get; set; }
        public string ScenarioKind { get; set; }
        public string Stage { get; set; }
        public string MachineProfileName { get; set; }
        public string BuildProfile { get; set; }
        public string ExpectedArtifactSource { get; set; }
        public string NonceSha256 { get; set; }
        public string RepositoryRevision { get; set; }
        public bool RepositoryDirty { get; set; }
        public string RunnerBuildIdentity { get; set; }
        public string ClientModuleVersion { get; set; }
        public string ClientModuleSha256 { get; set; }
        public string DedicatedModuleVersion { get; set; }
        public string DedicatedModuleSha256 { get; set; }
        public string GameExecutableVersion { get; set; }
        public string DedicatedExecutableVersion { get; set; }
        public Dictionary<string, bool> EffectiveFeatureFlags { get; set; } = new Dictionary<string, bool>();
        public string ResultPolicy { get; set; }
        public string CompletionMode { get; set; }
        public List<CoopAutomationRoleIdentity> Roles { get; set; } = new List<CoopAutomationRoleIdentity>();
        public List<int> RequiredPorts { get; set; } = new List<int>();
        public bool PortInspectionAvailable { get; set; }
        public List<CoopAutomationPortIdentity> Ports { get; set; } = new List<CoopAutomationPortIdentity>();
        public List<CoopAutomationFixtureIdentity> InputFixtures { get; set; } = new List<CoopAutomationFixtureIdentity>();
        public Dictionary<string, string> ArtifactCategories { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, int> StateDeadlinesSeconds { get; set; } = new Dictionary<string, int>();
        public string ReproductionDescriptorPath { get; set; }
        public string TerminalOutcome { get; set; }
        public string TerminalReason { get; set; }
        public DateTime? CompletedUtc { get; set; }
    }

    public sealed class CoopAutomationRunLease
    {
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public string NonceSha256 { get; set; }
        public string OwnerRoleType { get; set; }
        public string OwnerRoleInstanceId { get; set; }
        public int OwnerProcessId { get; set; }
        public DateTime OwnerProcessStartUtc { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime LastHeartbeatUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string Status { get; set; }
    }

    public sealed class CoopAutomationEnvelopeIdentity
    {
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public string NonceSha256 { get; set; }
        public string SourceRoleType { get; set; }
        public string SourceRoleInstanceId { get; set; }
        public string TargetRoleType { get; set; }
        public string TargetRoleInstanceId { get; set; }
        public long Sequence { get; set; }
        public string CommandId { get; set; }
        public DateTime IssuedUtc { get; set; }
        public string CampaignId { get; set; }
        public string BattleInstanceId { get; set; }
        public string BattleStage { get; set; }
    }

    public sealed class CoopAutomationEventRecord
    {
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public string NonceSha256 { get; set; }
        public string RoleType { get; set; }
        public string RoleInstanceId { get; set; }
        public long Sequence { get; set; }
        public DateTime TimestampUtc { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string EventType { get; set; }
        public string Message { get; set; }
    }

    public sealed class CoopAutomationKnownIssueAnnotation
    {
        public string KnownIssueId { get; set; }
        public string OriginalOutcome { get; set; }
        public List<string> AffectedVersions { get; set; } = new List<string>();
        public List<string> AffectedSha256 { get; set; } = new List<string>();
        public string EvidenceReference { get; set; }
        public string QuarantineReason { get; set; }
        public string ReviewOrExpiryCondition { get; set; }
    }

    public enum CoopAutomationRecoveryState
    {
        None = 0,
        PendingUnacknowledged = 1,
        AcknowledgedNonTerminal = 2,
        TerminalAcknowledged = 3,
        IdentityMismatch = 4,
        Ambiguous = 5
    }

    public static class CoopAutomationRunContract
    {
        public const int CurrentManifestSchemaVersion = 1;
        public const int CurrentProtocolMajorVersion = 1;
        public const int CurrentProtocolMinorVersion = 0;

        private static readonly Dictionary<string, int> ExitCodes =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "Pass", 0 },
                { "EnvironmentBlocked", 10 },
                { "PreconditionsFailed", 11 },
                { "AssertionFailed", 20 },
                { "Crash", 30 },
                { "Timeout", 31 },
                { "RunnerInternalError", 40 },
                { "Cancelled", 50 }
            };

        private static readonly string[] OutcomePrecedence =
        {
            "RunnerInternalError", "Crash", "Timeout", "AssertionFailed",
            "Cancelled", "PreconditionsFailed", "EnvironmentBlocked", "Pass"
        };

        private static readonly HashSet<string> StableFailureReasons =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "RunIdMismatch", "NonceMismatch", "TopologyRejected",
                "SnapshotDecodeFailed", "MaterializationAckTimeout",
                "ReadinessGateStuck", "ControlledAgentNotSpawned",
                "ResultIdentityMismatch", "NoHeartbeat", "NoProgress",
                "CrashReporterDetected"
            };

        public static bool IsStableFailureReason(string reasonCode)
        {
            return StableFailureReasons.Contains(reasonCode ?? string.Empty);
        }

        public static bool TryValidateKnownIssueAnnotation(
            CoopAutomationKnownIssueAnnotation annotation,
            out string failureCode,
            out string failureMessage)
        {
            if (annotation == null)
                return Fail("KnownIssueAnnotationMissing", "The known-issue annotation is missing.", out failureCode, out failureMessage);
            if (string.Equals(annotation.OriginalOutcome, "Pass", StringComparison.Ordinal))
                return Fail("KnownIssueUnexpectedPassReviewRequired", "A quarantined unexpected pass requires review and cannot retain a known-failure annotation.", out failureCode, out failureMessage);
            if (string.IsNullOrWhiteSpace(annotation.KnownIssueId) ||
                string.IsNullOrWhiteSpace(annotation.EvidenceReference) ||
                string.IsNullOrWhiteSpace(annotation.QuarantineReason) ||
                string.IsNullOrWhiteSpace(annotation.ReviewOrExpiryCondition))
                return Fail("KnownIssueMetadataIncomplete", "Known-issue identity, evidence, quarantine reason, and review/expiry condition are required.", out failureCode, out failureMessage);
            if (annotation.AffectedVersions == null || annotation.AffectedVersions.Count == 0 ||
                annotation.AffectedVersions.Exists(string.IsNullOrWhiteSpace))
                return Fail("KnownIssueVersionsMissing", "At least one exact affected version identity is required.", out failureCode, out failureMessage);
            if (annotation.AffectedSha256 == null || annotation.AffectedSha256.Count == 0)
                return Fail("KnownIssueHashesMissing", "At least one exact affected binary hash is required.", out failureCode, out failureMessage);
            for (int i = 0; i < annotation.AffectedSha256.Count; i++)
            {
                if (!CoopAutomationJoinContract.IsSha256(annotation.AffectedSha256[i]))
                    return Fail("KnownIssueHashInvalid", "Every affected binary hash must be SHA-256.", out failureCode, out failureMessage);
            }

            failureCode = string.Empty;
            failureMessage = string.Empty;
            return true;
        }

        public static bool TryValidateProtocolVersion(int major, int minor, out string failureCode, out string failureMessage)
        {
            if (major != CurrentProtocolMajorVersion)
                return Fail("ProtocolMajorUnsupported", "The protocol major version is unsupported.", out failureCode, out failureMessage);
            if (minor < 0 || minor > CurrentProtocolMinorVersion)
                return Fail("ProtocolMinorUnsupported", "The protocol minor version is unsupported by this implementation.", out failureCode, out failureMessage);

            failureCode = string.Empty;
            failureMessage = string.Empty;
            return true;
        }

        public static bool TryValidateEnvelope(
            CoopAutomationEnvelopeIdentity envelope,
            string expectedRunId,
            string expectedNonceSha256,
            string expectedSourceRoleType,
            string expectedSourceRoleInstanceId,
            string expectedTargetRoleType,
            string expectedTargetRoleInstanceId,
            long lastAcceptedSequence,
            DateTime nowUtc,
            out string failureCode,
            out string failureMessage)
        {
            if (envelope == null)
                return Fail("EnvelopeMissing", "The command envelope is missing.", out failureCode, out failureMessage);
            if (!TryValidateProtocolVersion(envelope.ProtocolMajorVersion, envelope.ProtocolMinorVersion, out failureCode, out failureMessage))
                return false;
            if (!CoopAutomationJoinContract.IsValidRunId(envelope.RunId) || !string.Equals(envelope.RunId, expectedRunId, StringComparison.Ordinal))
                return Fail("RunIdMismatch", "The envelope RunId does not match the active run.", out failureCode, out failureMessage);
            if (!CoopAutomationJoinContract.IsSha256(envelope.NonceSha256) || !FixedTimeHexEquals(envelope.NonceSha256, expectedNonceSha256))
                return Fail("NonceMismatch", "The envelope nonce fingerprint does not match the active run.", out failureCode, out failureMessage);
            if (!MatchesRole(envelope.SourceRoleType, envelope.SourceRoleInstanceId, expectedSourceRoleType, expectedSourceRoleInstanceId))
                return Fail("SourceRoleMismatch", "The envelope source role does not match the expected role instance.", out failureCode, out failureMessage);
            if (!MatchesRole(envelope.TargetRoleType, envelope.TargetRoleInstanceId, expectedTargetRoleType, expectedTargetRoleInstanceId))
                return Fail("TargetRoleMismatch", "The envelope target role does not match the expected role instance.", out failureCode, out failureMessage);
            if (envelope.Sequence <= 0)
                return Fail("SequenceInvalid", "The envelope sequence must be positive.", out failureCode, out failureMessage);
            if (envelope.Sequence == lastAcceptedSequence)
                return Fail("DuplicateCommand", "The command sequence was already accepted.", out failureCode, out failureMessage);
            if (envelope.Sequence < lastAcceptedSequence)
                return Fail("SequenceStale", "The command sequence is older than the accepted stream position.", out failureCode, out failureMessage);
            if (lastAcceptedSequence > 0 && envelope.Sequence != lastAcceptedSequence + 1)
                return Fail("SequenceGap", "The command sequence is not the next monotonic stream value.", out failureCode, out failureMessage);
            if (!Guid.TryParse(envelope.CommandId, out Guid commandId) || commandId == Guid.Empty)
                return Fail("CommandIdInvalid", "The envelope command id must be a non-empty GUID.", out failureCode, out failureMessage);

            DateTime issuedUtc = NormalizeUtc(envelope.IssuedUtc);
            DateTime normalizedNowUtc = NormalizeUtc(nowUtc);
            if (issuedUtc == DateTime.MinValue || issuedUtc > normalizedNowUtc.AddMinutes(1))
                return Fail("IssuedUtcInvalid", "The envelope issue time is invalid or too far in the future.", out failureCode, out failureMessage);

            failureCode = string.Empty;
            failureMessage = string.Empty;
            return true;
        }

        public static bool TryValidateLease(
            CoopAutomationRunLease lease,
            string expectedRunId,
            string expectedNonceSha256,
            DateTime nowUtc,
            out string failureCode,
            out string failureMessage)
        {
            if (lease == null)
                return Fail("LeaseMissing", "The run lease is missing.", out failureCode, out failureMessage);
            if (!TryValidateProtocolVersion(lease.ProtocolMajorVersion, lease.ProtocolMinorVersion, out failureCode, out failureMessage))
                return false;
            if (!string.Equals(lease.RunId, expectedRunId, StringComparison.Ordinal))
                return Fail("LeaseRunIdMismatch", "The lease belongs to another run.", out failureCode, out failureMessage);
            if (!FixedTimeHexEquals(lease.NonceSha256, expectedNonceSha256))
                return Fail("LeaseNonceMismatch", "The lease nonce fingerprint does not match the active run.", out failureCode, out failureMessage);
            if (!MatchesRole(lease.OwnerRoleType, lease.OwnerRoleInstanceId, "Runner", "runner-01"))
                return Fail("LeaseOwnerInvalid", "The lease owner must be runner-01.", out failureCode, out failureMessage);
            if (lease.OwnerProcessId <= 0 || NormalizeUtc(lease.OwnerProcessStartUtc) == DateTime.MinValue)
                return Fail("LeaseProcessInvalid", "The lease owner process identity is incomplete.", out failureCode, out failureMessage);

            DateTime createdUtc = NormalizeUtc(lease.CreatedUtc);
            DateTime heartbeatUtc = NormalizeUtc(lease.LastHeartbeatUtc);
            DateTime expiresUtc = NormalizeUtc(lease.ExpiresUtc);
            if (createdUtc == DateTime.MinValue || heartbeatUtc < createdUtc || expiresUtc <= heartbeatUtc)
                return Fail("LeaseTimelineInvalid", "The lease timeline is invalid.", out failureCode, out failureMessage);
            if (expiresUtc <= NormalizeUtc(nowUtc))
                return Fail("LeaseExpired", "The run lease expired and requires inspection before recovery.", out failureCode, out failureMessage);

            failureCode = string.Empty;
            failureMessage = string.Empty;
            return true;
        }

        public static CoopAutomationRecoveryState ClassifyRecoveryState(
            bool requestExists,
            bool processedRequestExists,
            bool statusExists,
            bool statusIdentityMatches,
            bool statusIsTerminal)
        {
            if (!requestExists && !processedRequestExists && !statusExists)
                return CoopAutomationRecoveryState.None;
            if (statusExists && !statusIdentityMatches)
                return CoopAutomationRecoveryState.IdentityMismatch;
            if (requestExists && !processedRequestExists && !statusExists)
                return CoopAutomationRecoveryState.PendingUnacknowledged;
            if (!requestExists && processedRequestExists && statusExists)
                return statusIsTerminal ? CoopAutomationRecoveryState.TerminalAcknowledged : CoopAutomationRecoveryState.AcknowledgedNonTerminal;
            return CoopAutomationRecoveryState.Ambiguous;
        }

        public static int GetExitCode(string outcome)
        {
            return ExitCodes.TryGetValue(outcome ?? string.Empty, out int exitCode) ? exitCode : ExitCodes["RunnerInternalError"];
        }

        public static string SelectInvocationOutcome(IEnumerable<string> outcomes)
        {
            var observed = new HashSet<string>(outcomes ?? Array.Empty<string>(), StringComparer.Ordinal);
            for (int i = 0; i < OutcomePrecedence.Length; i++)
            {
                if (observed.Contains(OutcomePrecedence[i]))
                    return OutcomePrecedence[i];
            }
            return "RunnerInternalError";
        }

        private static bool MatchesRole(string actualType, string actualInstance, string expectedType, string expectedInstance)
        {
            return !string.IsNullOrWhiteSpace(actualType) && !string.IsNullOrWhiteSpace(actualInstance) &&
                   string.Equals(actualType, expectedType, StringComparison.Ordinal) &&
                   string.Equals(actualInstance, expectedInstance, StringComparison.Ordinal);
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == DateTime.MinValue)
                return value;
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }

        private static bool FixedTimeHexEquals(string left, string right)
        {
            if (!CoopAutomationJoinContract.IsSha256(left) || !CoopAutomationJoinContract.IsSha256(right))
                return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++)
                difference |= char.ToUpperInvariant(left[i]) ^ char.ToUpperInvariant(right[i]);
            return difference == 0;
        }

        private static bool Fail(string code, string message, out string failureCode, out string failureMessage)
        {
            failureCode = code;
            failureMessage = message;
            return false;
        }
    }
}
