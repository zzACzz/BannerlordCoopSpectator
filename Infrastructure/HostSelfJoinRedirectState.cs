using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using CoopSpectator.DedicatedHelper;

namespace CoopSpectator.Infrastructure
{
    internal static class HostSelfJoinRedirectState
    {
        private const string MarkerFolderName = "CoopSpectator";
        private const string MarkerFileName = "host_self_join.marker";
        private const string HostedPeerFileName = "host_local_peer.marker";
        private static readonly TimeSpan MarkerLifetime = TimeSpan.FromHours(8);

        private static bool _pendingLoopbackRewrite;
        private static int _expectedPort;
        private static string _expectedServerName;
        private static int _activeJoinedHostPort;
        private static string _activeJoinedHostServerName;
        private static string _lastPersistedHostedPeerUserName;

        public static void ClearPendingSelfJoinRewrite()
        {
            _pendingLoopbackRewrite = false;
            _expectedPort = 0;
            _expectedServerName = null;
        }

        public static void ClearPersistedHostSession()
        {
            _activeJoinedHostServerName = null;
            _activeJoinedHostPort = 0;

            try
            {
                if (File.Exists(MarkerFilePath))
                    File.Delete(MarkerFilePath);
            }
            catch (Exception ex)
            {
                ModLogger.Info("HostSelfJoinRedirectState: failed to clear marker. " + ex.Message);
            }
        }

        public static void ClearPersistedHostedPeer()
        {
            _lastPersistedHostedPeerUserName = null;
            _activeJoinedHostServerName = null;
            _activeJoinedHostPort = 0;

            try
            {
                if (File.Exists(HostedPeerMarkerFilePath))
                    File.Delete(HostedPeerMarkerFilePath);
            }
            catch (Exception ex)
            {
                ModLogger.Info("HostSelfJoinRedirectState: failed to clear hosted peer marker. " + ex.Message);
            }
        }

        public static void PersistHostSession(DedicatedServerLaunchSettings settings, int port)
        {
            string normalizedServerName = Normalize(settings?.ServerName);
            if (string.IsNullOrWhiteSpace(normalizedServerName) || port <= 0)
                return;

            try
            {
                Directory.CreateDirectory(MarkerDirectoryPath);
                File.WriteAllLines(
                    MarkerFilePath,
                    new[]
                    {
                        normalizedServerName,
                        port.ToString(),
                        DateTime.UtcNow.Ticks.ToString()
                    });

                ModLogger.Info(
                    "HostSelfJoinRedirectState: persisted local host marker. " +
                    "serverName=" + normalizedServerName +
                    " port=" + port +
                    " path=" + MarkerFilePath + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("HostSelfJoinRedirectState: failed to persist marker. " + ex.Message);
            }
        }

        public static void ArmForNextJoinIfCurrentHost(string serverName, string serverAddress, int port)
        {
            string normalizedServerName = Normalize(serverName);
            if (!MatchesPersistedHostSession(normalizedServerName, port))
                return;

            _pendingLoopbackRewrite = true;
            _expectedPort = port;
            _expectedServerName = normalizedServerName;
            ModLogger.Info(
                "HostSelfJoinRedirectState: armed localhost self-join rewrite. " +
                "serverName=" + normalizedServerName +
                " address=" + (serverAddress ?? "") +
                " port=" + port + ".");
        }

        public static bool TryConsumeLoopbackRewrite(ref string serverAddress, int port, string source)
        {
            if (!_pendingLoopbackRewrite)
                return false;

            if (!MatchesPersistedHostSession(_expectedServerName, _expectedPort))
            {
                ClearPendingSelfJoinRewrite();
                return false;
            }

            if (_expectedPort > 0 && port != _expectedPort)
                return false;

            if (string.IsNullOrWhiteSpace(serverAddress) || IsLoopback(serverAddress))
            {
                ClearPendingSelfJoinRewrite();
                return false;
            }

            string was = serverAddress;
            string expectedServerName = _expectedServerName;
            serverAddress = "127.0.0.1";
            _activeJoinedHostServerName = expectedServerName;
            _activeJoinedHostPort = port;
            ClearPendingSelfJoinRewrite();
            ModLogger.Info(
                "HostSelfJoinRedirectState: " + source +
                " redirecting own dedicated join " +
                "\"" + was + "\" -> 127.0.0.1" +
                " [serverName=" + (expectedServerName ?? "") +
                " port=" + port + "].");
            return true;
        }

        public static bool TryPersistJoinedLocalHostPeer(string userName, string source)
        {
            string normalizedUserName = Normalize(userName);
            if (string.IsNullOrWhiteSpace(normalizedUserName) ||
                string.IsNullOrWhiteSpace(_activeJoinedHostServerName) ||
                _activeJoinedHostPort <= 0)
            {
                return false;
            }

            if (string.Equals(_lastPersistedHostedPeerUserName, normalizedUserName, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(HostedPeerMarkerFilePath))
            {
                return true;
            }

            try
            {
                string activeServerName = _activeJoinedHostServerName;
                int activeHostPort = _activeJoinedHostPort;
                Directory.CreateDirectory(MarkerDirectoryPath);
                File.WriteAllLines(
                    HostedPeerMarkerFilePath,
                    new[]
                    {
                        activeServerName,
                        activeHostPort.ToString(),
                        normalizedUserName,
                        DateTime.UtcNow.Ticks.ToString()
                    });

                _lastPersistedHostedPeerUserName = normalizedUserName;
                _activeJoinedHostServerName = null;
                _activeJoinedHostPort = 0;
                ModLogger.Info(
                    "HostSelfJoinRedirectState: persisted hosted local peer marker. " +
                    "serverName=" + activeServerName +
                    " port=" + activeHostPort +
                    " userName=" + normalizedUserName +
                    " source=" + (source ?? "unknown") + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("HostSelfJoinRedirectState: failed to persist hosted peer marker. " + ex.Message);
                return false;
            }
        }

        public static bool TryResolvePersistedHostedPeerUserName(out string userName)
        {
            userName = string.Empty;

            if (!TryReadHostedPeerMarker(out _, out int _, out string markerUserName, out long markerTicks))
                return false;

            if (string.IsNullOrWhiteSpace(markerUserName))
                return false;

            if (markerTicks <= 0 || IsExpired(markerTicks))
            {
                ClearPersistedHostedPeer();
                return false;
            }

            userName = markerUserName;
            return true;
        }

        private static bool MatchesPersistedHostSession(string serverName, int port)
        {
            if (string.IsNullOrWhiteSpace(serverName) || port <= 0)
                return false;

            if (!TryReadMarker(out string markerServerName, out int markerPort, out long markerTicks))
                return false;

            if (markerPort != port)
                return false;

            if (!string.Equals(markerServerName, serverName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (markerTicks <= 0 || IsExpired(markerTicks))
            {
                ClearPersistedHostSession();
                return false;
            }

            return IsLocalDedicatedPortActive(port);
        }

        private static bool TryReadMarker(out string serverName, out int port, out long createdUtcTicks)
        {
            serverName = string.Empty;
            port = 0;
            createdUtcTicks = 0;

            try
            {
                if (!File.Exists(MarkerFilePath))
                    return false;

                string[] lines = File.ReadAllLines(MarkerFilePath);
                if (lines == null || lines.Length < 3)
                    return false;

                serverName = Normalize(lines[0]);
                int.TryParse(lines[1], out port);
                long.TryParse(lines[2], out createdUtcTicks);
                return !string.IsNullOrWhiteSpace(serverName) && port > 0;
            }
            catch (Exception ex)
            {
                ModLogger.Info("HostSelfJoinRedirectState: failed to read marker. " + ex.Message);
                return false;
            }
        }

        private static bool TryReadHostedPeerMarker(out string serverName, out int port, out string userName, out long createdUtcTicks)
        {
            serverName = string.Empty;
            port = 0;
            userName = string.Empty;
            createdUtcTicks = 0;

            try
            {
                if (!File.Exists(HostedPeerMarkerFilePath))
                    return false;

                string[] lines = File.ReadAllLines(HostedPeerMarkerFilePath);
                if (lines == null || lines.Length < 4)
                    return false;

                serverName = Normalize(lines[0]);
                int.TryParse(lines[1], out port);
                userName = Normalize(lines[2]);
                long.TryParse(lines[3], out createdUtcTicks);
                return !string.IsNullOrWhiteSpace(userName);
            }
            catch (Exception ex)
            {
                ModLogger.Info("HostSelfJoinRedirectState: failed to read hosted peer marker. " + ex.Message);
                return false;
            }
        }

        private static bool IsExpired(long createdUtcTicks)
        {
            try
            {
                DateTime createdUtc = new DateTime(createdUtcTicks, DateTimeKind.Utc);
                return DateTime.UtcNow - createdUtc > MarkerLifetime;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static bool IsLocalDedicatedPortActive(int port)
        {
            try
            {
                IPEndPoint[] udpListeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();
                for (int i = 0; i < udpListeners.Length; i++)
                {
                    IPEndPoint endpoint = udpListeners[i];
                    if (endpoint != null && endpoint.Port == port)
                        return true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("HostSelfJoinRedirectState: UDP listener probe failed. " + ex.Message);
            }

            return false;
        }

        private static bool IsLoopback(string serverAddress)
        {
            return string.Equals(serverAddress, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(serverAddress, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(serverAddress, "::1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(serverAddress, "[::1]", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string MarkerDirectoryPath => Path.Combine(Path.GetTempPath(), MarkerFolderName);
        private static string MarkerFilePath => Path.Combine(MarkerDirectoryPath, MarkerFileName);
        private static string HostedPeerMarkerFilePath => Path.Combine(MarkerDirectoryPath, HostedPeerFileName);
    }
}
