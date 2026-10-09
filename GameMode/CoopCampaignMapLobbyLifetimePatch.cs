using System;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    internal static class CoopCampaignMapLobbyLifetimePatch
    {
        private static readonly object ApplyLock = new object();
        private static bool _applied;

        internal static void EnsureApplied()
        {
            lock (ApplyLock)
            {
                if (_applied)
                    return;
                var target = AccessTools.DeclaredMethod(typeof(MissionLobbyComponent),
                    nameof(MissionLobbyComponent.OnMissionTick), new[] { typeof(float) });
                var prefix = AccessTools.DeclaredMethod(typeof(CoopCampaignMapLobbyLifetimePatch),
                    nameof(Prefix));
                if (target == null || prefix == null)
                    throw new MissingMethodException("Replica map lobby lifetime target unavailable.");
                new Harmony("com.coopspectator.campaign-map-lifetime").Patch(
                    target, prefix: new HarmonyMethod(prefix));
                _applied = true;
            }
        }

        private static bool Prefix(MissionLobbyComponent __instance)
        {
            // Use the invoked lobby's own mission, never global Mission.Current.
            // Do not suppress WaitingFirstPlayers or the native Ending/UDP path.
            return CoopCampaignMapLobbyLifetimePolicy.ShouldRunNativeTick(
                GameNetwork.IsServer,
                __instance?.Mission?.GetMissionBehavior<
                    MissionMultiplayerCoopCampaignMapPrototype>() != null,
                __instance != null && __instance.CurrentMultiplayerState ==
                    MissionLobbyComponent.MultiplayerGameState.Playing);
        }
    }
}
