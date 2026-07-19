using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Patches
{
    public static class PreMissionTopologyContractPatch
    {
        private static readonly object Sync = new object();
        private static bool _startMultiplayerPatched;
        private static bool _loadMissionPatched;
        private static bool _initializeCustomGamePatched;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            lock (Sync)
            {
                TryPatchGameNetworkHandlerStartMultiplayer(harmony);
                TryPatchBaseNetworkComponentLoadMission(harmony);
                TryPatchBaseNetworkComponentInitializeCustomGame(harmony);
            }
        }

        private static void TryPatchGameNetworkHandlerStartMultiplayer(
            Harmony harmony)
        {
            if (_startMultiplayerPatched)
                return;

            Type targetType = AccessTools.TypeByName(
                "TaleWorlds.MountAndBlade.GameNetworkHandler");
            MethodInfo target = targetType?
                .GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                    method.GetParameters().Length == 0 &&
                    (string.Equals(
                         method.Name,
                         "OnStartMultiplayer",
                         StringComparison.Ordinal) ||
                     method.Name.EndsWith(
                         ".OnStartMultiplayer",
                         StringComparison.Ordinal)));
            MethodInfo prefix = typeof(PreMissionTopologyContractPatch)
                .GetMethod(
                    nameof(GameNetworkHandler_OnStartMultiplayer_Prefix),
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info(
                    "PreMissionTopologyContractPatch: GameNetworkHandler.OnStartMultiplayer target unavailable; registration patch deferred.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix));
            _startMultiplayerPatched = true;
            ModLogger.Info(
                "PreMissionTopologyContractPatch: patched GameNetworkHandler.OnStartMultiplayer for pre-base global topology component registration.");
        }

        private static void TryPatchBaseNetworkComponentLoadMission(
            Harmony harmony)
        {
            if (_loadMissionPatched)
                return;

            Type targetType = AccessTools.TypeByName(
                "TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents.BaseNetworkComponent");
            MethodInfo target = targetType?.GetMethod(
                "HandleServerEventLoadMission",
                BindingFlags.Instance |
                BindingFlags.NonPublic,
                null,
                new[] { typeof(GameNetworkMessage) },
                null);
            MethodInfo prefix = typeof(PreMissionTopologyContractPatch)
                .GetMethod(
                    nameof(BaseNetworkComponent_HandleServerEventLoadMission_Prefix),
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info(
                    "PreMissionTopologyContractPatch: BaseNetworkComponent.HandleServerEventLoadMission target unavailable; load deferral patch deferred.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix));
            _loadMissionPatched = true;
            ModLogger.Info(
                "PreMissionTopologyContractPatch: patched BaseNetworkComponent.HandleServerEventLoadMission for topology contract gate.");
        }

        private static void TryPatchBaseNetworkComponentInitializeCustomGame(
            Harmony harmony)
        {
            if (_initializeCustomGamePatched)
                return;

            Type targetType = AccessTools.TypeByName(
                "TaleWorlds.MountAndBlade.Multiplayer.NetworkComponents.BaseNetworkComponent");
            MethodInfo target = targetType?.GetMethod(
                "HandleServerEventInitializeCustomGame",
                BindingFlags.Instance |
                BindingFlags.NonPublic,
                null,
                new[] { typeof(GameNetworkMessage) },
                null);
            MethodInfo prefix = typeof(PreMissionTopologyContractPatch)
                .GetMethod(
                    nameof(BaseNetworkComponent_HandleServerEventInitializeCustomGame_Prefix),
                    BindingFlags.Static |
                    BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info(
                    "PreMissionTopologyContractPatch: BaseNetworkComponent.HandleServerEventInitializeCustomGame target unavailable; active-join deferral patch deferred.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix));
            _initializeCustomGamePatched = true;
            ModLogger.Info(
                "PreMissionTopologyContractPatch: patched BaseNetworkComponent.HandleServerEventInitializeCustomGame for topology contract gate.");
        }

        private static void GameNetworkHandler_OnStartMultiplayer_Prefix()
        {
            try
            {
                if (GameNetwork.GetNetworkComponent<CoopPreMissionTopologyNetworkComponent>() != null)
                    return;

                GameNetwork.AddNetworkComponent<CoopPreMissionTopologyNetworkComponent>();
                ModLogger.Info(
                    "PreMissionTopologyContractPatch: registered CoopPreMissionTopologyNetworkComponent before native BaseNetworkComponent.");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "PreMissionTopologyContractPatch: global topology component registration failed. " +
                    "Exception=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private static bool BaseNetworkComponent_HandleServerEventLoadMission_Prefix(
            object __instance,
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is LoadMission message))
                    return true;

                return !CoopPreMissionTopologyNetworkComponent
                    .ShouldDeferClientLoadMission(
                        __instance,
                        message);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "PreMissionTopologyContractPatch: LoadMission topology gate failed. " +
                    "Exception=" + ex.GetType().Name + ":" + ex.Message);
                if (baseMessage is LoadMission message &&
                    CoopPreMissionTopologyNetworkComponent.AbortUnsafeClientMissionLoad(
                        message.GameType,
                        message.Map,
                        message.BattleIndex,
                        "LoadMission topology gate exception",
                        ex.GetType().Name + ":" + ex.Message))
                {
                    return false;
                }

                return true;
            }
        }

        private static bool BaseNetworkComponent_HandleServerEventInitializeCustomGame_Prefix(
            object __instance,
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is InitializeCustomGameMessage message))
                    return true;

                return !CoopPreMissionTopologyNetworkComponent
                    .ShouldDeferClientInitializeCustomGame(
                        __instance,
                        message);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "PreMissionTopologyContractPatch: InitializeCustomGame topology gate failed. " +
                    "Exception=" + ex.GetType().Name + ":" + ex.Message);
                if (baseMessage is InitializeCustomGameMessage message &&
                    message.InMission &&
                    CoopPreMissionTopologyNetworkComponent.AbortUnsafeClientMissionLoad(
                        message.GameType,
                        message.Map,
                        message.BattleIndex,
                        "InitializeCustomGame topology gate exception",
                        ex.GetType().Name + ":" + ex.Message))
                {
                    return false;
                }

                return true;
            }
        }
    }
}
