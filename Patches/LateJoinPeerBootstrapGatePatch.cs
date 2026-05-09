using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class LateJoinPeerBootstrapGatePatch
    {
        private sealed class DeferredLateJoinBootstrapHandler
        {
            public object Instance { get; set; }
            public MethodBase OriginalMethod { get; set; }
            public NetworkCommunicator Peer { get; set; }
            public long Sequence { get; set; }
            public DateTime DeferredUtc { get; set; }
        }

        private static readonly Dictionary<int, List<DeferredLateJoinBootstrapHandler>> DeferredHandlersByPeerIndex =
            new Dictionary<int, List<DeferredLateJoinBootstrapHandler>>();

        private static readonly HashSet<string> ReplayGuardKeys = new HashSet<string>(StringComparer.Ordinal);
        private static long _nextDeferredSequence;

        public static void Apply(Harmony harmony)
        {
            PatchLateJoinHandler(
                harmony,
                "TaleWorlds.MountAndBlade.MissionNetworkComponent",
                "SendAgentsToPeer",
                nameof(MissionNetworkComponent_SendAgentsToPeer_Prefix));
            PatchLateJoinHandler(
                harmony,
                "TaleWorlds.MountAndBlade.MissionNetworkComponent",
                "SendMissilesToPeer",
                nameof(MissionNetworkComponent_SendMissilesToPeer_Prefix));
        }

        internal static void TryReplayDeferredPeerBootstrap(NetworkCommunicator peer, string source)
        {
            if (!GameNetwork.IsServer || peer == null || peer.IsServerPeer)
                return;

            if (!CoopMissionNetworkBridge.IsPeerCurrentBattleSnapshotBootstrapReady(peer, out string readinessSummary))
                return;

            if (!DeferredHandlersByPeerIndex.TryGetValue(peer.Index, out List<DeferredLateJoinBootstrapHandler> deferredHandlers) ||
                deferredHandlers == null ||
                deferredHandlers.Count <= 0)
            {
                return;
            }

            DeferredHandlersByPeerIndex.Remove(peer.Index);
            foreach (DeferredLateJoinBootstrapHandler deferredHandler in deferredHandlers
                .Where(handler => handler != null && handler.Instance != null && handler.OriginalMethod != null)
                .OrderBy(handler => handler.Sequence)
                .ToArray())
            {
                string replayKey = BuildReplayGuardKey(peer.Index, deferredHandler.OriginalMethod);
                ReplayGuardKeys.Add(replayKey);
                try
                {
                    deferredHandler.OriginalMethod.Invoke(
                        deferredHandler.Instance,
                        new object[] { peer });
                    ModLogger.Info(
                        "LateJoinPeerBootstrapGatePatch: replayed deferred server late-join bootstrap handler. " +
                        "Peer=" + (peer.UserName ?? "null") +
                        " Method=" + deferredHandler.OriginalMethod.DeclaringType?.FullName + "." + deferredHandler.OriginalMethod.Name +
                        " DeferredForMs=" + Math.Max(0d, (DateTime.UtcNow - deferredHandler.DeferredUtc).TotalMilliseconds).ToString("0") +
                        " SnapshotReadiness=" + (readinessSummary ?? "unknown") +
                        " Source=" + (source ?? "unknown"));
                }
                catch (TargetInvocationException ex)
                {
                    ModLogger.Error(
                        "LateJoinPeerBootstrapGatePatch: deferred server late-join bootstrap replay failed. " +
                        "Peer=" + (peer.UserName ?? "null") +
                        " Method=" + deferredHandler.OriginalMethod.DeclaringType?.FullName + "." + deferredHandler.OriginalMethod.Name +
                        " Source=" + (source ?? "unknown"),
                        ex.InnerException ?? ex);
                }
                catch (Exception ex)
                {
                    ModLogger.Error(
                        "LateJoinPeerBootstrapGatePatch: deferred server late-join bootstrap replay failed. " +
                        "Peer=" + (peer.UserName ?? "null") +
                        " Method=" + deferredHandler.OriginalMethod.DeclaringType?.FullName + "." + deferredHandler.OriginalMethod.Name +
                        " Source=" + (source ?? "unknown"),
                        ex);
                }
                finally
                {
                    ReplayGuardKeys.Remove(replayKey);
                }
            }
        }

        internal static void ClearDeferredPeerBootstrap(NetworkCommunicator peer, string source)
        {
            if (peer == null)
                return;

            if (DeferredHandlersByPeerIndex.TryGetValue(peer.Index, out List<DeferredLateJoinBootstrapHandler> handlers))
            {
                DeferredHandlersByPeerIndex.Remove(peer.Index);
                if (handlers != null && handlers.Count > 0)
                {
                    ModLogger.Info(
                        "LateJoinPeerBootstrapGatePatch: cleared deferred server late-join bootstrap handlers. " +
                        "Peer=" + (peer.UserName ?? "null") +
                        " Count=" + handlers.Count +
                        " Source=" + (source ?? "unknown"));
                }
            }
        }

        internal static void ClearAllDeferredPeerBootstrap(string source)
        {
            int count = DeferredHandlersByPeerIndex.Sum(entry => entry.Value?.Count ?? 0);
            DeferredHandlersByPeerIndex.Clear();
            ReplayGuardKeys.Clear();
            if (count <= 0)
                return;

            ModLogger.Info(
                "LateJoinPeerBootstrapGatePatch: cleared all deferred server late-join bootstrap handlers. " +
                "Count=" + count +
                " Source=" + (source ?? "unknown"));
        }

        private static void PatchLateJoinHandler(
            Harmony harmony,
            string typeName,
            string methodName,
            string prefixMethodName)
        {
            Type targetType = AccessTools.TypeByName(typeName);
            if (targetType == null)
            {
                ModLogger.Info("LateJoinPeerBootstrapGatePatch: type not found. Type=" + typeName);
                return;
            }

            MethodInfo target = targetType.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(NetworkCommunicator) },
                null);
            MethodInfo prefix = typeof(LateJoinPeerBootstrapGatePatch).GetMethod(
                prefixMethodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info(
                    "LateJoinPeerBootstrapGatePatch: method not found. Type=" + typeName +
                    " Method=" + methodName);
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info(
                "LateJoinPeerBootstrapGatePatch: patched " +
                typeName + "." + methodName + ".");
        }

        private static bool MissionNetworkComponent_SendAgentsToPeer_Prefix(
            object __instance,
            NetworkCommunicator networkPeer,
            MethodBase __originalMethod)
        {
            return !TryDeferLateJoinBootstrap(__instance, networkPeer, __originalMethod);
        }

        private static bool MissionNetworkComponent_SendMissilesToPeer_Prefix(
            object __instance,
            NetworkCommunicator networkPeer,
            MethodBase __originalMethod)
        {
            return !TryDeferLateJoinBootstrap(__instance, networkPeer, __originalMethod);
        }

        private static bool TryDeferLateJoinBootstrap(
            object instance,
            NetworkCommunicator peer,
            MethodBase originalMethod)
        {
            if (!GameNetwork.IsServer || peer == null || peer.IsServerPeer || instance == null || originalMethod == null)
                return false;

            Mission mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            string replayKey = BuildReplayGuardKey(peer.Index, originalMethod);
            if (ReplayGuardKeys.Contains(replayKey))
                return false;

            if (CoopMissionNetworkBridge.IsPeerCurrentBattleSnapshotBootstrapReady(peer, out string readinessSummary))
                return false;

            RegisterDeferredHandler(instance, peer, originalMethod);
            ModLogger.Info(
                "LateJoinPeerBootstrapGatePatch: deferred server late-join bootstrap handler until peer battle snapshot is ready. " +
                "Peer=" + (peer.UserName ?? "null") +
                " Method=" + originalMethod.DeclaringType?.FullName + "." + originalMethod.Name +
                " SnapshotReadiness=" + (readinessSummary ?? "unknown"));
            return true;
        }

        private static void RegisterDeferredHandler(
            object instance,
            NetworkCommunicator peer,
            MethodBase originalMethod)
        {
            if (!DeferredHandlersByPeerIndex.TryGetValue(peer.Index, out List<DeferredLateJoinBootstrapHandler> deferredHandlers) ||
                deferredHandlers == null)
            {
                deferredHandlers = new List<DeferredLateJoinBootstrapHandler>();
                DeferredHandlersByPeerIndex[peer.Index] = deferredHandlers;
            }

            string methodKey = BuildReplayGuardKey(peer.Index, originalMethod);
            if (deferredHandlers.Any(handler =>
                    handler != null &&
                    handler.Instance == instance &&
                    string.Equals(BuildReplayGuardKey(peer.Index, handler.OriginalMethod), methodKey, StringComparison.Ordinal)))
            {
                return;
            }

            deferredHandlers.Add(new DeferredLateJoinBootstrapHandler
            {
                Instance = instance,
                OriginalMethod = originalMethod,
                Peer = peer,
                Sequence = ++_nextDeferredSequence,
                DeferredUtc = DateTime.UtcNow
            });
        }

        private static string BuildReplayGuardKey(int peerIndex, MethodBase originalMethod)
        {
            return peerIndex + "|" +
                   (originalMethod?.DeclaringType?.FullName ?? "unknown") + "|" +
                   (originalMethod?.MetadataToken.ToString() ?? "unknown");
        }
    }
}
