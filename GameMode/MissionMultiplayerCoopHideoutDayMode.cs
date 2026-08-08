using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
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

            var scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext;
            if (!CoopHideoutBossPhaseContract.IsHideoutScenario(scenarioContext?.ScenarioKind))
            {
                ModLogger.Info(
                    "CoopHideoutDay: rejected mission start because the active battle snapshot is not a hideout scenario. " +
                    "Scene=" + normalizedScene +
                    " ScenarioKind=" + (scenarioContext?.ScenarioKind ?? "missing") + ".");
                return;
            }

            ModLogger.Info(
                "CoopHideoutDay: opening isolated day hideout mission. " +
                "Scene=" + normalizedScene +
                " Shell=" + BattleMissionShell + ".");
            MissionInitializerRecord record = new MissionInitializerRecord(normalizedScene);
            MissionState.OpenNew(BattleMissionShell, record, CreateBehaviorsForMission);
        }

        private static IEnumerable<MissionBehavior> CreateBehaviorsForMission(Mission mission)
        {
            List<MissionBehavior> list =
                MissionMultiplayerCoopBattleMode
                    .CreateBehaviorsForOfficialOpenNewBridge(mission)
                    .Where(behavior => behavior != null)
                    .ToList();

            if (!list.Any(behavior => behavior is CoopHideoutBossPhaseController))
                list.Add(new CoopHideoutBossPhaseController());

#if !COOPSPECTATOR_DEDICATED
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
