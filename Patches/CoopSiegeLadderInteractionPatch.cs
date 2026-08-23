using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;
using NetworkMessages.FromClient;
using NetworkMessages.FromServer;

namespace CoopSpectator.Patches
{
    internal static class CoopSiegeLadderInteractionPatch
    {
        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            PatchIncoming(
                harmony,
                "HandleServerEventSetSiegeLadderState",
                nameof(HandleServerEventSetSiegeLadderStatePrefix));
            PatchIncoming(
                harmony,
                "HandleServerEventSetUsableGameObjectIsDeactivated",
                nameof(HandleServerEventSetUsableGameObjectIsDeactivatedPrefix));
            PatchIncoming(
                harmony,
                "HandleServerEventSetUsableGameObjectIsDisabledForPlayers",
                nameof(HandleServerEventSetUsableGameObjectIsDisabledForPlayersPrefix));
            PatchIncoming(
                harmony,
                "HandleServerEventUseObject",
                nameof(HandleServerEventUseObjectPrefix));
            PatchIncoming(
                harmony,
                "HandleServerEventStopUsingObject",
                nameof(HandleServerEventStopUsingObjectPrefix));
            PatchIncoming(
                harmony,
                "HandleServerEventSynchronizeMissionObject",
                nameof(HandleServerEventSynchronizeMissionObjectPrefix));
            PatchIncoming(
                harmony,
                "HandleServerEventSetMissionObjectDisabled",
                nameof(HandleServerEventSetMissionObjectDisabledPrefix));
            PatchIncoming(
                harmony,
                "HandleServerEventSetMissionObjectVisibility",
                nameof(HandleServerEventSetMissionObjectVisibilityPrefix));

            MethodInfo requestUseObjectOnWrite =
                AccessTools.Method(typeof(RequestUseObject), "OnWrite");
            MethodInfo requestUseObjectPrefix =
                AccessTools.Method(
                    typeof(CoopSiegeLadderInteractionPatch),
                    nameof(RequestUseObjectOnWritePrefix));
            if (requestUseObjectOnWrite != null && requestUseObjectPrefix != null)
            {
                harmony.Patch(
                    requestUseObjectOnWrite,
                    prefix: new HarmonyMethod(requestUseObjectPrefix)
                    {
                        priority = Priority.First
                    });
            }
        }

        private static void PatchIncoming(
            Harmony harmony,
            string targetMethodName,
            string prefixMethodName)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                targetMethodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(CoopSiegeLadderInteractionPatch).GetMethod(
                prefixMethodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
                return;

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                });
        }

        private static bool HandleServerEventSetSiegeLadderStatePrefix(
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (baseMessage is SetSiegeLadderState message)
                {
                    Mission mission = Mission.Current;
                    MissionObjectId serverId = message.SiegeLadderId;
                    CoopSiegeLadderInteractionRuntime.ObserveServerLadderState(
                        mission,
                        serverId,
                        (int)message.State);
                    TryTranslateAndSet(
                        mission,
                        message,
                        nameof(SetSiegeLadderState.SiegeLadderId),
                        serverId,
                        CoopSiegeLadderMissionObjectTarget.Ladder);
                    CoopSiegeLadderInteractionRuntime.TryApplyCachedAuthoritativeState(
                        mission,
                        out _);
                }
            }
            catch
            {
            }

            return true;
        }

        private static bool HandleServerEventSetUsableGameObjectIsDeactivatedPrefix(
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (baseMessage is SetUsableMissionObjectIsDeactivated message)
                {
                    Mission mission = Mission.Current;
                    MissionObjectId serverId = message.UsableGameObjectId;
                    CoopSiegeLadderInteractionRuntime.ObserveServerPointDeactivated(
                        mission,
                        serverId,
                        message.IsDeactivated);
                    TryTranslateAndSet(
                        mission,
                        message,
                        nameof(SetUsableMissionObjectIsDeactivated.UsableGameObjectId),
                        serverId,
                        CoopSiegeLadderMissionObjectTarget.AttackerStandingPoint);
                    CoopSiegeLadderInteractionRuntime.TryApplyCachedAuthoritativeState(
                        mission,
                        out _);
                }
            }
            catch
            {
            }

            return true;
        }

        private static bool HandleServerEventSetUsableGameObjectIsDisabledForPlayersPrefix(
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (baseMessage is SetUsableMissionObjectIsDisabledForPlayers message)
                {
                    Mission mission = Mission.Current;
                    MissionObjectId serverId = message.UsableGameObjectId;
                    CoopSiegeLadderInteractionRuntime.ObserveServerPointDisabledForPlayers(
                        mission,
                        serverId,
                        message.IsDisabledForPlayers);
                    TryTranslateAndSet(
                        mission,
                        message,
                        nameof(SetUsableMissionObjectIsDisabledForPlayers.UsableGameObjectId),
                        serverId,
                        CoopSiegeLadderMissionObjectTarget.AttackerStandingPoint);
                    CoopSiegeLadderInteractionRuntime.TryApplyCachedAuthoritativeState(
                        mission,
                        out _);
                }
            }
            catch
            {
            }

            return true;
        }

        private static bool HandleServerEventUseObjectPrefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (baseMessage is UseObject message)
                {
                    Mission mission = Mission.Current;
                    CoopSiegeLadderInteractionRuntime.ObserveServerPointUser(
                        mission,
                        message.UsableGameObjectId,
                        message.AgentIndex);
                    TryTranslateAndSet(
                        mission,
                        message,
                        nameof(UseObject.UsableGameObjectId),
                        message.UsableGameObjectId,
                        CoopSiegeLadderMissionObjectTarget.AttackerStandingPoint);
                    CoopSiegeLadderInteractionRuntime.TryApplyCachedAuthoritativeState(
                        mission,
                        out _);
                }
            }
            catch
            {
            }

            return true;
        }

        private static bool HandleServerEventStopUsingObjectPrefix(
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (baseMessage is StopUsingObject message)
                {
                    Mission mission = Mission.Current;
                    CoopSiegeLadderInteractionRuntime.ObserveServerUserStopped(
                        mission,
                        message.AgentIndex);
                    CoopSiegeLadderInteractionRuntime.TryApplyCachedAuthoritativeState(
                        mission,
                        out _);
                }
            }
            catch
            {
            }

            return true;
        }

        private static bool HandleServerEventSynchronizeMissionObjectPrefix(
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (baseMessage is SynchronizeMissionObject message)
                {
                    TryTranslateAndSet(
                        Mission.Current,
                        message,
                        nameof(SynchronizeMissionObject.MissionObjectId),
                        message.MissionObjectId,
                        CoopSiegeLadderMissionObjectTarget.AnyRegistered);
                }
            }
            catch
            {
            }

            return true;
        }

        private static bool HandleServerEventSetMissionObjectDisabledPrefix(
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (baseMessage is SetMissionObjectDisabled message)
                {
                    Mission mission = Mission.Current;
                    MissionObjectId serverId = message.MissionObjectId;
                    CoopSiegeLadderInteractionRuntime.ObserveServerRootDisabled(
                        mission,
                        serverId,
                        rootDisabled: true);
                    TryTranslateAndSet(
                        mission,
                        message,
                        nameof(SetMissionObjectDisabled.MissionObjectId),
                        serverId,
                        CoopSiegeLadderMissionObjectTarget.Ladder);
                    CoopSiegeLadderInteractionRuntime.TryApplyCachedAuthoritativeState(
                        mission,
                        out _);
                }
            }
            catch
            {
            }

            return true;
        }

        private static bool HandleServerEventSetMissionObjectVisibilityPrefix(
            GameNetworkMessage baseMessage)
        {
            try
            {
                if (baseMessage is SetMissionObjectVisibility message)
                {
                    Mission mission = Mission.Current;
                    MissionObjectId serverId = message.MissionObjectId;
                    CoopSiegeLadderInteractionRuntime.ObserveServerRootVisibility(
                        mission,
                        serverId,
                        message.Visible);
                    TryTranslateAndSet(
                        mission,
                        message,
                        nameof(SetMissionObjectVisibility.MissionObjectId),
                        serverId,
                        CoopSiegeLadderMissionObjectTarget.Ladder);
                    CoopSiegeLadderInteractionRuntime.TryApplyCachedAuthoritativeState(
                        mission,
                        out _);
                }
            }
            catch
            {
            }

            return true;
        }

        private static void RequestUseObjectOnWritePrefix(RequestUseObject __instance)
        {
            try
            {
                if (__instance == null ||
                    !CoopSiegeLadderInteractionRuntime.TryTranslateLocalAttackerPointId(
                        Mission.Current,
                        __instance.UsableMissionObjectId,
                        out MissionObjectId serverId))
                {
                    return;
                }

                SetMissionObjectId(
                    __instance,
                    nameof(RequestUseObject.UsableMissionObjectId),
                    serverId);
            }
            catch
            {
            }
        }

        private static bool TryTranslateAndSet(
            Mission mission,
            object message,
            string memberName,
            MissionObjectId serverId,
            CoopSiegeLadderMissionObjectTarget target)
        {
            if (!CoopSiegeLadderInteractionRuntime.TryTranslateServerMissionObjectId(
                    mission,
                    serverId,
                    target,
                    out MissionObjectId localId))
            {
                return false;
            }

            return SetMissionObjectId(message, memberName, localId);
        }

        private static bool SetMissionObjectId(
            object instance,
            string propertyName,
            MissionObjectId missionObjectId)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            FieldInfo backingField = AccessTools.Field(
                instance.GetType(),
                "<" + propertyName + ">k__BackingField");
            if (backingField == null || backingField.FieldType != typeof(MissionObjectId))
                return false;

            backingField.SetValue(instance, missionObjectId);
            return true;
        }
    }
}
