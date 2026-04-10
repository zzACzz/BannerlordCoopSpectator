using System;
using System.Net;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.DedicatedHelper
{
    public sealed class DedicatedServerLaunchSettings
    {
        private const int DefaultMaxPlayerCount = 100;
        private const string OfficialBootstrapPlayerCountGameType = "TeamDeathmatch";
        private const int OfficialBootstrapPlayerCountLimitFallback = 120;

        public string ServerName { get; set; }
        public string ServerPassword { get; set; }
        public string AdminPassword { get; set; }
        public int MaxPlayerCount { get; set; }
        public DedicatedServerHostingMode HostingMode { get; set; }
        public string AdvertisedHostAddress { get; set; }

        public DedicatedServerLaunchSettings Clone()
        {
            return new DedicatedServerLaunchSettings
            {
                ServerName = ServerName,
                ServerPassword = ServerPassword,
                AdminPassword = AdminPassword,
                MaxPlayerCount = MaxPlayerCount,
                HostingMode = HostingMode,
                AdvertisedHostAddress = AdvertisedHostAddress
            };
        }

        public static int GetMinAllowedPlayerCount() => MultiplayerOptions.OptionType.MaxNumberOfPlayers.GetMinimumValue();

        public static int GetMaxAllowedPlayerCount()
        {
            int nativeMax = MultiplayerOptions.OptionType.MaxNumberOfPlayers.GetMaximumValue();
            int bootstrapModeMax = GetOfficialBootstrapPlayerCountLimit();
            return Math.Min(nativeMax, bootstrapModeMax);
        }

        public static int ClampToAllowedPlayerCount(int value)
        {
            int minPlayers = GetMinAllowedPlayerCount();
            int maxPlayers = GetMaxAllowedPlayerCount();
            if (value < minPlayers)
                return minPlayers;

            if (value > maxPlayers)
                return maxPlayers;

            return value;
        }

        public static DedicatedServerLaunchSettings CreateDefault(string defaultServerName, string defaultAdminPassword)
        {
            return new DedicatedServerLaunchSettings
            {
                ServerName = NormalizeSingleLine(defaultServerName),
                ServerPassword = string.Empty,
                AdminPassword = NormalizeSingleLine(defaultAdminPassword),
                MaxPlayerCount = ClampToAllowedPlayerCount(DefaultMaxPlayerCount),
                HostingMode = DedicatedServerHostingMode.PublicListed,
                AdvertisedHostAddress = string.Empty
            };
        }

        public static bool TryValidateAndNormalize(
            DedicatedServerLaunchSettings source,
            string defaultServerName,
            string defaultAdminPassword,
            out DedicatedServerLaunchSettings normalized,
            out string error)
        {
            normalized = CreateDefault(defaultServerName, defaultAdminPassword);

            if (source != null)
            {
                normalized.ServerName = NormalizeSingleLine(source.ServerName);
                normalized.ServerPassword = NormalizeSingleLine(source.ServerPassword);
                normalized.AdminPassword = NormalizeSingleLine(source.AdminPassword);
                normalized.MaxPlayerCount = ClampToAllowedPlayerCount(source.MaxPlayerCount);
                normalized.HostingMode = NormalizeHostingMode(source.HostingMode);
                normalized.AdvertisedHostAddress = NormalizeHostAddress(source.AdvertisedHostAddress);
            }

            if (string.IsNullOrWhiteSpace(normalized.ServerName))
            {
                error = "Dedicated Server settings: Server Name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(normalized.AdminPassword))
            {
                error = "Dedicated Server settings: Admin Password is required.";
                return false;
            }

            if (normalized.HostingMode == DedicatedServerHostingMode.VpnOverlay)
            {
                if (string.IsNullOrWhiteSpace(normalized.AdvertisedHostAddress))
                {
                    error = "Dedicated Server settings: VPN/Overlay mode requires Advertised Host Address.";
                    return false;
                }

                if (IsLoopbackHostAddress(normalized.AdvertisedHostAddress))
                {
                    error = "Dedicated Server settings: Advertised Host Address cannot be localhost in VPN/Overlay mode.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public bool UsesAdvertisedHostOverride()
        {
            return HostingMode == DedicatedServerHostingMode.VpnOverlay &&
                   !string.IsNullOrWhiteSpace(AdvertisedHostAddress);
        }

        private static int GetOfficialBootstrapPlayerCountLimit()
        {
            try
            {
                int officialModePlayerCount = MultiplayerOptions.Instance.GetNumberOfPlayersForGameMode(OfficialBootstrapPlayerCountGameType);
                if (officialModePlayerCount > 0)
                    return officialModePlayerCount;
            }
            catch (Exception)
            {
            }

            return OfficialBootstrapPlayerCountLimitFallback;
        }

        private static DedicatedServerHostingMode NormalizeHostingMode(DedicatedServerHostingMode mode)
        {
            return Enum.IsDefined(typeof(DedicatedServerHostingMode), mode)
                ? mode
                : DedicatedServerHostingMode.PublicListed;
        }

        private static string NormalizeHostAddress(string value)
        {
            return NormalizeSingleLine(value);
        }

        private static bool IsLoopbackHostAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();
            if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "::1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "[::1]", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IPAddress.TryParse(normalized, out IPAddress address))
                return IPAddress.IsLoopback(address);

            return false;
        }

        private static string NormalizeSingleLine(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
        }
    }
}
