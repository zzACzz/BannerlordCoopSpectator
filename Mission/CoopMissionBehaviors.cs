// Файл: Mission/CoopMissionBehaviors.cs
// Призначення: логіка місії для кооп-спектатора — клієнтський зворотний зв'язок (етап 3.5), логування стану для Етапу 3.3 (spectator/unit/spawn), заглушка серверного спавну (етап 3.4).

using System; // Exception
using System.Collections.Generic; // List<string>
using System.Collections; // IEnumerable
using System.Linq; // Distinct, FirstOrDefault
using System.Reflection; // MethodInfo
using System.Runtime.CompilerServices; // RuntimeHelpers
using TaleWorlds.Core; // BasicCharacterObject, MBObjectManager (для спавну)
using TaleWorlds.Library; // Vec2, Vec3
using TaleWorlds.MountAndBlade; // Mission, MissionLogic, GameNetwork, Agent, Team, MissionMode
using TaleWorlds.ObjectSystem; // MBObjectManager
using CoopSpectator.Campaign; // BattleRosterFileHelper (варіант A: roster з кампанії)
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback
using CoopSpectator.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    /// <summary>
    /// Клієнтська логіка в MP-місії: логування стану (mission entered, has agent, is spectator, team, spawn, returned to spectator), статус "у битві", завершення місії (етап 3.3 / 3.5).
    /// </summary>
    public sealed class CoopMissionClientLogic : MissionLogic
    {
        private const float LogStateIntervalSeconds = 5f; // Інтервал між повторними логами стану (щоб не спамити).
        private float _timeUntilNextStateLog;
        private Agent _lastControlledAgent; // Попередній Agent.Main для детекції зміни контролю / повернення в spectator.
        private string _lastRequestedPreferredTroopKey;
        private string _lastObservedSpectatorTroopSelectionKey;
        private bool _visualSpawnAutoConfirmHooked;
        private bool _visualSpawnAutoConfirmTriggered;
        private object _visualSpawnComponent;
        private Delegate _onMyAgentVisualSpawnedDelegate;
        private bool _hasLoggedAutoConfirmBehaviorDiagnostics;
        private bool _hasLoggedMissionBehaviorCatalog;
        private bool _hasLoggedClassLoadoutDiagnostics;
        private bool _hasLoggedActiveClassLoadoutDiagnostics;
        private bool _hasLoggedClassLoadoutPendingInitialization;
        private bool _hasLoggedTeamSelectDiagnostics;
        private bool _hasLoggedScoreboardDiagnostics;
        private string _lastAppliedClassLoadoutFilterKey;
        private string _lastAppliedTeamSelectCultureSyncKey;
        private string _lastAppliedScoreboardCultureSyncKey;
        private const bool EnableClientPreferredTroopRequestExperiment = false;
        private const bool EnableVisualSpawnAutoConfirmExperiment = false;
        private const bool EnableFixedClientTeamSelectCulturesExperiment = true;
        private const string FixedClientAttackerCultureId = "empire";
        private const string FixedClientDefenderCultureId = "vlandia";

        public override void AfterStart()
        {
            ModLogger.Info("CoopMissionClientLogic AfterStart ENTER");
            base.AfterStart();
            Mission mission = Mission;
            if (mission == null) return;

            LogMissionEntered(mission);
            LogCurrentState(mission);
            _lastControlledAgent = Agent.Main;
            _timeUntilNextStateLog = LogStateIntervalSeconds;
            if (EnableVisualSpawnAutoConfirmExperiment)
                TryHookVisualSpawnAutoConfirm(mission);
            LogClassLoadoutDiagnosticsOnce(mission);
            LogActiveClassLoadoutDiagnosticsOnce(mission);
            TrySyncFixedTeamSelectCultures(mission);
            TrySyncFixedScoreboardCultures(mission);
            TryFilterClassLoadoutToCoopUnits(mission);

#if !COOPSPECTATOR_DEDICATED
            UiFeedback.ShowMessageDeferred("Coop: in battle. (Leave battle on host to return to lobby.)");
#endif
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            Mission mission = Mission;
            if (mission == null) return;

            Agent currentMain = Agent.Main;
            if (currentMain != _lastControlledAgent)
            {
                if (currentMain != null)
                {
                    ModLogger.Info("CoopMissionClientLogic: controlled agent changed — now controlling agent " + (currentMain.Name?.ToString() ?? currentMain.Index.ToString()));
                    _lastRequestedPreferredTroopKey = null;
                    _lastObservedSpectatorTroopSelectionKey = null;
                    _visualSpawnAutoConfirmTriggered = false;
                }
                else if (_lastControlledAgent != null)
                {
                    ModLogger.Info("CoopMissionClientLogic: returned to spectator (agent lost or died).");
                    _lastRequestedPreferredTroopKey = null;
                    _lastObservedSpectatorTroopSelectionKey = null;
                    _visualSpawnAutoConfirmTriggered = false;
                    _lastAppliedClassLoadoutFilterKey = null;
                }
                _lastControlledAgent = currentMain;
            }

            if (EnableVisualSpawnAutoConfirmExperiment)
                TryHookVisualSpawnAutoConfirm(mission);
            LogClassLoadoutDiagnosticsOnce(mission);
            LogActiveClassLoadoutDiagnosticsOnce(mission);
            TrySyncFixedTeamSelectCultures(mission);
            TrySyncFixedScoreboardCultures(mission);
            TryFilterClassLoadoutToCoopUnits(mission);
            TryResetSpawnPreviewStateForPreferredTroopChange(mission);
            if (EnableClientPreferredTroopRequestExperiment)
                TryTriggerClientPreferredTroopRequest(mission);

            _timeUntilNextStateLog -= dt;
            if (_timeUntilNextStateLog <= 0f)
            {
                _timeUntilNextStateLog = LogStateIntervalSeconds;
                LogCurrentState(mission);
            }
        }

        public override void OnMissionResultReady(MissionResult missionResult)
        {
            base.OnMissionResultReady(missionResult);
            ModLogger.Info("CoopMissionClientLogic: mission result ready — returning to lobby.");
        }

        protected override void OnEndMission()
        {
            if (EnableVisualSpawnAutoConfirmExperiment)
                UnhookVisualSpawnAutoConfirm();
            base.OnEndMission();
        }

        private static void LogMissionEntered(Mission mission)
        {
            ModLogger.Info("CoopMissionClientLogic: mission entered.");
            try
            {
                string mode = mission.Mode.ToString();
                ModLogger.Info("CoopMissionClientLogic: current mission mode = " + mode);
            }
            catch (Exception ex) { ModLogger.Info("CoopMissionClientLogic: mission mode log failed: " + ex.Message); }
        }

        private static void LogCurrentState(Mission mission)
        {
            try
            {
                bool hasAgent = Agent.Main != null;
                bool isSpectator = !hasAgent;
                ModLogger.Info("CoopMissionClientLogic: player has agent = " + hasAgent + " player is spectator = " + isSpectator);

                if (GameNetwork.IsSessionActive)
                {
                    var myPeer = GameNetwork.MyPeer;
                    ModLogger.Info("CoopMissionClientLogic: peer connected/synchronized = " + (myPeer != null));
                    if (hasAgent && Agent.Main?.Team != null)
                    {
                        int teamIndex = Agent.Main.Team?.TeamIndex ?? -1;
                        var side = Agent.Main.Team?.Side ?? BattleSideEnum.None;
                        ModLogger.Info("CoopMissionClientLogic: team index = " + teamIndex + " side = " + side);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: LogCurrentState failed: " + ex.Message);
            }
        }

        private void TryTriggerClientPreferredTroopRequest(Mission mission)
        {
            if (!EnableClientPreferredTroopRequestExperiment)
                return;

            if (mission == null || !GameNetwork.IsClient || Agent.Main != null || !GameNetwork.IsSessionActive)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || !myPeer.IsConnectionActive || !myPeer.IsSynchronized)
                return;

            MissionPeer missionPeer = myPeer.GetComponent<MissionPeer>();
            if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                return;

            int preferredTroopIndex = missionPeer.SelectedTroopIndex;
            if (preferredTroopIndex < 0)
                return;

            string requestKey = (missionPeer.Team.TeamIndex) + "|" + preferredTroopIndex;
            if (string.Equals(_lastRequestedPreferredTroopKey, requestKey, StringComparison.Ordinal))
                return;

            foreach (MissionBehavior behavior in mission.MissionBehaviors)
            {
                if (behavior == null)
                    continue;

                MethodInfo[] methods;
                try
                {
                    methods = behavior.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(method => string.Equals(method.Name, "RequestChangePreferredTroopType", StringComparison.Ordinal))
                        .ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in methods)
                {
                    if (!TryInvokePreferredTroopRequestMethod(behavior, method, myPeer, missionPeer, preferredTroopIndex))
                        continue;

                    _lastRequestedPreferredTroopKey = requestKey;
                    ModLogger.Info(
                        "CoopMissionClientLogic: invoked native preferred troop request. " +
                        "Behavior=" + behavior.GetType().FullName +
                        " Method=" + method +
                        " Team=" + missionPeer.Team.TeamIndex +
                        " TroopIndex=" + preferredTroopIndex);
                    return;
                }
            }
        }

        private void TryResetSpawnPreviewStateForPreferredTroopChange(Mission mission)
        {
            if (mission == null || !GameNetwork.IsClient || Agent.Main != null || !GameNetwork.IsSessionActive)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || !myPeer.IsConnectionActive || !myPeer.IsSynchronized)
                return;

            MissionPeer missionPeer = myPeer.GetComponent<MissionPeer>();
            if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                return;

            int selectedTroopIndex = missionPeer.SelectedTroopIndex;
            if (selectedTroopIndex < 0)
                return;

            string selectionKey = missionPeer.Team.TeamIndex + "|" + selectedTroopIndex;
            if (string.Equals(_lastObservedSpectatorTroopSelectionKey, selectionKey, StringComparison.Ordinal))
                return;

            _lastObservedSpectatorTroopSelectionKey = selectionKey;
            _lastRequestedPreferredTroopKey = null;
            _visualSpawnAutoConfirmTriggered = false;

            ModLogger.Info(
                "CoopMissionClientLogic: spectator troop selection changed. " +
                "Team=" + missionPeer.Team.TeamIndex +
                " TroopIndex=" + selectedTroopIndex +
                " AutoConfirmReset=True");
        }

        private static bool TryInvokePreferredTroopRequestMethod(
            MissionBehavior behavior,
            MethodInfo method,
            NetworkCommunicator myPeer,
            MissionPeer missionPeer,
            int preferredTroopIndex)
        {
            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    if (parameterType == typeof(int))
                        arguments[i] = preferredTroopIndex;
                    else if (parameterType.IsAssignableFrom(typeof(NetworkCommunicator)))
                        arguments[i] = myPeer;
                    else if (parameterType.IsAssignableFrom(typeof(MissionPeer)))
                        arguments[i] = missionPeer;
                    else if (parameterType == typeof(bool))
                        arguments[i] = true;
                    else
                        return false;
                }

                method.Invoke(behavior, arguments);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: preferred troop request invoke failed for " + behavior.GetType().FullName + "." + method.Name + ": " + ex.Message);
                return false;
            }
        }

        private void TryHookVisualSpawnAutoConfirm(Mission mission)
        {
            if (!EnableVisualSpawnAutoConfirmExperiment)
                return;

            if (_visualSpawnAutoConfirmHooked || mission == null || !GameNetwork.IsClient)
                return;

            try
            {
                MissionBehavior visualSpawnBehavior = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().Name.IndexOf("MultiplayerMissionAgentVisualSpawnComponent", StringComparison.OrdinalIgnoreCase) >= 0);
                if (visualSpawnBehavior == null)
                    return;

                EventInfo eventInfo = visualSpawnBehavior.GetType().GetEvent(
                    "OnMyAgentVisualSpawned",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (eventInfo == null)
                    return;

                MethodInfo handler = GetType().GetMethod(
                    nameof(OnMyAgentVisualSpawned),
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (handler == null)
                    return;

                Delegate callback = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, handler, false);
                if (callback == null)
                    return;

                eventInfo.AddEventHandler(visualSpawnBehavior, callback);
                _visualSpawnComponent = visualSpawnBehavior;
                _onMyAgentVisualSpawnedDelegate = callback;
                _visualSpawnAutoConfirmHooked = true;
                ModLogger.Info("CoopMissionClientLogic: hooked OnMyAgentVisualSpawned for coop auto-confirm.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: visual spawn auto-confirm hook failed: " + ex.Message);
            }
        }

        private void UnhookVisualSpawnAutoConfirm()
        {
            if (!EnableVisualSpawnAutoConfirmExperiment)
                return;

            if (!_visualSpawnAutoConfirmHooked || _visualSpawnComponent == null || _onMyAgentVisualSpawnedDelegate == null)
                return;

            try
            {
                EventInfo eventInfo = _visualSpawnComponent.GetType().GetEvent(
                    "OnMyAgentVisualSpawned",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                eventInfo?.RemoveEventHandler(_visualSpawnComponent, _onMyAgentVisualSpawnedDelegate);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: visual spawn auto-confirm unhook failed: " + ex.Message);
            }
            finally
            {
                _visualSpawnAutoConfirmHooked = false;
                _visualSpawnComponent = null;
                _onMyAgentVisualSpawnedDelegate = null;
            }
        }

        private void OnMyAgentVisualSpawned()
        {
            if (!EnableVisualSpawnAutoConfirmExperiment)
                return;

            if (_visualSpawnAutoConfirmTriggered || Agent.Main != null)
                return;

            Mission mission = Mission;
            if (mission == null)
                return;

            _visualSpawnAutoConfirmTriggered = true;
            LogMissionBehaviorCatalogOnce(mission);

            TryInvokeAutoSpawnConfirm(mission);
        }

        private void TryInvokeAutoSpawnConfirm(Mission mission)
        {
            if (!EnableVisualSpawnAutoConfirmExperiment)
                return;

            foreach (MissionBehavior behavior in mission.MissionBehaviors)
            {
                if (behavior == null)
                    continue;

                string typeName = behavior.GetType().Name;
                if (typeName.IndexOf("MissionLobbyEquipmentNetworkComponent", StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("MultiplayerTeamSelectComponent", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                LogAutoConfirmBehaviorDiagnosticsOnce(behavior);

                MethodInfo[] candidateMethods;
                try
                {
                    candidateMethods = behavior.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(method =>
                            method.ReturnType == typeof(void) &&
                            method.GetParameters().Length == 0 &&
                            method.Name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            (method.Name.IndexOf("Request", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             method.Name.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             method.Name.IndexOf("Finalize", StringComparison.OrdinalIgnoreCase) >= 0))
                        .OrderBy(method => method.Name)
                        .ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo method in candidateMethods)
                {
                    try
                    {
                        method.Invoke(behavior, Array.Empty<object>());
                        ModLogger.Info(
                            "CoopMissionClientLogic: auto-confirm spawn invoked. " +
                            "Behavior=" + behavior.GetType().FullName +
                            " Method=" + method.Name);
                        return;
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info(
                            "CoopMissionClientLogic: auto-confirm candidate failed. " +
                            "Behavior=" + behavior.GetType().FullName +
                            " Method=" + method.Name +
                            " Error=" + ex.Message);
                    }
                }
            }
        }

        private void LogMissionBehaviorCatalogOnce(Mission mission)
        {
            if (_hasLoggedMissionBehaviorCatalog || mission == null)
                return;

            _hasLoggedMissionBehaviorCatalog = true;

            try
            {
                MissionBehavior[] behaviors = mission.MissionBehaviors?.Where(behavior => behavior != null).ToArray() ?? Array.Empty<MissionBehavior>();
                ModLogger.Info("CoopMissionClientLogic: mission behavior catalog count = " + behaviors.Length);
                foreach (MissionBehavior behavior in behaviors)
                    ModLogger.Info("CoopMissionClientLogic: mission behavior => " + behavior.GetType().FullName);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: mission behavior catalog logging failed: " + ex.Message);
            }
        }

        private void LogAutoConfirmBehaviorDiagnosticsOnce(MissionBehavior behavior)
        {
            if (_hasLoggedAutoConfirmBehaviorDiagnostics || behavior == null)
                return;

            _hasLoggedAutoConfirmBehaviorDiagnostics = true;

            try
            {
                MethodInfo[] interestingMethods = behavior.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method =>
                        method.Name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Visual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Agent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Invul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        method.Name.IndexOf("Request", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(method => method.Name)
                    .ToArray();

                ModLogger.Info(
                    "CoopMissionClientLogic: auto-confirm diagnostics for behavior " +
                    behavior.GetType().FullName +
                    " MethodCount=" + interestingMethods.Length);

                foreach (MethodInfo method in interestingMethods)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    string parameterList = string.Join(", ", parameters.Select(parameter => parameter.ParameterType.Name + " " + parameter.Name));
                    ModLogger.Info(
                        "CoopMissionClientLogic: auto-confirm method candidate => " +
                        method.ReturnType.Name + " " +
                        method.Name + "(" + parameterList + ")");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: auto-confirm diagnostics failed: " + ex.Message);
            }
        }

        private void LogClassLoadoutDiagnosticsOnce(Mission mission)
        {
            if (_hasLoggedClassLoadoutDiagnostics || mission == null)
                return;

            try
            {
                object classLoadout = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0);
                if (classLoadout == null)
                    return;

                _hasLoggedClassLoadoutDiagnostics = true;

                Type loadoutType = classLoadout.GetType();
                ModLogger.Info("CoopMissionClientLogic: class loadout diagnostics type = " + loadoutType.FullName);

                foreach (FieldInfo field in loadoutType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    object value = null;
                    string valueDescription;
                    try
                    {
                        value = field.GetValue(classLoadout);
                        valueDescription = DescribeDiagnosticValue(value);
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: class loadout field => " +
                        field.FieldType.FullName + " " +
                        field.Name + " = " + valueDescription);

                    LogNestedDiagnosticMembers(field.Name, value);
                }

                foreach (PropertyInfo property in loadoutType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property.GetIndexParameters().Length != 0)
                        continue;

                    object value = null;
                    string valueDescription;
                    try
                    {
                        if (!property.CanRead)
                            continue;

                        value = property.GetValue(classLoadout, null);
                        valueDescription = DescribeDiagnosticValue(value);
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: class loadout property => " +
                        property.PropertyType.FullName + " " +
                        property.Name + " = " + valueDescription);

                    LogNestedDiagnosticMembers(property.Name, value);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: class loadout diagnostics failed: " + ex.Message);
            }
        }

        private static void LogNestedDiagnosticMembers(string ownerName, object value)
        {
            if (value == null)
                return;

            Type valueType = value.GetType();
            string typeName = valueType.FullName ?? valueType.Name;
            bool interestingType =
                typeName.IndexOf("VM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Binding", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Class", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Troop", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!interestingType)
                return;

            foreach (FieldInfo nestedField in valueType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsInterestingDiagnosticMember(nestedField.Name, nestedField.FieldType))
                    continue;

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(nestedField.GetValue(value));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: class loadout nested field => " +
                    ownerName + "." + nestedField.Name +
                    " : " + nestedField.FieldType.FullName +
                    " = " + valueDescription);
            }

            foreach (PropertyInfo nestedProperty in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nestedProperty.GetIndexParameters().Length != 0 ||
                    !nestedProperty.CanRead ||
                    !IsInterestingDiagnosticMember(nestedProperty.Name, nestedProperty.PropertyType))
                {
                    continue;
                }

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(nestedProperty.GetValue(value, null));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: class loadout nested property => " +
                    ownerName + "." + nestedProperty.Name +
                    " : " + nestedProperty.PropertyType.FullName +
                    " = " + valueDescription);
            }

            LogDiagnosticCollectionItems(ownerName, value);
        }

        private static void LogDiagnosticCollectionItems(string ownerName, object value)
        {
            if (value == null || value is string || !(value is IEnumerable enumerable))
                return;

            int itemIndex = 0;
            foreach (object item in enumerable)
            {
                if (itemIndex >= 4)
                    break;

                if (item == null)
                {
                    ModLogger.Info("CoopMissionClientLogic: diagnostic collection item => " + ownerName + "[" + itemIndex + "] = null");
                    itemIndex++;
                    continue;
                }

                Type itemType = item.GetType();
                ModLogger.Info(
                    "CoopMissionClientLogic: diagnostic collection item => " +
                    ownerName + "[" + itemIndex + "] = " +
                    DescribeDiagnosticValue(item));

                foreach (FieldInfo nestedField in itemType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsInterestingDiagnosticMember(nestedField.Name, nestedField.FieldType))
                        continue;

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(nestedField.GetValue(item));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: diagnostic item field => " +
                        ownerName + "[" + itemIndex + "]." + nestedField.Name +
                        " : " + nestedField.FieldType.FullName +
                        " = " + valueDescription);
                }

                foreach (PropertyInfo nestedProperty in itemType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (nestedProperty.GetIndexParameters().Length != 0 ||
                        !nestedProperty.CanRead ||
                        !IsInterestingDiagnosticMember(nestedProperty.Name, nestedProperty.PropertyType))
                    {
                        continue;
                    }

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(nestedProperty.GetValue(item, null));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: diagnostic item property => " +
                        ownerName + "[" + itemIndex + "]." + nestedProperty.Name +
                        " : " + nestedProperty.PropertyType.FullName +
                        " = " + valueDescription);
                }

                itemIndex++;
            }
        }

        private static bool IsInterestingDiagnosticMember(string memberName, Type memberType)
        {
            string typeName = memberType?.FullName ?? memberType?.Name ?? string.Empty;
            return memberName.IndexOf("class", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("score", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("culture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("banner", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("troop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Scoreboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Culture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("VM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Binding", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeDiagnosticValue(object value)
        {
            if (value == null)
                return "null";

            if (value is string text)
                return "\"" + text + "\"";

            if (value is System.Collections.ICollection collection)
                return value.GetType().FullName + " Count=" + collection.Count;

            return value.GetType().FullName + " :: " + value;
        }

        private void LogActiveClassLoadoutDiagnosticsOnce(Mission mission)
        {
            if (_hasLoggedActiveClassLoadoutDiagnostics || mission == null)
                return;

            try
            {
                object classLoadout = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0);
                if (classLoadout == null)
                    return;

                Type loadoutType = classLoadout.GetType();
                FieldInfo dataSourceField = loadoutType.GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo tryToInitializeField = loadoutType.GetField("_tryToInitialize", BindingFlags.Instance | BindingFlags.NonPublic);
                PropertyInfo isActiveProperty = loadoutType.GetProperty("IsActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (dataSourceField == null)
                    return;

                object dataSource = dataSourceField.GetValue(classLoadout);
                bool isActive = false;
                bool tryToInitialize = false;
                try
                {
                    if (isActiveProperty != null)
                        isActive = (bool)isActiveProperty.GetValue(classLoadout, null);
                }
                catch
                {
                }

                try
                {
                    if (tryToInitializeField != null)
                        tryToInitialize = (bool)tryToInitializeField.GetValue(classLoadout);
                }
                catch
                {
                }

                if (dataSource == null && !isActive && !tryToInitialize)
                    return;

                if (dataSource == null)
                {
                    if (!_hasLoggedClassLoadoutPendingInitialization)
                    {
                        _hasLoggedClassLoadoutPendingInitialization = true;
                        ModLogger.Info(
                            "CoopMissionClientLogic: class loadout waiting for datasource. " +
                            "IsActive=" + isActive +
                            " TryToInitialize=" + tryToInitialize);
                    }
                    return;
                }

                _hasLoggedActiveClassLoadoutDiagnostics = true;
                ModLogger.Info(
                    "CoopMissionClientLogic: active class loadout state. " +
                    "IsActive=" + isActive +
                    " TryToInitialize=" + tryToInitialize +
                    " DataSource=" + DescribeDiagnosticValue(dataSource));
                Type dataSourceType = dataSource.GetType();
                ModLogger.Info("CoopMissionClientLogic: active class loadout datasource type = " + dataSourceType.FullName);

                foreach (FieldInfo field in dataSourceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsInterestingDiagnosticMember(field.Name, field.FieldType))
                        continue;

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(field.GetValue(dataSource));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: active datasource field => " +
                        field.FieldType.FullName + " " +
                        field.Name + " = " + valueDescription);

                    object nestedValue = null;
                    try
                    {
                        nestedValue = field.GetValue(dataSource);
                    }
                    catch
                    {
                    }

                    LogNestedDiagnosticMembers("datasource." + field.Name, nestedValue);
                }

                foreach (PropertyInfo property in dataSourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property.GetIndexParameters().Length != 0 ||
                        !property.CanRead ||
                        !IsInterestingDiagnosticMember(property.Name, property.PropertyType))
                    {
                        continue;
                    }

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(property.GetValue(dataSource, null));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: active datasource property => " +
                        property.PropertyType.FullName + " " +
                        property.Name + " = " + valueDescription);

                    object nestedValue = null;
                    try
                    {
                        nestedValue = property.GetValue(dataSource, null);
                    }
                    catch
                    {
                    }

                    LogNestedDiagnosticMembers("datasource." + property.Name, nestedValue);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: active class loadout diagnostics failed: " + ex.Message);
            }
        }

        private void TrySyncFixedTeamSelectCultures(Mission mission)
        {
            if (!EnableFixedClientTeamSelectCulturesExperiment || mission == null || !GameNetwork.IsClient)
                return;

            try
            {
                object teamSelection = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletTeamSelection", StringComparison.OrdinalIgnoreCase) >= 0);
                if (teamSelection == null)
                    return;

                object dataSource = GetMemberValue(teamSelection, "_dataSource") ?? GetMemberValue(teamSelection, "DataSource");
                if (dataSource == null)
                    return;

                LogTeamSelectDiagnosticsOnce(teamSelection, dataSource);

                string syncKey = BuildTeamSelectCultureSyncKey(mission, dataSource);
                if (string.Equals(_lastAppliedTeamSelectCultureSyncKey, syncKey, StringComparison.Ordinal))
                    return;

                int updatedTeamCount = 0;
                HashSet<int> visited = new HashSet<int>();
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_attackerTeam") ?? GetMemberValue(dataSource, "AttackerTeam"),
                    FixedClientAttackerCultureId,
                    "datasource.attacker",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_defenderTeam") ?? GetMemberValue(dataSource, "DefenderTeam"),
                    FixedClientDefenderCultureId,
                    "datasource.defender",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_team1") ?? GetMemberValue(dataSource, "Team1"),
                    FixedClientAttackerCultureId,
                    "datasource.team1",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_team2") ?? GetMemberValue(dataSource, "Team2"),
                    FixedClientDefenderCultureId,
                    "datasource.team2",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToTeamVmCollection(
                    GetMemberValue(dataSource, "_teams") ?? GetMemberValue(dataSource, "Teams"),
                    visited);

                if (updatedTeamCount <= 0)
                    return;

                InvokeRefreshValuesIfPresent(dataSource);
                InvokeRefreshValuesIfPresent(teamSelection);
                _lastAppliedTeamSelectCultureSyncKey = syncKey;

                ModLogger.Info(
                    "CoopMissionClientLogic: synced fixed cultures into early team-select UI. " +
                    "SyncKey=" + syncKey +
                    " UpdatedTeams=" + updatedTeamCount +
                    " AttackerCulture=" + FixedClientAttackerCultureId +
                    " DefenderCulture=" + FixedClientDefenderCultureId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: fixed team-select culture sync failed: " + ex.Message);
            }
        }

        private void LogTeamSelectDiagnosticsOnce(object teamSelection, object dataSource)
        {
            if (_hasLoggedTeamSelectDiagnostics)
                return;

            _hasLoggedTeamSelectDiagnostics = true;

            try
            {
                ModLogger.Info("CoopMissionClientLogic: team select diagnostics type = " + teamSelection.GetType().FullName);
                ModLogger.Info("CoopMissionClientLogic: team select datasource type = " + dataSource.GetType().FullName);

                foreach (FieldInfo field in dataSource.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsInterestingTeamSelectMember(field.Name, field.FieldType))
                        continue;

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(field.GetValue(dataSource));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: team select datasource field => " +
                        field.FieldType.FullName + " " +
                        field.Name + " = " + valueDescription);
                }

                foreach (PropertyInfo property in dataSource.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property.GetIndexParameters().Length != 0 ||
                        !property.CanRead ||
                        !IsInterestingTeamSelectMember(property.Name, property.PropertyType))
                    {
                        continue;
                    }

                    string valueDescription;
                    try
                    {
                        valueDescription = DescribeDiagnosticValue(property.GetValue(dataSource, null));
                    }
                    catch (Exception ex)
                    {
                        valueDescription = "unavailable (" + ex.Message + ")";
                    }

                    ModLogger.Info(
                        "CoopMissionClientLogic: team select datasource property => " +
                        property.PropertyType.FullName + " " +
                        property.Name + " = " + valueDescription);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: team select diagnostics failed: " + ex.Message);
            }
        }

        private void TrySyncFixedScoreboardCultures(Mission mission)
        {
            if (!EnableFixedClientTeamSelectCulturesExperiment || mission == null || !GameNetwork.IsClient)
                return;

            try
            {
                object scoreboardBehavior = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletMultiplayerScoreboard", StringComparison.OrdinalIgnoreCase) >= 0);
                if (scoreboardBehavior == null)
                    return;

                object dataSource = ResolveScoreboardDataSource(scoreboardBehavior);
                if (dataSource == null)
                    return;

                LogScoreboardDiagnosticsOnce(scoreboardBehavior, dataSource);

                string syncKey = BuildScoreboardCultureSyncKey(mission, dataSource);
                if (string.Equals(_lastAppliedScoreboardCultureSyncKey, syncKey, StringComparison.Ordinal))
                    return;

                int updatedTeamCount = 0;
                HashSet<int> visited = new HashSet<int>();
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_team1") ?? GetMemberValue(dataSource, "Team1") ?? GetMemberValue(dataSource, "_attackerTeam") ?? GetMemberValue(dataSource, "AttackerTeam"),
                    FixedClientAttackerCultureId,
                    "scoreboard.team1",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(
                    GetMemberValue(dataSource, "_team2") ?? GetMemberValue(dataSource, "Team2") ?? GetMemberValue(dataSource, "_defenderTeam") ?? GetMemberValue(dataSource, "DefenderTeam"),
                    FixedClientDefenderCultureId,
                    "scoreboard.team2",
                    visited);
                updatedTeamCount += TryApplyFixedCultureToTeamVmCollection(
                    GetMemberValue(dataSource, "_teams") ?? GetMemberValue(dataSource, "Teams") ?? GetMemberValue(dataSource, "_scoreboardSides") ?? GetMemberValue(dataSource, "ScoreboardSides"),
                    visited);
                updatedTeamCount += TryApplyFixedCultureToScoreboardSideCollection(
                    GetMemberValue(dataSource, "_sides") ?? GetMemberValue(dataSource, "Sides"),
                    visited);
                updatedTeamCount += TryApplyFixedCultureToScoreboardSideDictionary(
                    GetMemberValue(dataSource, "_missionSides") ?? GetMemberValue(dataSource, "MissionSides"),
                    visited);

                if (updatedTeamCount <= 0)
                    return;

                InvokeRefreshValuesIfPresent(dataSource);
                InvokeRefreshValuesIfPresent(scoreboardBehavior);
                _lastAppliedScoreboardCultureSyncKey = syncKey;

                ModLogger.Info(
                    "CoopMissionClientLogic: synced fixed cultures into scoreboard UI. " +
                    "SyncKey=" + syncKey +
                    " UpdatedTeams=" + updatedTeamCount +
                    " AttackerCulture=" + FixedClientAttackerCultureId +
                    " DefenderCulture=" + FixedClientDefenderCultureId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: fixed scoreboard culture sync failed: " + ex.Message);
            }
        }

        private void LogScoreboardDiagnosticsOnce(object scoreboardBehavior, object dataSource)
        {
            if (_hasLoggedScoreboardDiagnostics)
                return;

            _hasLoggedScoreboardDiagnostics = true;

            try
            {
                ModLogger.Info("CoopMissionClientLogic: scoreboard diagnostics type = " + scoreboardBehavior.GetType().FullName);
                ModLogger.Info("CoopMissionClientLogic: scoreboard datasource type = " + dataSource.GetType().FullName);
                LogInterestingMembers("scoreboard behavior", scoreboardBehavior);
                LogInterestingMembers("scoreboard datasource", dataSource);
                LogNestedDiagnosticMembers("scoreboard.datasource", dataSource);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: scoreboard diagnostics failed: " + ex.Message);
            }
        }

        private static object ResolveScoreboardDataSource(object scoreboardBehavior)
        {
            if (scoreboardBehavior == null)
                return null;

            object directDataSource = GetMemberValue(scoreboardBehavior, "_dataSource") ??
                                      GetMemberValue(scoreboardBehavior, "DataSource") ??
                                      GetMemberValue(scoreboardBehavior, "_scoreboardDataSource") ??
                                      GetMemberValue(scoreboardBehavior, "ScoreboardDataSource") ??
                                      GetMemberValue(scoreboardBehavior, "_multiplayerScoreboardVM") ??
                                      GetMemberValue(scoreboardBehavior, "MultiplayerScoreboardVM");
            if (IsUsefulScoreboardDataSourceCandidate(directDataSource))
                return directDataSource;

            foreach (FieldInfo field in scoreboardBehavior.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsInterestingScoreboardMember(field.Name, field.FieldType))
                    continue;

                object value = null;
                try
                {
                    value = field.GetValue(scoreboardBehavior);
                }
                catch
                {
                }

                if (IsUsefulScoreboardDataSourceCandidate(value))
                    return value;
            }

            foreach (PropertyInfo property in scoreboardBehavior.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead || !IsInterestingScoreboardMember(property.Name, property.PropertyType))
                    continue;

                object value = null;
                try
                {
                    value = property.GetValue(scoreboardBehavior, null);
                }
                catch
                {
                }

                if (IsUsefulScoreboardDataSourceCandidate(value))
                    return value;
            }

            return null;
        }

        private static bool IsUsefulScoreboardDataSourceCandidate(object value)
        {
            if (value == null)
                return false;

            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
                return false;

            string typeName = type.FullName ?? type.Name;
            if (typeName.IndexOf("Scoreboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("VM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("DataSource", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool IsInterestingScoreboardMember(string memberName, Type memberType)
        {
            string typeName = memberType?.FullName ?? memberType?.Name ?? string.Empty;
            return memberName.IndexOf("score", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("vm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Scoreboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("VM", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void LogInterestingMembers(string ownerName, object instance)
        {
            if (instance == null)
                return;

            foreach (FieldInfo field in instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsInterestingScoreboardMember(field.Name, field.FieldType) && !IsInterestingTeamSelectMember(field.Name, field.FieldType))
                    continue;

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(field.GetValue(instance));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: " + ownerName + " field => " +
                    field.FieldType.FullName + " " +
                    field.Name + " = " + valueDescription);
            }

            foreach (PropertyInfo property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0 ||
                    !property.CanRead ||
                    (!IsInterestingScoreboardMember(property.Name, property.PropertyType) && !IsInterestingTeamSelectMember(property.Name, property.PropertyType)))
                {
                    continue;
                }

                string valueDescription;
                try
                {
                    valueDescription = DescribeDiagnosticValue(property.GetValue(instance, null));
                }
                catch (Exception ex)
                {
                    valueDescription = "unavailable (" + ex.Message + ")";
                }

                ModLogger.Info(
                    "CoopMissionClientLogic: " + ownerName + " property => " +
                    property.PropertyType.FullName + " " +
                    property.Name + " = " + valueDescription);
            }
        }

        private static bool IsInterestingTeamSelectMember(string memberName, Type memberType)
        {
            string typeName = memberType?.FullName ?? memberType?.Name ?? string.Empty;
            return memberName.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("culture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("attacker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("defender", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Team", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Culture", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int TryApplyFixedCultureToTeamVmCollection(object teamsObject, HashSet<int> visited)
        {
            if (!(teamsObject is IEnumerable teams))
                return 0;

            int updatedTeamCount = 0;
            foreach (object teamVm in teams)
            {
                string cultureId = ResolveFixedCultureIdForClientTeamVm(teamVm);
                if (string.IsNullOrWhiteSpace(cultureId))
                    continue;

                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(teamVm, cultureId, "datasource.teams", visited);
            }

            return updatedTeamCount;
        }

        private static int TryApplyFixedCultureToScoreboardSideCollection(object sidesObject, HashSet<int> visited)
        {
            if (!(sidesObject is IEnumerable sides))
                return 0;

            int updatedTeamCount = 0;
            foreach (object sideVm in sides)
            {
                string cultureId = ResolveFixedCultureIdForClientTeamVm(sideVm);
                if (string.IsNullOrWhiteSpace(cultureId))
                    continue;

                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(sideVm, cultureId, "scoreboard.sides", visited);
            }

            return updatedTeamCount;
        }

        private static int TryApplyFixedCultureToScoreboardSideDictionary(object missionSidesObject, HashSet<int> visited)
        {
            if (!(missionSidesObject is IDictionary dictionary))
                return 0;

            int updatedTeamCount = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                string cultureId = null;
                if (entry.Key is BattleSideEnum keySide)
                {
                    if (keySide == BattleSideEnum.Attacker)
                        cultureId = FixedClientAttackerCultureId;
                    else if (keySide == BattleSideEnum.Defender)
                        cultureId = FixedClientDefenderCultureId;
                }

                if (string.IsNullOrWhiteSpace(cultureId))
                    cultureId = ResolveFixedCultureIdForClientTeamVm(entry.Value);
                if (string.IsNullOrWhiteSpace(cultureId))
                    continue;

                updatedTeamCount += TryApplyFixedCultureToKnownTeamVm(entry.Value, cultureId, "scoreboard.missionSides", visited);
            }

            return updatedTeamCount;
        }

        private static string ResolveFixedCultureIdForClientTeamVm(object teamVm)
        {
            if (teamVm == null)
                return null;

            object sideValue = GetMemberValue(teamVm, "Side") ?? GetMemberValue(teamVm, "_side");
            if (sideValue is BattleSideEnum side)
            {
                if (side == BattleSideEnum.Attacker)
                    return FixedClientAttackerCultureId;
                if (side == BattleSideEnum.Defender)
                    return FixedClientDefenderCultureId;
            }
            else
            {
                string sideText = sideValue?.ToString();
                if (string.Equals(sideText, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase))
                    return FixedClientAttackerCultureId;
                if (string.Equals(sideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase))
                    return FixedClientDefenderCultureId;
            }

            object teamIndexValue = GetMemberValue(teamVm, "TeamIndex") ?? GetMemberValue(teamVm, "_teamIndex");
            if (teamIndexValue is int teamIndex)
            {
                if (teamIndex == 1)
                    return FixedClientAttackerCultureId;
                if (teamIndex == 2)
                    return FixedClientDefenderCultureId;
            }

            return null;
        }

        private static int TryApplyFixedCultureToKnownTeamVm(object teamVm, string cultureId, string path, HashSet<int> visited)
        {
            if (teamVm == null || string.IsNullOrWhiteSpace(cultureId))
                return 0;

            int objectId = RuntimeHelpers.GetHashCode(teamVm);
            if (visited != null && !visited.Add(objectId))
                return 0;

            BasicCultureObject culture = MBObjectManager.Instance?.GetObject<BasicCultureObject>(cultureId);
            string cultureName = culture?.Name?.ToString();
            if (string.IsNullOrWhiteSpace(cultureName))
                cultureName = cultureId;

            bool updated = false;
            updated |= TrySetTeamVmCultureMember(teamVm, "Culture", culture, cultureId);
            updated |= TrySetTeamVmCultureMember(teamVm, "_culture", culture, cultureId);
            updated |= TrySetTeamVmCultureMember(teamVm, "<Culture>k__BackingField", culture, cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "CultureId", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "_cultureId", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "CultureCode", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "_cultureCode", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "FactionId", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "_factionId", cultureId);
            updated |= TrySetTeamVmTextMember(teamVm, "CultureName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "_cultureName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "FactionName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "_factionName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "Name", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "_name", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "TeamName", cultureName);
            updated |= TrySetTeamVmTextMember(teamVm, "_teamName", cultureName);

            if (updated)
            {
                InvokeRefreshValuesIfPresent(teamVm);
                ModLogger.Info(
                    "CoopMissionClientLogic: fixed early team-select culture VM. " +
                    "Path=" + path +
                    " Type=" + teamVm.GetType().FullName +
                    " AppliedCulture=" + cultureId +
                    " AppliedName=" + cultureName);
                return 1;
            }

            return 0;
        }

        private static bool TrySetTeamVmCultureMember(object instance, string memberName, BasicCultureObject culture, string cultureId)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            try
            {
                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    if (property.PropertyType == typeof(string))
                    {
                        property.SetValue(instance, cultureId, null);
                        return true;
                    }

                    if (culture != null && property.PropertyType.IsAssignableFrom(culture.GetType()))
                    {
                        property.SetValue(instance, culture, null);
                        return true;
                    }
                }

                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    if (field.FieldType == typeof(string))
                    {
                        field.SetValue(instance, cultureId);
                        return true;
                    }

                    if (culture != null && field.FieldType.IsAssignableFrom(culture.GetType()))
                    {
                        field.SetValue(instance, culture);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TrySetTeamVmTextMember(object instance, string memberName, string value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName) || value == null)
                return false;

            try
            {
                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0 && property.PropertyType == typeof(string))
                {
                    property.SetValue(instance, value, null);
                    return true;
                }

                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(string))
                {
                    field.SetValue(instance, value);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void InvokeRefreshValuesIfPresent(object instance)
        {
            if (instance == null)
                return;

            try
            {
                MethodInfo method = instance.GetType().GetMethod("RefreshValues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                method?.Invoke(instance, Array.Empty<object>());
            }
            catch
            {
            }
        }

        private void TryFilterClassLoadoutToCoopUnits(Mission mission)
        {
            if (mission == null)
                return;

            try
            {
                object classLoadout = mission.MissionBehaviors
                    .FirstOrDefault(behavior =>
                        behavior != null &&
                        behavior.GetType().FullName != null &&
                        behavior.GetType().FullName.IndexOf("MissionGauntletClassLoadout", StringComparison.OrdinalIgnoreCase) >= 0);
                if (classLoadout == null)
                    return;

                FieldInfo dataSourceField = classLoadout.GetType().GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
                object dataSource = dataSourceField?.GetValue(classLoadout);
                if (dataSource == null)
                    return;

                HashSet<string> allowedTroopIds = ResolveAllowedCoopTroopIdsForClientPicker();
                string filterKey = BuildClassLoadoutFilterKey(mission, dataSource);
                if (string.Equals(_lastAppliedClassLoadoutFilterKey, filterKey, StringComparison.Ordinal))
                    return;

                object classesObject = GetMemberValue(dataSource, "_classes") ?? GetMemberValue(dataSource, "Classes");
                if (!(classesObject is IList groups) || groups.Count == 0)
                    return;

                if (allowedTroopIds.Count > 0 && !HasAnyAllowedCoopHeroClass(groups, allowedTroopIds))
                {
                    ModLogger.Info(
                        "CoopMissionClientLogic: allowed troop ids did not match any class-loadout subclass; " +
                        "falling back to broad coop filter. " +
                        "AllowedTroops=" + string.Join(",", allowedTroopIds));
                    allowedTroopIds.Clear();
                }

                bool anyRemoved = false;
                int keptSubclassCount = 0;
                for (int groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
                {
                    object groupVm = groups[groupIndex];
                    object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                    if (!(subClassesObject is IList subClasses))
                        continue;

                    for (int subClassIndex = subClasses.Count - 1; subClassIndex >= 0; subClassIndex--)
                    {
                        object heroClassVm = subClasses[subClassIndex];
                        if (ShouldKeepHeroClassVmForCoopPicker(heroClassVm, allowedTroopIds))
                        {
                            keptSubclassCount++;
                            continue;
                        }

                        subClasses.RemoveAt(subClassIndex);
                        anyRemoved = true;
                    }

                    if (subClasses.Count == 0)
                    {
                        groups.RemoveAt(groupIndex);
                        anyRemoved = true;
                    }
                }

                if (!anyRemoved)
                    return;

                object currentSelectedClass = GetMemberValue(dataSource, "_currentSelectedClass") ?? GetMemberValue(dataSource, "CurrentSelectedClass");
                if (!ShouldKeepHeroClassVmForCoopPicker(currentSelectedClass, allowedTroopIds))
                {
                    object firstCoopClass = FindPreferredAllowedCoopHeroClass(groups, allowedTroopIds);
                    if (firstCoopClass != null)
                    {
                        SetMemberValue(dataSource, "_currentSelectedClass", firstCoopClass);
                        SetMemberValue(dataSource, "CurrentSelectedClass", firstCoopClass);
                    }
                }

                _lastAppliedClassLoadoutFilterKey = filterKey;
                ModLogger.Info(
                    "CoopMissionClientLogic: filtered class loadout to coop units. " +
                    "FilterKey=" + filterKey + " " +
                    "AllowedTroops=" + string.Join(",", allowedTroopIds) + " " +
                    "RemainingGroups=" + groups.Count +
                    " RemainingCoopSubClasses=" + keptSubclassCount);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: class loadout coop filter failed: " + ex.Message);
            }
        }

        private static bool HasAnyAllowedCoopHeroClass(IList groups, HashSet<string> allowedTroopIds)
        {
            if (groups == null || allowedTroopIds == null || allowedTroopIds.Count == 0)
                return false;

            foreach (object groupVm in groups)
            {
                object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                if (!(subClassesObject is IList subClasses))
                    continue;

                foreach (object heroClassVm in subClasses)
                {
                    if (ShouldKeepHeroClassVmForCoopPicker(heroClassVm, allowedTroopIds))
                        return true;
                }
            }

            return false;
        }

        private static object FindFirstAllowedCoopHeroClass(IList groups, HashSet<string> allowedTroopIds)
        {
            foreach (object groupVm in groups)
            {
                object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                if (!(subClassesObject is IList subClasses))
                    continue;

                foreach (object heroClassVm in subClasses)
                {
                    if (ShouldKeepHeroClassVmForCoopPicker(heroClassVm, allowedTroopIds))
                        return heroClassVm;
                }
            }

            return null;
        }

        private static object FindPreferredAllowedCoopHeroClass(IList groups, HashSet<string> allowedTroopIds)
        {
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            string preferredTroopId = NormalizeTroopId(selectionState.TroopId);
            if (!string.IsNullOrWhiteSpace(preferredTroopId))
            {
                foreach (object groupVm in groups)
                {
                    object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                    if (!(subClassesObject is IList subClasses))
                        continue;

                    foreach (object heroClassVm in subClasses)
                    {
                        if (!ShouldKeepHeroClassVmForCoopPicker(heroClassVm, allowedTroopIds))
                            continue;

                        string heroClassTroopId = NormalizeTroopId(TryGetHeroClassVmTroopId(heroClassVm));
                        if (string.Equals(heroClassTroopId, preferredTroopId, StringComparison.Ordinal))
                            return heroClassVm;
                    }
                }
            }

            return FindFirstAllowedCoopHeroClass(groups, allowedTroopIds);
        }

        private static bool ShouldKeepHeroClassVmForCoopPicker(object heroClassVm, HashSet<string> allowedTroopIds)
        {
            if (!IsCoopHeroClassVm(heroClassVm))
                return false;

            if (allowedTroopIds == null || allowedTroopIds.Count == 0)
                return true;

            string heroClassTroopId = TryGetHeroClassVmTroopId(heroClassVm);
            if (string.IsNullOrWhiteSpace(heroClassTroopId))
                return true;

            return allowedTroopIds.Contains(heroClassTroopId);
        }

        private static bool IsCoopHeroClassVm(object heroClassVm)
        {
            if (heroClassVm == null)
                return false;

            object heroClass = GetMemberValue(heroClassVm, "HeroClass");
            if (IsCoopHeroClassObject(heroClass))
                return true;

            string troopTypeId = GetMemberValue(heroClassVm, "_troopTypeId") as string ?? GetMemberValue(heroClassVm, "TroopTypeId") as string;
            if (!string.IsNullOrEmpty(troopTypeId) && troopTypeId.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            foreach (string memberName in new[] { "Name", "_name", "ClassName", "_className", "Title", "_title" })
            {
                string text = GetMemberValue(heroClassVm, memberName)?.ToString();
                if (!string.IsNullOrEmpty(text) && text.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string TryGetHeroClassVmTroopId(object heroClassVm)
        {
            if (heroClassVm == null)
                return null;

            object heroClass = GetMemberValue(heroClassVm, "HeroClass");
            string heroClassTroopId = TryGetHeroClassTroopId(heroClass);
            if (!string.IsNullOrWhiteSpace(heroClassTroopId))
                return heroClassTroopId;

            return NormalizeTroopId(
                GetMemberValue(heroClassVm, "_troopTypeId") as string ??
                GetMemberValue(heroClassVm, "TroopTypeId") as string);
        }

        private static string TryGetHeroClassTroopId(object heroClass)
        {
            if (heroClass == null)
                return null;

            foreach (string memberName in new[] { "Character", "CharacterObject", "TroopCharacter", "HeroCharacter" })
            {
                object character = GetMemberValue(heroClass, memberName);
                if (character == null)
                    continue;

                string stringId = NormalizeTroopId(
                    GetMemberValue(character, "StringId") as string ??
                    GetMemberValue(character, "Id") as string);
                if (!string.IsNullOrWhiteSpace(stringId))
                    return stringId;
            }

            return NormalizeTroopId(GetMemberValue(heroClass, "StringId") as string ?? GetMemberValue(heroClass, "Id") as string);
        }

        private static HashSet<string> ResolveAllowedCoopTroopIdsForClientPicker()
        {
            HashSet<string> allowedTroopIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return allowedTroopIds;

            BattleSideEnum authoritativeSide = CoopMissionSpawnLogic.ResolveAuthoritativeSide(missionPeer, Mission.Current, "client-picker");
            IReadOnlyList<string> allowedBaseTroopIds = ResolveAllowedBaseTroopIdsForClientPicker(authoritativeSide);
            foreach (string baseTargetTroopId in allowedBaseTroopIds)
            {
                string cultureSpecificTargetTroopId = TryBuildClientCultureSpecificCoopTroopId(baseTargetTroopId, missionPeer?.Culture);
                AddAllowedCoopTroopId(allowedTroopIds, cultureSpecificTargetTroopId);
                AddAllowedCoopTroopId(allowedTroopIds, baseTargetTroopId);
            }

            List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                .GetMPHeroClasses(missionPeer.Culture)
                ?.Where(heroClass => heroClass?.HeroCharacter != null)
                .ToList();

            int selectedTroopIndex = missionPeer.SelectedTroopIndex;
            bool shouldUseVanillaSelectedClassFallback =
                allowedTroopIds.Count == 0 &&
                !CoopMissionSpawnLogic.HasSideScopedRoster();
            if (shouldUseVanillaSelectedClassFallback &&
                cultureClasses != null &&
                selectedTroopIndex >= 0 &&
                selectedTroopIndex < cultureClasses.Count)
            {
                AddAllowedCoopTroopId(allowedTroopIds, cultureClasses[selectedTroopIndex]?.HeroCharacter?.StringId);
            }

            return allowedTroopIds;
        }

        private static IReadOnlyList<string> ResolveAllowedBaseTroopIdsForClientPicker(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None && CoopMissionSpawnLogic.HasSideScopedRoster())
                return Array.Empty<string>();

            IReadOnlyList<string> authorityTroopIds = CoopMissionSpawnLogic.GetAllowedControlTroopIdsSnapshot(side);
            if (authorityTroopIds.Count > 0)
                return authorityTroopIds;

            IReadOnlyList<RosterEntryState> entryStates = CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(side);
            if (entryStates.Count > 0)
            {
                return entryStates
                    .Select(entry => entry?.CharacterId)
                    .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            BattleRuntimeState state = BattleSnapshotRuntimeState.GetState();
            if (state?.Sides != null && state.Sides.Count > 0)
            {
                string canonicalSideKey = side == BattleSideEnum.Attacker
                    ? "attacker"
                    : side == BattleSideEnum.Defender
                        ? "defender"
                        : null;
                if (!string.IsNullOrWhiteSpace(canonicalSideKey) &&
                    state.SidesByKey.TryGetValue(canonicalSideKey, out BattleSideState sideState) &&
                    sideState?.TroopIds != null &&
                    sideState.TroopIds.Count > 0)
                {
                    return sideState.TroopIds
                        .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                }
            }

            IReadOnlyList<string> fixedTestTroopIds = CoopMissionSpawnLogic.GetFixedTestAllowedControlTroopIdsForSidePublic(side);
            if (fixedTestTroopIds.Count > 0)
                return fixedTestTroopIds;

            return Array.Empty<string>();
        }

        private static string TryBuildClientCultureSpecificCoopTroopId(string targetTroopId, BasicCultureObject culture)
        {
            string cultureToken = TryExtractCultureTokenForClientPicker(culture);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return NormalizeTroopId(targetTroopId);

            return TryBuildCultureSpecificCoopTroopIdForClientPicker(targetTroopId, cultureToken);
        }

        private static string TryBuildCultureSpecificCoopTroopIdForClientPicker(string targetTroopId, string cultureToken)
        {
            string normalizedTargetTroopId = NormalizeTroopId(targetTroopId);
            if (string.IsNullOrWhiteSpace(normalizedTargetTroopId) ||
                string.IsNullOrWhiteSpace(cultureToken) ||
                !normalizedTargetTroopId.StartsWith("mp_coop_", StringComparison.Ordinal))
            {
                return null;
            }

            string role = GetTroopRoleForClientPicker(normalizedTargetTroopId);
            if (string.IsNullOrWhiteSpace(role))
                return null;

            string weight = GetTroopWeightForClientPicker(normalizedTargetTroopId);
            string prefix = string.IsNullOrWhiteSpace(weight)
                ? "mp_coop_" + role + "_"
                : "mp_coop_" + weight + "_" + role + "_";

            return prefix + cultureToken + "_troop";
        }

        private static string TryExtractCultureTokenForClientPicker(BasicCultureObject culture)
        {
            string cultureId = culture?.StringId;
            if (string.IsNullOrWhiteSpace(cultureId))
                return null;

            int dotIndex = cultureId.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < cultureId.Length - 1)
                return cultureId.Substring(dotIndex + 1).Trim().ToLowerInvariant();

            return cultureId.Trim().ToLowerInvariant();
        }

        private static string GetTroopRoleForClientPicker(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            if (troopId.Contains("_cavalry_"))
                return "cavalry";
            if (troopId.Contains("_ranged_") || troopId.Contains("_archer_"))
                return "ranged";
            if (troopId.Contains("_infantry_"))
                return "infantry";
            return string.Empty;
        }

        private static string GetTroopWeightForClientPicker(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            if (troopId.Contains("_heavy_"))
                return "heavy";
            if (troopId.Contains("_light_"))
                return "light";
            if (troopId.Contains("_medium_"))
                return "medium";
            return string.Empty;
        }

        private static void AddAllowedCoopTroopId(HashSet<string> allowedTroopIds, string troopId)
        {
            string normalizedTroopId = NormalizeTroopId(troopId);
            if (string.IsNullOrWhiteSpace(normalizedTroopId) || normalizedTroopId.IndexOf("mp_coop_", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            allowedTroopIds.Add(normalizedTroopId);

            string alternateTroopId = GetAlternateCoopTroopIdVariant(normalizedTroopId);
            if (!string.IsNullOrWhiteSpace(alternateTroopId))
                allowedTroopIds.Add(alternateTroopId);
        }

        private static string NormalizeTroopId(string troopId)
        {
            return string.IsNullOrWhiteSpace(troopId)
                ? null
                : troopId.Trim().ToLowerInvariant();
        }

        private static string GetAlternateCoopTroopIdVariant(string troopId)
        {
            string normalizedTroopId = NormalizeTroopId(troopId);
            if (string.IsNullOrWhiteSpace(normalizedTroopId))
                return null;

            if (normalizedTroopId.EndsWith("_troop", StringComparison.Ordinal))
                return normalizedTroopId.Substring(0, normalizedTroopId.Length - "_troop".Length) + "_hero";

            if (normalizedTroopId.EndsWith("_hero", StringComparison.Ordinal))
                return normalizedTroopId.Substring(0, normalizedTroopId.Length - "_hero".Length) + "_troop";

            return null;
        }

        private static bool IsCoopHeroClassObject(object heroClass)
        {
            if (heroClass == null)
                return false;

            foreach (string memberName in new[] { "StringId", "Id" })
            {
                string text = GetMemberValue(heroClass, memberName) as string;
                if (!string.IsNullOrEmpty(text) && text.IndexOf("mp_coop_", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            foreach (string memberName in new[] { "Name", "ClassName" })
            {
                string text = GetMemberValue(heroClass, memberName)?.ToString();
                if (!string.IsNullOrEmpty(text) && text.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            foreach (string memberName in new[] { "Character", "CharacterObject", "TroopCharacter", "HeroCharacter" })
            {
                object character = GetMemberValue(heroClass, memberName);
                if (character == null)
                    continue;

                string stringId = GetMemberValue(character, "StringId") as string ?? GetMemberValue(character, "Id") as string;
                if (!string.IsNullOrEmpty(stringId) && stringId.IndexOf("mp_coop_", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                string name = GetMemberValue(character, "Name")?.ToString();
                if (!string.IsNullOrEmpty(name) && name.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }

            return null;
        }

        private static void SetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(instance, value, null);
                    return;
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    field.SetValue(instance, value);
                }
                catch
                {
                }
            }
        }

        private static string BuildClassLoadoutFilterKey(Mission mission, object dataSource)
        {
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            string dataSourceIdentity = dataSource.GetType().FullName + "#" + dataSource.GetHashCode();
            string culture = missionPeer?.Culture?.StringId ?? "none";
            string missionMode = mission?.Mode.ToString() ?? "unknown";
            string selectionStamp = BuildClientCoopSelectionStamp(missionPeer);
            string allowedTroopIds = string.Join(",", ResolveAllowedCoopTroopIdsForClientPicker().OrderBy(id => id, StringComparer.Ordinal));
            return missionMode + "|" + culture + "|" + selectionStamp + "|" + allowedTroopIds + "|" + dataSourceIdentity;
        }

        private static string BuildTeamSelectCultureSyncKey(Mission mission, object dataSource)
        {
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            string culture = missionPeer?.Culture?.StringId ?? "none";
            string missionMode = mission?.Mode.ToString() ?? "unknown";
            string dataSourceIdentity = dataSource.GetType().FullName + "#" + RuntimeHelpers.GetHashCode(dataSource);
            string selectionStamp = BuildClientCoopSelectionStamp(missionPeer);
            return missionMode + "|" + culture + "|" + selectionStamp + "|" + FixedClientAttackerCultureId + "|" + FixedClientDefenderCultureId + "|" + dataSourceIdentity;
        }

        private static string BuildScoreboardCultureSyncKey(Mission mission, object dataSource)
        {
            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            string culture = missionPeer?.Culture?.StringId ?? "none";
            string missionMode = mission?.Mode.ToString() ?? "unknown";
            string dataSourceIdentity = dataSource.GetType().FullName + "#" + RuntimeHelpers.GetHashCode(dataSource);
            string selectionStamp = BuildClientCoopSelectionStamp(missionPeer);
            return missionMode + "|" + culture + "|" + selectionStamp + "|scoreboard|" + FixedClientAttackerCultureId + "|" + FixedClientDefenderCultureId + "|" + dataSourceIdentity;
        }

        private static string BuildClientCoopSelectionStamp(MissionPeer missionPeer)
        {
            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            int selectedTroopIndex = CoopMissionSpawnLogic.HasSideScopedRoster()
                ? -1
                : missionPeer?.SelectedTroopIndex ?? -1;
            return
                "requested=" + selectionState.RequestedSide +
                "|assigned=" + selectionState.Side +
                "|entry=" + (selectionState.EntryId ?? "null") +
                "|troop=" + (selectionState.TroopId ?? "null") +
                "|selectedIndex=" + selectedTroopIndex;
        }

    }

    /// <summary>
    /// Серверна логіка: заглушка для майбутнього спавну гравців (етап 3.4), логування peer/spawn для Етапу 3.3. Варіант A: читає battle_roster.json.
    /// </summary>
    public sealed class CoopMissionSpawnLogic : MissionLogic
    {
        private const bool EnableDirectCoopPlayerSpawnExperiment = false;
        private const bool EnableCoopClassRestrictionSyncExperiment = false;
        private bool _hasLoggedStart;
        private const float ServerLogIntervalSeconds = 8f;
        private float _timeUntilNextPeerLog;
        private static Mission _lastDedicatedObservedMission;
        private static Mission _lastDiagnosticSpawnMission;
        private static Mission _lastDiagnosticOwnershipMission;
        private static bool _hasLoggedObjectManagerDiagnostics;
        private static bool _hasLoggedLoadedCharacterCatalog;
        private static bool _hasSpawnedDiagnosticAllowedAgent;
        private static bool _hasTransferredDiagnosticAllowedAgentToPeer;
        private static readonly HashSet<int> _spawnedCoopPeerIndices = new HashSet<int>();
        private static HashSet<string> _loggedForcedPreferredClassKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<int, int> _lastBridgedSelectedTroopIndexByPeer = new Dictionary<int, int>();
        private static readonly Dictionary<int, string> _appliedFixedMissionCultureByPeer = new Dictionary<int, string>();
        private static readonly Dictionary<FormationClass, bool> _appliedCoopClassAvailabilityStates = new Dictionary<FormationClass, bool>();
        private static Agent _diagnosticAllowedAgent;
        private const bool EnableFixedMissionCulturesExperiment = true;
        private const string FixedMissionAttackerCultureId = "empire";
        private const string FixedMissionDefenderCultureId = "vlandia";
        private static readonly FormationClass[] RestrictableFormationClasses =
        {
            FormationClass.Infantry,
            FormationClass.Ranged,
            FormationClass.Cavalry,
            FormationClass.HorseArcher
        };

        /// <summary>Після читання файлу — список troop ID з кампанії для обмеження вибору юнітів клієнтами (варіант A).</summary>
        public static List<string> CampaignRosterTroopIds { get; private set; } = new List<string>();
        public static List<string> ControlTroopIds { get; private set; } = new List<string>();
        public static List<string> AllowedControlTroopIds { get; private set; } = new List<string>();
        public static List<string> AllowedControlEntryIds { get; private set; } = new List<string>();
        public static List<BasicCharacterObject> AllowedControlCharacters { get; private set; } = new List<BasicCharacterObject>();
        public static Dictionary<BattleSideEnum, List<string>> AllowedControlTroopIdsBySide { get; private set; } = new Dictionary<BattleSideEnum, List<string>>();
        public static Dictionary<BattleSideEnum, List<string>> AllowedControlEntryIdsBySide { get; private set; } = new Dictionary<BattleSideEnum, List<string>>();
        public static Dictionary<BattleSideEnum, List<BattleRosterEntryProjectionState>> AllowedControlEntriesBySide { get; private set; } = new Dictionary<BattleSideEnum, List<BattleRosterEntryProjectionState>>();
        public static Dictionary<BattleSideEnum, List<RosterEntryState>> AllowedControlEntryStatesBySide { get; private set; } = new Dictionary<BattleSideEnum, List<RosterEntryState>>();
        public static Dictionary<BattleSideEnum, List<BasicCharacterObject>> AllowedControlCharactersBySide { get; private set; } = new Dictionary<BattleSideEnum, List<BasicCharacterObject>>();
        /// <summary>Перший дозволений troop ID із нормалізованого roster. Поки що лише для контрольованої серверної фіксації.</summary>
        public static string SelectedAllowedTroopId { get; private set; }
        public static string SelectedAllowedEntryId { get; private set; }
        /// <summary>Резолвлений character object для першого дозволеного troop ID. Поки тільки для діагностики.</summary>
        public static BasicCharacterObject SelectedAllowedCharacter { get; private set; }

        public override void AfterStart()
        {
            ModLogger.Info("CoopMissionSpawnLogic AfterStart ENTER");
            base.AfterStart();
            Mission mission = Mission;
            if (mission == null) return;
            if (_hasLoggedStart) return;
            _hasLoggedStart = true;
            _spawnedCoopPeerIndices.Clear();
            _appliedCoopClassAvailabilityStates.Clear();
            CoopBattleSpawnRequestState.Reset();

            ModLogger.Info("CoopMissionSpawnLogic: server mission entered.");
            try
            {
                string mode = mission.Mode.ToString();
                ModLogger.Info("CoopMissionSpawnLogic: current mission mode = " + mode);
            }
            catch (Exception ex) { ModLogger.Info("CoopMissionSpawnLogic: mission mode log failed: " + ex.Message); }

            if (GameNetwork.IsSessionActive)
            {
                int peerCount = GameNetwork.NetworkPeerCount;
                ModLogger.Info("CoopMissionSpawnLogic: peer count (after start) = " + peerCount);
            }

            RefreshAllowedTroopsFromRoster("server behavior");
            ModLogger.Info("CoopMissionSpawnLogic: server mission started. Campaign roster: " + CampaignRosterTroopIds.Count + " normalized troop IDs.");
            if (!string.IsNullOrEmpty(SelectedAllowedTroopId))
                ModLogger.Info("CoopMissionSpawnLogic: selected allowed troop id = " + SelectedAllowedTroopId);
            else
                ModLogger.Info("CoopMissionSpawnLogic: no allowed troop id available yet (roster empty).");
            LogAllowedCharacterResolution();
            CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, "CoopMissionSpawnLogic.AfterStart", mission);

            _timeUntilNextPeerLog = ServerLogIntervalSeconds;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!_hasLoggedStart) return;
            _timeUntilNextPeerLog -= dt;
            if (_timeUntilNextPeerLog <= 0f)
            {
                _timeUntilNextPeerLog = ServerLogIntervalSeconds;
                try
                {
                    if (GameNetwork.IsSessionActive)
                        ModLogger.Info("CoopMissionSpawnLogic: peer count = " + GameNetwork.NetworkPeerCount);
                }
                catch (Exception ex) { ModLogger.Info("CoopMissionSpawnLogic: peer log failed: " + ex.Message); }
            }

            TryRefreshPendingSpawnRequests(Mission, "server behavior");
            TrySpawnPeersIntoCoopControl(Mission, "server behavior");
            TryUpdateBattlePhaseState(Mission, "server behavior tick");
        }

        protected override void OnEndMission()
        {
            CoopBattleSpawnRequestState.Reset();
            CoopBattlePhaseRuntimeState.SetPhase(CoopBattlePhase.BattleEnded, "CoopMissionSpawnLogic.OnEndMission", Mission, allowRegression: true);
            base.OnEndMission();
        }

        private static List<string> NormalizeRosterTroopIds(List<string> troopIds)
        {
            if (troopIds == null || troopIds.Count == 0)
                return new List<string>();

            return troopIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static BasicCharacterObject ResolveAllowedCharacter(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            try
            {
                BasicCharacterObject direct = MBObjectManager.Instance.GetObject<BasicCharacterObject>(troopId);
                if (direct != null)
                    return direct;

                LogClosestLoadedCharacterIds(troopId);

                string guaranteedFallbackId = TryResolveGuaranteedMissionSafeTroopId(troopId);
                if (!string.IsNullOrWhiteSpace(guaranteedFallbackId) && !string.Equals(guaranteedFallbackId, troopId, StringComparison.Ordinal))
                {
                    ModLogger.Info("CoopMissionSpawnLogic: direct lookup failed for '" + troopId + "'. Trying guaranteed mission-safe fallback '" + guaranteedFallbackId + "'.");
                    BasicCharacterObject fallback = MBObjectManager.Instance.GetObject<BasicCharacterObject>(guaranteedFallbackId);
                    if (fallback != null)
                        return fallback;
                }

                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: character lookup failed for troop id '" + troopId + "': " + ex.Message);
                return null;
            }
        }

        private static string TryResolveGuaranteedMissionSafeTroopId(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            string normalized = troopId.Trim().ToLowerInvariant();
            if (string.Equals(normalized, "mp_coop_light_cavalry_sturgia_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_sturgia_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_battania_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_battania_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_vlandia_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_vlandia_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_empire_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_empire_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_aserai_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_aserai_troop";
            if (string.Equals(normalized, "mp_coop_light_cavalry_khuzait_troop", StringComparison.Ordinal))
                return "mp_light_cavalry_khuzait_troop";
            if (string.Equals(normalized, "mp_coop_heavy_infantry_empire_troop", StringComparison.Ordinal))
                return "mp_heavy_infantry_empire_troop";
            if (string.Equals(normalized, "mp_coop_heavy_infantry_vlandia_troop", StringComparison.Ordinal))
                return "mp_heavy_infantry_vlandia_troop";
            if (normalized.StartsWith("imperial_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("sturgian_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("battanian_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("aserai_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("khuzait_"))
                return "imperial_infantryman";
            if (normalized.StartsWith("vlandian_"))
                return "imperial_infantryman";

            return "imperial_infantryman";
        }

        private static void LogClosestLoadedCharacterIds(string troopId)
        {
            try
            {
                IEnumerable<BasicCharacterObject> loadedCharacters = TryGetLoadedCharacters();
                if (loadedCharacters == null)
                {
                    LogObjectManagerDiagnostics("loaded character list is null for troop id '" + troopId + "'");
                    return;
                }

                List<BasicCharacterObject> loadedCharacterList = loadedCharacters
                    .Where(character => character != null)
                    .ToList();

                ModLogger.Info("CoopMissionSpawnLogic: loaded BasicCharacterObject count = " + loadedCharacterList.Count + ".");
                LogLoadedCharacterCatalog(loadedCharacterList);

                string prefix = troopId;
                int underscoreIndex = troopId.IndexOf('_');
                if (underscoreIndex > 0)
                    prefix = troopId.Substring(0, underscoreIndex + 1);

                List<string> closestIds = loadedCharacterList
                    .Where(character => character != null && !string.IsNullOrWhiteSpace(character.StringId))
                    .Select(character => character.StringId)
                    .Where(id =>
                        id.IndexOf(troopId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        troopId.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.Ordinal)
                    .Take(8)
                    .ToList();

                if (closestIds.Count > 0)
                    ModLogger.Info("CoopMissionSpawnLogic: closest loaded BasicCharacterObject ids for '" + troopId + "' = [" + string.Join(", ", closestIds) + "].");
                else
                    ModLogger.Info("CoopMissionSpawnLogic: no close loaded BasicCharacterObject ids found for '" + troopId + "'.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: closest character id dump failed for '" + troopId + "': " + ex.Message);
            }
        }

        private static IEnumerable<BasicCharacterObject> TryGetLoadedCharacters()
        {
            try
            {
                var methods = typeof(MBObjectManager).GetMethods();
                var genericMethod = methods.FirstOrDefault(method =>
                    method.Name == "GetObjectTypeList" &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 0);

                if (genericMethod == null)
                {
                    LogObjectManagerDiagnostics("GetObjectTypeList<T>() method was not found");
                    return null;
                }

                object result = genericMethod.MakeGenericMethod(typeof(BasicCharacterObject)).Invoke(MBObjectManager.Instance, null);
                if (result is IEnumerable<BasicCharacterObject> typedEnumerable)
                    return typedEnumerable;

                if (result is System.Collections.IEnumerable nonGenericEnumerable)
                    return nonGenericEnumerable.Cast<object>().OfType<BasicCharacterObject>().ToList();

                LogObjectManagerDiagnostics("GetObjectTypeList<T>() returned non-enumerable result of type '" + (result?.GetType().FullName ?? "null") + "'");
                return null;
            }
            catch (Exception ex)
            {
                LogObjectManagerDiagnostics("TryGetLoadedCharacters exception: " + ex.Message);
                return null;
            }
        }

        private static void LogObjectManagerDiagnostics(string reason)
        {
            if (_hasLoggedObjectManagerDiagnostics)
                return;

            _hasLoggedObjectManagerDiagnostics = true;

            try
            {
                var interestingMethods = typeof(MBObjectManager).GetMethods()
                    .Where(method => method.Name.IndexOf("Object", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                     method.Name.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(method =>
                    {
                        string parameters = string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name));
                        return method.Name + "(" + parameters + ")";
                    })
                    .Distinct()
                    .ToList();

                ModLogger.Info("CoopMissionSpawnLogic: MBObjectManager diagnostics reason = " + reason + ".");
                ModLogger.Info("CoopMissionSpawnLogic: MBObjectManager list-like methods = [" + string.Join(", ", interestingMethods) + "].");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: MBObjectManager diagnostics failed: " + ex.Message);
            }
        }

        private static void LogLoadedCharacterCatalog(List<BasicCharacterObject> loadedCharacterList)
        {
            if (_hasLoggedLoadedCharacterCatalog || loadedCharacterList == null)
                return;

            _hasLoggedLoadedCharacterCatalog = true;

            try
            {
                List<string> catalog = loadedCharacterList
                    .Where(character => !string.IsNullOrWhiteSpace(character.StringId))
                    .Select(character => character.StringId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .Take(120)
                    .ToList();

                ModLogger.Info("CoopMissionSpawnLogic: loaded BasicCharacterObject catalog sample = [" + string.Join(", ", catalog) + "].");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: loaded character catalog dump failed: " + ex.Message);
            }
        }

        private static void LogAllowedCharacterResolution()
        {
            if (string.IsNullOrWhiteSpace(SelectedAllowedTroopId))
                return;

            if (SelectedAllowedCharacter == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: selected allowed troop id could not be resolved to BasicCharacterObject.");
                return;
            }

            try
            {
                string name = SelectedAllowedCharacter.Name?.ToString() ?? SelectedAllowedCharacter.StringId;
                ModLogger.Info(
                    "CoopMissionSpawnLogic: selected allowed character resolved. " +
                    "Id=" + SelectedAllowedCharacter.StringId +
                    " Name=" + name +
                    " Level=" + SelectedAllowedCharacter.Level +
                    " Mounted=" + SelectedAllowedCharacter.IsMounted);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: allowed character log failed: " + ex.Message);
            }
        }

        public static void TryRunDedicatedMissionObserver(Mission mission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            bool isNewMission = !ReferenceEquals(_lastDedicatedObservedMission, mission);
            if (isNewMission)
            {
                _lastDedicatedObservedMission = mission;
                _lastDiagnosticSpawnMission = null;
                _lastDiagnosticOwnershipMission = null;
                _hasSpawnedDiagnosticAllowedAgent = false;
                _hasTransferredDiagnosticAllowedAgentToPeer = false;
                _spawnedCoopPeerIndices.Clear();
                _loggedForcedPreferredClassKeys.Clear();
                _lastBridgedSelectedTroopIndexByPeer.Clear();
                _appliedFixedMissionCultureByPeer.Clear();
                _diagnosticAllowedAgent = null;

                ModLogger.Info("CoopMissionSpawnLogic: dedicated observer detected active mission.");

                try
                {
                    string mode = mission.Mode.ToString();
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer mission mode = " + mode);
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer mission mode log failed: " + ex.Message);
                }

                if (GameNetwork.IsSessionActive)
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer peer count = " + GameNetwork.NetworkPeerCount);

                RefreshAllowedTroopsFromRoster("dedicated observer");
                ModLogger.Info("CoopMissionSpawnLogic: dedicated observer roster loaded. Campaign roster: " + CampaignRosterTroopIds.Count + " normalized troop IDs.");
                if (!string.IsNullOrEmpty(SelectedAllowedTroopId))
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer selected allowed troop id = " + SelectedAllowedTroopId);
                else
                    ModLogger.Info("CoopMissionSpawnLogic: dedicated observer found no allowed troop id (roster empty).");
                LogAllowedCharacterResolution();
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, "dedicated observer mission detected", mission);
            }

            TryForceFixedMissionCultures(mission, "dedicated observer");
            TryForcePreferredHeroClassForPeer(mission, "dedicated observer");
            TrySpawnPeersIntoCoopControl(mission, "dedicated observer");
            TryUpdateBattlePhaseState(mission, "dedicated observer");
            TryConsumeBattlePhaseRequests(mission);
        }

        private static void TryUpdateBattlePhaseState(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase >= CoopBattlePhase.BattleActive)
                return;

            int assignedPeerCount = 0;
            int previewReadyPeerCount = 0;
            int controlledPeerCount = 0;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " phase-update");
                if (authoritativeSide == BattleSideEnum.None)
                    continue;

                assignedPeerCount++;
                if (missionPeer.HasSpawnedAgentVisuals)
                    previewReadyPeerCount++;
                if (missionPeer.ControlledAgent != null)
                    controlledPeerCount++;
            }

            if (controlledPeerCount > 0)
            {
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.Deployment, source + " controlled-agent", mission);
                if (assignedPeerCount > 0 && controlledPeerCount >= assignedPeerCount)
                    CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.PreBattleHold, source + " all-peers-controlled", mission);
                return;
            }

            if (previewReadyPeerCount > 0)
            {
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.Deployment, source + " visuals-ready", mission);
                return;
            }

            if (assignedPeerCount > 0)
            {
                CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.UnitSelection, source + " side-assigned", mission);
                return;
            }

            CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, source + " waiting-for-side", mission);
        }

        private static void TryConsumeBattlePhaseRequests(Mission mission)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (!CoopBattlePhaseBridgeFile.ConsumeStartBattleRequest(out string requestSource))
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.PreBattleHold)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: ignored start battle request because phase is not ready. " +
                    "CurrentPhase=" + currentPhase +
                    " Source=" + (requestSource ?? "unknown"));
                return;
            }

            CoopBattlePhaseRuntimeState.SetPhase(
                CoopBattlePhase.BattleActive,
                "bridge-file start battle request from " + (requestSource ?? "unknown"),
                mission,
                allowRegression: false);
        }

        private static void TryForceFixedMissionCultures(Mission mission, string source)
        {
            if (!EnableFixedMissionCulturesExperiment || mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " team-sync");
                string targetCultureId = ResolveFixedMissionCultureIdForSide(authoritativeSide);
                if (string.IsNullOrWhiteSpace(targetCultureId))
                    continue;

                BasicCultureObject targetCulture = MBObjectManager.Instance?.GetObject<BasicCultureObject>(targetCultureId);
                if (targetCulture == null)
                    continue;

                string currentCultureId = missionPeer.Culture?.StringId;
                if (string.Equals(currentCultureId, targetCulture.StringId, StringComparison.Ordinal))
                    continue;

                SetServerMemberValue(missionPeer, "Culture", targetCulture);
                if (!ReferenceEquals(GetServerMemberValue(missionPeer, "Culture"), targetCulture))
                    SetServerMemberValue(missionPeer, "_culture", targetCulture);
                if (!ReferenceEquals(GetServerMemberValue(missionPeer, "Culture"), targetCulture))
                    SetServerMemberValue(missionPeer, "<Culture>k__BackingField", targetCulture);

                string appliedCultureId = (GetServerMemberValue(missionPeer, "Culture") as BasicCultureObject)?.StringId;
                if (!string.Equals(appliedCultureId, targetCulture.StringId, StringComparison.Ordinal))
                    continue;

                if (_appliedFixedMissionCultureByPeer.TryGetValue(peer.Index, out string lastAppliedCultureId) &&
                    string.Equals(lastAppliedCultureId, appliedCultureId, StringComparison.Ordinal))
                {
                    continue;
                }

                _appliedFixedMissionCultureByPeer[peer.Index] = appliedCultureId;

                TryBroadcastFixedMissionCulture(peer, missionPeer, targetCulture);

                ModLogger.Info(
                    "CoopMissionSpawnLogic: forced fixed mission culture (" + source + "). " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " TeamIndex=" + missionPeer.Team.TeamIndex +
                    " Side=" + missionPeer.Team.Side +
                    " PreviousCulture=" + (currentCultureId ?? "null") +
                    " AppliedCulture=" + appliedCultureId);
            }
        }

        private static string ResolveFixedMissionCultureIdForTeam(Team team)
        {
            return ResolveFixedMissionCultureIdForSide(team?.Side ?? BattleSideEnum.None);
        }

        private static string ResolveFixedMissionCultureIdForSide(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return null;

            if (side == BattleSideEnum.Attacker)
                return FixedMissionAttackerCultureId;

            if (side == BattleSideEnum.Defender)
                return FixedMissionDefenderCultureId;

            return null;
        }

        private static void TryBroadcastFixedMissionCulture(NetworkCommunicator peer, MissionPeer missionPeer, BasicCultureObject targetCulture)
        {
            if (peer == null || missionPeer == null || targetCulture == null || !GameNetwork.IsServer)
                return;

            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new NetworkMessages.FromServer.ChangeCulture(missionPeer, targetCulture));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);

                ModLogger.Info(
                    "CoopMissionSpawnLogic: rebroadcast fixed culture after server override. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " TeamIndex=" + (missionPeer.Team?.TeamIndex ?? -1) +
                    " Side=" + (missionPeer.Team?.Side.ToString() ?? "None") +
                    " Culture=" + targetCulture.StringId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: rebroadcast fixed culture failed: " + ex.Message);
            }
        }

        private static object GetServerMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }

            return null;
        }

        private static void SetServerMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return;

            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    property.SetValue(instance, value, null);
                    return;
                }
                catch
                {
                }
            }

            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                try
                {
                    field.SetValue(instance, value);
                }
                catch
                {
                }
            }
        }

        private static void RefreshAllowedTroopsFromRoster(string source)
        {
            BattleRuntimeState rosterState = BattleSnapshotRuntimeState.GetState();
            BattleSnapshotProjectionState rosterProjection = BattleSnapshotRuntimeState.GetProjection();
            if (rosterState == null || rosterProjection == null)
            {
                BattleSnapshotMessage rosterSnapshot = BattleSnapshotRuntimeState.GetCurrent() ?? BattleRosterFileHelper.ReadSnapshot();
                if (rosterSnapshot != null)
                {
                    rosterState = BattleSnapshotRuntimeState.GetState();
                    rosterProjection = BattleSnapshotRuntimeState.GetProjection();
                }
            }

            List<string> roster = BattleRosterFileHelper.ReadRoster();
            CampaignRosterTroopIds = NormalizeRosterTroopIds(roster);
            AllowedControlTroopIds = new List<string>();
            AllowedControlEntryIds = new List<string>();
            AllowedControlCharacters = new List<BasicCharacterObject>();
            AllowedControlTroopIdsBySide = new Dictionary<BattleSideEnum, List<string>>();
            AllowedControlEntryIdsBySide = new Dictionary<BattleSideEnum, List<string>>();
            AllowedControlEntriesBySide = new Dictionary<BattleSideEnum, List<BattleRosterEntryProjectionState>>();
            AllowedControlEntryStatesBySide = new Dictionary<BattleSideEnum, List<RosterEntryState>>();
            AllowedControlCharactersBySide = new Dictionary<BattleSideEnum, List<BasicCharacterObject>>();

            SelectedAllowedTroopId = null;
            SelectedAllowedEntryId = null;
            SelectedAllowedCharacter = null;

            if (rosterState?.Sides != null && rosterState.Sides.Count > 0 && rosterProjection != null && !EnableFixedMissionCulturesExperiment)
            {
                CampaignRosterTroopIds = NormalizeRosterTroopIds(rosterState.FlatTroopIds);
                ControlTroopIds = CampaignRosterTroopIds.ToList();

                List<string> attackerRoster = new List<string>();
                List<string> defenderRoster = new List<string>();
                List<RosterEntryState> playerSideEntries = new List<RosterEntryState>();

                foreach (BattleSideState sideState in rosterState.Sides)
                {
                    BattleSideEnum side = ResolveBattleSideFromState(sideState);
                    List<string> sideTroopIds = NormalizeRosterTroopIds(sideState.TroopIds);
                    if (sideState?.Entries == null || sideState.Entries.Count == 0)
                        continue;

                    if (side == BattleSideEnum.Attacker)
                        attackerRoster = sideTroopIds;
                    else if (side == BattleSideEnum.Defender)
                        defenderRoster = sideTroopIds;

                    if (sideState.IsPlayerSide && playerSideEntries.Count == 0)
                        playerSideEntries = sideState.Entries.Where(entry => entry != null).ToList();

                    foreach (RosterEntryState entryState in sideState.Entries.Where(entry => entry != null))
                        TryAddAllowedControlEntryState(side, entryState, rosterProjection, preferAsSelected: SelectedAllowedCharacter == null && sideState.IsPlayerSide);
                }

                List<RosterEntryState> preferredEntries = playerSideEntries.Count > 0
                    ? playerSideEntries
                    : rosterState.EntriesById.Values.Where(entry => entry != null).ToList();
                if (SelectedAllowedCharacter == null && preferredEntries.Count > 0)
                {
                    SelectedAllowedEntryId = preferredEntries[0].EntryId;
                    SelectedAllowedTroopId = preferredEntries[0].CharacterId;
                    SelectedAllowedCharacter = ResolveAllowedCharacter(SelectedAllowedTroopId);
                    if (SelectedAllowedCharacter != null)
                        TryAddAllowedControlEntryState(BattleSideEnum.None, preferredEntries[0], rosterProjection, preferAsSelected: true);
                }

                RefreshAuthorityStateFromAllowedTroops(source, "battle-snapshot");
                ModLogger.Info(
                    "CoopMissionSpawnLogic: using battle snapshot runtime source = " + (BattleSnapshotRuntimeState.GetSource() ?? "unknown") +
                    " entries = " + rosterState.EntriesById.Count +
                    " parties = " + rosterState.PartiesById.Count +
                    " sides = " + rosterState.Sides.Count);
                LogAllowedControlTroops(source, "battle-snapshot", attackerRoster, defenderRoster);
                return;
            }

            if (EnableFixedMissionCulturesExperiment)
            {
                List<string> attackerRoster = new List<string>
                {
                    "mp_coop_light_cavalry_empire_troop",
                    "mp_coop_heavy_infantry_empire_troop",
                };
                List<string> defenderRoster = new List<string>
                {
                    "mp_coop_light_cavalry_vlandia_troop",
                    "mp_coop_heavy_infantry_vlandia_troop",
                };

                CampaignRosterTroopIds = attackerRoster.Concat(defenderRoster).ToList();
                ControlTroopIds = CampaignRosterTroopIds.ToList();

                foreach (string troopId in attackerRoster)
                    TryAddAllowedControlTroop(BattleSideEnum.Attacker, troopId, preferAsSelected: SelectedAllowedCharacter == null);

                foreach (string troopId in defenderRoster)
                    TryAddAllowedControlTroop(BattleSideEnum.Defender, troopId, preferAsSelected: false);

                RefreshAuthorityStateFromAllowedTroops(source, "fixed-test");
                LogAllowedControlTroops(source, "fixed-test", attackerRoster, defenderRoster);
                return;
            }

            ControlTroopIds = CampaignRosterTroopIds.Take(2).ToList();
            foreach (string troopId in BuildAllowedControlTroopCandidates(ControlTroopIds))
            {
                TryAddAllowedControlTroop(BattleSideEnum.None, troopId, preferAsSelected: SelectedAllowedCharacter == null);
            }

            if (SelectedAllowedCharacter == null && CampaignRosterTroopIds.Count > 0)
            {
                SelectedAllowedTroopId = CampaignRosterTroopIds[0];
                SelectedAllowedCharacter = ResolveAllowedCharacter(SelectedAllowedTroopId);
                if (SelectedAllowedCharacter != null)
                {
                    TryAddAllowedControlTroop(BattleSideEnum.None, SelectedAllowedTroopId, preferAsSelected: true);
                }
            }

            RefreshAuthorityStateFromAllowedTroops(source, "campaign-roster");
            LogAllowedControlTroops(source, "campaign-roster", ControlTroopIds, Array.Empty<string>());
        }

        private static void RefreshAuthorityStateFromAllowedTroops(string source, string mode)
        {
            CoopBattleAuthorityState.Reset(
                AllowedControlEntryIdsBySide,
                AllowedControlTroopIdsBySide,
                AllowedControlEntryIds,
                AllowedControlTroopIds,
                SelectedAllowedEntryId,
                SelectedAllowedTroopId);
            ModLogger.Info(
                "CoopMissionSpawnLogic: coop authority state refreshed. " +
                "Source=" + source +
                " Mode=" + mode +
                " Allowed=" + AllowedControlTroopIds.Count +
                " Entries=" + AllowedControlEntryIds.Count +
                " SideBuckets=" + AllowedControlTroopIdsBySide.Count +
                " FallbackSelected=" + (SelectedAllowedTroopId ?? "null"));
        }

        private static BattleSideEnum ResolveBattleSideFromSnapshot(BattleSideSnapshotMessage sideSnapshot)
        {
            if (sideSnapshot == null)
                return BattleSideEnum.None;

            string sideText = sideSnapshot.SideText ?? sideSnapshot.SideId;
            if (string.Equals(sideText, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sideText, "attacker", StringComparison.OrdinalIgnoreCase))
            {
                return BattleSideEnum.Attacker;
            }

            if (string.Equals(sideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sideText, "defender", StringComparison.OrdinalIgnoreCase))
            {
                return BattleSideEnum.Defender;
            }

            return BattleSideEnum.None;
        }

        private static BattleSideEnum ResolveBattleSideFromState(BattleSideState sideState)
        {
            if (sideState == null)
                return BattleSideEnum.None;

            if (string.Equals(sideState.CanonicalSideKey, "attacker", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;

            if (string.Equals(sideState.CanonicalSideKey, "defender", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static void LogAllowedControlTroops(string source, string mode, IReadOnlyCollection<string> attackerRoster, IReadOnlyCollection<string> defenderRoster)
        {
            string controlUnits = ControlTroopIds.Count > 0
                ? string.Join(", ", ControlTroopIds)
                : "(none)";
            ModLogger.Info("CoopMissionSpawnLogic: control troop candidates (" + source + ", " + mode + ") = [" + controlUnits + "].");

            string allowedUnits = AllowedControlTroopIds.Count > 0
                ? string.Join(", ", AllowedControlTroopIds)
                : "(none)";
            ModLogger.Info("CoopMissionSpawnLogic: allowed control troop ids (" + source + ", " + mode + ") = [" + allowedUnits + "].");

            string attackerUnits = AllowedControlTroopIdsBySide.TryGetValue(BattleSideEnum.Attacker, out List<string> attackerAllowed) && attackerAllowed.Count > 0
                ? string.Join(", ", attackerAllowed)
                : (attackerRoster != null && attackerRoster.Count > 0 ? string.Join(", ", attackerRoster) : "(none)");
            string defenderUnits = AllowedControlTroopIdsBySide.TryGetValue(BattleSideEnum.Defender, out List<string> defenderAllowed) && defenderAllowed.Count > 0
                ? string.Join(", ", defenderAllowed)
                : (defenderRoster != null && defenderRoster.Count > 0 ? string.Join(", ", defenderRoster) : "(none)");

            ModLogger.Info("CoopMissionSpawnLogic: attacker allowed troop ids (" + source + ", " + mode + ") = [" + attackerUnits + "].");
            ModLogger.Info("CoopMissionSpawnLogic: defender allowed troop ids (" + source + ", " + mode + ") = [" + defenderUnits + "].");
        }

        private static IEnumerable<string> BuildAllowedControlTroopCandidates(IEnumerable<string> baseControlTroopIds)
        {
            HashSet<string> yieldedTroopIds = new HashSet<string>(StringComparer.Ordinal);
            if (baseControlTroopIds == null)
                yield break;

            foreach (string troopId in baseControlTroopIds)
            {
                if (string.IsNullOrWhiteSpace(troopId) || !yieldedTroopIds.Add(troopId))
                    continue;

                yield return troopId;

                foreach (string companionTroopId in GetCuratedCompanionControlTroopIds(troopId))
                {
                    if (string.IsNullOrWhiteSpace(companionTroopId) || !yieldedTroopIds.Add(companionTroopId))
                        continue;

                    yield return companionTroopId;
                }
            }
        }

        private static IEnumerable<string> GetCuratedCompanionControlTroopIds(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                yield break;

            string normalizedTroopId = troopId.Trim().ToLowerInvariant();
            if (normalizedTroopId.IndexOf("_empire_", StringComparison.Ordinal) >= 0)
            {
                yield return "mp_coop_light_cavalry_empire_troop";
                yield return "mp_coop_heavy_infantry_empire_troop";
            }

            if (normalizedTroopId.IndexOf("_vlandia_", StringComparison.Ordinal) >= 0)
            {
                yield return "mp_coop_light_cavalry_vlandia_troop";
                yield return "mp_coop_heavy_infantry_vlandia_troop";
            }
        }

        private static bool TryAddAllowedControlTroop(BattleSideEnum side, string troopId, bool preferAsSelected)
        {
            if (string.IsNullOrWhiteSpace(troopId) || AllowedControlTroopIds.Contains(troopId))
                return false;

            BasicCharacterObject resolvedCharacter = ResolveAllowedCharacter(troopId);
            if (resolvedCharacter == null)
                return false;

            if (!AllowedControlTroopIdsBySide.TryGetValue(side, out List<string> sideTroopIds))
            {
                sideTroopIds = new List<string>();
                AllowedControlTroopIdsBySide[side] = sideTroopIds;
            }

            if (!AllowedControlCharactersBySide.TryGetValue(side, out List<BasicCharacterObject> sideCharacters))
            {
                sideCharacters = new List<BasicCharacterObject>();
                AllowedControlCharactersBySide[side] = sideCharacters;
            }

            AllowedControlTroopIds.Add(troopId);
            AllowedControlCharacters.Add(resolvedCharacter);
            sideTroopIds.Add(troopId);
            sideCharacters.Add(resolvedCharacter);

            if (preferAsSelected)
            {
                SelectedAllowedTroopId = troopId;
                SelectedAllowedCharacter = resolvedCharacter;
            }

            return true;
        }

        private static bool TryAddAllowedControlEntry(BattleSideEnum side, BattleRosterEntryProjectionState entryProjection, bool preferAsSelected)
        {
            if (entryProjection == null || string.IsNullOrWhiteSpace(entryProjection.EntryId) || string.IsNullOrWhiteSpace(entryProjection.CharacterId))
                return false;

            BasicCharacterObject resolvedCharacter = ResolveAllowedCharacter(entryProjection.CharacterId);
            if (resolvedCharacter == null)
                return false;

            if (!AllowedControlEntryIdsBySide.TryGetValue(side, out List<string> sideEntryIds))
            {
                sideEntryIds = new List<string>();
                AllowedControlEntryIdsBySide[side] = sideEntryIds;
            }

            if (!AllowedControlEntriesBySide.TryGetValue(side, out List<BattleRosterEntryProjectionState> sideEntries))
            {
                sideEntries = new List<BattleRosterEntryProjectionState>();
                AllowedControlEntriesBySide[side] = sideEntries;
            }

            if (!AllowedControlCharactersBySide.TryGetValue(side, out List<BasicCharacterObject> sideCharacters))
            {
                sideCharacters = new List<BasicCharacterObject>();
                AllowedControlCharactersBySide[side] = sideCharacters;
            }

            if (!AllowedControlEntryIds.Contains(entryProjection.EntryId))
                AllowedControlEntryIds.Add(entryProjection.EntryId);
            if (!sideEntryIds.Contains(entryProjection.EntryId))
                sideEntryIds.Add(entryProjection.EntryId);
            if (!sideEntries.Any(entry => string.Equals(entry?.EntryId, entryProjection.EntryId, StringComparison.Ordinal)))
                sideEntries.Add(entryProjection);

            if (!AllowedControlTroopIds.Contains(entryProjection.CharacterId))
                AllowedControlTroopIds.Add(entryProjection.CharacterId);
            if (!AllowedControlCharacters.Contains(resolvedCharacter))
                AllowedControlCharacters.Add(resolvedCharacter);

            if (!AllowedControlTroopIdsBySide.TryGetValue(side, out List<string> sideTroopIds))
            {
                sideTroopIds = new List<string>();
                AllowedControlTroopIdsBySide[side] = sideTroopIds;
            }

            if (!sideTroopIds.Contains(entryProjection.CharacterId))
                sideTroopIds.Add(entryProjection.CharacterId);
            if (!sideCharacters.Contains(resolvedCharacter))
                sideCharacters.Add(resolvedCharacter);

            if (preferAsSelected)
            {
                SelectedAllowedEntryId = entryProjection.EntryId;
                SelectedAllowedTroopId = entryProjection.CharacterId;
                SelectedAllowedCharacter = resolvedCharacter;
            }

            return true;
        }

        private static bool TryAddAllowedControlEntryState(
            BattleSideEnum side,
            RosterEntryState entryState,
            BattleSnapshotProjectionState rosterProjection,
            bool preferAsSelected)
        {
            if (entryState == null || rosterProjection?.EntriesById == null)
                return false;

            if (!AllowedControlEntryStatesBySide.TryGetValue(side, out List<RosterEntryState> sideEntryStates))
            {
                sideEntryStates = new List<RosterEntryState>();
                AllowedControlEntryStatesBySide[side] = sideEntryStates;
            }

            if (!sideEntryStates.Any(entry => string.Equals(entry?.EntryId, entryState.EntryId, StringComparison.Ordinal)))
                sideEntryStates.Add(entryState);

            if (!rosterProjection.EntriesById.TryGetValue(entryState.EntryId, out BattleRosterEntryProjectionState entryProjection) || entryProjection == null)
                return false;

            return TryAddAllowedControlEntry(side, entryProjection, preferAsSelected);
        }

        public static IReadOnlyList<string> GetAllowedControlTroopIdsSnapshot()
        {
            return CoopBattleAuthorityState.GetAllowedTroopIds(BattleSideEnum.None);
        }

        public static IReadOnlyList<string> GetAllowedControlTroopIdsSnapshot(BattleSideEnum side)
        {
            return CoopBattleAuthorityState.GetAllowedTroopIds(side);
        }

        public static bool HasSideScopedRoster()
        {
            return HasAllowedRosterForSide(BattleSideEnum.Attacker) || HasAllowedRosterForSide(BattleSideEnum.Defender);
        }

        public static IReadOnlyList<string> GetAllowedControlEntryIdsSnapshot()
        {
            return AllowedControlEntryIds.ToArray();
        }

        public static IReadOnlyList<string> GetAllowedControlEntryIdsSnapshot(BattleSideEnum side)
        {
            if (AllowedControlEntryIdsBySide.TryGetValue(side, out List<string> sideEntryIds) && sideEntryIds.Count > 0)
                return sideEntryIds.ToArray();

            return Array.Empty<string>();
        }

        public static IReadOnlyList<RosterEntryState> GetAllowedControlEntryStatesSnapshot(BattleSideEnum side)
        {
            if (AllowedControlEntryStatesBySide.TryGetValue(side, out List<RosterEntryState> sideEntries) && sideEntries.Count > 0)
                return sideEntries.ToArray();

            return Array.Empty<RosterEntryState>();
        }

        public static BattleSideEnum ResolveAuthoritativeSide(MissionPeer missionPeer, Mission mission, string source)
        {
            if (missionPeer == null)
                return BattleSideEnum.None;

            BattleSideEnum requestedSide = CoopBattleAuthorityState.GetRequestedSide(missionPeer);
            BattleSideEnum assignedSide = CoopBattleAuthorityState.GetAssignedSide(missionPeer);
            if (HasAllowedRosterForSide(assignedSide))
                return assignedSide;

            BattleSideEnum runtimeSide =
                missionPeer.Team != null && !ReferenceEquals(missionPeer.Team, mission?.SpectatorTeam)
                    ? missionPeer.Team.Side
                    : BattleSideEnum.None;
            if (runtimeSide != BattleSideEnum.None)
                CoopBattleAuthorityState.TryRequestSide(missionPeer, runtimeSide, source + " runtime-team");

            BattleSideEnum requestedOrRuntimeSide = requestedSide != BattleSideEnum.None ? requestedSide : runtimeSide;
            if (!HasAllowedRosterForSide(requestedOrRuntimeSide))
                return BattleSideEnum.None;

            CoopBattleAuthorityState.TryAssignSide(missionPeer, requestedOrRuntimeSide, source);
            return requestedOrRuntimeSide;
        }

        private static bool HasAllowedRosterForSide(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return false;

            if (AllowedControlEntryStatesBySide.TryGetValue(side, out List<RosterEntryState> sideEntryStates) && sideEntryStates.Count > 0)
                return true;

            if (AllowedControlTroopIdsBySide.TryGetValue(side, out List<string> sideTroopIds) && sideTroopIds.Count > 0)
                return true;

            string canonicalSideKey = side == BattleSideEnum.Attacker
                ? "attacker"
                : side == BattleSideEnum.Defender
                    ? "defender"
                    : null;
            if (string.IsNullOrWhiteSpace(canonicalSideKey))
                return false;

            BattleSideState sideState = BattleSnapshotRuntimeState.GetSideState(canonicalSideKey);
            return sideState?.Entries != null && sideState.Entries.Count > 0;
        }

        private static IReadOnlyList<string> GetFixedTestAllowedControlTroopIdsForSide(BattleSideEnum side)
        {
            if (!EnableFixedMissionCulturesExperiment)
                return Array.Empty<string>();

            if (side == BattleSideEnum.Attacker)
            {
                return new[]
                {
                    "mp_coop_light_cavalry_empire_troop",
                    "mp_coop_heavy_infantry_empire_troop",
                };
            }

            if (side == BattleSideEnum.Defender)
            {
                return new[]
                {
                    "mp_coop_light_cavalry_vlandia_troop",
                    "mp_coop_heavy_infantry_vlandia_troop",
                };
            }

            return Array.Empty<string>();
        }

        public static IReadOnlyList<string> GetFixedTestAllowedControlTroopIdsForSidePublic(BattleSideEnum side)
        {
            return GetFixedTestAllowedControlTroopIdsForSide(side);
        }

        private static void TryRefreshPendingSpawnRequests(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                if (missionPeer.ControlledAgent != null)
                {
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " controlled-agent");
                    continue;
                }

                if (missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                {
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " spectator");
                    continue;
                }

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, source + " pending-request");
                if (authoritativeSide == BattleSideEnum.None)
                {
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " no-side");
                    continue;
                }

                CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
                bool hasMeaningfulSelection =
                    !string.IsNullOrWhiteSpace(selectionState.EntryId) ||
                    !string.IsNullOrWhiteSpace(selectionState.TroopId);
                if (!hasMeaningfulSelection)
                {
                    CoopBattleSpawnRequestState.Clear(missionPeer, source + " no-selection");
                    continue;
                }

                CoopBattleSpawnRequestState.TryQueueFromSelection(missionPeer, source);
            }
        }

        private static void TrySpawnPeersIntoCoopControl(Mission mission, string source)
        {
            if (!EnableDirectCoopPlayerSpawnExperiment || mission == null || !GameNetwork.IsServer)
                return;

            if (GameNetwork.NetworkPeers == null || GameNetwork.NetworkPeers.Count == 0)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!TryGetSpawnablePeerState(mission, peer, out MissionPeer missionPeer, out string reason))
                {
                    LogSkippedSpawn(peer, reason, source);
                    continue;
                }

                if (!TryResolveAuthoritativeCharacterForPeer(missionPeer, out BasicCharacterObject selectedCharacter, out string selectedTroopId, out string selectedEntryId, out string resolveReason))
                {
                    LogSkippedSpawn(peer, "authoritative troop unresolved: " + resolveReason, source);
                    continue;
                }

                Agent spawnedAgent = SpawnCoopControlledAgent(mission, missionPeer, peer, selectedCharacter, source);
                if (spawnedAgent == null)
                    continue;

                _spawnedCoopPeerIndices.Add(peer.Index);
                CoopBattleSpawnRequestState.Clear(missionPeer, source + " spawn-succeeded");
                missionPeer.HasSpawnedAgentVisuals = true;
                missionPeer.EquipmentUpdatingExpired = false;

                string peerName = peer.UserName ?? peer.Index.ToString();
                string agentName = spawnedAgent.Name?.ToString() ?? spawnedAgent.Index.ToString();
                ModLogger.Info(
                    "CoopMissionSpawnLogic: coop direct spawn succeeded (" + source + "). " +
                    "Peer=" + peerName +
                    " TroopId=" + selectedTroopId +
                    " EntryId=" + (selectedEntryId ?? "null") +
                    " Agent=" + agentName +
                    " TeamIndex=" + (spawnedAgent.Team?.TeamIndex ?? -1) +
                    " Side=" + (spawnedAgent.Team?.Side ?? BattleSideEnum.None) +
                    " Position=" + spawnedAgent.Position);
            }
        }

        private static bool TryResolveAuthoritativeCharacterForPeer(
            MissionPeer missionPeer,
            out BasicCharacterObject selectedCharacter,
            out string selectedTroopId,
            out string selectedEntryId,
            out string reason)
        {
            selectedCharacter = null;
            selectedTroopId = null;
            selectedEntryId = null;
            reason = string.Empty;

            if (missionPeer == null)
            {
                reason = "mission peer missing";
                return false;
            }

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            RosterEntryState preferredEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            if (CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState pendingRequest))
            {
                selectedEntryId = pendingRequest.EntryId;
                string requestedTroopId = pendingRequest.TroopId;
                if (!string.IsNullOrWhiteSpace(requestedTroopId) &&
                    preferredEntry != null &&
                    string.Equals(preferredEntry.EntryId, pendingRequest.EntryId, StringComparison.Ordinal))
                {
                    selectedTroopId = ResolveCultureSpecificTargetTroopId(requestedTroopId, missionPeer.Culture);
                    selectedCharacter = BattleSnapshotRuntimeState.TryResolveCharacterObject(selectedEntryId) ?? ResolveAllowedCharacter(selectedTroopId);
                    if (selectedCharacter != null)
                        return true;
                }
            }

            selectedEntryId = preferredEntry?.EntryId ?? selectionState.EntryId;
            string baseTroopId = !string.IsNullOrWhiteSpace(selectionState.TroopId)
                ? selectionState.TroopId
                : preferredEntry?.CharacterId;
            selectedTroopId = string.IsNullOrWhiteSpace(baseTroopId)
                ? GetPreferredTargetTroopIdForPeerCulture(missionPeer)
                : ResolveCultureSpecificTargetTroopId(baseTroopId, missionPeer.Culture);
            if (string.IsNullOrWhiteSpace(selectedTroopId))
            {
                reason = "no authoritative troop selected";
                return false;
            }

            selectedCharacter = BattleSnapshotRuntimeState.TryResolveCharacterObject(selectedEntryId) ?? ResolveAllowedCharacter(selectedTroopId);
            if (selectedCharacter == null)
            {
                reason = "selected troop/entry not resolved (troop='" + selectedTroopId + "', entry='" + (selectedEntryId ?? "null") + "')";
                return false;
            }

            return true;
        }

        private static bool TryGetSpawnablePeerState(
            Mission mission,
            NetworkCommunicator peer,
            out MissionPeer missionPeer,
            out string reason)
        {
            missionPeer = null;
            reason = string.Empty;

            if (peer == null)
            {
                reason = "peer is null";
                return false;
            }

            if (peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
            {
                reason = "peer not ready";
                return false;
            }

            if (_spawnedCoopPeerIndices.Contains(peer.Index))
            {
                reason = "peer already had coop life";
                return false;
            }

            missionPeer = peer.GetComponent<MissionPeer>();
            if (missionPeer == null)
            {
                reason = "mission peer missing";
                return false;
            }

            if (missionPeer.ControlledAgent != null)
            {
                reason = "peer already controls agent";
                return false;
            }

            if (missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
            {
                reason = "peer still spectator";
                return false;
            }

            BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, "spawnable-peer-state");
            if (authoritativeSide == BattleSideEnum.None)
            {
                reason = "peer side not assigned";
                return false;
            }

            if (!missionPeer.TeamInitialPerkInfoReady)
            {
                reason = "team perk info not ready";
                return false;
            }

            if (!IsSpawnTimerReady(missionPeer))
            {
                reason = "spawn timer not ready";
                return false;
            }

            return true;
        }

        private static void LogSkippedSpawn(NetworkCommunicator peer, string reason, string source)
        {
            if (peer == null || string.IsNullOrWhiteSpace(reason))
                return;

            string logKey = peer.Index + "|spawn-skipped|" + reason;
            if (!_loggedForcedPreferredClassKeys.Add(logKey))
                return;

            ModLogger.Info(
                "CoopMissionSpawnLogic: coop direct spawn waiting (" + source + "). " +
                "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                " Reason=" + reason);
        }

        private static Agent SpawnCoopControlledAgent(
            Mission mission,
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            BasicCharacterObject troop,
            string source)
        {
            if (mission == null || missionPeer == null || peer == null || troop == null)
                return null;

            try
            {
                Team team = missionPeer.Team;
                if (team == null)
                    return null;

                GetDirectSpawnFrame(mission, team, out Vec3 spawnPosition, out Vec2 spawnDirection);

                Equipment spawnEquipment = troop.Equipment?.Clone(false);
                var origin = new BasicBattleAgentOrigin(troop);

                AgentBuildData buildData = new AgentBuildData(troop);
                buildData.MissionPeer(missionPeer);
                buildData.Team(team);
                buildData.TroopOrigin(origin);
                buildData.Controller(AgentControllerType.Player);
                buildData.InitialPosition(in spawnPosition);
                buildData.InitialDirection(in spawnDirection);
                buildData.SpawnsIntoOwnFormation(false);
                buildData.SpawnsUsingOwnTroopClass(false);
                if (spawnEquipment != null)
                    buildData.Equipment(spawnEquipment);

                Agent spawnedAgent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: false);
                if (spawnedAgent == null)
                    return null;

                missionPeer.ControlledAgent = spawnedAgent;
                missionPeer.FollowedAgent = spawnedAgent;
                peer.ControlledAgent = spawnedAgent;
                spawnedAgent.WieldInitialWeapons(
                    Agent.WeaponWieldActionType.Instant,
                    Equipment.InitialWeaponEquipPreference.Any);

                return spawnedAgent;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: coop direct spawn failed (" + source + "): " + ex.Message);
                return null;
            }
        }

        private static void GetDirectSpawnFrame(Mission mission, Team team, out Vec3 spawnPosition, out Vec2 spawnDirection)
        {
            spawnPosition = team.Side == BattleSideEnum.Attacker
                ? new Vec3(-12f, 0f, 0f)
                : new Vec3(12f, 0f, 0f);
            spawnDirection = team.Side == BattleSideEnum.Attacker
                ? new Vec2(1f, 0f)
                : new Vec2(-1f, 0f);

            if (team.ActiveAgents == null || team.ActiveAgents.Count == 0)
                return;

            Agent anchorAgent = team.ActiveAgents.FirstOrDefault(agent => agent != null && agent.IsActive());
            if (anchorAgent == null)
                return;

            float lateralOffset = team.Side == BattleSideEnum.Attacker ? -1.5f : 1.5f;
            spawnPosition = anchorAgent.Position + new Vec3(lateralOffset, 0f, 0f);

            Agent targetEnemy = mission.GetClosestEnemyAgent(team, spawnPosition, 200f);
            Vec3 lookAt = targetEnemy != null
                ? targetEnemy.Position
                : anchorAgent.Position + new Vec3(team.Side == BattleSideEnum.Attacker ? 6f : -6f, 0f, 0f);

            Vec3 toEnemy3 = lookAt - spawnPosition;
            spawnDirection = new Vec2(toEnemy3.x, toEnemy3.y);
            if (spawnDirection.LengthSquared < 0.001f)
                spawnDirection = team.Side == BattleSideEnum.Attacker ? new Vec2(1f, 0f) : new Vec2(-1f, 0f);
            spawnDirection.Normalize();
        }

        public static bool TryResolvePreferredHeroClassForPeer(
            MissionPeer missionPeer,
            MultiplayerClassDivisions.MPHeroClass vanillaClass,
            out MultiplayerClassDivisions.MPHeroClass preferredClass,
            out int preferredTroopIndex,
            out string debugReason)
        {
            return TryResolvePreferredHeroClassForPeer(
                missionPeer,
                vanillaClass,
                true,
                out preferredClass,
                out preferredTroopIndex,
                out debugReason);
        }

        private static bool TryResolvePreferredHeroClassForPeer(
            MissionPeer missionPeer,
            MultiplayerClassDivisions.MPHeroClass vanillaClass,
            bool requireSpawnTimerReady,
            out MultiplayerClassDivisions.MPHeroClass preferredClass,
            out int preferredTroopIndex,
            out string debugReason)
        {
            preferredClass = null;
            preferredTroopIndex = -1;
            debugReason = string.Empty;

            if (!GameNetwork.IsServer)
            {
                debugReason = "not server";
                return false;
            }

            if (missionPeer == null)
            {
                debugReason = "missionPeer is null";
                return false;
            }

            if (missionPeer.Team == null || missionPeer.Culture == null)
            {
                debugReason = "peer team/culture not ready";
                return false;
            }

            if (missionPeer.ControlledAgent != null)
            {
                debugReason = "peer already has controlled agent";
                return false;
            }

            if (!CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
            {
                debugReason = "no pending spawn request";
                return false;
            }

            if (requireSpawnTimerReady && !IsSpawnTimerReady(missionPeer))
            {
                debugReason = "spawn timer not ready";
                return false;
            }

            string targetTroopId = GetPreferredTargetTroopIdForPeerCulture(missionPeer);
            if (string.IsNullOrWhiteSpace(targetTroopId))
            {
                debugReason = "no selected allowed troop";
                return false;
            }

            List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                .GetMPHeroClasses(missionPeer.Culture)
                ?.Where(heroClass => heroClass?.HeroCharacter != null)
                .ToList();

            if (cultureClasses == null || cultureClasses.Count == 0)
            {
                debugReason = "peer culture has no MP classes";
                return false;
            }

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            RosterEntryState preferredAllowedEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            int currentSelectedTroopIndex = missionPeer.SelectedTroopIndex;
            if (currentSelectedTroopIndex >= 0 && currentSelectedTroopIndex < cultureClasses.Count)
            {
                MultiplayerClassDivisions.MPHeroClass currentlySelectedClass = cultureClasses[currentSelectedTroopIndex];
                string matchedAllowedTroopId = ResolveMatchedAllowedTroopId(currentlySelectedClass, missionPeer);
                if (!string.IsNullOrWhiteSpace(matchedAllowedTroopId))
                {
                    RosterEntryState matchedAllowedEntry = ResolveMatchedAllowedEntryState(currentlySelectedClass, missionPeer, selectionState, preferredAllowedEntry);
                    ApplyAuthoritativePreferredSelection(
                        missionPeer,
                        matchedAllowedEntry,
                        matchedAllowedTroopId,
                        "preserve player-selected allowed class");
                    preferredClass = currentlySelectedClass;
                    preferredTroopIndex = currentSelectedTroopIndex;
                    debugReason = "preserve player-selected allowed class '" + matchedAllowedTroopId + "' entry='" + (matchedAllowedEntry?.EntryId ?? "null") + "'";
                    return !ReferenceEquals(preferredClass, vanillaClass) || missionPeer.SelectedTroopIndex != preferredTroopIndex;
                }
            }

            int exactIndex = cultureClasses.FindIndex(heroClass =>
                MatchesPreferredHeroClass(heroClass, targetTroopId));
            if (exactIndex >= 0)
            {
                RosterEntryState exactMatchEntry = ResolveMatchedAllowedEntryState(cultureClasses[exactIndex], missionPeer, selectionState, preferredAllowedEntry);
                ApplyAuthoritativePreferredSelection(missionPeer, exactMatchEntry, targetTroopId, "exact troop id match");
                preferredClass = cultureClasses[exactIndex];
                preferredTroopIndex = exactIndex;
                debugReason = "exact troop id match for '" + targetTroopId + "' entry='" + (exactMatchEntry?.EntryId ?? selectionState.EntryId ?? "null") + "'";
                return !ReferenceEquals(preferredClass, vanillaClass) || missionPeer.SelectedTroopIndex != preferredTroopIndex;
            }

            int bestIndex = FindBestPeerCultureHeroClassIndex(cultureClasses, targetTroopId);
            if (bestIndex < 0 || bestIndex >= cultureClasses.Count)
            {
                debugReason = "no suitable class in peer culture";
                return false;
            }

            RosterEntryState surrogateEntry = ResolveMatchedAllowedEntryState(cultureClasses[bestIndex], missionPeer, selectionState, preferredAllowedEntry);
            ApplyAuthoritativePreferredSelection(missionPeer, surrogateEntry, targetTroopId, "peer-culture surrogate");
            preferredClass = cultureClasses[bestIndex];
            preferredTroopIndex = bestIndex;
            debugReason = "peer-culture surrogate for '" + targetTroopId + "' entry='" + (surrogateEntry?.EntryId ?? selectionState.EntryId ?? "null") + "'";
            return !ReferenceEquals(preferredClass, vanillaClass) || missionPeer.SelectedTroopIndex != preferredTroopIndex;
        }

        private static string ResolveMatchedAllowedTroopId(MultiplayerClassDivisions.MPHeroClass heroClass, MissionPeer missionPeer)
        {
            if (heroClass?.HeroCharacter == null || missionPeer == null)
                return null;

            foreach (string allowedTroopId in ResolveAllowedTargetTroopIdsForPeerCulture(missionPeer))
            {
                if (MatchesPreferredHeroClass(heroClass, allowedTroopId))
                    return allowedTroopId;
            }

            return null;
        }

        private static RosterEntryState ResolvePreferredAllowedEntryStateForPeer(
            MissionPeer missionPeer,
            CoopBattleAuthorityState.PeerSelectionState selectionState)
        {
            if (missionPeer == null)
                return null;

            IReadOnlyList<RosterEntryState> allowedEntries = CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(selectionState.Side);
            if (allowedEntries.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(selectionState.EntryId))
            {
                RosterEntryState selectedEntry = allowedEntries.FirstOrDefault(entry =>
                    string.Equals(entry?.EntryId, selectionState.EntryId, StringComparison.Ordinal));
                if (selectedEntry != null)
                    return selectedEntry;
            }

            if (!string.IsNullOrWhiteSpace(selectionState.TroopId))
            {
                RosterEntryState selectedByTroop = allowedEntries.FirstOrDefault(entry =>
                    string.Equals(entry?.CharacterId, selectionState.TroopId, StringComparison.OrdinalIgnoreCase));
                if (selectedByTroop != null)
                    return selectedByTroop;
            }

            return allowedEntries.FirstOrDefault(entry => entry != null);
        }

        private static RosterEntryState ResolveMatchedAllowedEntryState(
            MultiplayerClassDivisions.MPHeroClass heroClass,
            MissionPeer missionPeer,
            CoopBattleAuthorityState.PeerSelectionState selectionState,
            RosterEntryState preferredAllowedEntry)
        {
            if (heroClass?.HeroCharacter == null || missionPeer == null)
                return null;

            if (!string.IsNullOrWhiteSpace(selectionState.EntryId) &&
                MatchesPreferredHeroClass(heroClass, selectionState.TroopId))
            {
                RosterEntryState selectedEntry = CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(selectionState.Side)
                    .FirstOrDefault(entry => string.Equals(entry?.EntryId, selectionState.EntryId, StringComparison.Ordinal));
                if (selectedEntry != null)
                    return selectedEntry;
            }

            if (preferredAllowedEntry != null && MatchesPreferredHeroClass(heroClass, preferredAllowedEntry.CharacterId))
                return preferredAllowedEntry;

            return CoopMissionSpawnLogic.GetAllowedControlEntryStatesSnapshot(selectionState.Side)
                .FirstOrDefault(entry =>
                    entry != null &&
                    MatchesPreferredHeroClass(heroClass, entry.CharacterId));
        }

        private static string ResolveMatchedAllowedEntryId(MultiplayerClassDivisions.MPHeroClass heroClass, MissionPeer missionPeer)
        {
            if (heroClass?.HeroCharacter == null || missionPeer == null)
                return null;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            RosterEntryState preferredAllowedEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            return ResolveMatchedAllowedEntryState(heroClass, missionPeer, selectionState, preferredAllowedEntry)?.EntryId;
        }

        private static void ApplyAuthoritativePreferredSelection(
            MissionPeer missionPeer,
            RosterEntryState preferredEntry,
            string preferredTroopId,
            string source)
        {
            if (missionPeer == null)
                return;

            if (preferredEntry != null && !string.IsNullOrWhiteSpace(preferredEntry.EntryId))
            {
                if (CoopBattleAuthorityState.TrySetSelectedEntryId(missionPeer, preferredEntry.EntryId, source + " entry"))
                    return;
            }

            if (!string.IsNullOrWhiteSpace(preferredTroopId))
                CoopBattleAuthorityState.TrySetSelectedTroopId(missionPeer, preferredTroopId, source + " troop");
        }

        private static void TryForcePreferredHeroClassForPeer(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || GameNetwork.NetworkPeers == null || GameNetwork.NetworkPeers.Count == 0)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.ControlledAgent != null)
                    continue;

                if (missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam) || missionPeer.Culture == null)
                    continue;

                if (!CoopBattleSpawnRequestState.HasPendingRequest(missionPeer))
                    continue;

                bool hasExplicitSelection = CoopBattleAuthorityState.HasExplicitSelection(missionPeer);

                int currentTroopIndex = missionPeer.SelectedTroopIndex;
                MultiplayerClassDivisions.MPHeroClass currentClass = null;
                List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                    .GetMPHeroClasses(missionPeer.Culture)
                    ?.Where(heroClass => heroClass?.HeroCharacter != null)
                    .ToList();
                if (cultureClasses != null && currentTroopIndex >= 0 && currentTroopIndex < cultureClasses.Count)
                    currentClass = cultureClasses[currentTroopIndex];

                if (!hasExplicitSelection && currentClass != null)
                    continue;

                // If the peer already has a local vanilla class selection, avoid rebroadcasting the same
                // preferred index during the preview/equipment-update window. This rebroadcast path is
                // currently the strongest suspect for dedicated crashes on infantry preview.
                if (hasExplicitSelection && currentClass != null)
                    continue;

                if (!TryResolvePreferredHeroClassForPeer(
                        missionPeer,
                        currentClass,
                        false,
                        out MultiplayerClassDivisions.MPHeroClass preferredClass,
                        out int preferredTroopIndex,
                        out string debugReason))
                {
                    continue;
                }

                if (preferredTroopIndex < 0)
                    continue;

                ApplySelectedTroopIndexBridge(missionPeer, peer, preferredTroopIndex);

                string classId = preferredClass?.HeroCharacter?.StringId ?? "null";
                string peerName = peer.UserName ?? peer.Index.ToString();
                string logKey = peer.Index + "|forced-selected-index|" + preferredTroopIndex + "|" + classId;
                if (_loggedForcedPreferredClassKeys.Add(logKey))
                {
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: forced preferred troop index before vanilla spawn (" + source + "). " +
                        "Peer=" + peerName +
                        " Culture=" + (missionPeer.Culture?.StringId ?? "null") +
                        " TroopIndex=" + preferredTroopIndex +
                        " HeroClass=" + classId +
                        " Reason=" + debugReason);
                }
            }

            TrySyncCoopClassRestrictions(mission, source);
        }

        private static void ApplySelectedTroopIndexBridge(MissionPeer missionPeer, NetworkCommunicator peer, int preferredTroopIndex)
        {
            if (missionPeer == null || peer == null || preferredTroopIndex < 0)
                return;

            if (missionPeer.SelectedTroopIndex == preferredTroopIndex &&
                _lastBridgedSelectedTroopIndexByPeer.TryGetValue(peer.Index, out int lastBridgedTroopIndex) &&
                lastBridgedTroopIndex == preferredTroopIndex)
            {
                return;
            }

            missionPeer.SelectedTroopIndex = preferredTroopIndex;
            _lastBridgedSelectedTroopIndexByPeer[peer.Index] = preferredTroopIndex;

            GameNetwork.BeginBroadcastModuleEvent();
            GameNetwork.WriteMessage(new NetworkMessages.FromServer.UpdateSelectedTroopIndex(peer, preferredTroopIndex));
            GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
        }

        private static void TrySyncCoopClassRestrictions(Mission mission, string source)
        {
            if (!EnableCoopClassRestrictionSyncExperiment)
                return;

            if (mission == null || !GameNetwork.IsServer)
                return;

            MissionLobbyComponent lobbyComponent = mission.GetMissionBehavior<MissionLobbyComponent>();
            if (lobbyComponent == null)
                return;

            HashSet<FormationClass> allowedClasses = ResolveAllowedFormationClassesForMission(mission);
            if (allowedClasses.Count == 0)
                return;

            foreach (FormationClass formationClass in RestrictableFormationClasses)
            {
                bool desiredAvailability = allowedClasses.Contains(formationClass);
                if (_appliedCoopClassAvailabilityStates.TryGetValue(formationClass, out bool appliedAvailability) &&
                    appliedAvailability == desiredAvailability)
                {
                    continue;
                }

                bool finalAvailability = ApplyCoopClassRestriction(lobbyComponent, formationClass, desiredAvailability);
                _appliedCoopClassAvailabilityStates[formationClass] = finalAvailability;

                ModLogger.Info(
                    "CoopMissionSpawnLogic: synced class restriction (" + source + "). " +
                    "FormationClass=" + formationClass +
                    " DesiredAvailability=" + desiredAvailability +
                    " FinalAvailability=" + finalAvailability);
            }
        }

        private static HashSet<FormationClass> ResolveAllowedFormationClassesForMission(Mission mission)
        {
            HashSet<FormationClass> allowedClasses = new HashSet<FormationClass>();
            if (mission == null || GameNetwork.NetworkPeers == null)
                return allowedClasses;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || missionPeer.Team == null || ReferenceEquals(missionPeer.Team, mission.SpectatorTeam))
                    continue;

                BattleSideEnum authoritativeSide = ResolveAuthoritativeSide(missionPeer, mission, "class-restriction-sync");
                if (authoritativeSide == BattleSideEnum.None)
                    continue;

                BasicCharacterObject controlledCharacter = missionPeer.ControlledAgent?.Character as BasicCharacterObject;
                if (controlledCharacter != null)
                {
                    allowedClasses.Add(controlledCharacter.DefaultFormationClass);
                    continue;
                }

                foreach (BasicCharacterObject allowedCharacter in ResolveAllowedCharactersForPeerCulture(missionPeer))
                {
                    if (allowedCharacter != null)
                        allowedClasses.Add(allowedCharacter.DefaultFormationClass);
                }
            }

            if (allowedClasses.Count == 0)
            {
                foreach (string allowedTroopId in GetAllowedControlTroopIdsSnapshot())
                {
                    BasicCharacterObject fallbackCharacter = ResolveAllowedCharacter(allowedTroopId);
                    if (fallbackCharacter != null)
                        allowedClasses.Add(fallbackCharacter.DefaultFormationClass);
                }
            }

            return allowedClasses;
        }

        private static bool ApplyCoopClassRestriction(
            MissionLobbyComponent lobbyComponent,
            FormationClass formationClass,
            bool desiredAvailability)
        {
            bool finalAvailability = desiredAvailability;
            bool broadcastValue = !desiredAvailability;

            try
            {
                lobbyComponent.ChangeClassRestriction(formationClass, broadcastValue);
                finalAvailability = lobbyComponent.IsClassAvailable(formationClass);
                if (finalAvailability != desiredAvailability)
                {
                    broadcastValue = desiredAvailability;
                    lobbyComponent.ChangeClassRestriction(formationClass, broadcastValue);
                    finalAvailability = lobbyComponent.IsClassAvailable(formationClass);
                }

                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new NetworkMessages.FromServer.ChangeClassRestrictions(formationClass, broadcastValue));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSpawnLogic: class restriction sync failed. " +
                    "FormationClass=" + formationClass +
                    " DesiredAvailability=" + desiredAvailability +
                    " Error=" + ex.Message);
            }

            return finalAvailability;
        }

        private static string GetPreferredTargetTroopIdForPeerCulture(MissionPeer missionPeer)
        {
            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            RosterEntryState preferredEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            string authoritativeBaseTroopId = !string.IsNullOrWhiteSpace(selectionState.TroopId)
                ? selectionState.TroopId
                : preferredEntry?.CharacterId;
            if (string.IsNullOrWhiteSpace(authoritativeBaseTroopId))
                return ResolveAllowedTargetTroopIdsForPeerCulture(missionPeer).FirstOrDefault();

            string cultureSpecificTargetTroopId = ResolveCultureSpecificTargetTroopId(authoritativeBaseTroopId, missionPeer?.Culture);
            return string.IsNullOrWhiteSpace(cultureSpecificTargetTroopId)
                ? authoritativeBaseTroopId
                : cultureSpecificTargetTroopId;
        }

        private static List<RosterEntryState> ResolveOrderedAllowedEntriesForPeer(MissionPeer missionPeer)
        {
            List<RosterEntryState> orderedEntries = new List<RosterEntryState>();
            if (missionPeer == null)
                return orderedEntries;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            IReadOnlyList<RosterEntryState> allowedEntries = GetAllowedControlEntryStatesSnapshot(selectionState.Side);
            if (allowedEntries.Count == 0)
                return orderedEntries;

            RosterEntryState preferredEntry = ResolvePreferredAllowedEntryStateForPeer(missionPeer, selectionState);
            if (preferredEntry != null)
                orderedEntries.Add(preferredEntry);

            foreach (RosterEntryState entry in allowedEntries)
            {
                if (entry == null)
                    continue;

                if (orderedEntries.Any(candidate => string.Equals(candidate.EntryId, entry.EntryId, StringComparison.Ordinal)))
                    continue;

                orderedEntries.Add(entry);
            }

            return orderedEntries;
        }

        private static List<string> ResolveAllowedTargetTroopIdsForPeerCulture(MissionPeer missionPeer)
        {
            List<string> targetTroopIds = new List<string>();
            List<RosterEntryState> orderedEntries = ResolveOrderedAllowedEntriesForPeer(missionPeer);
            IReadOnlyList<string> allowedBaseTroopIds = orderedEntries.Count > 0
                ? orderedEntries
                    .Select(entry => entry?.CharacterId)
                    .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : GetAllowedControlTroopIdsSnapshot(CoopBattleAuthorityState.GetSelectionState(missionPeer).Side);
            if (allowedBaseTroopIds.Count == 0)
                return targetTroopIds;

            string cultureToken = ExtractCultureToken(missionPeer?.Culture);
            HashSet<string> seenTroopIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (string baseTargetTroopId in allowedBaseTroopIds)
            {
                string preferredTroopId = baseTargetTroopId;
                if (!string.IsNullOrWhiteSpace(cultureToken))
                {
                    string cultureSpecificCoopTroopId = TryBuildCultureSpecificCoopTroopId(baseTargetTroopId, cultureToken);
                    if (!string.IsNullOrWhiteSpace(cultureSpecificCoopTroopId))
                    {
                        BasicCharacterObject cultureSpecificCharacter = ResolveAllowedCharacter(cultureSpecificCoopTroopId);
                        if (cultureSpecificCharacter != null)
                            preferredTroopId = cultureSpecificCoopTroopId;
                    }
                }

                if (string.IsNullOrWhiteSpace(preferredTroopId) || !seenTroopIds.Add(preferredTroopId))
                    continue;

                targetTroopIds.Add(preferredTroopId);
            }

            return targetTroopIds;
        }

        private static string ResolveCultureSpecificTargetTroopId(string baseTargetTroopId, BasicCultureObject culture)
        {
            if (string.IsNullOrWhiteSpace(baseTargetTroopId))
                return null;

            string cultureToken = ExtractCultureToken(culture);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return baseTargetTroopId;

            string cultureSpecificCoopTroopId = TryBuildCultureSpecificCoopTroopId(baseTargetTroopId, cultureToken);
            if (string.IsNullOrWhiteSpace(cultureSpecificCoopTroopId))
                return baseTargetTroopId;

            return ResolveAllowedCharacter(cultureSpecificCoopTroopId) != null
                ? cultureSpecificCoopTroopId
                : baseTargetTroopId;
        }

        private static IEnumerable<BasicCharacterObject> ResolveAllowedCharactersForPeerCulture(MissionPeer missionPeer)
        {
            List<RosterEntryState> orderedEntries = ResolveOrderedAllowedEntriesForPeer(missionPeer);
            if (orderedEntries.Count > 0)
            {
                HashSet<string> yieldedTroopIds = new HashSet<string>(StringComparer.Ordinal);
                string cultureToken = ExtractCultureToken(missionPeer?.Culture);
                foreach (RosterEntryState entry in orderedEntries)
                {
                    string baseTroopId = entry?.CharacterId;
                    if (string.IsNullOrWhiteSpace(baseTroopId))
                        continue;

                    string resolvedTroopId = baseTroopId;
                    if (!string.IsNullOrWhiteSpace(cultureToken))
                    {
                        string cultureSpecificTroopId = TryBuildCultureSpecificCoopTroopId(baseTroopId, cultureToken);
                        if (!string.IsNullOrWhiteSpace(cultureSpecificTroopId) && ResolveAllowedCharacter(cultureSpecificTroopId) != null)
                            resolvedTroopId = cultureSpecificTroopId;
                    }

                    if (!yieldedTroopIds.Add(resolvedTroopId))
                        continue;

                    BasicCharacterObject allowedCharacter = ResolveAllowedCharacter(resolvedTroopId);
                    if (allowedCharacter != null)
                        yield return allowedCharacter;
                }

                yield break;
            }

            foreach (string targetTroopId in ResolveAllowedTargetTroopIdsForPeerCulture(missionPeer))
            {
                BasicCharacterObject allowedCharacter = ResolveAllowedCharacter(targetTroopId);
                if (allowedCharacter != null)
                    yield return allowedCharacter;
            }
        }

        private static string TryBuildCultureSpecificCoopTroopId(string targetTroopId, string cultureToken)
        {
            if (string.IsNullOrWhiteSpace(targetTroopId) ||
                string.IsNullOrWhiteSpace(cultureToken) ||
                !targetTroopId.StartsWith("mp_coop_", StringComparison.Ordinal))
            {
                return null;
            }

            string normalizedTarget = targetTroopId.Trim().ToLowerInvariant();
            string role = GetTroopRole(normalizedTarget);
            if (string.IsNullOrWhiteSpace(role))
                return null;

            string weight = GetTroopWeight(normalizedTarget);
            string prefix = string.IsNullOrWhiteSpace(weight)
                ? "mp_coop_" + role + "_"
                : "mp_coop_" + weight + "_" + role + "_";

            return prefix + cultureToken + "_troop";
        }

        private static string ExtractCultureToken(BasicCultureObject culture)
        {
            string cultureId = culture?.StringId;
            if (string.IsNullOrWhiteSpace(cultureId))
                return null;

            int dotIndex = cultureId.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < cultureId.Length - 1)
                return cultureId.Substring(dotIndex + 1).Trim().ToLowerInvariant();

            return cultureId.Trim().ToLowerInvariant();
        }

        private static bool IsSpawnTimerReady(MissionPeer missionPeer)
        {
            if (missionPeer?.SpawnTimer == null || Mission.Current == null)
                return false;

            try
            {
                float elapsed = Mission.Current.CurrentTime - missionPeer.SpawnTimer.StartTime;
                return elapsed >= missionPeer.SpawnTimer.Duration;
            }
            catch
            {
                return false;
            }
        }

        private static int FindBestPeerCultureHeroClassIndex(List<MultiplayerClassDivisions.MPHeroClass> cultureClasses, string targetTroopId)
        {
            if (cultureClasses == null || cultureClasses.Count == 0 || string.IsNullOrWhiteSpace(targetTroopId))
                return -1;

            string normalizedTarget = targetTroopId.Trim().ToLowerInvariant();
            string targetRole = GetTroopRole(normalizedTarget);
            string targetWeight = GetTroopWeight(normalizedTarget);
            bool targetMounted = normalizedTarget.Contains("_cavalry_");
            bool targetRanged = normalizedTarget.Contains("_ranged_") || normalizedTarget.Contains("_archer_");

            int bestIndex = -1;
            int bestScore = int.MinValue;

            for (int i = 0; i < cultureClasses.Count; i++)
            {
                string candidateId = cultureClasses[i]?.HeroCharacter?.StringId;
                if (string.IsNullOrWhiteSpace(candidateId))
                    continue;

                string normalizedCandidate = candidateId.Trim().ToLowerInvariant();
                int score = 0;

                if (string.Equals(normalizedCandidate, normalizedTarget, StringComparison.Ordinal))
                    score += 1000;

                string candidateRole = GetTroopRole(normalizedCandidate);
                if (!string.IsNullOrEmpty(targetRole) && string.Equals(candidateRole, targetRole, StringComparison.Ordinal))
                    score += 200;

                string candidateWeight = GetTroopWeight(normalizedCandidate);
                if (!string.IsNullOrEmpty(targetWeight) && string.Equals(candidateWeight, targetWeight, StringComparison.Ordinal))
                    score += 50;

                bool candidateMounted = normalizedCandidate.Contains("_cavalry_");
                if (candidateMounted == targetMounted)
                    score += 25;

                bool candidateRanged = normalizedCandidate.Contains("_ranged_") || normalizedCandidate.Contains("_archer_");
                if (candidateRanged == targetRanged)
                    score += 15;

                if (normalizedCandidate.Contains("_troop"))
                    score += 5;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool MatchesPreferredHeroClass(MultiplayerClassDivisions.MPHeroClass heroClass, string targetTroopId)
        {
            string heroId = heroClass?.HeroCharacter?.StringId;
            if (string.IsNullOrWhiteSpace(heroId) || string.IsNullOrWhiteSpace(targetTroopId))
                return false;

            if (string.Equals(heroId, targetTroopId, StringComparison.Ordinal))
                return true;

            string normalizedTarget = targetTroopId.Trim();
            string canonicalTarget = NormalizeTroopIdForHeroClassMatch(normalizedTarget);
            string canonicalHeroId = NormalizeTroopIdForHeroClassMatch(heroId);
            if (!string.IsNullOrWhiteSpace(canonicalTarget) &&
                !string.IsNullOrWhiteSpace(canonicalHeroId) &&
                string.Equals(canonicalHeroId, canonicalTarget, StringComparison.Ordinal))
            {
                return true;
            }

            if (normalizedTarget.EndsWith("_troop", StringComparison.Ordinal))
            {
                string expectedHeroId = normalizedTarget.Substring(0, normalizedTarget.Length - "_troop".Length) + "_hero";
                if (string.Equals(heroId, expectedHeroId, StringComparison.Ordinal))
                    return true;

                string canonicalExpectedHeroId = NormalizeTroopIdForHeroClassMatch(expectedHeroId);
                if (!string.IsNullOrWhiteSpace(canonicalExpectedHeroId) &&
                    !string.IsNullOrWhiteSpace(canonicalHeroId) &&
                    string.Equals(canonicalHeroId, canonicalExpectedHeroId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (normalizedTarget.EndsWith("_hero", StringComparison.Ordinal))
            {
                string expectedTroopId = normalizedTarget.Substring(0, normalizedTarget.Length - "_hero".Length) + "_troop";
                if (string.Equals(heroId, expectedTroopId, StringComparison.Ordinal))
                    return true;

                string canonicalExpectedTroopId = NormalizeTroopIdForHeroClassMatch(expectedTroopId);
                if (!string.IsNullOrWhiteSpace(canonicalExpectedTroopId) &&
                    !string.IsNullOrWhiteSpace(canonicalHeroId) &&
                    string.Equals(canonicalHeroId, canonicalExpectedTroopId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeTroopIdForHeroClassMatch(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return null;

            string normalized = troopId.Trim();
            if (normalized.StartsWith("mp_coop_", StringComparison.Ordinal))
                normalized = "mp_" + normalized.Substring("mp_coop_".Length);

            return normalized;
        }

        private static string GetTroopRole(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            if (troopId.Contains("_cavalry_"))
                return "cavalry";
            if (troopId.Contains("_ranged_") || troopId.Contains("_archer_"))
                return "ranged";
            if (troopId.Contains("_infantry_"))
                return "infantry";
            return string.Empty;
        }

        private static string GetTroopWeight(string troopId)
        {
            if (string.IsNullOrWhiteSpace(troopId))
                return string.Empty;

            if (troopId.Contains("_heavy_"))
                return "heavy";
            if (troopId.Contains("_light_"))
                return "light";
            if (troopId.Contains("_medium_"))
                return "medium";
            return string.Empty;
        }

        private static void TrySpawnSelectedAllowedCharacterForDiagnostics(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            if (_hasSpawnedDiagnosticAllowedAgent && ReferenceEquals(_lastDiagnosticSpawnMission, mission))
                return;

            if (SelectedAllowedCharacter == null)
                return;

            Team spawnTeam = mission.AttackerTeam ?? mission.DefenderTeam;
            if (spawnTeam == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: diagnostic spawn skipped (" + source + ") because no vanilla TDM team is available yet.");
                return;
            }

            Agent anchorAgent = null;
            try
            {
                if (spawnTeam.ActiveAgents != null && spawnTeam.ActiveAgents.Count > 0)
                    anchorAgent = spawnTeam.ActiveAgents[0];
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: diagnostic spawn could not inspect team agents (" + source + "): " + ex.Message);
            }

            if (anchorAgent == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: diagnostic spawn skipped (" + source + ") because team " + spawnTeam.TeamIndex + " has no active anchor agent yet.");
                return;
            }

            Agent spawnedAgent = SpawnDiagnosticAiAgent(mission, spawnTeam, SelectedAllowedCharacter, anchorAgent);
            if (spawnedAgent == null)
            {
                ModLogger.Info("CoopMissionSpawnLogic: diagnostic spawn failed (" + source + ") for troop id '" + SelectedAllowedCharacter.StringId + "'.");
                return;
            }

            _lastDiagnosticSpawnMission = mission;
            _hasSpawnedDiagnosticAllowedAgent = true;
            _diagnosticAllowedAgent = spawnedAgent;

            string spawnedName = spawnedAgent.Name?.ToString() ?? spawnedAgent.Index.ToString();
            ModLogger.Info(
                "CoopMissionSpawnLogic: diagnostic allowed agent spawned (" + source + "). " +
                "TroopId=" + SelectedAllowedCharacter.StringId +
                " TeamIndex=" + (spawnedAgent.Team?.TeamIndex ?? -1) +
                " Side=" + (spawnedAgent.Team?.Side ?? BattleSideEnum.None) +
                " Agent=" + spawnedName +
                " Position=" + spawnedAgent.Position);
        }

        private static Agent SpawnDiagnosticAiAgent(Mission mission, Team team, BasicCharacterObject troop, Agent anchorAgent)
        {
            if (mission == null || team == null || troop == null || anchorAgent == null)
                return null;

            try
            {
                float lateralOffset = team.Side == BattleSideEnum.Attacker ? -1.5f : 1.5f;
                Vec3 spawnPosition = anchorAgent.Position + new Vec3(lateralOffset, 0f, 0f);
                Agent targetEnemy = mission.GetClosestEnemyAgent(team, spawnPosition, 200f);
                Vec3 lookAt = targetEnemy != null
                    ? targetEnemy.Position
                    : anchorAgent.Position + new Vec3(team.Side == BattleSideEnum.Attacker ? 6f : -6f, 0f, 0f);

                var origin = new BasicBattleAgentOrigin(troop);
                Vec3 toEnemy3 = lookAt - spawnPosition;
                Vec2 toEnemy2 = new Vec2(toEnemy3.x, toEnemy3.y);
                if (toEnemy2.LengthSquared < 0.001f)
                    toEnemy2 = new Vec2(1f, 0f);
                toEnemy2.Normalize();

                AgentBuildData buildData = new AgentBuildData(troop);
                buildData.Team(team);
                buildData.Controller(AgentControllerType.AI);
                buildData.TroopOrigin(origin);
                buildData.InitialPosition(in spawnPosition);
                buildData.InitialDirection(in toEnemy2);
                buildData.SpawnsIntoOwnFormation(false);
                buildData.SpawnsUsingOwnTroopClass(false);

                Agent agent = mission.SpawnAgent(buildData, spawnFromAgentVisuals: false);
                if (agent != null)
                {
                    agent.SetAutomaticTargetSelection(true);
                    agent.SetFiringOrder(FiringOrder.RangedWeaponUsageOrderEnum.FireAtWill);
                }

                return agent;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSpawnLogic: SpawnDiagnosticAiAgent failed: " + ex.Message);
                return null;
            }
        }

        private static void TryTransferDiagnosticAllowedAgentToPeer(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer || _diagnosticAllowedAgent == null)
                return;

            if (_hasTransferredDiagnosticAllowedAgentToPeer && ReferenceEquals(_lastDiagnosticOwnershipMission, mission))
                return;

            if (GameNetwork.NetworkPeers == null || GameNetwork.NetworkPeers.Count == 0)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                Agent currentlyControlledAgent = missionPeer.ControlledAgent;
                if (currentlyControlledAgent == null)
                    continue;

                if (ReferenceEquals(currentlyControlledAgent, _diagnosticAllowedAgent))
                {
                    _hasTransferredDiagnosticAllowedAgentToPeer = true;
                    _lastDiagnosticOwnershipMission = mission;
                    return;
                }

                try
                {
                    Team peerTeam = missionPeer.Team ?? currentlyControlledAgent.Team ?? _diagnosticAllowedAgent.Team;
                    if (peerTeam != null && !ReferenceEquals(_diagnosticAllowedAgent.Team, peerTeam))
                    {
                        _diagnosticAllowedAgent.SetTeam(peerTeam, false);
                        _diagnosticAllowedAgent.ForceUpdateCachedAndFormationValues(true, false);
                    }

                    currentlyControlledAgent.MissionPeer = null;
                    currentlyControlledAgent.Controller = AgentControllerType.AI;

                    _diagnosticAllowedAgent.Controller = AgentControllerType.Player;
                    _diagnosticAllowedAgent.MissionPeer = missionPeer;

                    missionPeer.ControlledAgent = _diagnosticAllowedAgent;
                    missionPeer.FollowedAgent = _diagnosticAllowedAgent;
                    peer.ControlledAgent = _diagnosticAllowedAgent;

                    mission.TakeControlOfAgent(_diagnosticAllowedAgent);
                    _diagnosticAllowedAgent.UpdateSpawnEquipmentAndRefreshVisuals(_diagnosticAllowedAgent.SpawnEquipment);
                    _diagnosticAllowedAgent.WieldInitialWeapons(
                        Agent.WeaponWieldActionType.Instant,
                        Equipment.InitialWeaponEquipPreference.Any);

                    GameNetwork.BeginModuleEventAsServer(peer.VirtualPlayer);
                    GameNetwork.WriteMessage(new NetworkMessages.FromServer.SetAgentOwningMissionPeer(_diagnosticAllowedAgent.Index, peer.VirtualPlayer));
                    GameNetwork.EndModuleEventAsServer();

                    _hasTransferredDiagnosticAllowedAgentToPeer = true;
                    _lastDiagnosticOwnershipMission = mission;

                    string previousName = currentlyControlledAgent.Name?.ToString() ?? currentlyControlledAgent.Index.ToString();
                    string newName = _diagnosticAllowedAgent.Name?.ToString() ?? _diagnosticAllowedAgent.Index.ToString();
                    ModLogger.Info(
                        "CoopMissionSpawnLogic: transferred control to diagnostic allowed agent (" + source + "). " +
                        "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                        " PreviousAgent=" + previousName +
                        " NewAgent=" + newName +
                        " TeamIndex=" + (_diagnosticAllowedAgent.Team?.TeamIndex ?? -1) +
                        " Side=" + (_diagnosticAllowedAgent.Team?.Side ?? BattleSideEnum.None) +
                        " Mount=" + _diagnosticAllowedAgent.HasMount);
                    return;
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSpawnLogic: diagnostic control transfer failed (" + source + "): " + ex.Message);
                    return;
                }
            }
        }

    }
}
