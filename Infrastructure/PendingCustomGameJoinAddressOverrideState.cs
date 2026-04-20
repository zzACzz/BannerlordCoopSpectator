using System;

namespace CoopSpectator.Infrastructure
{
    internal static class PendingCustomGameJoinAddressOverrideState
    {
        private static readonly TimeSpan MarkerLifetime = TimeSpan.FromMinutes(5);

        private static string _pendingServerName;
        private static string _pendingOriginalAddress;
        private static int _pendingPort;
        private static string _pendingAdvertisedHostAddress;
        private static long _createdUtcTicks;

        public static void Clear(string reason = null)
        {
            bool hadState =
                !string.IsNullOrWhiteSpace(_pendingServerName) ||
                !string.IsNullOrWhiteSpace(_pendingOriginalAddress) ||
                _pendingPort > 0 ||
                !string.IsNullOrWhiteSpace(_pendingAdvertisedHostAddress);

            _pendingServerName = string.Empty;
            _pendingOriginalAddress = string.Empty;
            _pendingPort = 0;
            _pendingAdvertisedHostAddress = string.Empty;
            _createdUtcTicks = 0;

            if (hadState && !string.IsNullOrWhiteSpace(reason))
            {
                ModLogger.Info("PendingCustomGameJoinAddressOverrideState: cleared pending VPN redirect. reason=" + reason + ".");
            }
        }

        public static bool Arm(string serverName, string originalAddress, int port, string advertisedHostAddress)
        {
            string normalizedAdvertisedHost = Normalize(advertisedHostAddress);
            if (string.IsNullOrWhiteSpace(normalizedAdvertisedHost) || port <= 0)
                return false;

            _pendingServerName = Normalize(serverName);
            _pendingOriginalAddress = Normalize(originalAddress);
            _pendingPort = port;
            _pendingAdvertisedHostAddress = normalizedAdvertisedHost;
            _createdUtcTicks = DateTime.UtcNow.Ticks;
            return true;
        }

        public static bool TryConsume(string serverName, string originalAddress, int port, out string advertisedHostAddress)
        {
            advertisedHostAddress = string.Empty;

            if (_pendingPort <= 0 || string.IsNullOrWhiteSpace(_pendingAdvertisedHostAddress))
                return false;

            if (IsExpired(_createdUtcTicks))
            {
                Clear("expired");
                return false;
            }

            if (_pendingPort != port)
                return false;

            string normalizedServerName = Normalize(serverName);
            string normalizedOriginalAddress = Normalize(originalAddress);
            if (!string.Equals(_pendingServerName, normalizedServerName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(_pendingOriginalAddress, normalizedOriginalAddress, StringComparison.OrdinalIgnoreCase))
                return false;

            advertisedHostAddress = _pendingAdvertisedHostAddress;
            Clear();
            return true;
        }

        private static bool IsExpired(long createdUtcTicks)
        {
            if (createdUtcTicks <= 0)
                return true;

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

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
