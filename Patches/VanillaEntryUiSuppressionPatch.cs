using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.GameMode;
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

        public static void NotifyAuthoritativeSpawnRequested(string source)
        {
        }

        public static void Apply(Harmony harmony)
        {
            try
            {
                PatchMissionGauntletTeamSelection(harmony);
                PatchMissionGauntletClassLoadout(harmony);
                PatchMissionLobbyEquipmentNetworkComponent(harmony);
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

            MethodInfo target = type.GetMethod(
                "MissionLobbyComponentOnSelectingTeam",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(List<Team>) },
                null);
            MethodInfo prefix = typeof(VanillaEntryUiSuppressionPatch).GetMethod(
                nameof(MissionGauntletTeamSelection_OnSelectingTeam_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("VanillaEntryUiSuppressionPatch: MissionGauntletTeamSelection.MissionLobbyComponentOnSelectingTeam(List<Team>) not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("VanillaEntryUiSuppressionPatch: prefix applied to MissionGauntletTeamSelection.MissionLobbyComponentOnSelectingTeam(List<Team>).");
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
                "OnTeamChanged",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(NetworkCommunicator), typeof(Team), typeof(Team) },
                null);
            MethodInfo prefix = typeof(VanillaEntryUiSuppressionPatch).GetMethod(
                nameof(MissionGauntletClassLoadout_OnTeamChanged_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("VanillaEntryUiSuppressionPatch: MissionGauntletClassLoadout.OnTeamChanged(NetworkCommunicator, Team, Team) not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("VanillaEntryUiSuppressionPatch: prefix applied to MissionGauntletClassLoadout.OnTeamChanged(NetworkCommunicator, Team, Team).");
        }

        private static void PatchMissionLobbyEquipmentNetworkComponent(Harmony harmony)
        {
            Type type = typeof(MissionLobbyEquipmentNetworkComponent);
            MethodInfo target = type.GetMethod(
                "ToggleLoadout",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(bool) },
                null);
            MethodInfo prefix = typeof(VanillaEntryUiSuppressionPatch).GetMethod(
                nameof(MissionLobbyEquipmentNetworkComponent_ToggleLoadout_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("VanillaEntryUiSuppressionPatch: MissionLobbyEquipmentNetworkComponent.ToggleLoadout(bool) not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("VanillaEntryUiSuppressionPatch: prefix applied to MissionLobbyEquipmentNetworkComponent.ToggleLoadout(bool).");
        }

        private static bool MissionGauntletTeamSelection_OnSelectingTeam_Prefix(object __instance)
        {
            if (!ShouldSuppress(__instance, requireAuthoritativeTroopPath: false, out CoopBattleEntryPolicy.ClientSnapshot entryPolicy))
                return true;

            LogBlockedTeamSelection(
                "MissionGauntletTeamSelection.MissionLobbyComponentOnSelectingTeam",
                entryPolicy);
            return false;
        }

        private static bool MissionGauntletClassLoadout_OnTeamChanged_Prefix(
            object __instance,
            NetworkCommunicator peer,
            Team previousTeam,
            Team newTeam)
        {
            if (peer == null || !peer.IsMine || newTeam == null || (!newTeam.IsAttacker && !newTeam.IsDefender))
                return true;

            if (!ShouldSuppress(__instance, requireAuthoritativeTroopPath: true, out CoopBattleEntryPolicy.ClientSnapshot entryPolicy))
                return true;

            LogBlockedClassLoadout(
                "MissionGauntletClassLoadout.OnTeamChanged",
                entryPolicy);
            return false;
        }

        private static bool MissionLobbyEquipmentNetworkComponent_ToggleLoadout_Prefix(object __instance, bool isActive)
        {
            if (!ShouldSuppress(__instance, requireAuthoritativeTroopPath: true, out CoopBattleEntryPolicy.ClientSnapshot entryPolicy))
                return true;

            LogBlockedClassLoadout(
                "MissionLobbyEquipmentNetworkComponent.ToggleLoadout(" + isActive.ToString().ToLowerInvariant() + ")",
                entryPolicy);
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
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            entryPolicy = CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null)
                return false;

            if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay &&
                (mission.GetMissionBehavior<CoopMissionClientLogic>() != null ||
                 mission.GetMissionBehavior<CoopMissionSelectionView>() != null))
            {
                return true;
            }

            return requireAuthoritativeTroopPath
                ? entryPolicy.UseAuthoritativeTroopPath
                : entryPolicy.UseAuthoritativeSidePath;
        }

        private static void LogBlockedTeamSelection(string operationName, CoopBattleEntryPolicy.ClientSnapshot entryPolicy)
        {
            string blockKey = BuildPolicyLogKey(entryPolicy, "team");
            if (string.Equals(_lastBlockedTeamSelectionKey, blockKey, StringComparison.Ordinal))
                return;

            _lastBlockedTeamSelectionKey = blockKey;
            ModLogger.Info(
                "VanillaEntryUiSuppressionPatch: suppressed " + operationName + ". " +
                entryPolicy.Describe());
        }

        private static void LogBlockedClassLoadout(string operationName, CoopBattleEntryPolicy.ClientSnapshot entryPolicy)
        {
            string blockKey = BuildPolicyLogKey(entryPolicy, "class");
            if (string.Equals(_lastBlockedClassLoadoutKey, blockKey, StringComparison.Ordinal))
                return;

            _lastBlockedClassLoadoutKey = blockKey;
            ModLogger.Info(
                "VanillaEntryUiSuppressionPatch: suppressed " + operationName + ". " +
                entryPolicy.Describe());
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
