using System;
using CoopSpectator.DedicatedHelper;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;

namespace CoopSpectator.Campaign
{
    public sealed class CoopDedicatedServerSettingsVM : ViewModel
    {
        private readonly Action _onClose;
        private string _serverName;
        private string _serverPassword;
        private string _adminPassword;
        private int _maxPlayerCount;
        private string _statusText;
        private string _tokenStatusText;
        private string _tokenHelpText;
        private bool _isServerPasswordObfuscated = true;
        private bool _isAdminPasswordObfuscated = true;
        private bool _canStartServer;

        public CoopDedicatedServerSettingsVM(Action onClose)
        {
            _onClose = onClose;

            DedicatedServerLaunchSettings currentSettings = DedicatedHelperLauncher.GetCurrentLaunchSettings();
            _serverName = currentSettings.ServerName;
            _serverPassword = currentSettings.ServerPassword;
            _adminPassword = currentSettings.AdminPassword;
            _maxPlayerCount = DedicatedServerLaunchSettings.ClampToAllowedPlayerCount(currentSettings.MaxPlayerCount);
            _statusText = "Ready.";
            RefreshTokenStatus();
        }

        [DataSourceProperty] public string TitleText => "Coop Dedicated Server";
        [DataSourceProperty] public string DescriptionText => "Configure the current dedicated bootstrap path from campaign map pause menu.";
        [DataSourceProperty] public string LaunchNotesText => "Reuses the current modded dedicated launch path on port 7210. Current bootstrap map remains the existing listed TDM scene until battle handoff changes it.";
        [DataSourceProperty] public string PlayerCountHintText => "Current bootstrap mode is TeamDeathmatch, so Max Player Count is limited to the official " + MinPlayerCount + "-" + MaxAllowedPlayerCount + " range.";
        [DataSourceProperty] public string ServerName { get => _serverName; set => SetField(ref _serverName, value, nameof(ServerName)); }
        [DataSourceProperty] public string ServerPassword { get => _serverPassword; set => SetField(ref _serverPassword, value, nameof(ServerPassword)); }
        [DataSourceProperty] public string AdminPassword { get => _adminPassword; set => SetField(ref _adminPassword, value, nameof(AdminPassword)); }
        [DataSourceProperty] public int MaxPlayerCount
        {
            get => _maxPlayerCount;
            set => SetField(ref _maxPlayerCount, DedicatedServerLaunchSettings.ClampToAllowedPlayerCount(value), nameof(MaxPlayerCount));
        }
        [DataSourceProperty] public int MinPlayerCount => DedicatedServerLaunchSettings.GetMinAllowedPlayerCount();
        [DataSourceProperty] public int MaxAllowedPlayerCount => DedicatedServerLaunchSettings.GetMaxAllowedPlayerCount();
        [DataSourceProperty] public string StatusText { get => _statusText; private set => SetField(ref _statusText, value, nameof(StatusText)); }
        [DataSourceProperty] public string TokenStatusText { get => _tokenStatusText; private set => SetField(ref _tokenStatusText, value, nameof(TokenStatusText)); }
        [DataSourceProperty] public string TokenHelpText { get => _tokenHelpText; private set => SetField(ref _tokenHelpText, value, nameof(TokenHelpText)); }
        [DataSourceProperty] public bool IsServerPasswordObfuscated { get => _isServerPasswordObfuscated; private set => SetField(ref _isServerPasswordObfuscated, value, nameof(IsServerPasswordObfuscated)); }
        [DataSourceProperty] public bool IsAdminPasswordObfuscated { get => _isAdminPasswordObfuscated; private set => SetField(ref _isAdminPasswordObfuscated, value, nameof(IsAdminPasswordObfuscated)); }
        [DataSourceProperty] public bool CanStartServer { get => _canStartServer; private set => SetField(ref _canStartServer, value, nameof(CanStartServer)); }
        [DataSourceProperty] public string ServerPasswordToggleText => IsServerPasswordObfuscated ? "Show" : "Hide";
        [DataSourceProperty] public string AdminPasswordToggleText => IsAdminPasswordObfuscated ? "Show" : "Hide";

        public void ExecuteStartServer()
        {
            RefreshTokenStatus();
            if (!CanStartServer)
            {
                string message = "Token missing. In Bannerlord Multiplayer open Console (ALT+~), run customserver.gettoken, then refresh this panel.";
                StatusText = message;
                UiFeedback.ShowMessageDeferred(message);
                return;
            }

            DedicatedServerLaunchSettings requested = new DedicatedServerLaunchSettings
            {
                ServerName = ServerName,
                ServerPassword = ServerPassword,
                AdminPassword = AdminPassword,
                MaxPlayerCount = MaxPlayerCount
            };

            DedicatedServerLaunchSettings currentDefaults = DedicatedHelperLauncher.GetCurrentLaunchSettings();
            if (!DedicatedServerLaunchSettings.TryValidateAndNormalize(
                    requested,
                    currentDefaults.ServerName,
                    currentDefaults.AdminPassword,
                    out DedicatedServerLaunchSettings normalized,
                    out string error))
            {
                StatusText = error;
                UiFeedback.ShowMessageDeferred(error);
                return;
            }

            ServerName = normalized.ServerName;
            ServerPassword = normalized.ServerPassword;
            AdminPassword = normalized.AdminPassword;
            MaxPlayerCount = normalized.MaxPlayerCount;

            ModLogger.Info(
                "CoopDedicatedServerSettingsVM: Start Server requested. " +
                "serverName=" + ServerName +
                " serverPasswordSet=" + (!string.IsNullOrWhiteSpace(ServerPassword)) +
                " adminPasswordSet=" + (!string.IsNullOrWhiteSpace(AdminPassword)) +
                " maxPlayers=" + MaxPlayerCount + ".");

            StatusText = DedicatedHelperLauncher.Start(null, 7210, normalized);
        }

        public void ExecuteToggleServerPasswordVisibility()
        {
            IsServerPasswordObfuscated = !IsServerPasswordObfuscated;
            OnPropertyChanged(nameof(ServerPasswordToggleText));
        }

        public void ExecuteToggleAdminPasswordVisibility()
        {
            IsAdminPasswordObfuscated = !IsAdminPasswordObfuscated;
            OnPropertyChanged(nameof(AdminPasswordToggleText));
        }

        public void ExecuteOpenTokensFolder()
        {
            DedicatedHelperLauncher.OpenTokensFolderInExplorer();
            RefreshTokenStatus();
            StatusText = "Opened Tokens folder. Generate or paste the token there, then refresh token status.";
        }

        public void ExecuteRefreshTokenStatus()
        {
            RefreshTokenStatus();
        }

        public void ExecuteDone()
        {
            _onClose?.Invoke();
        }

        private void RefreshTokenStatus()
        {
            bool hasToken = DedicatedHelperLauncher.TryReadTokenFromFolder(out string token, out string folderWhereFound)
                            && !string.IsNullOrWhiteSpace(token);
            string targetFolder = DedicatedHelperLauncher.GetTokensFolderPath() ?? "(unknown)";

            CanStartServer = hasToken;
            TokenStatusText = hasToken
                ? "Token Status: found in " + folderWhereFound
                : "Token Status: missing. The current dedicated launch path expects a custom server token.";
            TokenHelpText = hasToken
                ? "If the token expires, regenerate it in Bannerlord Multiplayer -> Console (ALT+~) with customserver.gettoken, then refresh this panel."
                : "Get it in Bannerlord Multiplayer -> Console (ALT+~) -> customserver.gettoken. Expected folder: " + targetFolder;

            if (hasToken)
            {
                if (string.Equals(StatusText, "Ready.", StringComparison.Ordinal) ||
                    StatusText.StartsWith("Token Status:", StringComparison.Ordinal) ||
                    StatusText.StartsWith("Token missing.", StringComparison.Ordinal) ||
                    StatusText.StartsWith("Opened Tokens folder.", StringComparison.Ordinal))
                {
                    StatusText = "Ready. Token detected, dedicated start is available.";
                }
            }
            else
            {
                StatusText = "Token missing. Dedicated start from this menu stays disabled until a token is found.";
            }
        }
    }
}
