using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure.Automation
{
    internal static class CoopAutomationDedicatedControlBridge
    {
        private const string ListedServerAssemblyName = "TaleWorlds.MountAndBlade.ListedServer";
        private const string InitialStateTypeName =
            "TaleWorlds.MountAndBlade.ListedServer.InitialListedGameServerState";
        private const string IntermissionManagerTypeName =
            "TaleWorlds.MountAndBlade.ListedServer.ServerSideIntermissionManager";
        private static readonly TimeSpan RequestPollInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan BindRetryInterval = TimeSpan.FromMilliseconds(250);
        private static readonly object SubscriptionLock = new object();

        private static bool _initialized;
        private static volatile bool _enabled;
        private static volatile bool _subscribed;
        private static volatile bool _nativeReady;
        private static bool _readyStatusPublished;
        private static bool _bindingFailurePublished;
        private static bool _terminal;
        private static DateTime _nextRequestPollUtc = DateTime.MinValue;
        private static DateTime _nextBindAttemptUtc = DateTime.MinValue;
        private static volatile string _bindingFailureCode = string.Empty;
        private static volatile string _bindingFailureMessage = string.Empty;
        private static CoopAutomationRuntimeConfiguration _configuration;
        private static string _modulePath = string.Empty;
        private static string _moduleSha256 = string.Empty;
        private static int _processId;
        private static DateTime _processStartUtc;
        private static string _executablePath = string.Empty;
        private static string _readyPath = string.Empty;
        private static string _requestPath = string.Empty;
        private static string _processedRequestPath = string.Empty;
        private static string _statusPath = string.Empty;
        private static EventInfo _readyEvent;
        private static Action _readyHandler;
        private static CoopAutomationDedicatedBootstrapRequest _activeRequest;
        private static CoopAutomationDedicatedBootstrapStatus _status;
        private static int _phase;

        public static bool TryInitialize(out string failureCode, out string failureMessage)
        {
            failureCode = string.Empty;
            failureMessage = string.Empty;
            if (_initialized)
                return true;

            _initialized = true;
            if (!CoopAutomationRuntimeBridge.IsAutomationEnabled)
            {
                _enabled = false;
                return true;
            }

            if (!CoopAutomationRuntimeBridge.TryResolveConfiguration(
                    out _configuration,
                    out failureCode,
                    out failureMessage))
            {
                return false;
            }

            try
            {
                _modulePath = Assembly.GetExecutingAssembly().Location;
                _moduleSha256 = CoopAutomationRuntimeContract.ComputeFileSha256(_modulePath);
                if (!string.Equals(_moduleSha256, _configuration.ExpectedModuleSha256, StringComparison.Ordinal))
                {
                    return Fail(
                        "DedicatedModuleHashMismatch",
                        "The loaded dedicated module hash does not match the configured automation identity.",
                        out failureCode,
                        out failureMessage);
                }

                using (Process process = Process.GetCurrentProcess())
                {
                    _processId = process.Id;
                    _processStartUtc = process.StartTime.ToUniversalTime();
                    _executablePath = process.MainModule?.FileName ?? string.Empty;
                }
                if (_processId <= 0 || _processStartUtc == default(DateTime) || string.IsNullOrWhiteSpace(_executablePath))
                {
                    return Fail(
                        "DedicatedProcessIdentityInvalid",
                        "The dedicated control bridge could not establish the current process identity.",
                        out failureCode,
                        out failureMessage);
                }

                _readyPath = CombineRunPath(CoopAutomationDedicatedControlContract.ReadyRelativePath);
                _requestPath = CombineRunPath(CoopAutomationDedicatedControlContract.RequestRelativePath);
                _processedRequestPath = CombineRunPath(CoopAutomationDedicatedControlContract.ProcessedRequestRelativePath);
                _statusPath = CombineRunPath(CoopAutomationDedicatedControlContract.StatusRelativePath);
                _enabled = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                TryBindReadinessEvent();
                ModLogger.Info(
                    "CoopAutomationDedicatedControlBridge: initialized default-off run-scoped control. " +
                    "RunId=" + _configuration.RunId + " ProcessId=" + _processId + ".");
                return true;
            }
            catch (Exception ex)
            {
                _enabled = false;
                return Fail(
                    "DedicatedControlInitializationFailed",
                    "The dedicated control bridge initialization failed: " + ex.Message,
                    out failureCode,
                    out failureMessage);
            }
        }

        public static void Tick()
        {
            if (!_enabled)
                return;

            CoopAutomationRuntimeBridge.PumpRoleStatus(
                CoopAutomationDedicatedControlContract.DedicatedRoleType,
                CoopAutomationDedicatedControlContract.DedicatedRoleInstanceId,
                _status?.State ?? (_readyStatusPublished ? "DedicatedControlReady" : "WaitingForDedicatedReady"),
                "CoopAutomationDedicatedControlBridge.Tick",
                "Phase=" + _phase +
                ";NativeReady=" + _nativeReady +
                ";ReadyPublished=" + _readyStatusPublished +
                ";Acknowledgements=" + (_status?.Acknowledgements?.Count ?? 0),
                _status?.FailureCode ?? _bindingFailureCode,
                _status?.FailureMessage ?? _bindingFailureMessage);
            if (_terminal)
                return;

            try
            {
                TickCore();
            }
            catch (Exception ex)
            {
                try
                {
                    if (_activeRequest != null)
                    {
                        FailActiveRequest(
                            "DedicatedControlTickFailed",
                            "The dedicated control tick failed: " + ex.Message);
                    }
                    else
                    {
                        _terminal = true;
                        WriteReadyStatus(
                            CoopAutomationDedicatedControlContract.FailedState,
                            "DedicatedControlTickFailed",
                            "The dedicated control tick failed before request acceptance: " + ex.Message);
                    }
                }
                catch (Exception statusException)
                {
                    _terminal = true;
                    ModLogger.Info(
                        "CoopAutomationDedicatedControlBridge: tick and failure-status publication failed. " +
                        "Tick=" + ex.Message + " Status=" + statusException.Message);
                }
            }
        }

        private static void TickCore()
        {
            if (!_enabled || _terminal)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (!_subscribed && nowUtc >= _nextBindAttemptUtc)
            {
                _nextBindAttemptUtc = nowUtc.Add(BindRetryInterval);
                TryBindReadinessEvent();
            }

            if (!string.IsNullOrEmpty(_bindingFailureCode))
            {
                if (!_bindingFailurePublished)
                {
                    _bindingFailurePublished = true;
                    WriteReadyStatus(
                        CoopAutomationDedicatedControlContract.FailedState,
                        _bindingFailureCode,
                        _bindingFailureMessage);
                }
                return;
            }

            if (_nativeReady && !_readyStatusPublished)
            {
                WriteReadyStatus(
                    CoopAutomationDedicatedControlContract.ReadyState,
                    string.Empty,
                    string.Empty);
                _readyStatusPublished = true;
                ModLogger.Info(
                    "CoopAutomationDedicatedControlBridge: authoritative InitialListedGameServerState.OnActivated readiness published.");
            }
            if (!_readyStatusPublished)
                return;

            if (_activeRequest == null)
            {
                if (nowUtc < _nextRequestPollUtc)
                    return;
                _nextRequestPollUtc = nowUtc.Add(RequestPollInterval);
                TryAcceptRequest(nowUtc);
                return;
            }

            if (NormalizeUtc(_activeRequest.ExpiresUtc) <= nowUtc)
            {
                FailActiveRequest("RequestExpired", "The dedicated bootstrap request expired before terminal acknowledgement.");
                return;
            }

            try
            {
                ExecuteCurrentPhase();
            }
            catch (Exception ex)
            {
                FailActiveRequest(
                    "DedicatedControlExecutionFailed",
                    "The dedicated bootstrap control step failed: " + ex.Message);
            }
        }

        public static void Shutdown()
        {
            if (!_initialized)
                return;

            try
            {
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                lock (SubscriptionLock)
                {
                    if (_subscribed && _readyEvent != null && _readyHandler != null)
                        _readyEvent.RemoveEventHandler(null, _readyHandler);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopAutomationDedicatedControlBridge: readiness unsubscription failed: " + ex.Message);
            }
            finally
            {
                _subscribed = false;
                _enabled = false;
                _initialized = false;
                _nativeReady = false;
                _readyStatusPublished = false;
                _bindingFailurePublished = false;
                _terminal = false;
                _bindingFailureCode = string.Empty;
                _bindingFailureMessage = string.Empty;
                _configuration = null;
                _readyEvent = null;
                _readyHandler = null;
                _activeRequest = null;
                _status = null;
                _phase = 0;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (!_enabled || args?.LoadedAssembly == null)
                return;
            if (string.Equals(
                    args.LoadedAssembly.GetName().Name,
                    ListedServerAssemblyName,
                    StringComparison.Ordinal))
            {
                TryBindReadinessEvent();
            }
        }

        private static void TryBindReadinessEvent()
        {
            if (!_enabled || _subscribed || !string.IsNullOrEmpty(_bindingFailureCode))
                return;

            lock (SubscriptionLock)
            {
                if (_subscribed || !string.IsNullOrEmpty(_bindingFailureCode))
                    return;

                Assembly listedServerAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                    assembly => string.Equals(
                        assembly.GetName().Name,
                        ListedServerAssemblyName,
                        StringComparison.Ordinal));
                if (listedServerAssembly == null)
                    return;

                try
                {
                    Type initialStateType = listedServerAssembly.GetType(InitialStateTypeName, throwOnError: false);
                    if (initialStateType == null)
                    {
                        SetBindingFailure(
                            "DedicatedReadyTypeMissing",
                            "The exact InitialListedGameServerState type is unavailable in the loaded ListedServer assembly.");
                        return;
                    }

                    EventInfo readyEvent = initialStateType.GetEvent(
                        "OnActivated",
                        BindingFlags.Public | BindingFlags.Static);
                    if (readyEvent == null || readyEvent.EventHandlerType != typeof(Action))
                    {
                        SetBindingFailure(
                            "DedicatedReadyEventMissing",
                            "The exact public static InitialListedGameServerState.OnActivated event is unavailable.");
                        return;
                    }

                    _readyHandler = OnNativeReady;
                    readyEvent.AddEventHandler(null, _readyHandler);
                    _readyEvent = readyEvent;
                    _subscribed = true;
                    ModLogger.Info(
                        "CoopAutomationDedicatedControlBridge: subscribed to InitialListedGameServerState.OnActivated.");
                }
                catch (Exception ex)
                {
                    SetBindingFailure(
                        "DedicatedReadySubscriptionFailed",
                        "The exact readiness event subscription failed: " + ex.Message);
                }
            }
        }

        private static void OnNativeReady()
        {
            _nativeReady = true;
        }

        private static void TryAcceptRequest(DateTime nowUtc)
        {
            if (!File.Exists(_requestPath))
                return;

            if (!CoopAutomationProtocolFileIO.TryReadJson(
                    _requestPath,
                    1024 * 1024,
                    out CoopAutomationDedicatedBootstrapRequest request,
                    out string failureCode,
                    out string failureMessage))
            {
                WriteRejectedStatus(request, failureCode, failureMessage);
                return;
            }

            if (!CoopAutomationDedicatedControlContract.TryValidateRequest(
                    request,
                    _configuration,
                    _moduleSha256,
                    _processId,
                    _processStartUtc,
                    _executablePath,
                    nowUtc,
                    out failureCode,
                    out failureMessage))
            {
                WriteRejectedStatus(request, failureCode, failureMessage);
                return;
            }

            if (!CoopAutomationProtocolFileIO.TryMoveInboxToProcessed(
                    _requestPath,
                    _processedRequestPath,
                    out failureCode,
                    out failureMessage))
            {
                WriteRejectedStatus(request, failureCode, failureMessage);
                return;
            }

            _activeRequest = request;
            _status = CreateStatus(request);
            _status.State = CoopAutomationDedicatedControlContract.AcceptedState;
            _status.IsTerminal = false;
            _phase = 0;
            WriteBootstrapStatus();
            ModLogger.Info(
                "CoopAutomationDedicatedControlBridge: accepted exact bootstrap command " + request.CommandId + ".");
        }

        private static void ExecuteCurrentPhase()
        {
            switch (_phase)
            {
                case 0:
                    ExecuteOptionCommand(
                        "ServerName " + _activeRequest.ServerName,
                        "ServerName",
                        _activeRequest.ServerName,
                        MultiplayerOptions.OptionType.ServerName.GetStrValue());
                    return;
                case 1:
                    ExecuteOptionCommand(
                        "MaxNumberOfPlayers " + _activeRequest.MaxNumberOfPlayers,
                        "MaxNumberOfPlayers",
                        _activeRequest.MaxNumberOfPlayers.ToString(),
                        MultiplayerOptions.OptionType.MaxNumberOfPlayers.GetIntValue().ToString());
                    return;
                case 2:
                    ExecuteOptionCommand(
                        "GameType " + _activeRequest.GameType,
                        "GameType",
                        _activeRequest.GameType,
                        MultiplayerOptions.OptionType.GameType.GetStrValue());
                    return;
                case 3:
                    ExecuteOptionCommand(
                        "Map " + _activeRequest.Map,
                        "Map",
                        _activeRequest.Map,
                        MultiplayerOptions.OptionType.Map.GetStrValue());
                    return;
                case 4:
                    GameNetwork.HandleConsoleCommand(
                        "add_map_to_usable_maps " + _activeRequest.Map + " " + _activeRequest.GameType);
                    bool usableMapAccepted = IsUsableMapAccepted(_activeRequest.Map);
                    if (!usableMapAccepted)
                        throw new InvalidOperationException("The native usable-map collection did not accept the requested map.");
                    AddAcknowledgement("UsableMap", "Accepted", _activeRequest.Map, _activeRequest.Map);
                    _phase++;
                    WriteBootstrapStatus();
                    return;
                case 5:
                    GameNetwork.HandleConsoleCommand("start_game");
                    AddAcknowledgement("StartGameRequested", "Requested", "start_game", "start_game");
                    _phase++;
                    WriteBootstrapStatus();
                    return;
                case 6:
                    if (!IsListedServerPlaying())
                        return;
                    ValidateFinalNativeState();
                    AddAcknowledgement(
                        "StartGameConfirmed",
                        "Confirmed",
                        "IsPlaying=true;GameType=" + _activeRequest.GameType + ";Map=" + _activeRequest.Map,
                        "IsPlaying=true;GameType=" + MultiplayerOptions.OptionType.GameType.GetStrValue() +
                        ";Map=" + MultiplayerOptions.OptionType.Map.GetStrValue());
                    _status.State = CoopAutomationDedicatedControlContract.BootstrapAcceptedState;
                    _status.IsTerminal = true;
                    _terminal = true;
                    WriteBootstrapStatus();
                    ModLogger.Info(
                        "CoopAutomationDedicatedControlBridge: exact dedicated bootstrap accepted for command " +
                        _activeRequest.CommandId + ".");
                    return;
                default:
                    throw new InvalidOperationException("The dedicated bootstrap state machine entered an invalid phase.");
            }
        }

        private static void ExecuteOptionCommand(
            string command,
            string step,
            string expectedValue,
            string observedBefore)
        {
            GameNetwork.HandleConsoleCommand(command);
            string observedAfter;
            switch (step)
            {
                case "ServerName":
                    observedAfter = MultiplayerOptions.OptionType.ServerName.GetStrValue();
                    break;
                case "MaxNumberOfPlayers":
                    observedAfter = MultiplayerOptions.OptionType.MaxNumberOfPlayers.GetIntValue().ToString();
                    break;
                case "GameType":
                    observedAfter = MultiplayerOptions.OptionType.GameType.GetStrValue();
                    break;
                case "Map":
                    observedAfter = MultiplayerOptions.OptionType.Map.GetStrValue();
                    break;
                default:
                    throw new InvalidOperationException("The dedicated option step is not allowlisted: " + step + ".");
            }

            if (!string.Equals(observedAfter, expectedValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The native option state did not accept " + step +
                    ". Before=" + (observedBefore ?? string.Empty) +
                    " Expected=" + expectedValue +
                    " Observed=" + (observedAfter ?? string.Empty) + ".");
            }

            AddAcknowledgement(step, "Applied", expectedValue, observedAfter);
            _phase++;
            WriteBootstrapStatus();
        }

        private static void ValidateFinalNativeState()
        {
            if (!string.Equals(
                    MultiplayerOptions.OptionType.ServerName.GetStrValue(),
                    _activeRequest.ServerName,
                    StringComparison.Ordinal) ||
                MultiplayerOptions.OptionType.MaxNumberOfPlayers.GetIntValue() != _activeRequest.MaxNumberOfPlayers ||
                !string.Equals(
                    MultiplayerOptions.OptionType.GameType.GetStrValue(),
                    _activeRequest.GameType,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    MultiplayerOptions.OptionType.Map.GetStrValue(),
                    _activeRequest.Map,
                    StringComparison.Ordinal) ||
                !IsUsableMapAccepted(_activeRequest.Map))
            {
                throw new InvalidOperationException(
                    "The native dedicated state changed before start-game confirmation.");
            }
        }

        private static bool IsUsableMapAccepted(string expectedMap)
        {
            object manager = GetIntermissionManager();
            PropertyInfo usableMapsProperty = manager.GetType().GetProperty(
                "UsableMaps",
                BindingFlags.Public | BindingFlags.Instance);
            IEnumerable usableMaps = usableMapsProperty?.GetValue(manager, null) as IEnumerable;
            if (usableMaps == null)
                throw new InvalidOperationException("ServerSideIntermissionManager.UsableMaps is unavailable.");
            foreach (object value in usableMaps)
            {
                if (string.Equals(value as string, expectedMap, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool IsListedServerPlaying()
        {
            object manager = GetIntermissionManager();
            PropertyInfo listedServerProperty = manager.GetType().GetProperty(
                "ListedServer",
                BindingFlags.Public | BindingFlags.Instance);
            object listedServer = listedServerProperty?.GetValue(manager, null);
            if (listedServer == null)
                throw new InvalidOperationException("ServerSideIntermissionManager.ListedServer is unavailable.");
            PropertyInfo isPlayingProperty = listedServerProperty.PropertyType.GetProperty(
                "IsPlaying",
                BindingFlags.Public | BindingFlags.Instance) ??
                listedServer.GetType().GetProperty(
                    "IsPlaying",
                    BindingFlags.Public | BindingFlags.Instance);
            if (isPlayingProperty == null || isPlayingProperty.PropertyType != typeof(bool))
                throw new InvalidOperationException("IListedServer.IsPlaying is unavailable.");
            return (bool)isPlayingProperty.GetValue(listedServer, null);
        }

        private static object GetIntermissionManager()
        {
            Assembly listedServerAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                assembly => string.Equals(
                    assembly.GetName().Name,
                    ListedServerAssemblyName,
                    StringComparison.Ordinal));
            Type managerType = listedServerAssembly?.GetType(IntermissionManagerTypeName, throwOnError: false);
            PropertyInfo instanceProperty = managerType?.GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.Static);
            object manager = instanceProperty?.GetValue(null, null);
            if (manager == null)
                throw new InvalidOperationException("ServerSideIntermissionManager.Instance is unavailable.");
            return manager;
        }

        private static void AddAcknowledgement(
            string step,
            string state,
            string expectedValue,
            string observedValue)
        {
            int nextSequence = _status.Acknowledgements.Count + 1;
            _status.Acknowledgements.Add(new CoopAutomationDedicatedBootstrapAcknowledgement
            {
                StepSequence = nextSequence,
                Step = step,
                State = state,
                ExpectedValue = expectedValue ?? string.Empty,
                ObservedValue = observedValue ?? string.Empty,
                AcknowledgedUtc = DateTime.UtcNow
            });
            _status.State = step + state;
            _status.IsTerminal = false;
        }

        private static CoopAutomationDedicatedBootstrapStatus CreateStatus(
            CoopAutomationDedicatedBootstrapRequest request)
        {
            return new CoopAutomationDedicatedBootstrapStatus
            {
                SchemaVersion = CoopAutomationDedicatedControlContract.CurrentSchemaVersion,
                ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                RunId = _configuration.RunId,
                Sequence = request?.Sequence ?? 0,
                CommandId = request?.CommandId ?? string.Empty,
                SourceRoleType = CoopAutomationDedicatedControlContract.DedicatedRoleType,
                SourceRoleInstanceId = CoopAutomationDedicatedControlContract.DedicatedRoleInstanceId,
                TargetRoleType = CoopAutomationDedicatedControlContract.RunnerRoleType,
                TargetRoleInstanceId = CoopAutomationDedicatedControlContract.RunnerRoleInstanceId,
                RunTokenSha256 = _configuration.RunTokenSha256,
                DedicatedModuleSha256 = _moduleSha256,
                ProcessId = _processId,
                ProcessStartUtc = _processStartUtc,
                ExecutablePath = _executablePath,
                UpdatedUtc = DateTime.UtcNow,
                Acknowledgements = new System.Collections.Generic.List<CoopAutomationDedicatedBootstrapAcknowledgement>(),
                FailureCode = string.Empty,
                FailureMessage = string.Empty
            };
        }

        private static void WriteRejectedStatus(
            CoopAutomationDedicatedBootstrapRequest request,
            string failureCode,
            string failureMessage)
        {
            _status = CreateStatus(request);
            _status.State = CoopAutomationDedicatedControlContract.FailedState;
            _status.IsTerminal = true;
            _status.FailureCode = failureCode ?? "DedicatedBootstrapRejected";
            _status.FailureMessage = failureMessage ?? "The dedicated bootstrap request was rejected.";
            _terminal = true;
            WriteBootstrapStatus();
        }

        private static void FailActiveRequest(string failureCode, string failureMessage)
        {
            if (_status == null)
                _status = CreateStatus(_activeRequest);
            _status.State = CoopAutomationDedicatedControlContract.FailedState;
            _status.IsTerminal = true;
            _status.FailureCode = failureCode ?? "DedicatedBootstrapFailed";
            _status.FailureMessage = failureMessage ?? "The dedicated bootstrap failed.";
            _terminal = true;
            WriteBootstrapStatus();
            ModLogger.Info(
                "CoopAutomationDedicatedControlBridge: bootstrap failed. Code=" +
                _status.FailureCode + " Message=" + _status.FailureMessage);
        }

        private static void WriteBootstrapStatus()
        {
            _status.UpdatedUtc = DateTime.UtcNow;
            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(_statusPath, _status);
        }

        private static void WriteReadyStatus(string state, string failureCode, string failureMessage)
        {
            var status = new CoopAutomationDedicatedControlReadyStatus
            {
                SchemaVersion = CoopAutomationDedicatedControlContract.CurrentSchemaVersion,
                ProtocolMajorVersion = CoopAutomationRuntimeContract.CurrentProtocolMajorVersion,
                ProtocolMinorVersion = CoopAutomationRuntimeContract.CurrentProtocolMinorVersion,
                RunId = _configuration.RunId,
                RunTokenSha256 = _configuration.RunTokenSha256,
                RoleType = CoopAutomationDedicatedControlContract.DedicatedRoleType,
                RoleInstanceId = CoopAutomationDedicatedControlContract.DedicatedRoleInstanceId,
                State = state,
                UpdatedUtc = DateTime.UtcNow,
                ProcessId = _processId,
                ProcessStartUtc = _processStartUtc,
                ExecutablePath = _executablePath,
                ModulePath = _modulePath,
                ModuleSha256 = _moduleSha256,
                ExpectedModuleSha256 = _configuration.ExpectedModuleSha256,
                LifecycleSource = "InitialListedGameServerState.OnActivated",
                FailureCode = failureCode ?? string.Empty,
                FailureMessage = failureMessage ?? string.Empty
            };
            CoopAutomationProtocolFileIO.WriteJsonStrictAtomic(_readyPath, status);
        }

        private static void SetBindingFailure(string code, string message)
        {
            _bindingFailureCode = code ?? "DedicatedReadyBindingFailed";
            _bindingFailureMessage = message ?? "The dedicated readiness event could not be bound.";
        }

        private static string CombineRunPath(string relativePath)
        {
            return CoopAutomationRuntimeContract.CombineRunPath(_configuration.RunRoot, relativePath);
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == DateTime.MinValue)
                return value;
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
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
