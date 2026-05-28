using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class MissionLobbySpawnContractPatch
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo respawnTarget = typeof(MissionLobbyComponent).GetMethod(
                    nameof(MissionLobbyComponent.GetSpawnPeriodDurationForPeer),
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    types: new[] { typeof(MissionPeer) },
                    modifiers: null);
                MethodInfo respawnPrefix = typeof(MissionLobbySpawnContractPatch).GetMethod(
                    nameof(GetSpawnPeriodDurationForPeer_Prefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (respawnTarget == null || respawnPrefix == null)
                {
                    ModLogger.Info("MissionLobbySpawnContractPatch: respawn-period target or prefix not found. Skip.");
                    return;
                }

                MethodInfo tickTarget = typeof(MissionLobbyComponent).GetMethod(
                    nameof(MissionLobbyComponent.OnMissionTick),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(float) },
                    modifiers: null);
                MethodInfo tickPrefix = typeof(MissionLobbySpawnContractPatch).GetMethod(
                    nameof(OnMissionTick_Prefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (tickTarget == null || tickPrefix == null)
                {
                    ModLogger.Info("MissionLobbySpawnContractPatch: mission-tick target or prefix not found. Skip.");
                    return;
                }

                harmony.Patch(respawnTarget, prefix: new HarmonyMethod(respawnPrefix));
                harmony.Patch(tickTarget, prefix: new HarmonyMethod(tickPrefix));
                ModLogger.Info("MissionLobbySpawnContractPatch: patched MissionLobbyComponent.GetSpawnPeriodDurationForPeer.");
                ModLogger.Info("MissionLobbySpawnContractPatch: patched MissionLobbyComponent.OnMissionTick.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch.Apply failed: " + ex.Message);
            }
        }

        private static bool GetSpawnPeriodDurationForPeer_Prefix(MissionPeer peer, ref int __result)
        {
            try
            {
                Mission mission = Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return true;

                __result = ResolveListedShellRespawnPeriodForPeer(mission, peer);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch: prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool OnMissionTick_Prefix(MissionLobbyComponent __instance, float dt)
        {
            try
            {
                Mission mission = __instance?.Mission ?? Mission.Current;
                if (!ShouldUseListedShellLobbyContract(mission))
                    return true;

                if (__instance.CurrentMultiplayerState != MissionLobbyComponent.MultiplayerGameState.Playing)
                    return true;

                if (!GameNetwork.IsServerOrRecorder)
                    return true;

                MissionMultiplayerGameModeBase gameMode = mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
                MultiplayerTimerComponent timer = mission.GetMissionBehavior<MultiplayerTimerComponent>();
                if (gameMode == null || timer == null || gameMode.RoundController != null)
                    return true;

                bool timerPassed = timer.CheckIfTimerPassed();
                if (!timerPassed && !gameMode.CheckForMatchEnd())
                    return true;

                gameMode.GetWinnerTeam();
                SetRemainingAgentsInvulnerable(mission);
                ClearListedShellSpawnCompatibilityFlags();
                __instance.SetStateEndingAsServer();
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("MissionLobbySpawnContractPatch: mission-tick prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static int ResolveListedShellRespawnPeriodForPeer(Mission mission, MissionPeer peer)
        {
            MissionMultiplayerGameModeBase gameMode = mission?.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            if (gameMode?.WarmupComponent != null && gameMode.WarmupComponent.IsInWarmup)
                return 3;

            BattleSideEnum authoritativeSide = CoopBattleAuthorityState.GetAssignedSide(peer);
            if (authoritativeSide == BattleSideEnum.Attacker)
                return MultiplayerOptions.OptionType.RespawnPeriodTeam2.GetIntValue();

            if (authoritativeSide == BattleSideEnum.Defender)
                return MultiplayerOptions.OptionType.RespawnPeriodTeam1.GetIntValue();

            return -1;
        }

        private static void SetRemainingAgentsInvulnerable(Mission mission)
        {
            if (mission?.Agents == null)
                return;

            foreach (Agent agent in mission.Agents)
                agent.SetMortalityState(Agent.MortalityState.Invulnerable);
        }

        private static void ClearListedShellSpawnCompatibilityFlags()
        {
            ClearListedShellSpawnCompatibilityFlags(GameNetwork.NetworkPeers);
            ClearListedShellSpawnCompatibilityFlags(GameNetwork.DisconnectedNetworkPeers);
        }

        private static void ClearListedShellSpawnCompatibilityFlags(IEnumerable<NetworkCommunicator> peers)
        {
            if (peers == null)
                return;

            foreach (NetworkCommunicator peer in peers)
            {
                MissionPeer missionPeer = peer?.GetComponent<MissionPeer>();
                if (missionPeer == null)
                    continue;

                missionPeer.HasSpawnedAgentVisuals = false;
                missionPeer.EquipmentUpdatingExpired = true;
                missionPeer.WantsToSpawnAsBot = false;
            }
        }

        private static bool ShouldUseListedShellLobbyContract(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<ListedShellCompatibilityMode>() != null ||
                mission.GetMissionBehavior<ListedShellCompatibilityModeClient>() != null;
        }
    }
}
