using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Automation;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Multiplayer.Automation
{
    public static class CoopLobbyAutomationController
    {
        private static readonly object StateLock = new object();
        private static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan ServerListRetryInterval = TimeSpan.FromSeconds(5);

        private static bool _initialized;
        private static bool _configurationFailed;
        private static bool _terminal;
        private static bool _cancelRequested;
        private static bool _joinAccepted;
        private static bool _networkHandoffObserved;
        private static string _networkHandoffAddress = string.Empty;
        private static int _networkHandoffPort;
        private static DateTime _nextPumpUtc = DateTime.MinValue;
        private static DateTime _nextServerListUtc = DateTime.MinValue;
        private static CoopAutomationJoinConfiguration _configuration;
        private static CoopAutomationJoinRequest _request;
        private static object _lobbyClient;
        private static Task _platformLoginTask;
        private static Task _serverListTask;
        private static Task _joinTask;
        private static CoopObservedLobbyServer _selectedServer;
        private static bool _platformLoginAttempted;
        private static string _platformLoginTaskState = "NotStarted";
        private static string _platformLoginOutcome = "NotAttempted";
        private static string _state = "Disabled";
        private static string _lobbyState = string.Empty;
        private static string _lastFailureCode = string.Empty;
        private static string _lastFailureMessage = string.Empty;

        public static void PumpApplicationTick()
        {
            if (!ExperimentalFeatures.EnableTestAutomation || _terminal)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextPumpUtc)
                return;
            _nextPumpUtc = nowUtc.Add(PumpInterval);

            if (!_initialized)
                Initialize();

            if (_configurationFailed || _terminal || _configuration == null || _request == null)
                return;

            if (_cancelRequested)
            {
                Transition("Cancelled", "", "CancelledByCommand", "The automation join was cancelled by an explicit command.");
                return;
            }

            // Expiry bounds whether a native platform-login or join task may be started.
            // Once TaleWorlds owns either non-cancellable task, the module must not report
            // a false terminal failure while it can still complete in the background.
            if (_request.ExpiresUtc.ToUniversalTime() <= nowUtc &&
                _platformLoginTask == null &&
                _joinTask == null &&
                !_joinAccepted &&
                !_networkHandoffObserved)
            {
                Transition("Failed", _lobbyState, "RequestExpired", "The client join request expired before native platform login or join was started.");
                return;
            }

            if (_platformLoginTask != null)
            {
                PumpPlatformLoginTask();
                return;
            }

            if (_joinTask != null)
            {
                PumpJoinTask();
                return;
            }

            if (_serverListTask != null)
            {
                PumpServerListTask(nowUtc);
                return;
            }

            if (_networkHandoffObserved)
            {
                if (GameNetwork.IsClient && GameNetwork.IsSessionActive)
                {
                    Transition("Connected", _lobbyState, "", "");
                    return;
                }

                Transition("NetworkHandoff", _lobbyState, "", "");
                return;
            }

            if (_joinAccepted)
                return;

            if (nowUtc < _nextServerListUtc)
                return;

            if (!CoopLobbyAutomationDriver.TryGetLobbyClient(out _lobbyClient, out _lobbyState, out string discoveryFailure))
            {
                Transition("WaitingForLobby", _lobbyState, "", discoveryFailure);
                return;
            }

            if (!CoopLobbyAutomationDriver.IsReadyForServerList(_lobbyClient, _lobbyState))
            {
                if (string.Equals(_lobbyState, "Idle", StringComparison.Ordinal))
                {
                    if (_platformLoginAttempted)
                    {
                        _platformLoginOutcome = "StillIdle";
                        Transition(
                            "Failed",
                            _lobbyState,
                            "PlatformLoginStillIdle",
                            "The native platform login task completed but the lobby client remained Idle.");
                        return;
                    }

                    StartPlatformLogin();
                    return;
                }

                if (_platformLoginAttempted)
                {
                    Transition(
                        "WaitingForLobbyAfterPlatformLogin",
                        _lobbyState,
                        "",
                        "The native platform login task completed and the lobby is still transitioning.");
                    return;
                }

                Transition("WaitingForLobby", _lobbyState, "", "The lobby client is not ready for custom-server discovery.");
                return;
            }

            if (_platformLoginAttempted && !string.Equals(_platformLoginOutcome, "Succeeded", StringComparison.Ordinal))
            {
                _platformLoginOutcome = "Succeeded";
                Transition("PlatformLoginSucceeded", _lobbyState, "", "");
                return;
            }
            if (!_platformLoginAttempted)
                _platformLoginOutcome = "NotRequiredAlreadyAtLobby";

            if (!CoopLobbyAutomationDriver.TryStartServerListRequest(_lobbyClient, out _serverListTask, out string listFailure))
            {
                Transition("Failed", _lobbyState, "ServerListRequestFailed", listFailure);
                return;
            }

            Transition("RequestingServerList", _lobbyState, "", "");
        }

        public static string GetStatusSummary()
        {
            lock (StateLock)
            {
                return "AutomationJoin State=" + _state +
                       " RunId=" + (_configuration?.RunId ?? "(not configured)") +
                       " LobbyState=" + (_lobbyState ?? string.Empty) +
                       " PlatformLogin=" + _platformLoginOutcome +
                       " Server=" + (_selectedServer?.Descriptor?.ServerName ?? _request?.ServerName ?? string.Empty) +
                       ":" + (_selectedServer?.Descriptor?.Port ?? _request?.ServerPort ?? 0) +
                       (string.IsNullOrWhiteSpace(_lastFailureCode)
                           ? string.Empty
                           : " Failure=" + _lastFailureCode + ": " + _lastFailureMessage) + ".";
            }
        }

        public static bool TryArmConfiguredRun(string runId, out string message)
        {
            message = string.Empty;
            if (!ExperimentalFeatures.EnableTestAutomation)
            {
                message = "ERROR: COOPSPECTATOR_TEST_AUTOMATION=1 is required.";
                return false;
            }

            if (!_initialized)
                Initialize();

            if (_configuration == null || _request == null)
            {
                message = "ERROR: Automation run configuration or request is unavailable.";
                return false;
            }

            if (!string.Equals(runId ?? string.Empty, _configuration.RunId, StringComparison.Ordinal))
            {
                message = "ERROR: The requested RunId does not match the configured run.";
                return false;
            }

            if (_terminal)
            {
                message = "ERROR: The configured command already reached a terminal state. Use a new RunId.";
                return false;
            }

            _nextServerListUtc = DateTime.MinValue;
            message = "OK: Automation join is armed for RunId=" + _configuration.RunId + ".";
            return true;
        }

        public static bool TryCancel(out string message)
        {
            if (!ExperimentalFeatures.EnableTestAutomation || _terminal ||
                _platformLoginAttempted || _platformLoginTask != null ||
                _joinTask != null || _joinAccepted || _networkHandoffObserved)
            {
                message = "ERROR: No safely cancellable automation join is active. A native platform login or join already in progress cannot be marked cancelled.";
                return false;
            }

            _cancelRequested = true;
            message = "OK: Automation join cancellation requested.";
            return true;
        }

        public static void NotifyStartMultiplayerHandoff(
            string serverAddress,
            int port,
            int sessionKey,
            int peerIndex)
        {
            if (!ExperimentalFeatures.EnableTestAutomation || _terminal || _request == null)
                return;

            if (port != _request.ServerPort)
                return;

            lock (StateLock)
            {
                _networkHandoffObserved = true;
                _networkHandoffAddress = serverAddress ?? string.Empty;
                _networkHandoffPort = port;
            }

            ModLogger.Info(
                "AutomationJoin: native lobby handoff observed. " +
                "RunId=" + _request.RunId +
                " address=" + (serverAddress ?? string.Empty) +
                " port=" + port +
                " sessionKey=" + sessionKey +
                " peerIndex=" + peerIndex + ".");
        }

        private static void Initialize()
        {
            _initialized = true;
            if (!CoopAutomationJoinBridge.TryResolveConfiguration(
                    out _configuration,
                    out string configurationFailureCode,
                    out string configurationFailureMessage))
            {
                _configurationFailed = true;
                _state = "Failed";
                _lastFailureCode = configurationFailureCode;
                _lastFailureMessage = configurationFailureMessage;
                ModLogger.Error("AutomationJoin configuration rejected: " + configurationFailureCode + ": " + configurationFailureMessage, null);
                return;
            }

            if (!CoopAutomationJoinBridge.TryReadRequest(
                    _configuration,
                    out _request,
                    out string requestFailureCode,
                    out string requestFailureMessage))
            {
                _configurationFailed = true;
                _state = "Failed";
                _lastFailureCode = requestFailureCode;
                _lastFailureMessage = requestFailureMessage;
                ModLogger.Error("AutomationJoin request rejected: " + requestFailureCode + ": " + requestFailureMessage, null);
                if (_configuration != null && _request != null)
                {
                    try
                    {
                        CoopAutomationJoinBridge.WriteStatus(
                            _configuration,
                            _request,
                            "Failed",
                            string.Empty,
                            null,
                            requestFailureCode,
                            requestFailureMessage);
                    }
                    catch (Exception statusException)
                    {
                        ModLogger.Error("AutomationJoin rejected-request status write failed.", statusException);
                    }
                }
                return;
            }

            CoopAutomationJoinStatus existingStatus = CoopAutomationJoinBridge.TryReadStatus(_configuration);
            if (existingStatus != null &&
                string.Equals(existingStatus.CommandId, _request.CommandId, StringComparison.Ordinal) &&
                existingStatus.IsTerminal)
            {
                _state = existingStatus.State ?? "Failed";
                _terminal = true;
                _lastFailureCode = existingStatus.FailureCode ?? string.Empty;
                _lastFailureMessage = existingStatus.FailureMessage ?? string.Empty;
                ModLogger.Info("AutomationJoin: existing terminal status preserved for RunId=" + _request.RunId + ".");
                return;
            }

            Transition("ModuleReady", "", "", "");
            ModLogger.Info(
                "AutomationJoin: run-scoped join request accepted. " +
                "RunId=" + _request.RunId +
                " commandId=" + _request.CommandId +
                " serverName=" + _request.ServerName +
                " port=" + _request.ServerPort +
                " passwordProvided=" + _request.PasswordProvided + ".");
        }

        private static void StartPlatformLogin()
        {
            if (!CoopLobbyAutomationDriver.TryGetPlatformLoginContext(
                    _lobbyClient,
                    out CoopPlatformLoginContext context,
                    out string contextFailureCode,
                    out string contextFailureMessage))
            {
                if (IsPlatformLoginContextPending(contextFailureCode))
                {
                    Transition("WaitingForLobby", _lobbyState, "", contextFailureMessage);
                    return;
                }

                _platformLoginOutcome = "ContextRejected";
                Transition("Failed", _lobbyState, contextFailureCode, contextFailureMessage);
                return;
            }

            if (!CoopLobbyAutomationDriver.TryStartPlatformLogin(
                    context,
                    out _platformLoginTask,
                    out string loginFailureCode,
                    out string loginFailureMessage))
            {
                _platformLoginTaskState = "NotStarted";
                _platformLoginOutcome = string.Equals(
                    loginFailureCode,
                    "PlatformLoginPrivilegeDenied",
                    StringComparison.Ordinal)
                    ? "PrivilegeDenied"
                    : "StartFailed";
                Transition("Failed", _lobbyState, loginFailureCode, loginFailureMessage);
                return;
            }

            _platformLoginAttempted = true;
            _platformLoginTaskState = "Running";
            _platformLoginOutcome = "Requested";
            Transition("RequestingPlatformLogin", _lobbyState, "", "");
        }

        private static void PumpPlatformLoginTask()
        {
            if (!_platformLoginTask.IsCompleted)
            {
                Transition("WaitingForPlatformLogin", _lobbyState, "", "");
                return;
            }

            Task completedTask = _platformLoginTask;
            _platformLoginTask = null;
            if (completedTask.IsCanceled)
            {
                _platformLoginTaskState = "Canceled";
                _platformLoginOutcome = "Canceled";
                Transition("Failed", _lobbyState, "PlatformLoginCancelled", "The native platform login task was cancelled.");
                return;
            }
            if (completedTask.IsFaulted)
            {
                _platformLoginTaskState = "Faulted";
                _platformLoginOutcome = "Faulted";
                Transition(
                    "Failed",
                    _lobbyState,
                    "PlatformLoginFaulted",
                    completedTask.Exception?.GetBaseException().Message ?? "The native platform login task failed.");
                return;
            }

            _platformLoginTaskState = "RanToCompletion";
            if (!CoopLobbyAutomationDriver.TryGetLobbyClient(out _lobbyClient, out _lobbyState, out string clientFailure))
            {
                _platformLoginOutcome = "PostLoginClientUnavailable";
                Transition("Failed", _lobbyState, "PlatformLoginPostStateUnavailable", clientFailure);
                return;
            }
            if (!CoopLobbyAutomationDriver.TryGetPlatformLoginContext(
                    _lobbyClient,
                    out CoopPlatformLoginContext context,
                    out string contextFailureCode,
                    out string contextFailureMessage))
            {
                _platformLoginOutcome = "PostLoginContextUnavailable";
                Transition("Failed", _lobbyState, contextFailureCode, contextFailureMessage);
                return;
            }

            _lobbyState = context.LobbyClientState;
            if (context.HasMultiplayerPrivilege == false)
            {
                _platformLoginOutcome = "PrivilegeDenied";
                Transition(
                    "Failed",
                    _lobbyState,
                    "PlatformLoginPrivilegeDenied",
                    "The platform denied multiplayer privilege during the native login attempt.");
                return;
            }
            if (string.Equals(_lobbyState, "AtLobby", StringComparison.Ordinal))
            {
                _platformLoginOutcome = "Succeeded";
                Transition("PlatformLoginSucceeded", _lobbyState, "", "");
                return;
            }
            if (string.Equals(_lobbyState, "Idle", StringComparison.Ordinal))
            {
                _platformLoginOutcome = "StillIdle";
                Transition(
                    "Failed",
                    _lobbyState,
                    "PlatformLoginStillIdle",
                    "The native platform login task completed but the lobby client remained Idle.");
                return;
            }

            _platformLoginOutcome = "TaskCompletedLobbyTransitionPending";
            Transition(
                "WaitingForLobbyAfterPlatformLogin",
                _lobbyState,
                "",
                "The native platform login task completed and the lobby is still transitioning.");
        }

        private static bool IsPlatformLoginContextPending(string failureCode)
        {
            return string.Equals(failureCode, "PlatformLoginGameNotReady", StringComparison.Ordinal) ||
                   string.Equals(failureCode, "PlatformLoginGameStateManagerNotReady", StringComparison.Ordinal) ||
                   string.Equals(failureCode, "PlatformLoginActiveStateNotReady", StringComparison.Ordinal) ||
                   string.Equals(failureCode, "PlatformLoginLobbyStateNotActive", StringComparison.Ordinal);
        }

        private static void PumpServerListTask(DateTime nowUtc)
        {
            if (!_serverListTask.IsCompleted)
                return;

            Task completedTask = _serverListTask;
            _serverListTask = null;
            if (!CoopLobbyAutomationDriver.TryReadServerListResult(
                    completedTask,
                    out List<CoopObservedLobbyServer> servers,
                    out string listFailure))
            {
                Transition("Failed", _lobbyState, "ServerListResultFailed", listFailure);
                return;
            }

            var descriptors = new List<CoopAutomationServerDescriptor>(servers.Count);
            for (int i = 0; i < servers.Count; i++)
                descriptors.Add(servers[i].Descriptor);

            CoopAutomationServerSelection selection = CoopAutomationJoinContract.SelectExactServer(_request, descriptors);
            if (selection.Status == CoopAutomationServerSelectionStatus.None)
            {
                _nextServerListUtc = nowUtc.Add(ServerListRetryInterval);
                Transition("WaitingForServer", _lobbyState, "", "No exact server match was present in the latest lobby list.");
                return;
            }

            if (selection.Status == CoopAutomationServerSelectionStatus.Ambiguous)
            {
                Transition(
                    "Failed",
                    _lobbyState,
                    "ServerMatchAmbiguous",
                    "More than one lobby server matched the exact automation identity.");
                return;
            }

            _selectedServer = servers[selection.SelectedIndex];
            if (_request.RequireLocalHostOwnership &&
                !HostSelfJoinRedirectState.IsPersistedHostSessionActive(
                    _selectedServer.Descriptor.ServerName,
                    _selectedServer.Descriptor.Port))
            {
                Transition(
                    "Failed",
                    _lobbyState,
                    "LocalHostOwnershipNotConfirmed",
                    "The selected lobby entry does not match an active local dedicated host marker and UDP port.");
                return;
            }

            if (_selectedServer.Descriptor.PasswordProtected && string.IsNullOrEmpty(_configuration.ServerPassword))
            {
                Transition("Failed", _lobbyState, "ServerPasswordMissing", "The selected server requires a password, but no protected launcher environment supplied one.");
                return;
            }

            if (_request.PasswordProvided != !string.IsNullOrEmpty(_configuration.ServerPassword))
            {
                Transition("Failed", _lobbyState, "PasswordPresenceMismatch", "The request password-presence claim does not match the launcher environment.");
                return;
            }

            if (!CoopLobbyAutomationDriver.TryStartJoinRequest(
                    _lobbyClient,
                    _selectedServer.NativeServerId,
                    _configuration.ServerPassword,
                    out _joinTask,
                    out string joinFailure))
            {
                Transition("Failed", _lobbyState, "JoinRequestFailed", joinFailure);
                return;
            }

            Transition("JoinRequested", _lobbyState, "", "");
        }

        private static void PumpJoinTask()
        {
            if (!_joinTask.IsCompleted)
                return;

            Task completedTask = _joinTask;
            _joinTask = null;
            if (!CoopLobbyAutomationDriver.TryReadJoinResult(completedTask, out bool accepted, out string joinFailure))
            {
                Transition("Failed", _lobbyState, "JoinResultFailed", joinFailure);
                return;
            }

            if (!accepted)
            {
                Transition("Failed", _lobbyState, "JoinRejected", "The native lobby rejected the custom-game join request.");
                return;
            }

            _joinAccepted = true;
            Transition("JoinAccepted", _lobbyState, "", "");
        }

        private static void Transition(
            string state,
            string lobbyState,
            string failureCode,
            string failureMessage)
        {
            bool changed;
            lock (StateLock)
            {
                changed = !string.Equals(_state, state ?? string.Empty, StringComparison.Ordinal) ||
                          !string.Equals(_lobbyState, lobbyState ?? string.Empty, StringComparison.Ordinal) ||
                          !string.Equals(_lastFailureCode, failureCode ?? string.Empty, StringComparison.Ordinal) ||
                          !string.Equals(_lastFailureMessage, failureMessage ?? string.Empty, StringComparison.Ordinal);

                _state = state ?? string.Empty;
                _lobbyState = lobbyState ?? string.Empty;
                _lastFailureCode = failureCode ?? string.Empty;
                _lastFailureMessage = failureMessage ?? string.Empty;
                _terminal = CoopAutomationJoinContract.IsTerminalState(_state);
            }

            if (!changed || _configuration == null || _request == null)
                return;

            CoopAutomationServerDescriptor server = _selectedServer?.Descriptor;
            if (_networkHandoffObserved && server != null)
            {
                server = new CoopAutomationServerDescriptor
                {
                    Id = server.Id,
                    ServerName = server.ServerName,
                    Address = string.IsNullOrWhiteSpace(_networkHandoffAddress) ? server.Address : _networkHandoffAddress,
                    Port = _networkHandoffPort > 0 ? _networkHandoffPort : server.Port,
                    GameType = server.GameType,
                    Map = server.Map,
                    UniqueMapId = server.UniqueMapId,
                    PasswordProtected = server.PasswordProtected
                };
            }

            try
            {
                CoopAutomationJoinBridge.WriteStatus(
                    _configuration,
                    _request,
                    _state,
                    _lobbyState,
                    server,
                    _lastFailureCode,
                    _lastFailureMessage,
                    _platformLoginAttempted,
                    _platformLoginTaskState,
                    _platformLoginOutcome);
            }
            catch (Exception ex)
            {
                _terminal = true;
                _state = "Failed";
                _lastFailureCode = "StatusWriteFailed";
                _lastFailureMessage = ex.Message;
                ModLogger.Error("AutomationJoin status write failed.", ex);
                return;
            }

            ModLogger.Info(
                "AutomationJoin: state=" + _state +
                " RunId=" + _request.RunId +
                " lobbyState=" + (_lobbyState ?? string.Empty) +
                (string.IsNullOrWhiteSpace(_lastFailureCode)
                    ? "."
                    : " failure=" + _lastFailureCode + ": " + _lastFailureMessage));
        }
    }
}
