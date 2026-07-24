using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.VillageBattle;
using CoopSpectator.Network.Messages;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network
{
    public sealed class CoopPreMissionTopologyNetworkComponent : UdpNetworkComponent
    {
        private const double PendingLoadTimeoutSeconds = 10d;
        private const double ServerContractRefreshSeconds = 0.25d;

        private readonly Dictionary<int, string> _lastSentContractKeyByPeerIndex =
            new Dictionary<int, string>();
        private readonly object _pendingLoadSync = new object();

        private DateTime _nextServerContractRefreshUtc = DateTime.MinValue;
        private PendingClientMissionLoad _pendingClientMissionLoad;

        protected override void AddRemoveMessageHandlers(
            GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            base.AddRemoveMessageHandlers(registerer);
            if (GameNetwork.IsClientOrReplay)
            {
                registerer.RegisterBaseHandler<CoopPreMissionTopologyContractMessage>(
                    HandleServerPreMissionTopologyContract);
            }
        }

        public override void OnUdpNetworkHandlerTick(float dt)
        {
            base.OnUdpNetworkHandlerTick(dt);

            if (GameNetwork.IsServer)
                TryRefreshServerContracts();

            if (GameNetwork.IsClientOrReplay)
                TryStartPendingClientMissionLoad();
        }

        public override void HandleNewClientConnect(PlayerConnectionInfo playerConnectionInfo)
        {
            base.HandleNewClientConnect(playerConnectionInfo);
            if (!GameNetwork.IsServer)
                return;

            NetworkCommunicator peer = playerConnectionInfo?.NetworkPeer;
            if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                return;

            TrySendServerContract(peer, "new-client-connect", force: true);
        }

        public override void HandlePlayerDisconnect(NetworkCommunicator networkPeer)
        {
            base.HandlePlayerDisconnect(networkPeer);
            if (networkPeer != null)
                _lastSentContractKeyByPeerIndex.Remove(networkPeer.Index);
        }

        public override void OnDisconnectedFromServer()
        {
            base.OnDisconnectedFromServer();
            ClearPendingClientMissionLoad("disconnected-from-server");
            CoopPreMissionTopologyRuntimeState.Clear("disconnected-from-server");
        }

        public override void OnUdpNetworkHandlerClose()
        {
            ClearPendingClientMissionLoad("network-component-close");
            CoopPreMissionTopologyRuntimeState.Clear("network-component-close");
            base.OnUdpNetworkHandlerClose();
        }

        public static bool ShouldDeferClientLoadMission(
            object baseNetworkComponent,
            LoadMission message)
        {
            if (!GameNetwork.IsClientOrReplay ||
                message == null ||
                !RequiresPreMissionContract(message.GameType, message.Map))
            {
                return false;
            }

            if (CoopPreMissionTopologyRuntimeState.TryActivateForMissionLoad(
                    message.Map,
                    message.BattleIndex,
                    out _,
                    out string activationDiagnostics))
            {
                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: allowing LoadMission after pre-mission contract activation. " +
                    "GameType=" + (message.GameType ?? string.Empty) +
                    " Map=" + (message.Map ?? string.Empty) +
                    " BattleIndex=" + message.BattleIndex +
                    " Contract={" + activationDiagnostics + "}.");
                return false;
            }

            CoopPreMissionTopologyNetworkComponent component =
                GameNetwork.GetNetworkComponent<CoopPreMissionTopologyNetworkComponent>();
            if (component == null)
            {
                AbortUnsafeClientMissionLoad(
                    message.GameType,
                    message.Map,
                    message.BattleIndex,
                    "LoadMission",
                    "global topology component is missing; " +
                    activationDiagnostics);
                return true;
            }

            component.QueuePendingClientMissionLoad(
                baseNetworkComponent,
                message.GameType,
                message.Map,
                message.BattleIndex,
                requiresLobbyState: false,
                notBeforeUtc: DateTime.UtcNow,
                source: "LoadMission");
            ModLogger.Info(
                "CoopPreMissionTopologyNetworkComponent: deferred LoadMission until matching pre-mission contract arrives. " +
                "GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex +
                " Contract={" + activationDiagnostics + "}.");
            return true;
        }

        public static bool ShouldDeferClientInitializeCustomGame(
            object baseNetworkComponent,
            InitializeCustomGameMessage message)
        {
            if (!GameNetwork.IsClientOrReplay ||
                message == null ||
                !message.InMission ||
                !RequiresPreMissionContract(message.GameType, message.Map))
            {
                return false;
            }

            if (CoopPreMissionTopologyRuntimeState.TryActivateForMissionLoad(
                    message.Map,
                    message.BattleIndex,
                    out _,
                    out string activationDiagnostics))
            {
                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: allowing InitializeCustomGame after pre-mission contract activation. " +
                    "GameType=" + (message.GameType ?? string.Empty) +
                    " Map=" + (message.Map ?? string.Empty) +
                    " BattleIndex=" + message.BattleIndex +
                    " Contract={" + activationDiagnostics + "}.");
                return false;
            }

            CoopPreMissionTopologyNetworkComponent component =
                GameNetwork.GetNetworkComponent<CoopPreMissionTopologyNetworkComponent>();
            if (component == null)
            {
                AbortUnsafeClientMissionLoad(
                    message.GameType,
                    message.Map,
                    message.BattleIndex,
                    "InitializeCustomGame",
                    "global topology component is missing; " +
                    activationDiagnostics);
                return true;
            }

            component.QueuePendingClientMissionLoad(
                baseNetworkComponent,
                message.GameType,
                message.Map,
                message.BattleIndex,
                requiresLobbyState: true,
                notBeforeUtc: DateTime.UtcNow.AddMilliseconds(200d),
                source: "InitializeCustomGame");
            ModLogger.Info(
                "CoopPreMissionTopologyNetworkComponent: deferred InitializeCustomGame until matching pre-mission contract arrives. " +
                "GameType=" + (message.GameType ?? string.Empty) +
                " Map=" + (message.Map ?? string.Empty) +
                " BattleIndex=" + message.BattleIndex +
                " Contract={" + activationDiagnostics + "}.");
            return true;
        }

        public static bool RequiresPreMissionContract(
            string gameType,
            string runtimeScene)
        {
            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                SceneRuntimeClassifier.IsOfficialMultiplayerBattleScene(runtimeScene))
            {
                return false;
            }

            bool isCoopBattleRuntime =
                string.Equals(
                    gameType,
                    CoopGameModeIds.OfficialBattle,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    gameType,
                    CoopGameModeIds.CoopBattle,
                    StringComparison.OrdinalIgnoreCase);
            return isCoopBattleRuntime ||
                   SceneRuntimeClassifier.IsCampaignBattleScene(runtimeScene) ||
                   SceneRuntimeClassifier.IsVillageBattleScene(runtimeScene);
        }

        public static bool AbortUnsafeClientMissionLoad(
            string gameType,
            string map,
            int battleIndex,
            string source,
            string reason)
        {
            if (!GameNetwork.IsClientOrReplay ||
                !RequiresPreMissionContract(gameType, map))
            {
                return false;
            }

            AbortPendingClientMissionLoad(
                new PendingClientMissionLoad
                {
                    GameType = gameType ?? string.Empty,
                    Map = map ?? string.Empty,
                    BattleIndex = battleIndex,
                    Source = source ?? "unknown"
                },
                reason);
            return true;
        }

        private void HandleServerPreMissionTopologyContract(
            GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopPreMissionTopologyContractMessage message))
                return;

            if (!CoopPreMissionTopologyRuntimeState.TryAccept(
                    message,
                    out string diagnostics))
            {
                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: rejected pre-mission contract. " +
                    "Diagnostics={" + diagnostics + "}.");
                return;
            }

            ModLogger.Info(
                "CoopPreMissionTopologyNetworkComponent: accepted pre-mission contract. " +
                "Diagnostics={" + diagnostics + "}.");
            TryStartPendingClientMissionLoad();
        }

        private void TryRefreshServerContracts()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextServerContractRefreshUtc)
                return;

            _nextServerContractRefreshUtc =
                nowUtc.AddSeconds(ServerContractRefreshSeconds);
            if (GameNetwork.NetworkPeers == null)
                return;

            for (int i = 0; i < GameNetwork.NetworkPeers.Count; i++)
            {
                NetworkCommunicator peer = GameNetwork.NetworkPeers[i];
                if (peer == null ||
                    peer.IsServerPeer ||
                    !peer.IsConnectionActive)
                {
                    continue;
                }

                TrySendServerContract(
                    peer,
                    "server-network-tick",
                    force: false);
            }
        }

        private void TrySendServerContract(
            NetworkCommunicator peer,
            string source,
            bool force)
        {
            if (peer == null ||
                peer.IsServerPeer ||
                !peer.IsConnectionActive)
            {
                return;
            }

            int battleIndex =
                GameNetwork.GetNetworkComponent<BaseNetworkComponentData>()?
                    .CurrentBattleIndex ?? -1;
            CoopPreMissionTopologyContractMessage message =
                CoopPreMissionTopologyRuntimeState.TryBuildServerMessage(
                    battleIndex,
                    out string contractDiagnostics);
            if (message == null)
                return;

            string contractKey =
                message.BattleIndex + "|" +
                (message.RuntimeScene ?? string.Empty) + "|" +
                (message.ContractHash ?? string.Empty);
            if (!force &&
                _lastSentContractKeyByPeerIndex.TryGetValue(
                    peer.Index,
                    out string previousContractKey) &&
                string.Equals(
                    previousContractKey,
                    contractKey,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (GameNetwork.IsServer &&
                Mission.Current == null &&
                ExactVillageBattleScenarioContract.IsValidatedPreMissionScenario(
                    message.ScenarioContext,
                    message.RuntimeScene,
                    out string villageBattleDiagnostics))
            {
                PendingBattleMissionStartupState.ArmForPreMissionContract(
                    message.RuntimeScene,
                    "pre-mission topology contract " + (source ?? "unknown"));
                contractDiagnostics +=
                    " StartupBarrier=armed-village-pre-open" +
                    " VillageBattle={" + villageBattleDiagnostics + "}";
            }

            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(message);
                GameNetwork.EndModuleEventAsServer();
                _lastSentContractKeyByPeerIndex[peer.Index] = contractKey;
                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: sent pre-mission contract. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " Source=" + (source ?? "unknown") +
                    " Contract={" + contractDiagnostics + "}.");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: pre-mission contract send failed. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " Source=" + (source ?? "unknown") +
                    " Exception=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void QueuePendingClientMissionLoad(
            object baseNetworkComponent,
            string gameType,
            string map,
            int battleIndex,
            bool requiresLobbyState,
            DateTime notBeforeUtc,
            string source)
        {
            var pending = new PendingClientMissionLoad
            {
                BaseNetworkComponent = baseNetworkComponent,
                GameType = gameType ?? string.Empty,
                Map = map ?? string.Empty,
                BattleIndex = battleIndex,
                RequiresLobbyState = requiresLobbyState,
                QueuedUtc = DateTime.UtcNow,
                NotBeforeUtc = notBeforeUtc,
                Source = source ?? "unknown"
            };

            lock (_pendingLoadSync)
            {
                _pendingClientMissionLoad = pending;
            }
        }

        private void TryStartPendingClientMissionLoad()
        {
            PendingClientMissionLoad pending;
            lock (_pendingLoadSync)
            {
                pending = _pendingClientMissionLoad;
            }

            if (pending == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - pending.QueuedUtc >
                TimeSpan.FromSeconds(PendingLoadTimeoutSeconds))
            {
                ClearPendingClientMissionLoad("pre-mission-contract-timeout");
                AbortPendingClientMissionLoad(
                    pending,
                    "matching pre-mission topology contract did not arrive within " +
                    PendingLoadTimeoutSeconds.ToString("0") + " seconds");
                return;
            }

            if (nowUtc < pending.NotBeforeUtc)
                return;

            if (!CoopPreMissionTopologyRuntimeState.TryActivateForMissionLoad(
                    pending.Map,
                    pending.BattleIndex,
                    out _,
                    out string activationDiagnostics))
            {
                return;
            }

            if (GameStateManager.Current?.ActiveState is MissionState)
                return;

            if (pending.RequiresLobbyState &&
                !IsSupportedClientLobbyState(
                    GameStateManager.Current?.ActiveState))
            {
                return;
            }

            lock (_pendingLoadSync)
            {
                if (!ReferenceEquals(
                        _pendingClientMissionLoad,
                        pending))
                {
                    return;
                }

                _pendingClientMissionLoad = null;
            }

            try
            {
                ResetBaseNetworkClientLoadState(
                    pending.BaseNetworkComponent);
                if (GameNetwork.MyPeer != null)
                    GameNetwork.MyPeer.IsSynchronized = false;

                BaseNetworkComponentData data =
                    GameNetwork.GetNetworkComponent<BaseNetworkComponentData>();
                data?.UpdateCurrentBattleIndex(pending.BattleIndex);
                bool started =
                    TaleWorlds.MountAndBlade.Module.CurrentModule != null &&
                    TaleWorlds.MountAndBlade.Module.CurrentModule.StartMultiplayerGame(
                        pending.GameType,
                        pending.Map);
                if (!started)
                {
                    AbortPendingClientMissionLoad(
                        pending,
                        "Module.StartMultiplayerGame returned false");
                    return;
                }

                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: started deferred client mission after contract activation. " +
                    "Source=" + pending.Source +
                    " GameType=" + pending.GameType +
                    " Map=" + pending.Map +
                    " BattleIndex=" + pending.BattleIndex +
                    " Contract={" + activationDiagnostics + "}.");
            }
            catch (Exception ex)
            {
                AbortPendingClientMissionLoad(
                    pending,
                    ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void ClearPendingClientMissionLoad(string source)
        {
            PendingClientMissionLoad previous;
            lock (_pendingLoadSync)
            {
                previous = _pendingClientMissionLoad;
                _pendingClientMissionLoad = null;
            }

            if (previous != null)
            {
                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: cleared pending client mission load. " +
                    "Source=" + (source ?? "unknown") +
                    " Map=" + previous.Map +
                    " BattleIndex=" + previous.BattleIndex + ".");
            }
        }

        private static bool IsSupportedClientLobbyState(object activeState)
        {
            string typeName = activeState?.GetType().Name ?? string.Empty;
            return string.Equals(
                       typeName,
                       "LobbyGameStateCustomGameClient",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       typeName,
                       "LobbyGameStateCommunityClient",
                       StringComparison.Ordinal);
        }

        private static void ResetBaseNetworkClientLoadState(
            object baseNetworkComponent)
        {
            if (baseNetworkComponent == null)
                return;

            try
            {
                Type type = baseNetworkComponent.GetType();
                SetPropertyValue(
                    type,
                    baseNetworkComponent,
                    "CurrentIntermissionTimer",
                    0f);

                PropertyInfo stateProperty = type.GetProperty(
                    "ClientIntermissionState",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                if (stateProperty?.CanWrite == true)
                {
                    object defaultState =
                        Activator.CreateInstance(stateProperty.PropertyType);
                    stateProperty.SetValue(
                        baseNetworkComponent,
                        defaultState,
                        null);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: failed to reset BaseNetworkComponent client load state. " +
                    "Exception=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private static void SetPropertyValue(
            Type type,
            object instance,
            string propertyName,
            object value)
        {
            PropertyInfo property = type?.GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
            if (property?.CanWrite == true)
                property.SetValue(instance, value, null);
        }

        private static void AbortPendingClientMissionLoad(
            PendingClientMissionLoad pending,
            string reason)
        {
            string message =
                "Coop pre-mission topology validation failed. " +
                "The mission was not opened to avoid an unsafe scene mismatch. " +
                "Map=" + (pending?.Map ?? string.Empty) +
                " BattleIndex=" + (pending?.BattleIndex ?? -1) +
                " Reason=" + (reason ?? "unknown");
            ModLogger.Info(
                "CoopPreMissionTopologyNetworkComponent: " + message);

            try
            {
                LoadingWindow.DisableGlobalLoadingWindow();
            }
            catch
            {
            }

            try
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(message));
            }
            catch
            {
            }

            try
            {
                BannerlordNetwork.EndMultiplayerLobbyMission();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopPreMissionTopologyNetworkComponent: controlled mission abort failed. " +
                    "Exception=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private sealed class PendingClientMissionLoad
        {
            public object BaseNetworkComponent;
            public string GameType;
            public string Map;
            public int BattleIndex;
            public bool RequiresLobbyState;
            public DateTime QueuedUtc;
            public DateTime NotBeforeUtc;
            public string Source;
        }
    }
}
