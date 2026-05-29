using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Patches
{
    internal static class PendingBattleLoadMissionTransportTokenPatch
    {
        private static bool _isApplied;

        public static void Apply(Harmony harmony)
        {
            if (_isApplied)
                return;

            try
            {
                MethodInfo targetMethod = typeof(GameNetwork).GetMethod(
                    "WriteMessage",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(GameNetworkMessage) },
                    null);
                MethodInfo prefixMethod = typeof(PendingBattleLoadMissionTransportTokenPatch).GetMethod(
                    nameof(WriteMessage_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (targetMethod == null || prefixMethod == null)
                {
                    ModLogger.Info("PendingBattleLoadMissionTransportTokenPatch: GameNetwork.WriteMessage target/prefix not found. Skip.");
                    return;
                }

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                _isApplied = true;
                ModLogger.Info("PendingBattleLoadMissionTransportTokenPatch: patched GameNetwork.WriteMessage for pending battle LoadMission token capture.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("PendingBattleLoadMissionTransportTokenPatch.Apply failed.", ex);
            }
        }

        private static void WriteMessage_Prefix(GameNetworkMessage message)
        {
            if (!GameNetwork.IsServer)
                return;

            if (!(message is LoadMission loadMission))
                return;

            PendingBattleMissionStartupState.TryCapturePendingTransportToken(
                loadMission.Map,
                loadMission.BattleIndex,
                "PendingBattleLoadMissionTransportTokenPatch");
        }
    }
}
