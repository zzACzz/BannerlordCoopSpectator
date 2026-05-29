using System;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Rewrites native custom/community LobbyMissionType arm to neutral state when the active
    /// custom-game join context is the coop-owned listed TeamDeathmatch ingress.
    /// </summary>
    internal static class ListedShellLobbyMissionTypeNeutralizationPatch
    {
        private const int MatchmakerLobbyMissionTypeCode = 0;
        private const int CustomLobbyMissionTypeCode = 1;
        private const int CommunityLobbyMissionTypeCode = 2;

        private static bool _isApplied;

        public static void Apply(Harmony harmony)
        {
            if (_isApplied)
                return;

            try
            {
                var targetMethod = AccessTools.Method(
                    typeof(BannerlordNetwork),
                    "StartMultiplayerLobbyMission",
                    new[] { typeof(LobbyMissionType) });
                var prefixMethod = AccessTools.Method(
                    typeof(ListedShellLobbyMissionTypeNeutralizationPatch),
                    nameof(StartMultiplayerLobbyMission_Prefix));
                if (targetMethod == null || prefixMethod == null)
                {
                    ModLogger.Info("ListedShellLobbyMissionTypeNeutralizationPatch: target/prefix method not found. Skip.");
                    return;
                }

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                _isApplied = true;
                ModLogger.Info("ListedShellLobbyMissionTypeNeutralizationPatch: patched BannerlordNetwork.StartMultiplayerLobbyMission.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLobbyMissionTypeNeutralizationPatch.Apply failed.", ex);
            }
        }

        private static void StartMultiplayerLobbyMission_Prefix(ref LobbyMissionType missionType)
        {
            if (!CustomGameJoinContextState.ShouldNeutralizeListedShellLobbyMissionType())
                return;

            int missionTypeCode = Convert.ToInt32(missionType);
            if (missionTypeCode != CustomLobbyMissionTypeCode &&
                missionTypeCode != CommunityLobbyMissionTypeCode)
            {
                return;
            }

            missionType = (LobbyMissionType)MatchmakerLobbyMissionTypeCode;
            ModLogger.Info(
                "ListedShellLobbyMissionTypeNeutralizationPatch: rewrote native StartMultiplayerLobbyMission for listed TeamDeathmatch join context. " +
                "OriginalLobbyMissionTypeCode=" + missionTypeCode +
                " ReplacementLobbyMissionTypeCode=" + MatchmakerLobbyMissionTypeCode + ".");
        }
    }
}
