using System;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Suppresses the native Battle/TDM round countdown and warmup banner in the
    /// multiplayer HUD when a coop battle-map runtime is active. This keeps the
    /// rest of the native HUD alive while removing misleading round/timer UI.
    /// </summary>
    public static class BattleMapHudSuppressionPatch
    {
        private const string HudVmTypeName = "TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.HUDExtensions.MissionMultiplayerHUDExtensionVM";
        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static string _lastSuppressionLogKey = string.Empty;

        private static Type _hudVmType;
        private static FieldInfo _missionField;
        private static PropertyInfo _isRoundCountdownAvailableProperty;
        private static PropertyInfo _isRoundCountdownSuspendedProperty;
        private static PropertyInfo _remainingRoundTimeProperty;
        private static PropertyInfo _warnRemainingTimeProperty;
        private static PropertyInfo _isInWarmupProperty;
        private static PropertyInfo _warmupInfoTextProperty;
        private static PropertyInfo _isGeneralWarningCountdownActiveProperty;
        private static PropertyInfo _generalWarningCountdownProperty;

        public static void Apply(Harmony harmony)
        {
            try
            {
                _hudVmType = AccessTools.TypeByName(HudVmTypeName);
                if (_hudVmType == null)
                {
                    ModLogger.Info("BattleMapHudSuppressionPatch: MissionMultiplayerHUDExtensionVM type not found. Skip.");
                    return;
                }

                CacheMembers();

                MethodBase constructor = AccessTools.Constructor(_hudVmType, new[] { typeof(Mission) });
                MethodInfo refreshValues = AccessTools.Method(_hudVmType, "RefreshValues");
                MethodInfo tick = AccessTools.Method(_hudVmType, "Tick", new[] { typeof(float) });

                MethodInfo constructorPostfix = typeof(BattleMapHudSuppressionPatch).GetMethod(
                    nameof(MissionMultiplayerHudExtensionVM_Constructor_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo refreshValuesPostfix = typeof(BattleMapHudSuppressionPatch).GetMethod(
                    nameof(MissionMultiplayerHudExtensionVM_RefreshValues_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo tickPostfix = typeof(BattleMapHudSuppressionPatch).GetMethod(
                    nameof(MissionMultiplayerHudExtensionVM_Tick_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (constructor != null && constructorPostfix != null)
                {
                    harmony.Patch(constructor, postfix: new HarmonyMethod(constructorPostfix));
                    ModLogger.Info("BattleMapHudSuppressionPatch: postfix applied to MissionMultiplayerHUDExtensionVM ctor(Mission).");
                }
                else
                {
                    ModLogger.Info("BattleMapHudSuppressionPatch: ctor(Mission) not found on MissionMultiplayerHUDExtensionVM. Skip.");
                }

                if (refreshValues != null && refreshValuesPostfix != null)
                {
                    harmony.Patch(refreshValues, postfix: new HarmonyMethod(refreshValuesPostfix));
                    ModLogger.Info("BattleMapHudSuppressionPatch: postfix applied to MissionMultiplayerHUDExtensionVM.RefreshValues.");
                }
                else
                {
                    ModLogger.Info("BattleMapHudSuppressionPatch: RefreshValues not found on MissionMultiplayerHUDExtensionVM. Skip.");
                }

                if (tick != null && tickPostfix != null)
                {
                    harmony.Patch(tick, postfix: new HarmonyMethod(tickPostfix));
                    ModLogger.Info("BattleMapHudSuppressionPatch: postfix applied to MissionMultiplayerHUDExtensionVM.Tick(float).");
                }
                else
                {
                    ModLogger.Info("BattleMapHudSuppressionPatch: Tick(float) not found on MissionMultiplayerHUDExtensionVM. Skip.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleMapHudSuppressionPatch.Apply failed.", ex);
            }
        }

        private static void CacheMembers()
        {
            if (_hudVmType == null)
                return;

            _missionField = _hudVmType.GetField("_mission", InstanceFlags);
            _isRoundCountdownAvailableProperty = _hudVmType.GetProperty("IsRoundCountdownAvailable", InstanceFlags);
            _isRoundCountdownSuspendedProperty = _hudVmType.GetProperty("IsRoundCountdownSuspended", InstanceFlags);
            _remainingRoundTimeProperty = _hudVmType.GetProperty("RemainingRoundTime", InstanceFlags);
            _warnRemainingTimeProperty = _hudVmType.GetProperty("WarnRemainingTime", InstanceFlags);
            _isInWarmupProperty = _hudVmType.GetProperty("IsInWarmup", InstanceFlags);
            _warmupInfoTextProperty = _hudVmType.GetProperty("WarmupInfoText", InstanceFlags);
            _isGeneralWarningCountdownActiveProperty = _hudVmType.GetProperty("IsGeneralWarningCountdownActive", InstanceFlags);
            _generalWarningCountdownProperty = _hudVmType.GetProperty("GeneralWarningCountdown", InstanceFlags);
        }

        private static void MissionMultiplayerHudExtensionVM_Constructor_Postfix(object __instance)
        {
            TryApplySuppression(__instance, "ctor");
        }

        private static void MissionMultiplayerHudExtensionVM_RefreshValues_Postfix(object __instance)
        {
            TryApplySuppression(__instance, "refresh-values");
        }

        private static void MissionMultiplayerHudExtensionVM_Tick_Postfix(object __instance, float dt)
        {
            TryApplySuppression(__instance, "tick");
        }

        private static void TryApplySuppression(object instance, string source)
        {
            Mission mission = ResolveMission(instance);
            if (!MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission?.SceneName))
                return;

            SetProperty(_isRoundCountdownAvailableProperty, instance, false);
            SetProperty(_isRoundCountdownSuspendedProperty, instance, true);
            SetProperty(_remainingRoundTimeProperty, instance, string.Empty);
            SetProperty(_warnRemainingTimeProperty, instance, false);
            SetProperty(_isInWarmupProperty, instance, false);
            SetProperty(_warmupInfoTextProperty, instance, string.Empty);
            SetProperty(_isGeneralWarningCountdownActiveProperty, instance, false);
            SetProperty(_generalWarningCountdownProperty, instance, 0);

            string logKey = (mission?.SceneName ?? "unknown") + "|" + source;
            if (string.Equals(_lastSuppressionLogKey, logKey, StringComparison.Ordinal))
                return;

            _lastSuppressionLogKey = logKey;
            ModLogger.Info(
                "BattleMapHudSuppressionPatch: suppressed native round/warmup HUD for coop battle-map runtime. " +
                "Scene=" + (mission?.SceneName ?? "unknown") +
                " Source=" + source + ".");
        }

        private static Mission ResolveMission(object instance)
        {
            if (_missionField == null || instance == null)
                return Mission.Current;

            try
            {
                return _missionField.GetValue(instance) as Mission ?? Mission.Current;
            }
            catch
            {
                return Mission.Current;
            }
        }

        private static void SetProperty(PropertyInfo property, object instance, object value)
        {
            if (property == null || instance == null || !property.CanWrite)
                return;

            try
            {
                property.SetValue(instance, value, null);
            }
            catch
            {
            }
        }
    }
}
