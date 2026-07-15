using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure.LordsHall;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Objects;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers.Logic;

namespace CoopSpectator.MissionBehaviors.LordsHall
{
    internal static class LordsHallMissionRuntime
    {
        public static bool TryPrepare(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!LordsHallScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    mission.SceneName,
                    out string scenarioDiagnostics))
            {
                diagnostics = "scenario={" + scenarioDiagnostics + "}";
                return false;
            }

            List<FightAreaMarker> markers = mission.ActiveMissionObjects?
                .FindAllWithType<FightAreaMarker>()
                .Where(marker => marker != null)
                .ToList() ?? new List<FightAreaMarker>();
            if (markers.Count == 0)
            {
                diagnostics = "fight-area-markers-empty";
                return false;
            }

            var archerPoints = new HashSet<GameEntity>();
            var infantryPoints = new HashSet<GameEntity>();
            try
            {
                foreach (FightAreaMarker marker in markers)
                {
                    foreach (GameEntity entity in marker.GetGameEntitiesWithTagInRange("defender_archer") ?? Enumerable.Empty<GameEntity>())
                        archerPoints.Add(entity);
                    foreach (GameEntity entity in marker.GetGameEntitiesWithTagInRange("defender_infantry") ?? Enumerable.Empty<GameEntity>())
                        infantryPoints.Add(entity);
                }
            }
            catch (Exception ex)
            {
                diagnostics = "marker-enumeration-exception " + ex.GetType().Name + ":" + ex.Message;
                return false;
            }

            int archerPointCount = archerPoints.Count;
            int infantryPointCount = infantryPoints.Count;
            int requiredDefenderPoints = Math.Max(
                1,
                scenarioContext?.SiegeContext?.LordsHallMaxDefenderSideTroopCount ?? 27);
            if (archerPointCount == 0 ||
                infantryPointCount == 0 ||
                archerPoints.Union(infantryPoints).Count() < requiredDefenderPoints)
            {
                diagnostics =
                    "defender-points-incomplete Markers=" + markers.Count +
                    " ArcherPoints=" + archerPointCount +
                    " InfantryPoints=" + infantryPointCount +
                    " RequiredPoints=" + requiredDefenderPoints;
                return false;
            }

            AmmoSupplyLogic ammoSupplyLogic = mission.GetMissionBehavior<AmmoSupplyLogic>();
            bool ammoSupplyCreated = false;
            if (ammoSupplyLogic == null)
            {
                try
                {
                    ammoSupplyLogic = new AmmoSupplyLogic(new List<BattleSideEnum>
                    {
                        BattleSideEnum.Defender
                    });
                    mission.AddMissionBehavior(ammoSupplyLogic);
                    ammoSupplyLogic.OnBehaviorInitialize();
                    ammoSupplyLogic.AfterStart();
                    ammoSupplyCreated = true;
                }
                catch (Exception ex)
                {
                    diagnostics = "ammo-supply-create-exception " + ex.GetType().Name + ":" + ex.Message;
                    return false;
                }
            }

            mission.DoesMissionRequireCivilianEquipment = false;
            diagnostics =
                "prepared Scenario={" + scenarioDiagnostics + "}" +
                " Markers=" + markers.Count +
                " ArcherPoints=" + archerPointCount +
                " InfantryPoints=" + infantryPointCount +
                " AmmoSupplyCreated=" + ammoSupplyCreated;
            return true;
        }
    }
}
