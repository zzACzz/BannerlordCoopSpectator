using System;
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

        public DedicatedServerLaunchSettings Clone()
        {
            return new DedicatedServerLaunchSettings
            {
                ServerName = ServerName,
                ServerPassword = ServerPassword,
                AdminPassword = AdminPassword,
                MaxPlayerCount = MaxPlayerCount
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
                MaxPlayerCount = ClampToAllowedPlayerCount(DefaultMaxPlayerCount)
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

            error = null;
            return true;
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
