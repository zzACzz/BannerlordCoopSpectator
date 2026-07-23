using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign.SiegeAssault
{
    internal static class ExactSiegeAssaultCampaignBattleAdapter
    {
        private const string ResultStage = "SiegeAssault";

        public static bool IsCampaignResultCandidate(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            return battle?.IsSiegeAssault == true &&
                   string.Equals(
                       result?.BattleStage,
                       ResultStage,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryApplyMissionSiegeEngineResult(
            MapEvent battle,
            BattleSnapshotMessage snapshot,
            Mission mission,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string diagnostics)
        {
            diagnostics = "not-exact-siege-assault";
            if (!TryValidateCampaignResult(
                    battle,
                    snapshot,
                    mission,
                    result,
                    out string validationDiagnostics))
            {
                diagnostics = validationDiagnostics;
                return false;
            }

            MissionSiegeEnginesLogic siegeEnginesLogic =
                mission.GetMissionBehavior<MissionSiegeEnginesLogic>();
            if (siegeEnginesLogic == null)
            {
                diagnostics = "mission-siege-engines-logic-missing";
                return false;
            }

            try
            {
                siegeEnginesLogic.GetMissionSiegeWeapons(
                    out IEnumerable<IMissionSiegeWeapon> defenderWeapons,
                    out IEnumerable<IMissionSiegeWeapon> attackerWeapons);

                if (!TryBuildApplications(
                        BattleSideEnum.Attacker,
                        attackerWeapons,
                        result.AttackerSiegeEngines,
                        out List<EngineHealthApplication> attackerApplications,
                        out string attackerDiagnostics))
                {
                    diagnostics =
                        "attacker-engine-contract-invalid {" +
                        attackerDiagnostics + "}";
                    return false;
                }

                if (!TryBuildApplications(
                        BattleSideEnum.Defender,
                        defenderWeapons,
                        result.DefenderSiegeEngines,
                        out List<EngineHealthApplication> defenderApplications,
                        out string defenderDiagnostics))
                {
                    diagnostics =
                        "defender-engine-contract-invalid {" +
                        defenderDiagnostics + "}";
                    return false;
                }

                foreach (EngineHealthApplication application in
                         attackerApplications.Concat(defenderApplications))
                {
                    application.Weapon.SetHealth(application.Health);
                }

                diagnostics =
                    "applied-exact-siege-assault-engine-state" +
                    " Attacker={" + attackerDiagnostics + "}" +
                    " Defender={" + defenderDiagnostics + "}" +
                    " Scenario={" + validationDiagnostics + "}";
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "engine-state-apply-faulted " +
                    ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static bool TryValidateCampaignResult(
            MapEvent battle,
            BattleSnapshotMessage snapshot,
            Mission mission,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string diagnostics)
        {
            diagnostics = "campaign-battle-invalid";
            if (battle?.IsSiegeAssault != true ||
                !battle.IsPlayerMapEvent ||
                battle.PlayerSide == BattleSideEnum.None)
            {
                return false;
            }

            Settlement settlement = battle.MapEventSettlement;
            if (settlement?.IsFortification != true ||
                settlement.SiegeEvent == null ||
                settlement.CurrentSiegeState ==
                    Settlement.SiegeState.InTheLordsHall)
            {
                diagnostics =
                    "campaign-stage-invalid Settlement=" +
                    (settlement?.StringId ?? "null") +
                    " IsFortification=" +
                    (settlement?.IsFortification ?? false) +
                    " HasSiegeEvent=" +
                    (settlement?.SiegeEvent != null) +
                    " SiegeState=" +
                    (settlement?.CurrentSiegeState.ToString() ?? "null");
                return false;
            }

            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                snapshot?.ScenarioContext;
            bool isWithDeploymentScenario =
                ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .IsSiegeAssaultScenario(scenarioContext);
            bool isNoDeploymentScenario =
                ExactCampaignSiegeAssaultNoDeploymentRuntime
                    .IsSiegeAssaultScenario(scenarioContext);
            if (!isWithDeploymentScenario && !isNoDeploymentScenario)
            {
                diagnostics = "scenario-not-exact-siege-assault";
                return false;
            }

            if (!string.Equals(
                    scenarioContext.CampaignBattleType,
                    battle.EventType.ToString(),
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "campaign-type-mismatch Snapshot=" +
                    (scenarioContext.CampaignBattleType ?? string.Empty) +
                    " Live=" + battle.EventType;
                return false;
            }

            if (!string.Equals(
                    scenarioContext.SiegeContext?.SettlementId,
                    settlement.StringId,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "settlement-mismatch Snapshot=" +
                    (scenarioContext.SiegeContext?.SettlementId ??
                     string.Empty) +
                    " Live=" + settlement.StringId;
                return false;
            }

            string runtimeScene = mission.SceneName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                !SceneMatches(runtimeScene, result?.MapScene) ||
                !SceneMatchesSnapshot(runtimeScene, snapshot))
            {
                diagnostics =
                    "scene-mismatch Runtime=" + runtimeScene +
                    " Result=" + (result?.MapScene ?? string.Empty) +
                    " Multiplayer=" +
                    (snapshot?.MultiplayerScene ?? string.Empty) +
                    " Campaign=" + (snapshot?.MapScene ?? string.Empty);
                return false;
            }

            if (!string.Equals(
                    result?.BattleStage,
                    ResultStage,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "result-stage-mismatch Stage=" +
                    (result?.BattleStage ?? string.Empty);
                return false;
            }

            if (!IsResolvedWinner(result.WinnerSide))
            {
                diagnostics =
                    "winner-unresolved Value=" +
                    (result.WinnerSide ?? string.Empty);
                return false;
            }

            if (result.DefenderPushedBack)
            {
                if (result.IsFinalStage ||
                    !string.Equals(
                        result.WinnerSide,
                        BattleSideEnum.Attacker.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics =
                        "pushed-back-result-inconsistent" +
                        " Winner=" + result.WinnerSide +
                        " IsFinalStage=" + result.IsFinalStage;
                    return false;
                }
            }
            else if (!result.IsFinalStage)
            {
                diagnostics = "non-pushed-back-result-not-final";
                return false;
            }

            if (result.AttackerSiegeEngines == null ||
                result.DefenderSiegeEngines == null)
            {
                diagnostics = "result-engine-lists-null";
                return false;
            }

            diagnostics =
                "validated" +
                " Settlement=" + settlement.StringId +
                " Scene=" + runtimeScene +
                " MissionShell=" +
                (isWithDeploymentScenario
                    ? "WithDeployment"
                    : "NoDeployment") +
                " Winner=" + result.WinnerSide +
                " DefenderPushedBack=" + result.DefenderPushedBack +
                " IsFinalStage=" + result.IsFinalStage;
            return true;
        }

        private static bool TryBuildApplications(
            BattleSideEnum side,
            IEnumerable<IMissionSiegeWeapon> liveSource,
            IEnumerable<BattleSiegeEngineSnapshotMessage> resultSource,
            out List<EngineHealthApplication> applications,
            out string diagnostics)
        {
            applications = new List<EngineHealthApplication>();
            List<MissionSiegeWeapon> liveWeapons =
                liveSource?
                    .OfType<MissionSiegeWeapon>()
                    .OrderBy(weapon => weapon.Index)
                    .ToList() ??
                new List<MissionSiegeWeapon>();
            List<BattleSiegeEngineSnapshotMessage> resultWeapons =
                resultSource?
                    .Where(weapon => weapon != null)
                    .OrderBy(weapon => weapon.Index)
                    .ToList() ??
                new List<BattleSiegeEngineSnapshotMessage>();

            if (liveWeapons.Count != resultWeapons.Count)
            {
                diagnostics =
                    "count-mismatch" +
                    " Side=" + side +
                    " Live=" + liveWeapons.Count +
                    " Result=" + resultWeapons.Count;
                return false;
            }

            for (int i = 0; i < liveWeapons.Count; i++)
            {
                MissionSiegeWeapon liveWeapon = liveWeapons[i];
                BattleSiegeEngineSnapshotMessage resultWeapon =
                    resultWeapons[i];
                string liveTypeId =
                    !string.IsNullOrWhiteSpace(liveWeapon.Type?.StringId)
                        ? liveWeapon.Type.StringId
                        : liveWeapon.Type?.ToString() ?? string.Empty;
                if (liveWeapon.Index != resultWeapon.Index ||
                    !string.Equals(
                        liveTypeId,
                        resultWeapon.EngineTypeId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics =
                        "identity-mismatch" +
                        " Side=" + side +
                        " Position=" + i +
                        " Live=" + liveTypeId + "#" + liveWeapon.Index +
                        " Result=" +
                        (resultWeapon.EngineTypeId ?? string.Empty) +
                        "#" + resultWeapon.Index;
                    return false;
                }

                float liveMaxHealth = liveWeapon.MaxHealth;
                float resultMaxHealth = resultWeapon.MaxHealth;
                if (!IsFinitePositive(liveMaxHealth) ||
                    !IsFinitePositive(resultMaxHealth))
                {
                    diagnostics =
                        "max-health-invalid" +
                        " Side=" + side +
                        " Engine=" + liveTypeId + "#" + liveWeapon.Index +
                        " Live=" + liveMaxHealth +
                        " Result=" + resultMaxHealth;
                    return false;
                }

                float maxHealthTolerance =
                    Math.Max(1f, liveMaxHealth * 0.001f);
                if (Math.Abs(liveMaxHealth - resultMaxHealth) >
                    maxHealthTolerance)
                {
                    diagnostics =
                        "max-health-mismatch" +
                        " Side=" + side +
                        " Engine=" + liveTypeId + "#" + liveWeapon.Index +
                        " Live=" + liveMaxHealth +
                        " Result=" + resultMaxHealth +
                        " Tolerance=" + maxHealthTolerance;
                    return false;
                }

                if (!IsFinite(resultWeapon.Health))
                {
                    diagnostics =
                        "health-invalid" +
                        " Side=" + side +
                        " Engine=" + liveTypeId + "#" + liveWeapon.Index +
                        " Value=" + resultWeapon.Health;
                    return false;
                }

                applications.Add(new EngineHealthApplication
                {
                    Weapon = liveWeapon,
                    Health = Math.Max(
                        0f,
                        Math.Min(liveMaxHealth, resultWeapon.Health))
                });
            }

            diagnostics =
                "validated" +
                " Side=" + side +
                " Count=" + applications.Count;
            return true;
        }

        private static bool SceneMatches(
            string runtimeScene,
            string candidate)
        {
            return !string.IsNullOrWhiteSpace(runtimeScene) &&
                   !string.IsNullOrWhiteSpace(candidate) &&
                   string.Equals(
                       runtimeScene,
                       candidate,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool SceneMatchesSnapshot(
            string runtimeScene,
            BattleSnapshotMessage snapshot)
        {
            return SceneMatches(runtimeScene, snapshot?.MultiplayerScene) ||
                   SceneMatches(runtimeScene, snapshot?.MapScene);
        }

        private static bool IsResolvedWinner(string winnerSide)
        {
            return string.Equals(
                       winnerSide,
                       BattleSideEnum.Attacker.ToString(),
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       winnerSide,
                       BattleSideEnum.Defender.ToString(),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private sealed class EngineHealthApplication
        {
            public MissionSiegeWeapon Weapon { get; set; }

            public float Health { get; set; }
        }
    }
}
