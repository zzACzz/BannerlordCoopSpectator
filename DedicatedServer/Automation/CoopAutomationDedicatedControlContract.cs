using System;
using System.Collections.Generic;
using System.IO;

namespace CoopSpectator.Infrastructure.Automation
{
    public sealed class CoopAutomationDedicatedControlReadyStatus
    {
        public int SchemaVersion { get; set; }
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public string RunTokenSha256 { get; set; }
        public string RoleType { get; set; }
        public string RoleInstanceId { get; set; }
        public string State { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public int ProcessId { get; set; }
        public DateTime ProcessStartUtc { get; set; }
        public string ExecutablePath { get; set; }
        public string ModulePath { get; set; }
        public string ModuleSha256 { get; set; }
        public string ExpectedModuleSha256 { get; set; }
        public string LifecycleSource { get; set; }
        public string FailureCode { get; set; }
        public string FailureMessage { get; set; }
    }

    public sealed class CoopAutomationDedicatedBootstrapRequest
    {
        public int SchemaVersion { get; set; }
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public long Sequence { get; set; }
        public string CommandId { get; set; }
        public string SourceRoleType { get; set; }
        public string SourceRoleInstanceId { get; set; }
        public string TargetRoleType { get; set; }
        public string TargetRoleInstanceId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string RunTokenSha256 { get; set; }
        public string ExpectedDedicatedModuleSha256 { get; set; }
        public int ExpectedProcessId { get; set; }
        public DateTime ExpectedProcessStartUtc { get; set; }
        public string ExpectedExecutablePath { get; set; }
        public string BootstrapProfile { get; set; }
        public string ServerName { get; set; }
        public int MaxNumberOfPlayers { get; set; }
        public string GameType { get; set; }
        public string Map { get; set; }
    }

    public sealed class CoopAutomationDedicatedBootstrapAcknowledgement
    {
        public int StepSequence { get; set; }
        public string Step { get; set; }
        public string State { get; set; }
        public string ExpectedValue { get; set; }
        public string ObservedValue { get; set; }
        public DateTime AcknowledgedUtc { get; set; }
    }

    public sealed class CoopAutomationDedicatedBootstrapStatus
    {
        public int SchemaVersion { get; set; }
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public long Sequence { get; set; }
        public string CommandId { get; set; }
        public string SourceRoleType { get; set; }
        public string SourceRoleInstanceId { get; set; }
        public string TargetRoleType { get; set; }
        public string TargetRoleInstanceId { get; set; }
        public string RunTokenSha256 { get; set; }
        public string DedicatedModuleSha256 { get; set; }
        public int ProcessId { get; set; }
        public DateTime ProcessStartUtc { get; set; }
        public string ExecutablePath { get; set; }
        public string State { get; set; }
        public bool IsTerminal { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public List<CoopAutomationDedicatedBootstrapAcknowledgement> Acknowledgements { get; set; } =
            new List<CoopAutomationDedicatedBootstrapAcknowledgement>();
        public string FailureCode { get; set; }
        public string FailureMessage { get; set; }
    }

    public static class CoopAutomationDedicatedControlContract
    {
        public const int CurrentSchemaVersion = 1;
        public const string ConnectionFeasibilityProfile = "ConnectionFeasibilityV1";
        public const string DedicatedRoleType = "DedicatedServer";
        public const string DedicatedRoleInstanceId = "dedicated-server-01";
        public const string RunnerRoleType = "Runner";
        public const string RunnerRoleInstanceId = "runner-01";
        public const string ReadyRelativePath = "state/dedicated-control.ready.json";
        public const string RequestRelativePath = "commands/dedicated-bootstrap.request.json";
        public const string ProcessedRequestRelativePath = "commands/processed/dedicated-bootstrap.request.json";
        public const string StatusRelativePath = "state/dedicated-bootstrap.status.json";
        public const string ReadyState = "Ready";
        public const string AcceptedState = "Accepted";
        public const string BootstrapAcceptedState = "BootstrapAccepted";
        public const string FailedState = "Failed";
        public const int RequiredAcknowledgementCount = 7;

        private static readonly string[] ExpectedSteps =
        {
            "ServerName",
            "MaxNumberOfPlayers",
            "GameType",
            "Map",
            "UsableMap",
            "StartGameRequested",
            "StartGameConfirmed"
        };

        public static bool TryValidateRequest(
            CoopAutomationDedicatedBootstrapRequest request,
            CoopAutomationRuntimeConfiguration configuration,
            string actualModuleSha256,
            int actualProcessId,
            DateTime actualProcessStartUtc,
            string actualExecutablePath,
            DateTime nowUtc,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;

            if (request == null)
                return Fail("RequestMissing", "The dedicated bootstrap request is missing.", out failureCode, out failureMessage);
            if (configuration == null)
                return Fail("RuntimeConfigurationMissing", "The dedicated automation configuration is missing.", out failureCode, out failureMessage);
            if (request.SchemaVersion != CurrentSchemaVersion)
                return Fail("SchemaMismatch", "The dedicated bootstrap request schema is unsupported.", out failureCode, out failureMessage);
            if (request.ProtocolMajorVersion != CoopAutomationRuntimeContract.CurrentProtocolMajorVersion ||
                request.ProtocolMinorVersion < 0 ||
                request.ProtocolMinorVersion > CoopAutomationRuntimeContract.CurrentProtocolMinorVersion)
            {
                return Fail("ProtocolUnsupported", "The dedicated bootstrap request protocol is unsupported.", out failureCode, out failureMessage);
            }
            if (!CoopAutomationRuntimeContract.IsValidRunId(request.RunId) ||
                !string.Equals(request.RunId, configuration.RunId, StringComparison.Ordinal))
            {
                return Fail("RunIdMismatch", "The dedicated bootstrap request belongs to another run.", out failureCode, out failureMessage);
            }
            if (request.Sequence != 1)
                return Fail("SequenceInvalid", "The connection-feasibility bootstrap accepts only sequence 1 in a fresh run root.", out failureCode, out failureMessage);
            if (!Guid.TryParse(request.CommandId, out Guid commandId) || commandId == Guid.Empty)
                return Fail("CommandIdInvalid", "The dedicated bootstrap command id must be a non-empty GUID.", out failureCode, out failureMessage);
            if (!MatchesRole(request.SourceRoleType, request.SourceRoleInstanceId, RunnerRoleType, RunnerRoleInstanceId))
                return Fail("SourceRoleMismatch", "The dedicated bootstrap source must be runner-01.", out failureCode, out failureMessage);
            if (!MatchesRole(request.TargetRoleType, request.TargetRoleInstanceId, DedicatedRoleType, DedicatedRoleInstanceId))
                return Fail("TargetRoleMismatch", "The dedicated bootstrap target must be dedicated-server-01.", out failureCode, out failureMessage);

            DateTime createdUtc = NormalizeUtc(request.CreatedUtc);
            DateTime expiresUtc = NormalizeUtc(request.ExpiresUtc);
            DateTime normalizedNowUtc = NormalizeUtc(nowUtc);
            if (createdUtc == DateTime.MinValue || expiresUtc == DateTime.MinValue || expiresUtc <= createdUtc)
                return Fail("LifetimeInvalid", "The dedicated bootstrap request lifetime is invalid.", out failureCode, out failureMessage);
            if (createdUtc > normalizedNowUtc.AddMinutes(1))
                return Fail("CreatedInFuture", "The dedicated bootstrap request was created too far in the future.", out failureCode, out failureMessage);
            if (expiresUtc <= normalizedNowUtc)
                return Fail("RequestExpired", "The dedicated bootstrap request has expired.", out failureCode, out failureMessage);
            if (expiresUtc - createdUtc > TimeSpan.FromMinutes(10))
                return Fail("LifetimeTooLong", "The dedicated bootstrap request lifetime exceeds ten minutes.", out failureCode, out failureMessage);

            if (!FixedTimeHexEquals(request.RunTokenSha256, configuration.RunTokenSha256))
                return Fail("RunTokenMismatch", "The dedicated bootstrap token hash does not match the configured run.", out failureCode, out failureMessage);
            if (!FixedTimeHexEquals(request.ExpectedDedicatedModuleSha256, actualModuleSha256) ||
                !FixedTimeHexEquals(request.ExpectedDedicatedModuleSha256, configuration.ExpectedModuleSha256))
            {
                return Fail("DedicatedModuleHashMismatch", "The dedicated bootstrap module hash does not match the loaded and configured identities.", out failureCode, out failureMessage);
            }
            if (request.ExpectedProcessId <= 0 || request.ExpectedProcessId != actualProcessId)
                return Fail("ProcessIdMismatch", "The dedicated bootstrap process id does not match the current process.", out failureCode, out failureMessage);
            if (request.ExpectedProcessStartUtc == default(DateTime) ||
                Math.Abs((NormalizeUtc(request.ExpectedProcessStartUtc) - NormalizeUtc(actualProcessStartUtc)).TotalSeconds) >= 1.0)
            {
                return Fail("ProcessStartMismatch", "The dedicated bootstrap process start time does not match the current process.", out failureCode, out failureMessage);
            }
            if (!string.Equals(NormalizePath(request.ExpectedExecutablePath), NormalizePath(actualExecutablePath), StringComparison.OrdinalIgnoreCase))
                return Fail("ExecutablePathMismatch", "The dedicated bootstrap executable path does not match the current process.", out failureCode, out failureMessage);

            if (!string.Equals(request.BootstrapProfile, ConnectionFeasibilityProfile, StringComparison.Ordinal))
                return Fail("BootstrapProfileUnsupported", "The requested dedicated bootstrap profile is unsupported.", out failureCode, out failureMessage);
            if (!IsSafeServerName(request.ServerName))
                return Fail("ServerNameInvalid", "The requested dedicated server name must contain only ASCII letters, digits, dot, underscore, or hyphen.", out failureCode, out failureMessage);
            if (request.MaxNumberOfPlayers != 16)
                return Fail("MaxPlayersUnsupported", "The connection-feasibility bootstrap requires exactly 16 maximum players.", out failureCode, out failureMessage);
            if (!string.Equals(request.GameType, "TeamDeathmatch", StringComparison.Ordinal))
                return Fail("GameTypeUnsupported", "The connection-feasibility bootstrap requires TeamDeathmatch.", out failureCode, out failureMessage);
            if (!string.Equals(request.Map, "mp_tdm_map_001", StringComparison.Ordinal))
                return Fail("MapUnsupported", "The connection-feasibility bootstrap requires mp_tdm_map_001.", out failureCode, out failureMessage);

            return true;
        }

        public static bool TryValidateReadyStatus(
            CoopAutomationDedicatedControlReadyStatus status,
            CoopAutomationRuntimeConfiguration configuration,
            string actualModuleSha256,
            int actualProcessId,
            DateTime actualProcessStartUtc,
            string actualExecutablePath,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (status == null)
                return Fail("ReadyStatusMissing", "The dedicated control readiness status is missing.", out failureCode, out failureMessage);
            if (configuration == null)
                return Fail("RuntimeConfigurationMissing", "The dedicated automation configuration is missing.", out failureCode, out failureMessage);
            if (status.SchemaVersion != CurrentSchemaVersion ||
                status.ProtocolMajorVersion != CoopAutomationRuntimeContract.CurrentProtocolMajorVersion ||
                status.ProtocolMinorVersion < 0 ||
                status.ProtocolMinorVersion > CoopAutomationRuntimeContract.CurrentProtocolMinorVersion)
            {
                return Fail("ReadyProtocolUnsupported", "The dedicated readiness protocol is unsupported.", out failureCode, out failureMessage);
            }
            if (!string.Equals(status.RunId, configuration.RunId, StringComparison.Ordinal) ||
                !FixedTimeHexEquals(status.RunTokenSha256, configuration.RunTokenSha256))
            {
                return Fail("ReadyRunMismatch", "The dedicated readiness status belongs to another run.", out failureCode, out failureMessage);
            }
            if (!MatchesRole(status.RoleType, status.RoleInstanceId, DedicatedRoleType, DedicatedRoleInstanceId))
                return Fail("ReadyRoleMismatch", "The dedicated readiness status belongs to another role.", out failureCode, out failureMessage);
            if (!string.Equals(status.State, ReadyState, StringComparison.Ordinal))
                return Fail("ReadyStateInvalid", "The dedicated control status is not Ready.", out failureCode, out failureMessage);
            if (status.ProcessId != actualProcessId ||
                Math.Abs((NormalizeUtc(status.ProcessStartUtc) - NormalizeUtc(actualProcessStartUtc)).TotalSeconds) >= 1.0 ||
                !string.Equals(NormalizePath(status.ExecutablePath), NormalizePath(actualExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                return Fail("ReadyProcessMismatch", "The dedicated readiness process identity does not match the current process.", out failureCode, out failureMessage);
            }
            if (!FixedTimeHexEquals(status.ModuleSha256, actualModuleSha256) ||
                !FixedTimeHexEquals(status.ExpectedModuleSha256, configuration.ExpectedModuleSha256))
            {
                return Fail("ReadyModuleMismatch", "The dedicated readiness module identity is invalid.", out failureCode, out failureMessage);
            }
            if (!string.Equals(status.LifecycleSource, "InitialListedGameServerState.OnActivated", StringComparison.Ordinal))
                return Fail("ReadyLifecycleSourceInvalid", "The dedicated readiness lifecycle source is not authoritative.", out failureCode, out failureMessage);
            return true;
        }

        public static bool TryValidateTerminalStatus(
            CoopAutomationDedicatedBootstrapStatus status,
            CoopAutomationDedicatedBootstrapRequest request,
            CoopAutomationRuntimeConfiguration configuration,
            string actualModuleSha256,
            int actualProcessId,
            DateTime actualProcessStartUtc,
            string actualExecutablePath,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (status == null || request == null || configuration == null)
                return Fail("TerminalStatusContextMissing", "The dedicated terminal status validation context is incomplete.", out failureCode, out failureMessage);
            if (status.SchemaVersion != CurrentSchemaVersion ||
                status.ProtocolMajorVersion != CoopAutomationRuntimeContract.CurrentProtocolMajorVersion ||
                status.ProtocolMinorVersion < 0 ||
                status.ProtocolMinorVersion > CoopAutomationRuntimeContract.CurrentProtocolMinorVersion)
            {
                return Fail("StatusProtocolUnsupported", "The dedicated bootstrap status protocol is unsupported.", out failureCode, out failureMessage);
            }
            if (!string.Equals(status.RunId, configuration.RunId, StringComparison.Ordinal) ||
                status.Sequence != request.Sequence ||
                !string.Equals(status.CommandId, request.CommandId, StringComparison.OrdinalIgnoreCase) ||
                !FixedTimeHexEquals(status.RunTokenSha256, configuration.RunTokenSha256))
            {
                return Fail("StatusCommandMismatch", "The dedicated bootstrap status belongs to another command or run.", out failureCode, out failureMessage);
            }
            if (!MatchesRole(status.SourceRoleType, status.SourceRoleInstanceId, DedicatedRoleType, DedicatedRoleInstanceId) ||
                !MatchesRole(status.TargetRoleType, status.TargetRoleInstanceId, RunnerRoleType, RunnerRoleInstanceId))
            {
                return Fail("StatusRoleMismatch", "The dedicated bootstrap status role routing is invalid.", out failureCode, out failureMessage);
            }
            if (!FixedTimeHexEquals(status.DedicatedModuleSha256, actualModuleSha256) ||
                status.ProcessId != actualProcessId ||
                Math.Abs((NormalizeUtc(status.ProcessStartUtc) - NormalizeUtc(actualProcessStartUtc)).TotalSeconds) >= 1.0 ||
                !string.Equals(NormalizePath(status.ExecutablePath), NormalizePath(actualExecutablePath), StringComparison.OrdinalIgnoreCase))
            {
                return Fail("StatusRuntimeIdentityMismatch", "The dedicated bootstrap status runtime identity is invalid.", out failureCode, out failureMessage);
            }
            if (!status.IsTerminal || !string.Equals(status.State, BootstrapAcceptedState, StringComparison.Ordinal))
                return Fail("StatusNotAccepted", "The dedicated bootstrap status is not a successful terminal acknowledgement.", out failureCode, out failureMessage);
            if (status.Acknowledgements == null || status.Acknowledgements.Count != RequiredAcknowledgementCount)
                return Fail("AcknowledgementCountInvalid", "The dedicated bootstrap acknowledgement history is incomplete.", out failureCode, out failureMessage);
            for (int i = 0; i < ExpectedSteps.Length; i++)
            {
                CoopAutomationDedicatedBootstrapAcknowledgement acknowledgement = status.Acknowledgements[i];
                if (acknowledgement == null || acknowledgement.StepSequence != i + 1 ||
                    !string.Equals(acknowledgement.Step, ExpectedSteps[i], StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(acknowledgement.State) ||
                    acknowledgement.AcknowledgedUtc == default(DateTime))
                {
                    return Fail("AcknowledgementSequenceInvalid", "The dedicated bootstrap acknowledgement history is reordered or incomplete.", out failureCode, out failureMessage);
                }
            }
            return true;
        }

        public static bool IsTerminalState(string state)
        {
            return string.Equals(state, BootstrapAcceptedState, StringComparison.Ordinal) ||
                   string.Equals(state, FailedState, StringComparison.Ordinal);
        }

        private static bool MatchesRole(string actualType, string actualInstance, string expectedType, string expectedInstance)
        {
            return string.Equals(actualType ?? string.Empty, expectedType, StringComparison.Ordinal) &&
                   string.Equals(actualInstance ?? string.Empty, expectedInstance, StringComparison.Ordinal);
        }

        private static bool IsSafeServerName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool allowed = character >= 'A' && character <= 'Z' ||
                               character >= 'a' && character <= 'z' ||
                               character >= '0' && character <= '9' ||
                               character == '.' || character == '_' || character == '-';
                if (!allowed)
                    return false;
            }
            return true;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == DateTime.MinValue)
                return value;
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            try { return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return value.Trim(); }
        }

        private static bool FixedTimeHexEquals(string left, string right)
        {
            if (!CoopAutomationRuntimeContract.IsSha256(left) || !CoopAutomationRuntimeContract.IsSha256(right))
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
