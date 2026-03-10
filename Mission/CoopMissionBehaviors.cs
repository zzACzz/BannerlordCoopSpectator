// Файл: Mission/CoopMissionBehaviors.cs
// Призначення: логіка місії для кооп-спектатора — клієнтський зворотний зв'язок (етап 3.5), логування стану для Етапу 3.3 (spectator/unit/spawn), заглушка серверного спавну (етап 3.4).

using System; // Exception
using System.Collections.Generic; // List<string>
using System.Collections; // IEnumerable
using System.Linq; // Distinct, FirstOrDefault
using System.Reflection; // MethodInfo
using TaleWorlds.Core; // BasicCharacterObject, MBObjectManager (для спавну)
using TaleWorlds.Library; // Vec2, Vec3
using TaleWorlds.MountAndBlade; // Mission, MissionLogic, GameNetwork, Agent, Team, MissionMode
using TaleWorlds.ObjectSystem; // MBObjectManager
using CoopSpectator.Campaign; // BattleRosterFileHelper (варіант A: roster з кампанії)
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback

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
        private bool _visualSpawnAutoConfirmHooked;
        private bool _visualSpawnAutoConfirmTriggered;
        private object _visualSpawnComponent;
        private Delegate _onMyAgentVisualSpawnedDelegate;
        private bool _hasLoggedAutoConfirmBehaviorDiagnostics;
        private bool _hasLoggedMissionBehaviorCatalog;
        private bool _hasLoggedClassLoadoutDiagnostics;
        private bool _hasLoggedActiveClassLoadoutDiagnostics;
        private bool _hasLoggedClassLoadoutPendingInitialization;
        private string _lastAppliedClassLoadoutFilterKey;

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
            TryHookVisualSpawnAutoConfirm(mission);
            LogClassLoadoutDiagnosticsOnce(mission);
            LogActiveClassLoadoutDiagnosticsOnce(mission);
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
                    _visualSpawnAutoConfirmTriggered = false;
                }
                else if (_lastControlledAgent != null)
                {
                    ModLogger.Info("CoopMissionClientLogic: returned to spectator (agent lost or died).");
                    _lastAppliedClassLoadoutFilterKey = null;
                }
                _lastControlledAgent = currentMain;
            }

            TryHookVisualSpawnAutoConfirm(mission);
            LogClassLoadoutDiagnosticsOnce(mission);
            LogActiveClassLoadoutDiagnosticsOnce(mission);
            TryFilterClassLoadoutToCoopUnits(mission);
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
                   memberName.IndexOf("troop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   memberName.IndexOf("data", StringComparison.OrdinalIgnoreCase) >= 0 ||
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

                string filterKey = BuildClassLoadoutFilterKey(mission, dataSource);
                if (string.Equals(_lastAppliedClassLoadoutFilterKey, filterKey, StringComparison.Ordinal))
                    return;

                object classesObject = GetMemberValue(dataSource, "_classes") ?? GetMemberValue(dataSource, "Classes");
                if (!(classesObject is IList groups) || groups.Count == 0)
                    return;

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
                        if (IsCoopHeroClassVm(heroClassVm))
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
                if (!IsCoopHeroClassVm(currentSelectedClass))
                {
                    object firstCoopClass = FindFirstCoopHeroClass(groups);
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
                    "RemainingGroups=" + groups.Count +
                    " RemainingCoopSubClasses=" + keptSubclassCount);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: class loadout coop filter failed: " + ex.Message);
            }
        }

        private static object FindFirstCoopHeroClass(IList groups)
        {
            foreach (object groupVm in groups)
            {
                object subClassesObject = GetMemberValue(groupVm, "_subClasses") ?? GetMemberValue(groupVm, "SubClasses");
                if (!(subClassesObject is IList subClasses))
                    continue;

                foreach (object heroClassVm in subClasses)
                {
                    if (IsCoopHeroClassVm(heroClassVm))
                        return heroClassVm;
                }
            }

            return null;
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
            int teamIndex = missionPeer?.Team?.TeamIndex ?? -1;
            int selectedTroopIndex = missionPeer?.SelectedTroopIndex ?? -1;
            string dataSourceIdentity = dataSource.GetType().FullName + "#" + dataSource.GetHashCode();
            string culture = missionPeer?.Culture?.StringId ?? "none";
            string missionMode = mission?.Mode.ToString() ?? "unknown";
            return missionMode + "|" + teamIndex + "|" + culture + "|" + selectedTroopIndex + "|" + dataSourceIdentity;
        }

    }

    /// <summary>
    /// Серверна логіка: заглушка для майбутнього спавну гравців (етап 3.4), логування peer/spawn для Етапу 3.3. Варіант A: читає battle_roster.json.
    /// </summary>
    public sealed class CoopMissionSpawnLogic : MissionLogic
    {
        private const bool EnableDirectCoopPlayerSpawnExperiment = false;
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
        private static readonly Dictionary<FormationClass, bool> _appliedCoopClassAvailabilityStates = new Dictionary<FormationClass, bool>();
        private static Agent _diagnosticAllowedAgent;
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
        /// <summary>Перший дозволений troop ID із нормалізованого roster. Поки що лише для контрольованої серверної фіксації.</summary>
        public static string SelectedAllowedTroopId { get; private set; }
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

            TrySpawnPeersIntoCoopControl(Mission, "server behavior");
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
            }

            TryForcePreferredHeroClassForPeer(mission, "dedicated observer");
            TrySpawnPeersIntoCoopControl(mission, "dedicated observer");
        }

        private static void RefreshAllowedTroopsFromRoster(string source)
        {
            List<string> roster = BattleRosterFileHelper.ReadRoster();
            CampaignRosterTroopIds = NormalizeRosterTroopIds(roster);
            ControlTroopIds = CampaignRosterTroopIds.Take(2).ToList();

            SelectedAllowedTroopId = null;
            SelectedAllowedCharacter = null;

            foreach (string troopId in ControlTroopIds)
            {
                BasicCharacterObject resolvedCharacter = ResolveAllowedCharacter(troopId);
                if (resolvedCharacter == null)
                    continue;

                SelectedAllowedTroopId = troopId;
                SelectedAllowedCharacter = resolvedCharacter;
                break;
            }

            if (SelectedAllowedCharacter == null && CampaignRosterTroopIds.Count > 0)
            {
                SelectedAllowedTroopId = CampaignRosterTroopIds[0];
                SelectedAllowedCharacter = ResolveAllowedCharacter(SelectedAllowedTroopId);
            }

            string controlUnits = ControlTroopIds.Count > 0
                ? string.Join(", ", ControlTroopIds)
                : "(none)";
            ModLogger.Info("CoopMissionSpawnLogic: control troop candidates (" + source + ") = [" + controlUnits + "].");
        }

        private static void TrySpawnPeersIntoCoopControl(Mission mission, string source)
        {
            if (!EnableDirectCoopPlayerSpawnExperiment || mission == null || !GameNetwork.IsServer || SelectedAllowedCharacter == null)
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

                Agent spawnedAgent = SpawnCoopControlledAgent(mission, missionPeer, peer, SelectedAllowedCharacter, source);
                if (spawnedAgent == null)
                    continue;

                _spawnedCoopPeerIndices.Add(peer.Index);
                missionPeer.HasSpawnedAgentVisuals = true;
                missionPeer.EquipmentUpdatingExpired = false;

                string peerName = peer.UserName ?? peer.Index.ToString();
                string agentName = spawnedAgent.Name?.ToString() ?? spawnedAgent.Index.ToString();
                ModLogger.Info(
                    "CoopMissionSpawnLogic: coop direct spawn succeeded (" + source + "). " +
                    "Peer=" + peerName +
                    " TroopId=" + SelectedAllowedCharacter.StringId +
                    " Agent=" + agentName +
                    " TeamIndex=" + (spawnedAgent.Team?.TeamIndex ?? -1) +
                    " Side=" + (spawnedAgent.Team?.Side ?? BattleSideEnum.None) +
                    " Position=" + spawnedAgent.Position);
            }
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

            int exactIndex = cultureClasses.FindIndex(heroClass =>
                MatchesPreferredHeroClass(heroClass, targetTroopId));
            if (exactIndex >= 0)
            {
                preferredClass = cultureClasses[exactIndex];
                preferredTroopIndex = exactIndex;
                debugReason = "exact troop id match for '" + targetTroopId + "'";
                return !ReferenceEquals(preferredClass, vanillaClass) || missionPeer.SelectedTroopIndex != preferredTroopIndex;
            }

            int bestIndex = FindBestPeerCultureHeroClassIndex(cultureClasses, targetTroopId);
            if (bestIndex < 0 || bestIndex >= cultureClasses.Count)
            {
                debugReason = "no suitable class in peer culture";
                return false;
            }

            preferredClass = cultureClasses[bestIndex];
            preferredTroopIndex = bestIndex;
            debugReason = "peer-culture surrogate for '" + targetTroopId + "'";
            return !ReferenceEquals(preferredClass, vanillaClass) || missionPeer.SelectedTroopIndex != preferredTroopIndex;
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

                int currentTroopIndex = missionPeer.SelectedTroopIndex;
                MultiplayerClassDivisions.MPHeroClass currentClass = null;
                List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                    .GetMPHeroClasses(missionPeer.Culture)
                    ?.Where(heroClass => heroClass?.HeroCharacter != null)
                    .ToList();
                if (cultureClasses != null && currentTroopIndex >= 0 && currentTroopIndex < cultureClasses.Count)
                    currentClass = cultureClasses[currentTroopIndex];

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

                missionPeer.SelectedTroopIndex = preferredTroopIndex;

                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(new NetworkMessages.FromServer.UpdateSelectedTroopIndex(peer, preferredTroopIndex));
                GameNetwork.EndBroadcastModuleEvent(GameNetwork.EventBroadcastFlags.None);

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

        private static void TrySyncCoopClassRestrictions(Mission mission, string source)
        {
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

                BasicCharacterObject controlledCharacter = missionPeer.ControlledAgent?.Character as BasicCharacterObject;
                if (controlledCharacter != null)
                {
                    allowedClasses.Add(controlledCharacter.DefaultFormationClass);
                    continue;
                }

                int currentTroopIndex = missionPeer.SelectedTroopIndex;
                MultiplayerClassDivisions.MPHeroClass currentClass = null;
                List<MultiplayerClassDivisions.MPHeroClass> cultureClasses = MultiplayerClassDivisions
                    .GetMPHeroClasses(missionPeer.Culture)
                    ?.Where(heroClass => heroClass?.HeroCharacter != null)
                    .ToList();
                if (cultureClasses != null && currentTroopIndex >= 0 && currentTroopIndex < cultureClasses.Count)
                    currentClass = cultureClasses[currentTroopIndex];

                if (!TryResolvePreferredHeroClassForPeer(
                        missionPeer,
                        currentClass,
                        false,
                        out MultiplayerClassDivisions.MPHeroClass preferredClass,
                        out _,
                        out _))
                {
                    continue;
                }

                BasicCharacterObject preferredCharacter = preferredClass?.HeroCharacter;
                if (preferredCharacter != null)
                    allowedClasses.Add(preferredCharacter.DefaultFormationClass);
            }

            if (allowedClasses.Count == 0 && SelectedAllowedCharacter != null)
                allowedClasses.Add(SelectedAllowedCharacter.DefaultFormationClass);

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
            string baseTargetTroopId = SelectedAllowedCharacter?.StringId ?? SelectedAllowedTroopId;
            if (string.IsNullOrWhiteSpace(baseTargetTroopId))
                return null;

            string cultureToken = ExtractCultureToken(missionPeer?.Culture);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return baseTargetTroopId;

            string cultureSpecificCoopTroopId = TryBuildCultureSpecificCoopTroopId(baseTargetTroopId, cultureToken);
            if (!string.IsNullOrWhiteSpace(cultureSpecificCoopTroopId))
            {
                BasicCharacterObject cultureSpecificCharacter = ResolveAllowedCharacter(cultureSpecificCoopTroopId);
                if (cultureSpecificCharacter != null)
                    return cultureSpecificCoopTroopId;
            }

            return baseTargetTroopId;
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
            if (normalizedTarget.EndsWith("_troop", StringComparison.Ordinal))
            {
                string expectedHeroId = normalizedTarget.Substring(0, normalizedTarget.Length - "_troop".Length) + "_hero";
                if (string.Equals(heroId, expectedHeroId, StringComparison.Ordinal))
                    return true;
            }

            if (normalizedTarget.EndsWith("_hero", StringComparison.Ordinal))
            {
                string expectedTroopId = normalizedTarget.Substring(0, normalizedTarget.Length - "_hero".Length) + "_troop";
                if (string.Equals(heroId, expectedTroopId, StringComparison.Ordinal))
                    return true;
            }

            return false;
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
