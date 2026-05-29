using System;
using System.Reflection;
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
        private static readonly object HostedStartSync = new object();
        private static readonly TimeSpan HostedStartLifetime = TimeSpan.FromSeconds(15);

        private static bool _isApplied;
        private static int _hostedListedStartDepth;
        private static string _hostedListedStartGameType = string.Empty;
        private static DateTime _hostedListedStartUpdatedUtc = DateTime.MinValue;
        private static FieldInfo _playerBasedCustomServerGameClientField;

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
                TryPatchHostedCustomServerStart(harmony);
                _isApplied = true;
                ModLogger.Info("ListedShellLobbyMissionTypeNeutralizationPatch: patched listed LobbyMissionType neutralization sources.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLobbyMissionTypeNeutralizationPatch.Apply failed.", ex);
            }
        }

        private static void StartMultiplayerLobbyMission_Prefix(ref LobbyMissionType missionType)
        {
            if (!TryResolveListedShellNeutralizationSource(out string neutralizationSource))
                return;

            int missionTypeCode = Convert.ToInt32(missionType);
            if (missionTypeCode != CustomLobbyMissionTypeCode &&
                missionTypeCode != CommunityLobbyMissionTypeCode)
            {
                return;
            }

            missionType = (LobbyMissionType)MatchmakerLobbyMissionTypeCode;
            ModLogger.Info(
                "ListedShellLobbyMissionTypeNeutralizationPatch: rewrote native StartMultiplayerLobbyMission for listed TeamDeathmatch ingress. " +
                "Source=" + neutralizationSource +
                " " +
                "OriginalLobbyMissionTypeCode=" + missionTypeCode +
                " ReplacementLobbyMissionTypeCode=" + MatchmakerLobbyMissionTypeCode + ".");
        }

        private static void TryPatchHostedCustomServerStart(Harmony harmony)
        {
            Type hostedCustomServerType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStatePlayerBasedCustomServer");
            if (hostedCustomServerType == null)
            {
                ModLogger.Info("ListedShellLobbyMissionTypeNeutralizationPatch: LobbyGameStatePlayerBasedCustomServer type not found. Skip hosted custom-server arm neutralization.");
                return;
            }

            MethodInfo targetMethod = hostedCustomServerType.GetMethod("HandleServerStartMultiplayer", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefixMethod = AccessTools.Method(
                typeof(ListedShellLobbyMissionTypeNeutralizationPatch),
                nameof(HandleServerStartMultiplayer_Prefix));
            MethodInfo postfixMethod = AccessTools.Method(
                typeof(ListedShellLobbyMissionTypeNeutralizationPatch),
                nameof(HandleServerStartMultiplayer_Postfix));
            if (targetMethod == null || prefixMethod == null || postfixMethod == null)
            {
                ModLogger.Info("ListedShellLobbyMissionTypeNeutralizationPatch: hosted custom-server target/prefix/postfix not found. Skip hosted arm neutralization.");
                return;
            }

            _playerBasedCustomServerGameClientField = hostedCustomServerType.GetField("_gameClient", BindingFlags.Instance | BindingFlags.NonPublic);
            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod), postfix: new HarmonyMethod(postfixMethod));
            ModLogger.Info("ListedShellLobbyMissionTypeNeutralizationPatch: patched LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer.");
        }

        private static void HandleServerStartMultiplayer_Prefix(object __instance, ref bool __state)
        {
            __state = false;

            try
            {
                if (!TryResolveHostedListedStartContext(__instance, out string gameType, out string scene))
                    return;

                if (!string.Equals(gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal))
                    return;

                ArmHostedListedStart(gameType, scene);
                __state = true;
                ModLogger.Info(
                    "ListedShellLobbyMissionTypeNeutralizationPatch: armed hosted custom-server listed ingress neutralization scope. " +
                    "GameType=" + gameType +
                    " Scene=" + scene + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLobbyMissionTypeNeutralizationPatch.HandleServerStartMultiplayer_Prefix failed.", ex);
            }
        }

        private static void HandleServerStartMultiplayer_Postfix(bool __state)
        {
            if (!__state)
                return;

            try
            {
                DisarmHostedListedStart();
                ModLogger.Info("ListedShellLobbyMissionTypeNeutralizationPatch: disarmed hosted custom-server listed ingress neutralization scope.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellLobbyMissionTypeNeutralizationPatch.HandleServerStartMultiplayer_Postfix failed.", ex);
            }
        }

        private static bool TryResolveListedShellNeutralizationSource(out string neutralizationSource)
        {
            if (CustomGameJoinContextState.ShouldNeutralizeListedShellLobbyMissionType())
            {
                neutralizationSource = "join-context";
                return true;
            }

            if (ShouldNeutralizeHostedListedStart())
            {
                neutralizationSource = "hosted-custom-server-start";
                return true;
            }

            neutralizationSource = string.Empty;
            return false;
        }

        private static bool TryResolveHostedListedStartContext(object instance, out string gameType, out string scene)
        {
            gameType = string.Empty;
            scene = string.Empty;

            try
            {
                object gameClient = _playerBasedCustomServerGameClientField?.GetValue(instance);
                if (gameClient == null)
                    return false;

                PropertyInfo customGameTypeProperty = gameClient.GetType().GetProperty("CustomGameType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo customGameSceneProperty = gameClient.GetType().GetProperty("CustomGameScene", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                gameType = Normalize(customGameTypeProperty?.GetValue(gameClient) as string);
                scene = Normalize(customGameSceneProperty?.GetValue(gameClient) as string);
                return !string.IsNullOrEmpty(gameType);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellLobbyMissionTypeNeutralizationPatch: failed to resolve hosted listed start context: " + ex.Message);
                return false;
            }
        }

        private static void ArmHostedListedStart(string gameType, string scene)
        {
            lock (HostedStartSync)
            {
                _hostedListedStartDepth++;
                _hostedListedStartGameType = Normalize(gameType);
                _hostedListedStartUpdatedUtc = DateTime.UtcNow;
            }
        }

        private static void DisarmHostedListedStart()
        {
            lock (HostedStartSync)
            {
                if (_hostedListedStartDepth > 0)
                    _hostedListedStartDepth--;

                if (_hostedListedStartDepth > 0)
                    return;

                _hostedListedStartDepth = 0;
                _hostedListedStartGameType = string.Empty;
                _hostedListedStartUpdatedUtc = DateTime.MinValue;
            }
        }

        private static bool ShouldNeutralizeHostedListedStart()
        {
            lock (HostedStartSync)
            {
                if (_hostedListedStartDepth <= 0)
                    return false;

                if (_hostedListedStartUpdatedUtc == DateTime.MinValue ||
                    DateTime.UtcNow - _hostedListedStartUpdatedUtc > HostedStartLifetime)
                {
                    _hostedListedStartDepth = 0;
                    _hostedListedStartGameType = string.Empty;
                    _hostedListedStartUpdatedUtc = DateTime.MinValue;
                    return false;
                }

                return string.Equals(_hostedListedStartGameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal);
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
