using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CoopSpectator.Infrastructure.Automation
{
    public sealed class CoopAutomationJoinRequest
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
        public string ExpectedClientModuleSha256 { get; set; }
        public string ServerName { get; set; }
        public int ServerPort { get; set; }
        public string GameType { get; set; }
        public string UniqueMapId { get; set; }
        public bool RequireLocalHostOwnership { get; set; }
        public bool PasswordProvided { get; set; }
        public string RequestedBy { get; set; }
    }

    public sealed class CoopAutomationServerDescriptor
    {
        public string Id { get; set; }
        public string ServerName { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
        public string GameType { get; set; }
        public string Map { get; set; }
        public string UniqueMapId { get; set; }
        public bool PasswordProtected { get; set; }
    }

    public enum CoopAutomationServerSelectionStatus
    {
        None = 0,
        Selected = 1,
        Ambiguous = 2
    }

    public sealed class CoopAutomationServerSelection
    {
        public CoopAutomationServerSelectionStatus Status { get; set; }
        public int SelectedIndex { get; set; } = -1;
        public int MatchingCount { get; set; }
    }

    public static class CoopAutomationJoinContract
    {
        public const int CurrentSchemaVersion = 2;
        public const string RequestRelativePath = "commands/client-join.request.json";
        public const string StatusRelativePath = "state/client-join.status.json";
        public const string LaunchArtifactRelativePath = "artifacts/processes/client-launch.json";
        public const string LauncherLockRelativePath = "work/client-launch.lock";

        public static bool TryValidateRequest(
            CoopAutomationJoinRequest request,
            string configuredRunId,
            string configuredRunTokenSha256,
            string actualClientModuleSha256,
            DateTime nowUtc,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;

            if (request == null)
                return Fail("RequestMissing", "The client join request is missing.", out failureCode, out failureMessage);

            if (request.SchemaVersion != CurrentSchemaVersion)
                return Fail("SchemaMismatch", "The client join request schema is unsupported.", out failureCode, out failureMessage);

            if (!CoopAutomationRunContract.TryValidateProtocolVersion(
                    request.ProtocolMajorVersion,
                    request.ProtocolMinorVersion,
                    out failureCode,
                    out failureMessage))
            {
                return false;
            }

            if (!IsValidRunId(request.RunId) ||
                !string.Equals(request.RunId, configuredRunId, StringComparison.Ordinal))
            {
                return Fail("RunIdMismatch", "The request RunId does not match the configured run.", out failureCode, out failureMessage);
            }

            if (request.Sequence <= 0)
                return Fail("SequenceInvalid", "The request sequence must be positive.", out failureCode, out failureMessage);

            if (!string.Equals(request.SourceRoleType, "Runner", StringComparison.Ordinal) ||
                !string.Equals(request.SourceRoleInstanceId, "runner-01", StringComparison.Ordinal))
            {
                return Fail("SourceRoleMismatch", "The join request source must be runner-01.", out failureCode, out failureMessage);
            }

            if (!string.Equals(request.TargetRoleType, "MultiplayerClient", StringComparison.Ordinal) ||
                !string.Equals(request.TargetRoleInstanceId, "multiplayer-client-01", StringComparison.Ordinal))
            {
                return Fail("TargetRoleMismatch", "The join request target must be multiplayer-client-01.", out failureCode, out failureMessage);
            }

            if (!Guid.TryParse(request.CommandId, out Guid commandId) || commandId == Guid.Empty)
                return Fail("CommandIdInvalid", "The request command id must be a non-empty GUID.", out failureCode, out failureMessage);

            DateTime createdUtc = NormalizeUtc(request.CreatedUtc);
            DateTime expiresUtc = NormalizeUtc(request.ExpiresUtc);
            DateTime normalizedNowUtc = NormalizeUtc(nowUtc);
            if (createdUtc == DateTime.MinValue || expiresUtc == DateTime.MinValue || expiresUtc <= createdUtc)
                return Fail("LifetimeInvalid", "The request lifetime is invalid.", out failureCode, out failureMessage);

            if (createdUtc > normalizedNowUtc.AddMinutes(1))
                return Fail("CreatedInFuture", "The request creation time is too far in the future.", out failureCode, out failureMessage);

            if (expiresUtc <= normalizedNowUtc)
                return Fail("RequestExpired", "The client join request has expired.", out failureCode, out failureMessage);

            if (expiresUtc - createdUtc > TimeSpan.FromMinutes(30))
                return Fail("LifetimeTooLong", "The client join request lifetime exceeds 30 minutes.", out failureCode, out failureMessage);

            if (!IsSha256(request.RunTokenSha256) ||
                !FixedTimeHexEquals(request.RunTokenSha256, configuredRunTokenSha256))
            {
                return Fail("RunTokenMismatch", "The request token hash does not match the configured run token.", out failureCode, out failureMessage);
            }

            if (!IsSha256(request.ExpectedClientModuleSha256) ||
                !FixedTimeHexEquals(request.ExpectedClientModuleSha256, actualClientModuleSha256))
            {
                return Fail("ClientModuleHashMismatch", "The loaded client module hash does not match the requested binary identity.", out failureCode, out failureMessage);
            }

            if (string.IsNullOrWhiteSpace(request.ServerName) || request.ServerName.Length > 128 || ContainsControlCharacter(request.ServerName))
                return Fail("ServerNameInvalid", "The requested server name is invalid.", out failureCode, out failureMessage);

            if (request.ServerPort <= 0 || request.ServerPort > 65535)
                return Fail("ServerPortInvalid", "The requested server port is invalid.", out failureCode, out failureMessage);

            if (ContainsControlCharacter(request.GameType) || ContainsControlCharacter(request.UniqueMapId))
                return Fail("ServerFilterInvalid", "An optional server filter contains a control character.", out failureCode, out failureMessage);

            return true;
        }

        public static CoopAutomationServerSelection SelectExactServer(
            CoopAutomationJoinRequest request,
            IReadOnlyList<CoopAutomationServerDescriptor> servers)
        {
            var result = new CoopAutomationServerSelection
            {
                Status = CoopAutomationServerSelectionStatus.None,
                SelectedIndex = -1,
                MatchingCount = 0
            };

            if (request == null || servers == null)
                return result;

            for (int i = 0; i < servers.Count; i++)
            {
                CoopAutomationServerDescriptor server = servers[i];
                if (!Matches(request, server))
                    continue;

                result.MatchingCount++;
                if (result.SelectedIndex < 0)
                    result.SelectedIndex = i;
            }

            if (result.MatchingCount == 1)
                result.Status = CoopAutomationServerSelectionStatus.Selected;
            else if (result.MatchingCount > 1)
                result.Status = CoopAutomationServerSelectionStatus.Ambiguous;

            return result;
        }

        public static bool IsValidRunId(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId) || runId.Length > 80)
                return false;

            for (int i = 0; i < runId.Length; i++)
            {
                char value = runId[i];
                bool valid = value >= 'a' && value <= 'z' ||
                             value >= 'A' && value <= 'Z' ||
                             value >= '0' && value <= '9' ||
                             value == '.' || value == '_' || value == '-';
                if (!valid)
                    return false;
            }

            char first = runId[0];
            return first >= 'a' && first <= 'z' ||
                   first >= 'A' && first <= 'Z' ||
                   first >= '0' && first <= '9';
        }

        public static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = c >= '0' && c <= '9' ||
                             c >= 'a' && c <= 'f' ||
                             c >= 'A' && c <= 'F';
                if (!isHex)
                    return false;
            }

            return true;
        }

        public static string ComputeSha256Hex(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        public static bool IsTerminalState(string state)
        {
            return string.Equals(state, "Connected", StringComparison.Ordinal) ||
                   string.Equals(state, "Failed", StringComparison.Ordinal) ||
                   string.Equals(state, "Cancelled", StringComparison.Ordinal);
        }

        private static bool Matches(CoopAutomationJoinRequest request, CoopAutomationServerDescriptor server)
        {
            if (server == null ||
                !string.Equals(server.ServerName ?? string.Empty, request.ServerName ?? string.Empty, StringComparison.Ordinal) ||
                server.Port != request.ServerPort)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(request.GameType) &&
                !string.Equals(server.GameType ?? string.Empty, request.GameType, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(request.UniqueMapId) &&
                !string.Equals(server.UniqueMapId ?? string.Empty, request.UniqueMapId, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static bool ContainsControlCharacter(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                    return true;
            }

            return false;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == DateTime.MinValue)
                return value;

            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }

        private static bool FixedTimeHexEquals(string left, string right)
        {
            if (!IsSha256(left) || !IsSha256(right))
                return false;

            int difference = 0;
            for (int i = 0; i < left.Length; i++)
                difference |= char.ToUpperInvariant(left[i]) ^ char.ToUpperInvariant(right[i]);

            return difference == 0;
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("X2"));
            return builder.ToString();
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
