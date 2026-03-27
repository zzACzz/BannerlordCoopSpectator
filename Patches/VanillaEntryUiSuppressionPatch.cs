using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.UI;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Blocks the vanilla TDM team/class overlay from opening once the coop entry
    /// path is authoritative, while keeping the underlying mission behaviors alive.
    /// </summary>
    public static class VanillaEntryUiSuppressionPatch
    {
        private const bool EnableSuppression = true;
        private static string _lastBlockedTeamSelectionKey;
        private static string _lastBlockedClassLoadoutKey;

        public static void Apply(Harmony harmony)
        {
            try
            {
                PatchMissionGauntletTeamSelection(harmony);
                PatchMissionGauntletClassLoadout(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error("VanillaEntryUiSuppressionPatch.Apply failed.", ex);
            }
        }

        private static void PatchMissionGauntletTeamSelection(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission.MissionGauntletTeamSelection");
            if (type == null)
            {
                ModLogger.Info("VanillaEntryUiSuppressionPatch: MissionGauntletTeamSelection type not found. Skip.");
                return;
            }

            MethodInfo target = type.GetMethod("OnOpen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(VanillaEntryUiSuppressionPatch).GetMethod(
                nameof(MissionGauntletTeamSelection_OnOpen_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("VanillaEntryUiSuppressionPatch: MissionGauntletTeamSelection.OnOpen not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("VanillaEntryUiSuppressionPatch: prefix applied to MissionGauntletTeamSelection.OnOpen.");
        }

        private static void PatchMissionGauntletClassLoadout(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission.MissionGauntletClassLoadout");
            if (type == null)
            {
                ModLogger.Info("VanillaEntryUiSuppressionPatch: MissionGauntletClassLoadout type not found. Skip.");
                return;
            }

            MethodInfo target = type.GetMethod(
                "OnTryToggle",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            MethodInfo prefix = typeof(VanillaEntryUiSuppressionPatch).GetMethod(
                nameof(MissionGauntletClassLoadout_OnTryToggle_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("VanillaEntryUiSuppressionPatch: MissionGauntletClassLoadout.OnTryToggle(bool) not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("VanillaEntryUiSuppressionPatch: prefix applied to MissionGauntletClassLoadout.OnTryToggle(bool).");
        }

        private static bool MissionGauntletTeamSelection_OnOpen_Prefix(object __instance)
        {
            if (!ShouldSuppress(__instance, requireAuthoritativeTroopPath: false, out CoopBattleEntryPolicy.ClientSnapshot entryPolicy))
                return true;

            string blockKey = BuildPolicyLogKey(entryPolicy, "team");
            if (!string.Equals(_lastBlockedTeamSelectionKey, blockKey, StringComparison.Ordinal))
            {
                _lastBlockedTeamSelectionKey = blockKey;
                ModLogger.Info(
                    "VanillaEntryUiSuppressionPatch: blocked MissionGauntletTeamSelection.OnOpen. " +
                    entryPolicy.Describe());
            }

            return false;
        }

        private static bool MissionGauntletClassLoadout_OnTryToggle_Prefix(object __instance, bool isActive)
        {
            if (!isActive)
                return true;

            if (!ShouldSuppress(__instance, requireAuthoritativeTroopPath: true, out CoopBattleEntryPolicy.ClientSnapshot entryPolicy))
                return true;

            string blockKey = BuildPolicyLogKey(entryPolicy, "class");
            if (!string.Equals(_lastBlockedClassLoadoutKey, blockKey, StringComparison.Ordinal))
            {
                _lastBlockedClassLoadoutKey = blockKey;
                ModLogger.Info(
                    "VanillaEntryUiSuppressionPatch: blocked MissionGauntletClassLoadout.OnTryToggle(true). " +
                    entryPolicy.Describe());
            }

            return false;
        }

        private static bool ShouldSuppress(
            object instance,
            bool requireAuthoritativeTroopPath,
            out CoopBattleEntryPolicy.ClientSnapshot entryPolicy)
        {
            entryPolicy = null;
            if (!EnableSuppression)
                return false;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (mission == null)
                return false;

            if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay &&
                (mission.GetMissionBehavior<CoopMissionClientLogic>() != null ||
                 mission.GetMissionBehavior<CoopMissionSelectionView>() != null))
            {
                entryPolicy = CoopBattleEntryPolicy.BuildClientSnapshot(
                    mission,
                    CoopBattleSelectionBridgeFile.ReadCurrentSelection());
                return true;
            }

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            entryPolicy = CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null)
                return false;

            return requireAuthoritativeTroopPath
                ? entryPolicy.UseAuthoritativeTroopPath
                : entryPolicy.UseAuthoritativeSidePath;
        }

        private static string BuildPolicyLogKey(CoopBattleEntryPolicy.ClientSnapshot entryPolicy, string channel)
        {
            if (entryPolicy == null)
                return channel + "|null";

            return
                channel + "|" +
                (entryPolicy.HasBridgeSide ? entryPolicy.BridgeSide.ToString() : "none") + "|" +
                (entryPolicy.BridgeTroopOrEntryId ?? "none") + "|" +
                entryPolicy.PlayerHasActiveAgent;
        }
    }
}
