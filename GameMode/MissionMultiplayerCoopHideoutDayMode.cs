using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Campaign;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.MissionLogics;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopHideoutDayMode : MissionBasedMultiplayerGameMode
    {
        private const string BattleMissionShell = "MultiplayerBattle";
        public const string GameModeId = CoopHideoutBossPhaseContract.GameModeId;

        public MissionMultiplayerCoopHideoutDayMode(string name) : base(name)
        {
        }

        public override void StartMultiplayerGame(string scene)
        {
            if (!CoopHideoutBossPhaseContract.TryNormalizeDayHideoutSceneName(
                    scene,
                    out string normalizedScene))
            {
                ModLogger.Info(
                    "CoopHideoutDay: rejected mission start because the requested scene is not a supported hideout scene. " +
                    "Scene=" + (scene ?? string.Empty) + ".");
                return;
            }

            BattleScenarioContextMessage scenarioContext = ResolvePreOpenScenarioContext(
                normalizedScene,
                out string scenarioSource);
            if (!CoopHideoutBossPhaseContract.IsHideoutScenario(scenarioContext?.ScenarioKind))
            {
                ModLogger.Info(
                    "CoopHideoutDay: rejected mission start because the active battle snapshot is not a hideout scenario. " +
                    "Scene=" + normalizedScene +
                    " ScenarioKind=" + (scenarioContext?.ScenarioKind ?? "missing") +
                    " ScenarioSource=" + scenarioSource + ".");
                return;
            }

            ModLogger.Info(
                "CoopHideoutDay: opening isolated day hideout mission. " +
                "Scene=" + normalizedScene +
                " ScenarioSource=" + scenarioSource +
                " Shell=" + BattleMissionShell + ".");
            MissionInitializerRecord record = new MissionInitializerRecord(normalizedScene);
            MissionState.OpenNew(BattleMissionShell, record, CreateBehaviorsForMission);
        }

        private static BattleScenarioContextMessage ResolvePreOpenScenarioContext(
            string normalizedScene,
            out string source)
        {
            if (GameNetwork.IsServer)
            {
                BattleSnapshotMessage snapshot = BattleRosterFileHelper.PeekSnapshot();
                string snapshotScene = !string.IsNullOrWhiteSpace(snapshot?.MultiplayerScene)
                    ? snapshot.MultiplayerScene
                    : snapshot?.MapScene;
                if (!CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                        normalizedScene,
                        snapshotScene,
                        snapshot?.ScenarioContext?.ScenarioKind))
                {
                    source =
                        "server-battle-roster-rejected:" +
                        "Scene=" + (snapshotScene ?? "missing") +
                        ",ScenarioKind=" + (snapshot?.ScenarioContext?.ScenarioKind ?? "missing");
                    return null;
                }

                BattleSnapshotRuntimeState.SetCurrent(
                    snapshot,
                    "CoopHideoutDay server pre-open battle roster");
                source = "server-battle-roster";
                return snapshot.ScenarioContext;
            }

            if (GameNetwork.IsClientOrReplay)
            {
                if (!CoopPreMissionTopologyRuntimeState.TryGetActive(
                        normalizedScene,
                        out CoopPreMissionTopologyContract contract,
                        out string topologyDiagnostics))
                {
                    source = "client-pre-mission-topology-rejected:" + topologyDiagnostics;
                    return null;
                }

                if (!CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                        normalizedScene,
                        contract.RuntimeScene,
                        contract.ScenarioContext?.ScenarioKind))
                {
                    source =
                        "client-pre-mission-topology-contract-rejected:" +
                        "Scene=" + (contract.RuntimeScene ?? "missing") +
                        ",ScenarioKind=" + (contract.ScenarioContext?.ScenarioKind ?? "missing");
                    return null;
                }

                source = "client-active-pre-mission-topology";
                return contract.ScenarioContext;
            }

            BattleSnapshotMessage current = BattleSnapshotRuntimeState.GetCurrent();
            string currentScene = !string.IsNullOrWhiteSpace(current?.MultiplayerScene)
                ? current.MultiplayerScene
                : current?.MapScene;
            if (CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                    normalizedScene,
                    currentScene,
                    current?.ScenarioContext?.ScenarioKind))
            {
                source = "existing-runtime-snapshot";
                return current.ScenarioContext;
            }

            source = "matching-pre-open-contract-missing";
            return null;
        }

        private static IEnumerable<MissionBehavior> CreateBehaviorsForMission(Mission mission)
        {
            List<MissionBehavior> list =
                MissionMultiplayerCoopBattleMode
                    .CreateBehaviorsForOfficialOpenNewBridge(mission)
                    .Where(behavior => behavior != null)
                    .ToList();

            if (GameNetwork.IsServer &&
                GameNetwork.IsDedicatedServer &&
                !list.Any(behavior => behavior is MissionMultiplayerGameModeBaseClient))
            {
                list.Insert(0, new MissionMultiplayerCoopBattleClient());
                ModLogger.Info(
                    "CoopHideoutDay: inserted dedicated MissionMultiplayerGameModeBaseClient bridge before MissionCustomGameServerComponent for MissionScoreboardComponent lifecycle compatibility.");
            }

            if (!list.Any(behavior => behavior is BattleMissionStarterLogic))
                list.Add(new BattleMissionStarterLogic());

            if (GameNetwork.IsServer &&
                !list.Any(behavior => behavior is AgentHumanAILogic))
            {
                list.Add(new AgentHumanAILogic());
                ModLogger.Info(
                    "CoopHideoutDay: inserted the current AgentHumanAILogic into the isolated server behavior stack.");
            }

            if (GameNetwork.IsServer &&
                !list.Any(behavior => behavior is AgentHumanAILogic))
            {
                throw new InvalidOperationException(
                    "CoopHideoutDay requires AgentHumanAILogic before agent materialization.");
            }

            if (GameNetwork.IsServer &&
                !list.Any(behavior => behavior is CoopHideoutStealthPatrolController))
            {
                list.Add(new CoopHideoutStealthPatrolController());
                ModLogger.Info(
                    "CoopHideoutDay: inserted the isolated hideout stealth and patrol controller into the server behavior stack.");
            }

            if (!list.Any(behavior => behavior is CoopHideoutBossPhaseController))
                list.Add(new CoopHideoutBossPhaseController());

#if !COOPSPECTATOR_DEDICATED
            if (!GameNetwork.IsServer &&
                !list.Any(behavior => behavior is MissionObjectiveLogic))
            {
                list.Add(new MissionObjectiveLogic());
            }

            if (!GameNetwork.IsServer &&
                !list.Any(behavior => behavior is CoopSpectator.UI.CoopHideoutMissionObjectiveController))
            {
                list.Add(new CoopSpectator.UI.CoopHideoutMissionObjectiveController(
                    isNight: false));
            }

            if (!GameNetwork.IsServer &&
                !list.Any(behavior =>
                    behavior is TaleWorlds.MountAndBlade.View.MissionViews.MissionObjectiveView))
            {
                MissionBehavior objectiveView =
                    TaleWorlds.MountAndBlade.View.ViewCreator.CreateMissionObjectiveView();
                if (objectiveView != null)
                    list.Add(objectiveView);
            }

            if (!GameNetwork.IsServer &&
                !list.Any(behavior => behavior is CoopSpectator.UI.CoopHideoutBossCinematicView))
            {
                list.Add(new CoopSpectator.UI.CoopHideoutBossCinematicView());
            }
#endif

            ModLogger.Info(
                "CoopHideoutDay: isolated behavior stack prepared. " +
                "IsServer=" + GameNetwork.IsServer +
                " Count=" + list.Count + ".");
            return list;
        }
    }
}
