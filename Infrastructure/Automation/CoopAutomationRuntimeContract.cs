using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CoopSpectator.Infrastructure.Automation
{
    public enum CoopAutomationResultPublicationDecision
    {
        Publish = 0,
        Suppress = 1,
        Reject = 2
    }

    public sealed class CoopAutomationRuntimeConfiguration
    {
        public string RunId { get; set; }
        public string RunRoot { get; set; }
        public string RunTokenSha256 { get; set; }
        public string ExpectedModuleSha256 { get; set; }
        public string ResultPolicy { get; set; }
    }

    public sealed class CoopAutomationRuntimeRoleStatus
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
        public string ResultPolicy { get; set; }
        public string FailureCode { get; set; }
        public string FailureMessage { get; set; }
    }

    public sealed class CoopAutomationOwnedHostStatus
    {
        public int SchemaVersion { get; set; }
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public string RunTokenSha256 { get; set; }
        public string ServerName { get; set; }
        public int ServerPort { get; set; }
        public int OwnerProcessId { get; set; }
        public DateTime OwnerProcessStartUtc { get; set; }
        public string OwnerExecutablePath { get; set; }
        public string Protocol { get; set; }
        public DateTime ConfirmedUtc { get; set; }
    }

    public sealed class CoopAutomationResultPublicationStatus
    {
        public int SchemaVersion { get; set; }
        public int ProtocolMajorVersion { get; set; }
        public int ProtocolMinorVersion { get; set; }
        public string RunId { get; set; }
        public string RunTokenSha256 { get; set; }
        public string ResultPolicy { get; set; }
        public string Decision { get; set; }
        public string BattleId { get; set; }
        public string Source { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public string FailureCode { get; set; }
        public string FailureMessage { get; set; }
    }

    public static class CoopAutomationRuntimeContract
    {
        public const int CurrentSchemaVersion = 1;
        public const int CurrentProtocolMajorVersion = 1;
        public const int CurrentProtocolMinorVersion = 0;
        public const string SuppressResultPolicy = "Suppress";
        public const string OwnedHostRelativePath = "state/dedicated-host.json";
        public const string ResultPublicationRelativePath = "state/result-publication.status.json";

        public static bool TryCreateConfiguration(
            bool automationEnabled,
            string runId,
            string runRootValue,
            string runToken,
            string expectedModuleSha256,
            string resultPolicy,
            out CoopAutomationRuntimeConfiguration configuration,
            out string failureCode,
            out string failureMessage)
        {
            configuration = null;
            failureCode = string.Empty;
            failureMessage = string.Empty;

            if (!automationEnabled)
                return Fail("AutomationDisabled", "Test automation is disabled.", out failureCode, out failureMessage);

            string normalizedRunId = (runId ?? string.Empty).Trim();
            if (!IsValidRunId(normalizedRunId))
                return Fail("RunIdInvalid", "The configured automation RunId is invalid.", out failureCode, out failureMessage);

            if (string.IsNullOrWhiteSpace(runRootValue))
                return Fail("RunRootMissing", "The configured automation run root is missing.", out failureCode, out failureMessage);

            string runRoot;
            string expectedRunRoot;
            try
            {
                runRoot = TrimDirectorySeparator(Path.GetFullPath(runRootValue));
                expectedRunRoot = TrimDirectorySeparator(Path.GetFullPath(
                    Path.Combine(Path.GetTempPath(), "CoopSpectator", "Automation", normalizedRunId)));
            }
            catch (Exception ex)
            {
                return Fail("RunRootInvalid", "The configured automation run root is invalid: " + ex.Message, out failureCode, out failureMessage);
            }

            if (!string.Equals(runRoot, expectedRunRoot, StringComparison.OrdinalIgnoreCase))
                return Fail("RunRootMismatch", "The automation run root is outside the required RunId-scoped temp path.", out failureCode, out failureMessage);

            if (string.IsNullOrEmpty(runToken) || runToken.Length < 32)
                return Fail("RunTokenInvalid", "The configured automation run token is missing or too short.", out failureCode, out failureMessage);

            string normalizedExpectedHash = NormalizeSha256(expectedModuleSha256);
            if (!IsSha256(normalizedExpectedHash))
                return Fail("ExpectedModuleHashInvalid", "The expected module SHA-256 is missing or invalid.", out failureCode, out failureMessage);

            string normalizedResultPolicy = (resultPolicy ?? string.Empty).Trim();
            if (!string.Equals(normalizedResultPolicy, SuppressResultPolicy, StringComparison.Ordinal))
                return Fail("ResultPolicyUnsupported", "Milestone 2B requires the exact Suppress result policy.", out failureCode, out failureMessage);

            configuration = new CoopAutomationRuntimeConfiguration
            {
                RunId = normalizedRunId,
                RunRoot = runRoot,
                RunTokenSha256 = ComputeSha256Hex(runToken),
                ExpectedModuleSha256 = normalizedExpectedHash,
                ResultPolicy = normalizedResultPolicy
            };
            return true;
        }

        public static CoopAutomationResultPublicationDecision ResolveResultPublicationDecision(
            bool automationEnabled,
            bool configurationValid,
            string resultPolicy)
        {
            if (!automationEnabled)
                return CoopAutomationResultPublicationDecision.Publish;

            if (!configurationValid)
                return CoopAutomationResultPublicationDecision.Reject;

            return string.Equals(resultPolicy, SuppressResultPolicy, StringComparison.Ordinal)
                ? CoopAutomationResultPublicationDecision.Suppress
                : CoopAutomationResultPublicationDecision.Reject;
        }

        public static bool TryValidateOwnedHost(
            CoopAutomationOwnedHostStatus host,
            CoopAutomationRuntimeConfiguration configuration,
            string expectedServerName,
            int expectedPort,
            DateTime nowUtc,
            out string failureCode,
            out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (host == null)
                return Fail("OwnedHostMissing", "The run-scoped owned-host status is missing.", out failureCode, out failureMessage);
            if (configuration == null)
                return Fail("RuntimeConfigurationMissing", "The runtime automation configuration is missing.", out failureCode, out failureMessage);
            if (host.SchemaVersion != CurrentSchemaVersion ||
                host.ProtocolMajorVersion != CurrentProtocolMajorVersion ||
                host.ProtocolMinorVersion > CurrentProtocolMinorVersion)
            {
                return Fail("OwnedHostProtocolUnsupported", "The owned-host status protocol is unsupported.", out failureCode, out failureMessage);
            }
            if (!string.Equals(host.RunId ?? string.Empty, configuration.RunId, StringComparison.Ordinal))
                return Fail("OwnedHostRunMismatch", "The owned-host status belongs to another run.", out failureCode, out failureMessage);
            if (!FixedTimeHexEquals(host.RunTokenSha256, configuration.RunTokenSha256))
                return Fail("OwnedHostTokenMismatch", "The owned-host status token hash does not match the configured run.", out failureCode, out failureMessage);
            if (!string.Equals(host.ServerName ?? string.Empty, expectedServerName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return Fail("OwnedHostServerNameMismatch", "The owned-host server name does not match the requested server.", out failureCode, out failureMessage);
            if (host.ServerPort <= 0 || host.ServerPort != expectedPort)
                return Fail("OwnedHostPortMismatch", "The owned-host port does not match the requested server.", out failureCode, out failureMessage);
            if (host.OwnerProcessId <= 0 || host.OwnerProcessStartUtc == default(DateTime) || string.IsNullOrWhiteSpace(host.OwnerExecutablePath))
                return Fail("OwnedHostProcessIdentityInvalid", "The owned-host process identity is incomplete.", out failureCode, out failureMessage);
            if (!string.Equals(host.Protocol ?? string.Empty, "UDP", StringComparison.OrdinalIgnoreCase))
                return Fail("OwnedHostProtocolInvalid", "The owned-host network protocol is not UDP.", out failureCode, out failureMessage);
            if (host.ConfirmedUtc == default(DateTime) || host.ConfirmedUtc.ToUniversalTime() > nowUtc.ToUniversalTime().AddMinutes(1) ||
                nowUtc.ToUniversalTime() - host.ConfirmedUtc.ToUniversalTime() > TimeSpan.FromMinutes(30))
            {
                return Fail("OwnedHostStatusStale", "The owned-host status is stale or from the future.", out failureCode, out failureMessage);
            }

            return true;
        }

        public static bool DoesLiveProcessMatch(
            CoopAutomationOwnedHostStatus host,
            int processId,
            DateTime processStartUtc,
            string executablePath)
        {
            if (host == null || processId <= 0)
                return false;

            DateTime expectedStartUtc = host.OwnerProcessStartUtc.ToUniversalTime();
            DateTime actualStartUtc = processStartUtc.ToUniversalTime();
            return host.OwnerProcessId == processId &&
                   Math.Abs((expectedStartUtc - actualStartUtc).TotalSeconds) < 1.0 &&
                   string.Equals(
                       NormalizePath(host.OwnerExecutablePath),
                       NormalizePath(executablePath),
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidRunId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || !char.IsLetterOrDigit(value[0]))
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                    return false;
            }
            return true;
        }

        public static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9') ||
                             (c >= 'a' && c <= 'f') ||
                             (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }
            return true;
        }

        public static string ComputeSha256Hex(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return ToHex(hash);
            }
        }

        public static string ComputeFileSha256(string path)
        {
            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        public static string CombineRunPath(string runRoot, string relativePath)
        {
            return Path.Combine(
                runRoot,
                (relativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
        }

        private static string NormalizeSha256(string value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            try { return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return value.Trim(); }
        }

        private static string TrimDirectorySeparator(string path)
        {
            return (path ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder((bytes?.Length ?? 0) * 2);
            for (int i = 0; i < (bytes?.Length ?? 0); i++)
                builder.Append(bytes[i].ToString("X2"));
            return builder.ToString();
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
