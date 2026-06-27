using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions;
using TaleWorlds.MountAndBlade.Missions.Handlers;
using TaleWorlds.MountAndBlade.Objects.Siege;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopSiegeMachineDeploymentController
    {
        private static readonly FieldInfo DeploymentPointWeaponsField =
            typeof(DeploymentPoint).GetField("_weapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DeploymentPointDeployedWeaponField =
            typeof(DeploymentPoint).GetField("<DeployedWeapon>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DeploymentPointDisbandedWeaponField =
            typeof(DeploymentPoint).GetField("<DisbandedWeapon>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo DeploymentPointDetermineTypeMethod =
            typeof(DeploymentPoint).GetMethod("DetermineDeploymentPointType", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo SiegeWeaponOnDeploymentStateChangedMethod =
            typeof(SiegeWeapon).GetMethod("OnDeploymentStateChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo SiegeControllerWeaponsField =
            typeof(MissionSiegeWeaponsController).GetField("_weapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SiegeControllerUndeployedWeaponsField =
            typeof(MissionSiegeWeaponsController).GetField("_undeployedWeapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SiegeControllerDeployedWeaponsField =
            typeof(MissionSiegeWeaponsController).GetField("_deployedWeapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SiegeWeaponRemoveOnDeployEntitiesField =
            typeof(SiegeWeapon).GetField("_removeOnDeployEntities", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SiegeWeaponAddOnDeployEntitiesField =
            typeof(SiegeWeapon).GetField("_addOnDeployEntities", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SiegeWeaponRemoveOnDeployTagField =
            typeof(SiegeWeapon).GetField("RemoveOnDeployTag", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SiegeWeaponAddOnDeployTagField =
            typeof(SiegeWeapon).GetField("AddOnDeployTag", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RangedSiegeWeaponSkeletonOwnerObjectsField =
            typeof(RangedSiegeWeapon).GetField("SkeletonOwnerObjects", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool TryApplySelection(
            Mission mission,
            Team team,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            DeploymentPoint deploymentPointToDisbandForMove,
            bool clearSelection,
            SiegeDeploymentHandler siegeDeploymentHandler,
            bool verboseDiagnostics,
            out string diagnostics)
        {
            var details = new List<string>();
            diagnostics = string.Empty;
            if (mission == null || team == null || deploymentPoint == null)
            {
                diagnostics =
                    "InvalidContext=True Mission=" + (mission == null ? "<null>" : mission.SceneName) +
                    " Team=" + FormatTeam(team) +
                    " DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint);
                return false;
            }

            if (team.Side == BattleSideEnum.None || deploymentPoint.Side != team.Side)
            {
                diagnostics =
                    "SideMismatch=True Team=" + FormatTeam(team) +
                    " DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint);
                return false;
            }

            if (!clearSelection && siegeWeapon == null)
            {
                diagnostics = "SiegeWeapon=<null>";
                return false;
            }

            try
            {
                if (deploymentPointToDisbandForMove != null &&
                    !ReferenceEquals(deploymentPointToDisbandForMove, deploymentPoint) &&
                    deploymentPointToDisbandForMove.IsDeployed)
                {
                    details.Add("MoveSourceDisband={" + ControlledDisband(
                        deploymentPointToDisbandForMove,
                        team,
                        keepDisabledPointHidden: true,
                        verboseDiagnostics: verboseDiagnostics) + "}");
                }

                if (deploymentPoint.IsDeployed &&
                    (clearSelection || !ReferenceEquals(deploymentPoint.DeployedWeapon, siegeWeapon)))
                {
                    details.Add("TargetDisband={" + ControlledDisband(
                        deploymentPoint,
                        team,
                        keepDisabledPointHidden: false,
                        verboseDiagnostics: verboseDiagnostics) + "}");
                }

                if (!clearSelection &&
                    siegeWeapon != null &&
                    !ReferenceEquals(deploymentPoint.DeployedWeapon, siegeWeapon))
                {
                    details.Add("Deploy={" + ControlledDeploy(
                        mission,
                        team,
                        deploymentPoint,
                        siegeWeapon,
                        siegeDeploymentHandler,
                        verboseDiagnostics) + "}");
                }

                details.Add("ForceUpdate={" + ForceUpdateTeamUnits(team) + "}");
                diagnostics =
                    "Applied=True ClearSelection=" + clearSelection +
                    " DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                    " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon) +
                    " Details=[" + string.Join(" ", details.ToArray()) + "]";
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Exception=" + ex.GetType().Name + ":" + ex.Message +
                    " Details=[" + string.Join(" ", details.ToArray()) + "]";
                return false;
            }
        }

        public static bool TryAutoDeploySide(
            Mission mission,
            Team team,
            SiegeDeploymentHandler siegeDeploymentHandler,
            bool treatSideAsPlayerSide,
            bool verboseDiagnostics,
            out string diagnostics)
        {
            diagnostics = "invalid-context";
            if (mission == null || team == null || team.Side == BattleSideEnum.None)
                return false;

            MissionSiegeEnginesLogic siegeEnginesLogic = mission.GetMissionBehavior<MissionSiegeEnginesLogic>();
            IMissionSiegeWeaponsController weaponsController = siegeEnginesLogic?.GetSiegeWeaponsController(team.Side);
            if (weaponsController == null)
            {
                diagnostics =
                    "WeaponsController=<null>" +
                    " Team=" + FormatTeam(team) +
                    " HasSiegeEnginesLogic=" + (siegeEnginesLogic != null);
                return false;
            }

            List<DeploymentPoint> deploymentPoints = CollectAutoDeployDeploymentPoints(
                mission,
                siegeDeploymentHandler,
                team.Side,
                out string deploymentPointSourceDiagnostics);
            string ghostDeploymentDiagnostics = NormalizeAutoDeployGhostDeployments(
                team,
                deploymentPoints,
                weaponsController,
                verboseDiagnostics);
            string reaffirmDeploymentDiagnostics = ReaffirmExistingAutoDeployDeployments(
                mission,
                team,
                deploymentPoints,
                weaponsController,
                siegeDeploymentHandler,
                verboseDiagnostics);
            List<DeploymentPoint> candidateDeploymentPoints = CollectAutoDeployCandidatePoints(
                mission,
                team,
                deploymentPoints,
                treatSideAsPlayerSide,
                weaponsController);
            string candidatePointDiagnostics = BuildAutoDeployCandidatePointDiagnostics(
                team,
                deploymentPoints,
                candidateDeploymentPoints,
                weaponsController);
            int expectedRemainingBefore = CountAutoDeployableRemainingWeaponCount(
                deploymentPoints,
                candidateDeploymentPoints,
                weaponsController);
            int deployedBefore = CountAutoDeployRelevantDeployedWeaponCount(
                deploymentPoints,
                weaponsController);
            if (deploymentPoints.Count <= 0)
            {
                diagnostics =
                    "DeploymentPoints=0" +
                    " Team=" + FormatTeam(team) +
                    " PointSources={" + deploymentPointSourceDiagnostics + "}" +
                    " GhostNormalization={" + ghostDeploymentDiagnostics + "}" +
                    " ReaffirmExisting={" + reaffirmDeploymentDiagnostics + "}" +
                    " Candidates=" + candidateDeploymentPoints.Count +
                    " CandidateDetails={" + candidatePointDiagnostics + "}" +
                    " DeployedBefore=" + deployedBefore +
                    " ExpectedRemainingBefore=" + expectedRemainingBefore;
                return expectedRemainingBefore <= 0;
            }

            var details = new List<string>();
            int attemptedCount = 0;
            int appliedCount = 0;
            int failedCount = 0;
            try
            {
                if (team.Side == BattleSideEnum.Attacker)
                {
                    AutoDeployAttackerSiegeWeapons(
                        mission,
                        team,
                        deploymentPoints,
                        weaponsController,
                        siegeDeploymentHandler,
                        treatSideAsPlayerSide,
                        verboseDiagnostics,
                        details,
                        ref attemptedCount,
                        ref appliedCount,
                        ref failedCount);
                }
                else if (team.Side == BattleSideEnum.Defender)
                {
                    AutoDeployDefenderSiegeWeapons(
                        mission,
                        team,
                        deploymentPoints,
                        weaponsController,
                        siegeDeploymentHandler,
                        verboseDiagnostics,
                        details,
                        ref attemptedCount,
                        ref appliedCount,
                        ref failedCount);
                }
                else
                {
                    diagnostics = "UnsupportedSide=" + team.Side;
                    return false;
                }
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Exception=" + ex.GetType().Name + ":" + ex.Message +
                    " Team=" + FormatTeam(team) +
                    " Points=" + deploymentPoints.Count +
                    " PointSources={" + deploymentPointSourceDiagnostics + "}" +
                    " GhostNormalization={" + ghostDeploymentDiagnostics + "}" +
                    " ReaffirmExisting={" + reaffirmDeploymentDiagnostics + "}" +
                    " Attempted=" + attemptedCount +
                    " Applied=" + appliedCount +
                    " Failed=" + failedCount +
                    " Details=[" + string.Join("; ", details.ToArray()) + "]";
                return false;
            }

            int expectedRemainingAfter = CountAutoDeployableRemainingWeaponCount(
                deploymentPoints,
                CollectAutoDeployCandidatePoints(
                    mission,
                    team,
                    deploymentPoints,
                    treatSideAsPlayerSide,
                    weaponsController),
                weaponsController);
            List<DeploymentPoint> candidateDeploymentPointsAfter = CollectAutoDeployCandidatePoints(
                mission,
                team,
                deploymentPoints,
                treatSideAsPlayerSide,
                weaponsController);
            string candidatePointDiagnosticsAfter = BuildAutoDeployCandidatePointDiagnostics(
                team,
                deploymentPoints,
                candidateDeploymentPointsAfter,
                weaponsController);
            int deployedAfter = CountAutoDeployRelevantDeployedWeaponCount(
                deploymentPoints,
                weaponsController);
            diagnostics =
                "Team=" + FormatTeam(team) +
                " TreatSideAsPlayerSide=" + treatSideAsPlayerSide +
                " Points=" + deploymentPoints.Count +
                " PointSources={" + deploymentPointSourceDiagnostics + "}" +
                " GhostNormalization={" + ghostDeploymentDiagnostics + "}" +
                " ReaffirmExisting={" + reaffirmDeploymentDiagnostics + "}" +
                " Candidates=" + candidateDeploymentPoints.Count +
                " CandidateDetailsBefore={" + candidatePointDiagnostics + "}" +
                " CandidateDetailsAfter={" + candidatePointDiagnosticsAfter + "}" +
                " DeployedBefore=" + deployedBefore +
                " DeployedAfter=" + deployedAfter +
                " ExpectedRemainingBefore=" + expectedRemainingBefore +
                " ExpectedRemainingAfter=" + expectedRemainingAfter +
                " Attempted=" + attemptedCount +
                " Applied=" + appliedCount +
                " Failed=" + failedCount +
                " Details=[" + string.Join("; ", details.ToArray()) + "]";
            return failedCount == 0 && expectedRemainingAfter <= 0;
        }

        private static void AutoDeployAttackerSiegeWeapons(
            Mission mission,
            Team team,
            List<DeploymentPoint> deploymentPoints,
            IMissionSiegeWeaponsController weaponsController,
            SiegeDeploymentHandler siegeDeploymentHandler,
            bool treatSideAsPlayerSide,
            bool verboseDiagnostics,
            List<string> details,
            ref int attemptedCount,
            ref int appliedCount,
            ref int failedCount)
        {
            List<DeploymentPoint> candidates = CollectAutoDeployCandidatePoints(
                mission,
                team,
                deploymentPoints,
                treatSideAsPlayerSide,
                weaponsController);
            ShuffleDeploymentPoints(candidates);

            foreach (DeploymentPoint deploymentPoint in candidates)
            {
                if (CountAutoDeployableRemainingWeaponCount(
                        deploymentPoints,
                        candidates,
                        weaponsController) <= 0)
                {
                    break;
                }

                TryAutoDeployWeaponFromPoint(
                    mission,
                    team,
                    deploymentPoint,
                    deploymentPoints,
                    weaponsController,
                    siegeDeploymentHandler,
                    verboseDiagnostics,
                    details,
                    ref attemptedCount,
                    ref appliedCount,
                    ref failedCount);
            }
        }

        private static void AutoDeployDefenderSiegeWeapons(
            Mission mission,
            Team team,
            List<DeploymentPoint> deploymentPoints,
            IMissionSiegeWeaponsController weaponsController,
            SiegeDeploymentHandler siegeDeploymentHandler,
            bool verboseDiagnostics,
            List<string> details,
            ref int attemptedCount,
            ref int appliedCount,
            ref int failedCount)
        {
            List<DeploymentPoint> candidates = deploymentPoints
                .Where(deploymentPoint => deploymentPoint != null && !deploymentPoint.IsDeployed)
                .ToList();
            List<ICastleKeyPosition> castleKeyPositions = CollectCastleKeyPositions(mission);

            while (candidates.Count > 0)
            {
                DeploymentPoint selectedPoint = SelectDefenderDeploymentPoint(candidates, castleKeyPositions);
                if (selectedPoint == null)
                    selectedPoint = candidates[0];

                bool deployed = TryAutoDeployWeaponFromPoint(
                    mission,
                    team,
                    selectedPoint,
                    deploymentPoints,
                    weaponsController,
                    siegeDeploymentHandler,
                    verboseDiagnostics,
                    details,
                    ref attemptedCount,
                    ref appliedCount,
                    ref failedCount);

                candidates.Remove(selectedPoint);
                if (deployed && castleKeyPositions.Count > 0)
                {
                    try
                    {
                        Threat threat = RangedSiegeWeaponAi.ThreatSeeker.GetMaxThreat(castleKeyPositions);
                        if (threat != null)
                            threat.ThreatValue *= 0.5f;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool TryAutoDeployWeaponFromPoint(
            Mission mission,
            Team team,
            DeploymentPoint deploymentPoint,
            List<DeploymentPoint> allDeploymentPoints,
            IMissionSiegeWeaponsController weaponsController,
            SiegeDeploymentHandler siegeDeploymentHandler,
            bool verboseDiagnostics,
            List<string> details,
            ref int attemptedCount,
            ref int appliedCount,
            ref int failedCount)
        {
            if (deploymentPoint == null)
                return false;

            attemptedCount++;
            string preparationDiagnostics = PrepareDeploymentPointWeaponCache(deploymentPoint);
            Type selectedType = SelectBestDeployableWeaponType(
                deploymentPoint,
                allDeploymentPoints,
                weaponsController);
            if (selectedType == null)
            {
                details?.Add(
                    "Point=" + FormatDeploymentPoint(deploymentPoint) +
                    " SelectedType=<null>" +
                    " Preparation={" + preparationDiagnostics + "}");
                return false;
            }

            SiegeWeapon selectedWeapon = ResolveDeployableWeaponOfType(
                deploymentPoint,
                team.Side,
                selectedType);
            if (selectedWeapon == null)
            {
                failedCount++;
                details?.Add(
                    "Point=" + FormatDeploymentPoint(deploymentPoint) +
                    " SelectedType=" + selectedType.Name +
                    " Weapon=<null>" +
                    " Preparation={" + preparationDiagnostics + "}");
                return false;
            }

            string enableDiagnostics = PrepareAutoDeployDeploymentPointAndWeapon(
                deploymentPoint,
                selectedWeapon);
            bool applied = TryApplySelection(
                mission,
                team,
                deploymentPoint,
                selectedWeapon,
                null,
                clearSelection: false,
                siegeDeploymentHandler,
                verboseDiagnostics,
                out string applyDiagnostics);
            if (applied)
                appliedCount++;
            else
                failedCount++;

            details?.Add(
                "Point=" + FormatDeploymentPoint(deploymentPoint) +
                " SelectedType=" + selectedType.Name +
                " Weapon=" + FormatSiegeWeapon(selectedWeapon) +
                " Applied=" + applied +
                " Preparation={" + preparationDiagnostics + "}" +
                " Enable={" + enableDiagnostics + "}" +
                " Apply={" + applyDiagnostics + "}");
            return applied;
        }

        private static Type SelectBestDeployableWeaponType(
            DeploymentPoint deploymentPoint,
            List<DeploymentPoint> allDeploymentPoints,
            IMissionSiegeWeaponsController weaponsController)
        {
            if (deploymentPoint == null || weaponsController == null)
                return null;

            Type selectedType = null;
            float selectedValue = float.MinValue;
            foreach (Type weaponType in CollectDeployableWeaponTypes(deploymentPoint))
            {
                if (weaponType == null)
                    continue;

                int maxCount = SafeGetMaxDeployableWeaponCount(weaponsController, weaponType);
                if (maxCount <= 0)
                    continue;

                int deployedCount = CountDeployedWeaponsOfType(allDeploymentPoints, weaponType);
                if (deployedCount >= maxCount)
                    continue;

                float value = GetAutoDeployWeaponValue(weaponType);
                if (selectedType == null || value > selectedValue)
                {
                    selectedType = weaponType;
                    selectedValue = value;
                }
            }

            return selectedType;
        }

        private static List<Type> CollectDeployableWeaponTypes(DeploymentPoint deploymentPoint)
        {
            var result = new List<Type>();
            if (deploymentPoint == null)
                return result;

            try
            {
                foreach (Type weaponType in deploymentPoint.DeployableWeaponTypes)
                {
                    if (weaponType != null && !result.Contains(weaponType))
                        result.Add(weaponType);
                }
            }
            catch
            {
            }

            foreach (SiegeWeapon siegeWeapon in EnumerateDeploymentPointSiegeWeapons(deploymentPoint))
            {
                Type weaponType = ResolveSiegeWeaponType(siegeWeapon);
                if (weaponType != null && !result.Contains(weaponType))
                    result.Add(weaponType);
            }

            return result;
        }

        private static SiegeWeapon ResolveDeployableWeaponOfType(
            DeploymentPoint deploymentPoint,
            BattleSideEnum side,
            Type weaponType)
        {
            if (deploymentPoint == null || side == BattleSideEnum.None || weaponType == null)
                return null;

            foreach (SiegeWeapon siegeWeapon in EnumerateDeploymentPointSiegeWeapons(deploymentPoint))
            {
                if (siegeWeapon == null || siegeWeapon.Side != side)
                    continue;

                Type candidateType = ResolveSiegeWeaponType(siegeWeapon);
                if (candidateType == weaponType)
                    return siegeWeapon;
            }

            return null;
        }

        private static IEnumerable<SiegeWeapon> EnumerateDeploymentPointSiegeWeapons(DeploymentPoint deploymentPoint)
        {
            var result = new List<SiegeWeapon>();
            if (deploymentPoint == null)
                return result;

            AddDeploymentPointSiegeWeapons(result, SafeEnumerateDeployableWeapons(deploymentPoint));
            AddDeploymentPointSiegeWeapons(result, SafeGetWeaponsUnder(deploymentPoint));
            return result;
        }

        private static void AddDeploymentPointSiegeWeapons(
            ICollection<SiegeWeapon> output,
            IEnumerable<SynchedMissionObject> candidates)
        {
            if (output == null || candidates == null)
                return;

            foreach (SynchedMissionObject candidate in candidates)
            {
                if (candidate is SiegeWeapon siegeWeapon && !output.Contains(siegeWeapon))
                    output.Add(siegeWeapon);
            }
        }

        private static IEnumerable<SynchedMissionObject> SafeEnumerateDeployableWeapons(DeploymentPoint deploymentPoint)
        {
            try
            {
                return deploymentPoint?.DeployableWeapons?.ToList() ?? new List<SynchedMissionObject>();
            }
            catch
            {
                return new List<SynchedMissionObject>();
            }
        }

        private static IEnumerable<SynchedMissionObject> SafeGetWeaponsUnder(DeploymentPoint deploymentPoint)
        {
            try
            {
                return deploymentPoint?.GetWeaponsUnder()?.ToList() ?? new List<SynchedMissionObject>();
            }
            catch
            {
                return new List<SynchedMissionObject>();
            }
        }

        private static string NormalizeAutoDeployGhostDeployments(
            Team team,
            List<DeploymentPoint> deploymentPoints,
            IMissionSiegeWeaponsController weaponsController,
            bool verboseDiagnostics)
        {
            if (deploymentPoints == null || weaponsController == null)
                return "Points=" + (deploymentPoints == null ? "<null>" : "0") +
                       " WeaponsController=" + (weaponsController == null ? "<null>" : "present");

            int scannedCount = 0;
            int deployedCount = 0;
            int relevantDeployedCount = 0;
            int materializedCount = 0;
            int ghostCount = 0;
            int disbandedCount = 0;
            int failedCount = 0;
            var details = new List<string>();

            foreach (DeploymentPoint deploymentPoint in deploymentPoints)
            {
                scannedCount++;
                if (deploymentPoint == null ||
                    !deploymentPoint.IsDeployed ||
                    deploymentPoint.DeployedWeapon == null)
                {
                    continue;
                }

                deployedCount++;
                SiegeWeapon deployedSiegeWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
                Type weaponType = ResolveSiegeWeaponType(deployedSiegeWeapon);
                if (deployedSiegeWeapon == null ||
                    weaponType == null ||
                    SafeGetMaxDeployableWeaponCount(weaponsController, weaponType) <= 0)
                {
                    continue;
                }

                relevantDeployedCount++;
                if (IsMaterializedDeployedSiegeWeapon(deploymentPoint, deployedSiegeWeapon))
                {
                    materializedCount++;
                    continue;
                }

                ghostCount++;
                string disbandDiagnostics = ControlledDisband(
                    deploymentPoint,
                    team,
                    keepDisabledPointHidden: true,
                    verboseDiagnostics: verboseDiagnostics);
                bool cleared =
                    !deploymentPoint.IsDeployed ||
                    deploymentPoint.DeployedWeapon == null ||
                    !ReferenceEquals(deploymentPoint.DeployedWeapon, deployedSiegeWeapon);
                if (cleared)
                    disbandedCount++;
                else
                    failedCount++;

                details.Add(
                    "Point=" + FormatDeploymentPoint(deploymentPoint) +
                    " Weapon=" + FormatSiegeWeapon(deployedSiegeWeapon) +
                    " Cleared=" + cleared +
                    " Disband={" + disbandDiagnostics + "}");
            }

            return "Scanned=" + scannedCount +
                   " Deployed=" + deployedCount +
                   " RelevantDeployed=" + relevantDeployedCount +
                   " MaterializedDeployed=" + materializedCount +
                   " GhostDeployments=" + ghostCount +
                   " GhostDeploymentsDisbanded=" + disbandedCount +
                   " GhostDeploymentFailures=" + failedCount +
                   " Details=[" + string.Join("; ", details.ToArray()) + "]";
        }

        private static string ReaffirmExistingAutoDeployDeployments(
            Mission mission,
            Team team,
            List<DeploymentPoint> deploymentPoints,
            IMissionSiegeWeaponsController weaponsController,
            SiegeDeploymentHandler siegeDeploymentHandler,
            bool verboseDiagnostics)
        {
            if (mission == null || team == null || deploymentPoints == null || weaponsController == null)
            {
                return "Mission=" + (mission == null ? "<null>" : mission.SceneName) +
                       " Team=" + FormatTeam(team) +
                       " Points=" + (deploymentPoints == null ? "<null>" : deploymentPoints.Count.ToString()) +
                       " WeaponsController=" + (weaponsController == null ? "<null>" : "present");
            }

            int scannedCount = 0;
            int deployedCount = 0;
            int relevantDeployedCount = 0;
            int reaffirmedCount = 0;
            int failedCount = 0;
            var details = new List<string>();

            foreach (DeploymentPoint deploymentPoint in deploymentPoints)
            {
                scannedCount++;
                if (deploymentPoint == null ||
                    !deploymentPoint.IsDeployed ||
                    deploymentPoint.DeployedWeapon == null)
                {
                    continue;
                }

                deployedCount++;
                SiegeWeapon deployedSiegeWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
                Type weaponType = ResolveSiegeWeaponType(deployedSiegeWeapon);
                if (deployedSiegeWeapon == null ||
                    weaponType == null ||
                    SafeGetMaxDeployableWeaponCount(weaponsController, weaponType) <= 0)
                {
                    continue;
                }

                relevantDeployedCount++;
                string enableDiagnostics = PrepareAutoDeployDeploymentPointAndWeapon(
                    deploymentPoint,
                    deployedSiegeWeapon);
                string deployDiagnostics = ControlledDeploy(
                    mission,
                    team,
                    deploymentPoint,
                    deployedSiegeWeapon,
                    siegeDeploymentHandler,
                    verboseDiagnostics);
                bool reaffirmed =
                    deploymentPoint.IsDeployed &&
                    ReferenceEquals(deploymentPoint.DeployedWeapon, deployedSiegeWeapon);
                if (reaffirmed)
                    reaffirmedCount++;
                else
                    failedCount++;

                details.Add(
                    "Point=" + FormatDeploymentPoint(deploymentPoint) +
                    " Weapon=" + FormatSiegeWeapon(deployedSiegeWeapon) +
                    " Reaffirmed=" + reaffirmed +
                    " Enable={" + enableDiagnostics + "}" +
                    " Deploy={" + deployDiagnostics + "}");
            }

            return "Scanned=" + scannedCount +
                   " Deployed=" + deployedCount +
                   " RelevantDeployed=" + relevantDeployedCount +
                   " ReaffirmedDeployments=" + reaffirmedCount +
                   " ReaffirmFailures=" + failedCount +
                   " ReaffirmDetails=[" + string.Join("; ", details.ToArray()) + "]";
        }

        private static bool IsMaterializedDeployedSiegeWeapon(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon)
        {
            if (deploymentPoint == null ||
                siegeWeapon == null ||
                !deploymentPoint.IsDeployed ||
                deploymentPoint.DeployedWeapon == null ||
                !ReferenceEquals(deploymentPoint.DeployedWeapon, siegeWeapon) ||
                siegeWeapon.IsDisabled)
            {
                return false;
            }

            try
            {
                return siegeWeapon.GameEntity.IsVisibleIncludeParents();
            }
            catch
            {
                return false;
            }
        }

        private static int CountDeployedWeaponsOfType(
            IEnumerable<DeploymentPoint> deploymentPoints,
            Type weaponType)
        {
            if (deploymentPoints == null || weaponType == null)
                return 0;

            int count = 0;
            foreach (DeploymentPoint deploymentPoint in deploymentPoints)
            {
                if (deploymentPoint == null ||
                    !deploymentPoint.IsDeployed ||
                    deploymentPoint.DeployedWeapon == null)
                {
                    continue;
                }

                SiegeWeapon deployedSiegeWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
                if (!IsMaterializedDeployedSiegeWeapon(deploymentPoint, deployedSiegeWeapon))
                    continue;

                if (ResolveSiegeWeaponType(deployedSiegeWeapon) == weaponType)
                    count++;
            }

            return count;
        }

        private static Type ResolveSiegeWeaponType(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return null;

            try
            {
                return MissionSiegeWeaponsController.GetWeaponType(siegeWeapon);
            }
            catch
            {
                return siegeWeapon.GetType();
            }
        }

        private static int SafeGetMaxDeployableWeaponCount(
            IMissionSiegeWeaponsController weaponsController,
            Type weaponType)
        {
            if (weaponsController == null || weaponType == null)
                return 0;

            try
            {
                return weaponsController.GetMaxDeployableWeaponCount(weaponType);
            }
            catch
            {
                return 0;
            }
        }

        private static float GetAutoDeployWeaponValue(Type weaponType)
        {
            if (weaponType == typeof(BatteringRam) ||
                weaponType == typeof(SiegeTower) ||
                weaponType == typeof(SiegeLadder))
            {
                return 0.9f + MBRandom.RandomFloat * 0.2f;
            }

            if (typeof(RangedSiegeWeapon).IsAssignableFrom(weaponType))
                return 0.7f + MBRandom.RandomFloat * 0.2f;

            return 1f;
        }

        private static DeploymentPoint.DeploymentPointType SafeGetDeploymentPointType(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return DeploymentPoint.DeploymentPointType.BatteringRam;

            try
            {
                return deploymentPoint.GetDeploymentPointType();
            }
            catch
            {
                return DeploymentPoint.DeploymentPointType.BatteringRam;
            }
        }

        private static void ShuffleDeploymentPoints(List<DeploymentPoint> deploymentPoints)
        {
            if (deploymentPoints == null)
                return;

            for (int i = deploymentPoints.Count - 1; i > 0; i--)
            {
                int swapIndex = MBRandom.RandomInt(i + 1);
                DeploymentPoint temp = deploymentPoints[i];
                deploymentPoints[i] = deploymentPoints[swapIndex];
                deploymentPoints[swapIndex] = temp;
            }
        }

        private static List<DeploymentPoint> CollectAutoDeployDeploymentPoints(
            Mission mission,
            SiegeDeploymentHandler siegeDeploymentHandler,
            BattleSideEnum side,
            out string diagnostics)
        {
            var result = new List<DeploymentPoint>();
            int handlerCount = -1;
            int activeMissionObjectCount = -1;
            int missionObjectCount = -1;

            try
            {
                if (siegeDeploymentHandler?.AllDeploymentPoints != null)
                {
                    handlerCount = 0;
                    foreach (DeploymentPoint deploymentPoint in siegeDeploymentHandler.AllDeploymentPoints)
                    {
                        if (deploymentPoint != null &&
                            deploymentPoint.Side == side)
                        {
                            handlerCount++;
                            if (!result.Contains(deploymentPoint))
                                result.Add(deploymentPoint);
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (mission?.ActiveMissionObjects != null)
                {
                    activeMissionObjectCount = 0;
                    foreach (DeploymentPoint deploymentPoint in mission.ActiveMissionObjects.FindAllWithType<DeploymentPoint>())
                    {
                        if (deploymentPoint != null &&
                            deploymentPoint.Side == side)
                        {
                            activeMissionObjectCount++;
                            if (!result.Contains(deploymentPoint))
                                result.Add(deploymentPoint);
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (mission?.MissionObjects != null)
                {
                    missionObjectCount = 0;
                    foreach (DeploymentPoint deploymentPoint in mission.MissionObjects.FindAllWithType<DeploymentPoint>())
                    {
                        if (deploymentPoint != null &&
                            deploymentPoint.Side == side)
                        {
                            missionObjectCount++;
                            if (!result.Contains(deploymentPoint))
                                result.Add(deploymentPoint);
                        }
                    }
                }
            }
            catch
            {
            }

            diagnostics =
                "HandlerAll=" + handlerCount +
                " ActiveMissionObjects=" + activeMissionObjectCount +
                " MissionObjects=" + missionObjectCount +
                " Result=" + result.Count;
            return result;
        }

        private static List<DeploymentPoint> CollectAutoDeployCandidatePoints(
            Mission mission,
            Team team,
            List<DeploymentPoint> deploymentPoints,
            bool treatSideAsPlayerSide,
            IMissionSiegeWeaponsController weaponsController)
        {
            if (team == null || deploymentPoints == null)
                return new List<DeploymentPoint>();

            if (team.Side == BattleSideEnum.Attacker)
            {
                List<DeploymentPoint> candidates = deploymentPoints
                    .Where(deploymentPoint =>
                        deploymentPoint != null &&
                        !deploymentPoint.IsDeployed &&
                        HasAutoDeployableWeaponType(deploymentPoint, weaponsController))
                    .OrderBy(deploymentPoint => deploymentPoint.IsDisabled ? 1 : 0)
                    .ToList();

                int breachCount = deploymentPoints.Count(
                    deploymentPoint => SafeGetDeploymentPointType(deploymentPoint) == DeploymentPoint.DeploymentPointType.Breach);
                bool deployOnlyRangedBecauseAiAttackerHasBreaches =
                    !treatSideAsPlayerSide &&
                    mission?.AttackerTeam != null &&
                    !ReferenceEquals(mission.AttackerTeam, mission.PlayerTeam) &&
                    breachCount >= 2;

                if (deployOnlyRangedBecauseAiAttackerHasBreaches)
                {
                    candidates = candidates
                        .Where(deploymentPoint => SafeGetDeploymentPointType(deploymentPoint) == DeploymentPoint.DeploymentPointType.Ranged)
                        .OrderBy(deploymentPoint => deploymentPoint.IsDisabled ? 1 : 0)
                        .ToList();
                }

                return candidates;
            }

            if (team.Side == BattleSideEnum.Defender)
            {
                return deploymentPoints
                    .Where(deploymentPoint => deploymentPoint != null && !deploymentPoint.IsDeployed)
                    .ToList();
            }

            return new List<DeploymentPoint>();
        }

        private static int CountAutoDeployableRemainingWeaponCount(
            List<DeploymentPoint> allDeploymentPoints,
            List<DeploymentPoint> candidateDeploymentPoints,
            IMissionSiegeWeaponsController weaponsController)
        {
            if (allDeploymentPoints == null || candidateDeploymentPoints == null || weaponsController == null)
                return 0;

            List<Type> deployableTypes = new List<Type>();
            foreach (DeploymentPoint deploymentPoint in candidateDeploymentPoints)
            {
                foreach (Type weaponType in CollectDeployableWeaponTypes(deploymentPoint))
                {
                    if (weaponType != null &&
                        !deployableTypes.Contains(weaponType) &&
                        SafeGetMaxDeployableWeaponCount(weaponsController, weaponType) > 0)
                    {
                        deployableTypes.Add(weaponType);
                    }
                }
            }

            int remainingCount = 0;
            foreach (Type weaponType in deployableTypes)
            {
                int maxCount = SafeGetMaxDeployableWeaponCount(weaponsController, weaponType);
                if (maxCount <= 0)
                    continue;

                int deployedCount = CountDeployedWeaponsOfType(allDeploymentPoints, weaponType);
                int undeployedCount = Math.Max(0, maxCount - deployedCount);
                if (undeployedCount <= 0)
                    continue;

                int candidateCount = candidateDeploymentPoints.Count(
                    deploymentPoint => HasDeployableWeaponType(deploymentPoint, weaponType));
                remainingCount += Math.Min(undeployedCount, candidateCount);
            }

            return remainingCount;
        }

        private static int CountAutoDeployRelevantDeployedWeaponCount(
            List<DeploymentPoint> deploymentPoints,
            IMissionSiegeWeaponsController weaponsController)
        {
            if (deploymentPoints == null || weaponsController == null)
                return 0;

            int count = 0;
            foreach (DeploymentPoint deploymentPoint in deploymentPoints)
            {
                if (deploymentPoint == null ||
                    !deploymentPoint.IsDeployed ||
                    deploymentPoint.DeployedWeapon == null)
                {
                    continue;
                }

                SiegeWeapon deployedSiegeWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
                if (!IsMaterializedDeployedSiegeWeapon(deploymentPoint, deployedSiegeWeapon))
                    continue;

                Type weaponType = ResolveSiegeWeaponType(deployedSiegeWeapon);
                if (weaponType != null &&
                    SafeGetMaxDeployableWeaponCount(weaponsController, weaponType) > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasDeployableWeaponType(DeploymentPoint deploymentPoint, Type weaponType)
        {
            if (deploymentPoint == null || weaponType == null)
                return false;

            foreach (Type candidateType in CollectDeployableWeaponTypes(deploymentPoint))
            {
                if (candidateType == weaponType)
                    return true;
            }

            return false;
        }

        private static bool HasAutoDeployableWeaponType(
            DeploymentPoint deploymentPoint,
            IMissionSiegeWeaponsController weaponsController)
        {
            if (deploymentPoint == null || weaponsController == null)
                return false;

            foreach (Type weaponType in CollectDeployableWeaponTypes(deploymentPoint))
            {
                if (weaponType != null &&
                    SafeGetMaxDeployableWeaponCount(weaponsController, weaponType) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildAutoDeployCandidatePointDiagnostics(
            Team team,
            List<DeploymentPoint> deploymentPoints,
            List<DeploymentPoint> candidateDeploymentPoints,
            IMissionSiegeWeaponsController weaponsController)
        {
            if (team == null || deploymentPoints == null)
                return "Team=" + FormatTeam(team) + " Points=" + (deploymentPoints == null ? "<null>" : "0");

            int undeployedCount = 0;
            int enabledUndeployedCount = 0;
            int disabledUndeployedCount = 0;
            int deployableUndeployedCount = 0;
            int enabledDeployableUndeployedCount = 0;
            int disabledDeployableUndeployedCount = 0;
            int skippedDeployedCount = 0;
            int skippedNoAllowedWeaponCount = 0;

            foreach (DeploymentPoint deploymentPoint in deploymentPoints)
            {
                if (deploymentPoint == null)
                    continue;

                if (deploymentPoint.IsDeployed)
                {
                    skippedDeployedCount++;
                    continue;
                }

                undeployedCount++;
                if (deploymentPoint.IsDisabled)
                    disabledUndeployedCount++;
                else
                    enabledUndeployedCount++;

                if (HasAutoDeployableWeaponType(deploymentPoint, weaponsController))
                {
                    deployableUndeployedCount++;
                    if (deploymentPoint.IsDisabled)
                        disabledDeployableUndeployedCount++;
                    else
                        enabledDeployableUndeployedCount++;
                }
                else
                {
                    skippedNoAllowedWeaponCount++;
                }
            }

            int candidateCount = candidateDeploymentPoints?.Count ?? 0;
            int disabledCandidateCount = candidateDeploymentPoints?.Count(
                deploymentPoint => deploymentPoint != null && deploymentPoint.IsDisabled) ?? 0;
            int enabledCandidateCount = candidateCount - disabledCandidateCount;

            return "Team=" + FormatTeam(team) +
                   " Undeployed=" + undeployedCount +
                   " EnabledUndeployed=" + enabledUndeployedCount +
                   " DisabledUndeployed=" + disabledUndeployedCount +
                   " DeployableUndeployed=" + deployableUndeployedCount +
                   " EnabledDeployableUndeployed=" + enabledDeployableUndeployedCount +
                   " DisabledDeployableUndeployed=" + disabledDeployableUndeployedCount +
                   " Candidates=" + candidateCount +
                   " EnabledCandidates=" + enabledCandidateCount +
                   " DisabledCandidates=" + disabledCandidateCount +
                   " SkippedDeployed=" + skippedDeployedCount +
                   " SkippedNoAllowedWeapon=" + skippedNoAllowedWeaponCount;
        }

        private static List<ICastleKeyPosition> CollectCastleKeyPositions(Mission mission)
        {
            var result = new List<ICastleKeyPosition>();
            try
            {
                if (mission?.ActiveMissionObjects == null)
                    return result;

                foreach (MissionObject activeMissionObject in mission.ActiveMissionObjects)
                {
                    if (activeMissionObject == null || !activeMissionObject.GameEntity.IsValid)
                        continue;

                    UsableMachine usableMachine = activeMissionObject.GameEntity.GetFirstScriptOfType<UsableMachine>();
                    if (!(usableMachine is ICastleKeyPosition castleKeyPosition))
                        continue;

                    IPrimarySiegeWeapon attackerSiegeWeapon = castleKeyPosition.AttackerSiegeWeapon;
                    if (attackerSiegeWeapon == null ||
                        attackerSiegeWeapon.WeaponSide != FormationAI.BehaviorSide.BehaviorSideNotSet)
                    {
                        result.Add(castleKeyPosition);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static DeploymentPoint SelectDefenderDeploymentPoint(
            List<DeploymentPoint> candidates,
            List<ICastleKeyPosition> castleKeyPositions)
        {
            if (candidates == null || candidates.Count <= 0)
                return null;

            try
            {
                if (castleKeyPositions == null || castleKeyPositions.Count <= 0)
                    return candidates[0];

                Threat threat = RangedSiegeWeaponAi.ThreatSeeker.GetMaxThreat(castleKeyPositions);
                if (threat == null)
                    return candidates[0];

                Vec3 threatPosition = threat.TargetingPosition;
                DeploymentPoint selectedPoint = null;
                float selectedDistanceSquared = float.MaxValue;
                foreach (DeploymentPoint deploymentPoint in candidates)
                {
                    if (deploymentPoint == null)
                        continue;

                    float distanceSquared = deploymentPoint.GameEntity.GlobalPosition.DistanceSquared(threatPosition);
                    if (selectedPoint == null || distanceSquared < selectedDistanceSquared)
                    {
                        selectedPoint = deploymentPoint;
                        selectedDistanceSquared = distanceSquared;
                    }
                }

                return selectedPoint ?? candidates[0];
            }
            catch
            {
                return candidates[0];
            }
        }

        private static string ControlledDisband(
            DeploymentPoint deploymentPoint,
            Team team,
            bool keepDisabledPointHidden,
            bool verboseDiagnostics)
        {
            if (deploymentPoint == null)
                return "DeploymentPoint=<null>";

            SiegeWeapon sourceWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
            var details = new List<string>();
            details.Add("Before=" + FormatDeploymentPoint(deploymentPoint));
            details.Add("Weapon=" + FormatSiegeWeapon(sourceWeapon));

            if (sourceWeapon != null)
            {
                details.Add("PrepareWeapon={" + PrepareSiegeWeaponDeployEntityLists(sourceWeapon, verboseDiagnostics) + "}");
                details.Add("ReleaseAgents={" + ReleaseAgentsFromSiegeMachineBeforeDisband(team, sourceWeapon) + "}");
            }

            bool showPoint = !(keepDisabledPointHidden && deploymentPoint.IsDisabled);
            details.Add("PointVisibility={" + SetSynchedVisibility(deploymentPoint, showPoint) + "}");
            if (sourceWeapon != null)
            {
                details.Add("WeaponParentVisibility={" + ToggleWeaponVisibility(deploymentPoint, sourceWeapon, false) + "}");
                details.Add("WeaponVisibility={" + SetSynchedVisibility(sourceWeapon, false) + "}");
            }

            details.Add("State={" + SetDeploymentPointState(deploymentPoint, null, sourceWeapon) + "}");

            if (sourceWeapon != null)
            {
                details.Add("WeaponStateChanged={" + InvokeSiegeWeaponDeploymentStateChanged(sourceWeapon, false) + "}");
                details.Add("VisualTree={" + NormalizeAuthoritativeSiegeWeaponVisualTree(deploymentPoint, sourceWeapon, false) + "}");
                details.Add("Detachments={" + DestroyDetachmentIfPresent(team, sourceWeapon) + "}");
                details.Add("Controller={" + SyncSiegeControllerAfterUndeploy(team.Side, sourceWeapon) + "}");
            }

            details.Add("After=" + FormatDeploymentPoint(deploymentPoint));
            return string.Join(" ", details.ToArray());
        }

        private static string ControlledDeploy(
            Mission mission,
            Team team,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            SiegeDeploymentHandler siegeDeploymentHandler,
            bool verboseDiagnostics)
        {
            var details = new List<string>();
            details.Add("Before=" + FormatDeploymentPoint(deploymentPoint));
            details.Add("Weapon=" + FormatSiegeWeapon(siegeWeapon));

            details.Add("PointPrepare={" + PrepareDeploymentPointWeaponCache(deploymentPoint) + "}");
            details.Add("WeaponPrepare={" + PrepareSiegeWeaponDeployEntityLists(siegeWeapon, verboseDiagnostics) + "}");
            details.Add("State={" + SetDeploymentPointState(deploymentPoint, siegeWeapon, null) + "}");
            details.Add("WeaponVisibility={" + SetSynchedVisibility(siegeWeapon, true) + "}");
            details.Add("PointVisibility={" + SetSynchedVisibility(deploymentPoint, false) + "}");
            details.Add("WeaponParentVisibility={" + ToggleWeaponVisibility(deploymentPoint, siegeWeapon, true) + "}");
            details.Add("ForcedUse={" + SetForcedUse(siegeWeapon) + "}");
            details.Add("WeaponStateChanged={" + InvokeSiegeWeaponDeploymentStateChanged(siegeWeapon, true) + "}");
            details.Add("VisualTree={" + NormalizeAuthoritativeSiegeWeaponVisualTree(deploymentPoint, siegeWeapon, true) + "}");
            details.Add("Controller={" + SyncSiegeControllerAfterDeploy(team.Side, siegeWeapon) + "}");
            details.Add("Formations={" + PrepareFormationsForSiegeMachineAssignment(team) + "}");
            details.Add("TickAux={" + TickAuxForInit(siegeWeapon) + "}");
            details.Add("FallbackUse={" + EnsureFormationUsesMachine(team, siegeWeapon) + "}");
            details.Add("AutoAssign={" + AutoAssignDetachments(team, siegeDeploymentHandler) + "}");
            details.Add("After=" + FormatDeploymentPoint(deploymentPoint));
            return string.Join(" ", details.ToArray());
        }

        private static string PrepareDeploymentPointWeaponCache(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return "DeploymentPoint=<null>";

            int weaponsUnderCount = -1;
            bool weaponsFieldSet = false;
            bool determineTypeInvoked = false;
            string error = string.Empty;
            try
            {
                MBList<SynchedMissionObject> weaponsUnder = deploymentPoint.GetWeaponsUnder();
                weaponsUnderCount = weaponsUnder?.Count ?? 0;
                if (DeploymentPointWeaponsField != null && weaponsUnder != null)
                {
                    DeploymentPointWeaponsField.SetValue(deploymentPoint, weaponsUnder);
                    weaponsFieldSet = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, "weapons", ex);
            }

            try
            {
                if (DeploymentPointDetermineTypeMethod != null)
                {
                    DeploymentPointDetermineTypeMethod.Invoke(deploymentPoint, Array.Empty<object>());
                    determineTypeInvoked = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, "determine-type", ex);
            }

            return "WeaponsUnder=" + weaponsUnderCount +
                   " WeaponsFieldSet=" + weaponsFieldSet +
                   " DetermineTypeInvoked=" + determineTypeInvoked +
                   " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static string PrepareAutoDeployDeploymentPointAndWeapon(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon)
        {
            if (deploymentPoint == null)
                return "DeploymentPoint=<null>";

            bool deploymentPointEnabled = false;
            bool siegeWeaponEnabled = false;
            string error = string.Empty;

            try
            {
                if (deploymentPoint.IsDisabled)
                {
                    deploymentPoint.SetEnabledAndMakeVisible();
                    deploymentPointEnabled = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, "enable-point", ex);
            }

            try
            {
                if (siegeWeapon != null && siegeWeapon.IsDisabled)
                {
                    siegeWeapon.SetEnabledAndMakeVisible();
                    siegeWeaponEnabled = true;
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, "enable-weapon", ex);
            }

            return "DeploymentPointEnabled=" + deploymentPointEnabled +
                   " SiegeWeaponEnabled=" + siegeWeaponEnabled +
                   " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static string PrepareSiegeWeaponDeployEntityLists(SiegeWeapon siegeWeapon, bool verboseDiagnostics)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            string removeDiagnostics = EnsureSiegeWeaponDeployEntityList(
                siegeWeapon,
                SiegeWeaponRemoveOnDeployEntitiesField,
                SiegeWeaponRemoveOnDeployTagField,
                "remove",
                verboseDiagnostics);
            string addDiagnostics = EnsureSiegeWeaponDeployEntityList(
                siegeWeapon,
                SiegeWeaponAddOnDeployEntitiesField,
                SiegeWeaponAddOnDeployTagField,
                "add",
                verboseDiagnostics);
            return "Remove={" + removeDiagnostics + "} Add={" + addDiagnostics + "}";
        }

        private static string EnsureSiegeWeaponDeployEntityList(
            SiegeWeapon siegeWeapon,
            FieldInfo listField,
            FieldInfo tagField,
            string label,
            bool verboseDiagnostics)
        {
            if (siegeWeapon == null)
                return "Skipped=True Reason=siege-weapon-null";

            if (listField == null)
                return "Skipped=True Reason=list-field-missing Label=" + (label ?? string.Empty);

            try
            {
                object existingValue = listField.GetValue(siegeWeapon);
                if (existingValue is List<GameEntity> existingList)
                {
                    return verboseDiagnostics
                        ? "Existing=True Initialized=False Count=" + existingList.Count + " Label=" + (label ?? string.Empty)
                        : "Existing=True Count=" + existingList.Count;
                }

                string tag = string.Empty;
                if (tagField != null)
                    tag = tagField.GetValue(siegeWeapon) as string ?? string.Empty;

                List<GameEntity> entities = new List<GameEntity>();
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    try
                    {
                        entities = Mission.Current?.Scene?
                            .FindEntitiesWithTag(tag)
                            .ToList() ?? new List<GameEntity>();
                    }
                    catch
                    {
                        entities = new List<GameEntity>();
                    }
                }

                listField.SetValue(siegeWeapon, entities);
                return verboseDiagnostics
                    ? "Existing=False Initialized=True Count=" + entities.Count + " Label=" + (label ?? string.Empty) + " Tag=" + (tag ?? string.Empty)
                    : "Initialized=True Count=" + entities.Count;
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string SetDeploymentPointState(
            DeploymentPoint deploymentPoint,
            SynchedMissionObject deployedWeapon,
            SynchedMissionObject disbandedWeapon)
        {
            if (deploymentPoint == null)
                return "DeploymentPoint=<null>";

            if (DeploymentPointDeployedWeaponField == null || DeploymentPointDisbandedWeaponField == null)
                return "FieldMissing=True";

            try
            {
                DeploymentPointDeployedWeaponField.SetValue(deploymentPoint, deployedWeapon);
                DeploymentPointDisbandedWeaponField.SetValue(deploymentPoint, disbandedWeapon);
                return "Deployed=" + FormatSynchedObject(deployedWeapon) +
                       " Disbanded=" + FormatSynchedObject(disbandedWeapon);
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string SetSynchedVisibility(SynchedMissionObject missionObject, bool visible)
        {
            if (missionObject == null)
                return "Object=<null>";

            string error = string.Empty;
            bool visibleSet = false;
            bool physicsSet = false;
            try
            {
                missionObject.SetVisibleSynched(visible);
                visibleSet = true;
            }
            catch (Exception ex)
            {
                error = AppendError(error, "visible", ex);
            }

            try
            {
                missionObject.SetPhysicsStateSynched(visible);
                physicsSet = true;
            }
            catch (Exception ex)
            {
                error = AppendError(error, "physics", ex);
            }

            return "Visible=" + visible +
                   " VisibleSet=" + visibleSet +
                   " PhysicsSet=" + physicsSet +
                   " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static string ToggleWeaponVisibility(
            DeploymentPoint deploymentPoint,
            SynchedMissionObject weapon,
            bool visible)
        {
            if (deploymentPoint == null || weapon == null)
                return "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) + " Weapon=" + FormatSynchedObject(weapon);

            try
            {
                deploymentPoint.ToggleWeaponVisibility(visible, weapon);
                return "Visible=" + visible + " Native=True";
            }
            catch (Exception ex)
            {
                return "Visible=" + visible + " Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string InvokeSiegeWeaponDeploymentStateChanged(SiegeWeapon siegeWeapon, bool isDeployed)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            if (SiegeWeaponOnDeploymentStateChangedMethod == null)
                return "MethodMissing=True";

            try
            {
                SiegeWeaponOnDeploymentStateChangedMethod.Invoke(siegeWeapon, new object[] { isDeployed });
                return "Invoked=True IsDeployed=" + isDeployed;
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                return "Invoked=False Error=" + inner.GetType().Name + ":" + inner.Message;
            }
            catch (Exception ex)
            {
                return "Invoked=False Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string SetForcedUse(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            try
            {
                siegeWeapon.SetForcedUse(true);
                return "Set=True";
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string TickAuxForInit(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            try
            {
                bool visibleIncludeParents = siegeWeapon.GameEntity.IsVisibleIncludeParents();
                siegeWeapon.TickAuxForInit();
                return "Invoked=True VisibleIncludeParents=" + visibleIncludeParents;
            }
            catch (Exception ex)
            {
                return "Invoked=False Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        public static string NormalizeLocalDeployedSiegeWeaponVisualTree(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool visible)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer)
            {
                return "Skipped=True Reason=not-remote-client" +
                       " IsClient=" + GameNetwork.IsClient +
                       " IsServer=" + GameNetwork.IsServer +
                       " IsDedicatedServer=" + GameNetwork.IsDedicatedServer;
            }

            return NormalizeDeployedSiegeWeaponVisualTree(
                deploymentPoint,
                siegeWeapon,
                visible,
                setSynchedMissionObjects: false);
        }

        private static string NormalizeAuthoritativeSiegeWeaponVisualTree(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool visible)
        {
            if (GameNetwork.IsServer)
            {
                return "Skipped=True Reason=server-authoritative-state-only" +
                       " IsClient=" + GameNetwork.IsClient +
                       " IsServer=" + GameNetwork.IsServer +
                       " IsDedicatedServer=" + GameNetwork.IsDedicatedServer;
            }

            return NormalizeDeployedSiegeWeaponVisualTree(
                deploymentPoint,
                siegeWeapon,
                visible,
                setSynchedMissionObjects: true);
        }

        private static string NormalizeDeployedSiegeWeaponVisualTree(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool visible,
            bool setSynchedMissionObjects)
        {
            if (deploymentPoint == null || siegeWeapon == null)
                return "DeploymentPoint=" + FormatDeploymentPoint(deploymentPoint) +
                       " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon);

            try
            {
                List<WeakGameEntity> chain = BuildSiegeWeaponEntityChain(deploymentPoint, siegeWeapon);
                if (chain.Count <= 0)
                    return "Visible=" + visible + " EntityChain=0";

                IEnumerable<WeakGameEntity> orderedChain = visible
                    ? chain.AsEnumerable().Reverse()
                    : chain;
                int entityVisibleSetCount = 0;
                int synchedObjectSetCount = 0;
                string activeVisualDiagnostics = string.Empty;
                string error = string.Empty;
                foreach (WeakGameEntity entity in orderedChain)
                {
                    if (!entity.IsValid)
                        continue;

                    string entityDiagnostics = SetEntityVisibility(entity, visible);
                    if (entityDiagnostics.IndexOf("EntityVisibleSet=True", StringComparison.OrdinalIgnoreCase) >= 0)
                        entityVisibleSetCount++;

                    SynchedMissionObject synchedMissionObject = null;
                    try
                    {
                        synchedMissionObject = entity.GetFirstScriptOfType<SynchedMissionObject>();
                    }
                    catch (Exception ex)
                    {
                        error = AppendError(error, "script", ex);
                    }

                    if (synchedMissionObject != null && setSynchedMissionObjects)
                    {
                        string synchedDiagnostics = SetSynchedVisibility(synchedMissionObject, visible);
                        if (synchedDiagnostics.IndexOf("VisibleSet=True", StringComparison.OrdinalIgnoreCase) >= 0)
                            synchedObjectSetCount++;
                    }
                }

                activeVisualDiagnostics = NormalizeRangedSiegeWeaponActiveVisualParts(
                    siegeWeapon,
                    visible,
                    setSynchedMissionObjects,
                    ref entityVisibleSetCount,
                    ref synchedObjectSetCount);

                return "Visible=" + visible +
                       " EntityChain=" + chain.Count +
                       " EntityVisibleSet=" + entityVisibleSetCount +
                       " SynchedObjectSet=" + synchedObjectSetCount +
                       " SynchedObjectSync=" + setSynchedMissionObjects +
                       " ActiveVisualParts={" + activeVisualDiagnostics + "}" +
                       " RootVisibleIncludeParents=" + SafeIsVisibleIncludeParents(siegeWeapon) +
                       " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
            }
            catch (Exception ex)
            {
                return "Visible=" + visible + " Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static List<WeakGameEntity> BuildSiegeWeaponEntityChain(
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon)
        {
            var result = new List<WeakGameEntity>();
            if (deploymentPoint == null || siegeWeapon == null)
                return result;

            WeakGameEntity stopEntity;
            try
            {
                stopEntity = deploymentPoint.GameEntity;
            }
            catch
            {
                stopEntity = WeakGameEntity.Invalid;
            }

            WeakGameEntity current;
            try
            {
                current = siegeWeapon.GameEntity;
            }
            catch
            {
                current = WeakGameEntity.Invalid;
            }

            int depth = 0;
            while (current.IsValid && depth < 8)
            {
                if (stopEntity.IsValid && AreSameEntity(current, stopEntity))
                    break;

                result.Add(current);

                WeakGameEntity parent;
                try
                {
                    parent = current.Parent;
                }
                catch
                {
                    break;
                }

                if (!parent.IsValid || AreSameEntity(parent, current))
                    break;

                current = parent;
                depth++;
            }

            return result;
        }

        private static string NormalizeRangedSiegeWeaponActiveVisualParts(
            SiegeWeapon siegeWeapon,
            bool visible,
            bool setSynchedMissionObjects,
            ref int entityVisibleSetCount,
            ref int synchedObjectSetCount)
        {
            if (!(siegeWeapon is RangedSiegeWeapon))
                return "Skipped=True Reason=not-ranged";

            if (RangedSiegeWeaponSkeletonOwnerObjectsField == null)
                return "Skipped=True Reason=skeleton-owner-field-missing";

            try
            {
                object value = RangedSiegeWeaponSkeletonOwnerObjectsField.GetValue(siegeWeapon);
                if (!(value is SynchedMissionObject[] owners) || owners.Length <= 0)
                    return "Owners=0";

                int scannedCount = 0;
                int visibleSetCount = 0;
                int synchedSetCount = 0;
                string error = string.Empty;
                foreach (SynchedMissionObject owner in owners)
                {
                    if (owner == null)
                        continue;

                    scannedCount++;
                    WeakGameEntity entity;
                    try
                    {
                        entity = owner.GameEntity;
                    }
                    catch (Exception ex)
                    {
                        error = AppendError(error, "owner-entity", ex);
                        continue;
                    }

                    if (!entity.IsValid)
                        continue;

                    string entityDiagnostics = SetEntityVisibility(entity, visible);
                    if (entityDiagnostics.IndexOf("EntityVisibleSet=True", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        visibleSetCount++;
                        entityVisibleSetCount++;
                    }

                    if (setSynchedMissionObjects)
                    {
                        string synchedDiagnostics = SetSynchedVisibility(owner, visible);
                        if (synchedDiagnostics.IndexOf("VisibleSet=True", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            synchedSetCount++;
                            synchedObjectSetCount++;
                        }
                    }
                }

                return "Owners=" + owners.Length +
                       " Scanned=" + scannedCount +
                       " EntityVisibleSet=" + visibleSetCount +
                       " SynchedObjectSet=" + synchedSetCount +
                       " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static bool AreSameEntity(WeakGameEntity left, WeakGameEntity right)
        {
            try
            {
                return left.IsValid && right.IsValid && left.Equals(right);
            }
            catch
            {
                return false;
            }
        }

        private static string SetEntityVisibility(WeakGameEntity entity, bool visible)
        {
            if (!entity.IsValid)
                return "Entity=<invalid>";

            try
            {
                entity.SetVisibilityExcludeParents(visible);
                return "EntityVisible=" + visible + " EntityVisibleSet=True";
            }
            catch (Exception ex)
            {
                return "EntityVisible=" + visible + " EntityVisibleSet=False Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static bool SafeIsVisibleIncludeParents(SiegeWeapon siegeWeapon)
        {
            try
            {
                return siegeWeapon?.GameEntity.IsVisibleIncludeParents() == true;
            }
            catch
            {
                return false;
            }
        }

        private static string PrepareFormationsForSiegeMachineAssignment(Team team)
        {
            if (team == null)
                return "Team=<null>";

            int formationCount = 0;
            int aiControlledSetCount = 0;
            string error = string.Empty;
            try
            {
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null)
                        continue;

                    formationCount++;
                    try
                    {
                        formation.SetControlledByAI(true, true);
                        aiControlledSetCount++;
                    }
                    catch (Exception ex)
                    {
                        error = AppendError(error, "formation-" + formationCount, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, "enumerate", ex);
            }

            return "Formations=" + formationCount +
                   " AiControlledSet=" + aiControlledSetCount +
                   " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static string EnsureFormationUsesMachine(Team team, SiegeWeapon siegeWeapon)
        {
            if (team == null || siegeWeapon == null)
                return "Team=" + FormatTeam(team) + " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon);

            UsableMachine usableMachine = siegeWeapon as UsableMachine;
            IDetachment detachment = siegeWeapon as IDetachment;
            if (usableMachine == null || detachment == null)
                return "UsableMachine=" + (usableMachine != null) + " Detachment=" + (detachment != null);

            try
            {
                bool managerAttached = team.DetachmentManager != null && team.DetachmentManager.ContainsDetachment(detachment);
                int userFormationCount = CountUserFormationsForTeam(usableMachine, team);
                if (managerAttached && userFormationCount > 0)
                    return "AlreadyAttached=True UserFormations=" + userFormationCount;
            }
            catch
            {
            }

            float detachmentWeight = float.MinValue;
            try
            {
                detachmentWeight = detachment.GetDetachmentWeight(team.Side);
            }
            catch
            {
            }

            Formation selectedFormation = null;
            float selectedDistanceSquared = float.MaxValue;
            int scannedFormations = 0;
            int eligibleFormations = 0;
            try
            {
                foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                {
                    scannedFormations++;
                    if (!IsEligibleFormationForSiegeMachine(formation, detachment))
                        continue;

                    eligibleFormations++;
                    float distanceSquared = GetFormationDistanceSquaredToMachine(formation, siegeWeapon);
                    if (distanceSquared < selectedDistanceSquared)
                    {
                        selectedDistanceSquared = distanceSquared;
                        selectedFormation = formation;
                    }
                }
            }
            catch (Exception ex)
            {
                return "EnumerateError=" + ex.GetType().Name + ":" + ex.Message +
                       " DetachmentWeight=" + FormatFloat(detachmentWeight);
            }

            if (selectedFormation == null)
            {
                return "SelectedFormation=<null> Scanned=" + scannedFormations +
                       " Eligible=" + eligibleFormations +
                       " DetachmentWeight=" + FormatFloat(detachmentWeight);
            }

            try
            {
                selectedFormation.SetControlledByAI(true, true);
                selectedFormation.StartUsingMachine(usableMachine, true);
                bool attached = team.DetachmentManager != null && team.DetachmentManager.ContainsDetachment(detachment);
                int userFormationCount = CountUserFormationsForTeam(usableMachine, team);
                return "Selected=True FormationIndex=" + selectedFormation.Index +
                       " Attached=" + attached +
                       " UserFormations=" + userFormationCount +
                       " Scanned=" + scannedFormations +
                       " Eligible=" + eligibleFormations +
                       " DistanceSquared=" + FormatFloat(selectedDistanceSquared) +
                       " DetachmentWeight=" + FormatFloat(detachmentWeight);
            }
            catch (Exception ex)
            {
                return "Selected=True FormationIndex=" + selectedFormation.Index +
                       " Error=" + ex.GetType().Name + ":" + ex.Message +
                       " DetachmentWeight=" + FormatFloat(detachmentWeight);
            }
        }

        private static int CountUserFormationsForTeam(UsableMachine usableMachine, Team team)
        {
            if (usableMachine == null || team == null || usableMachine.UserFormations == null)
                return 0;

            int count = 0;
            try
            {
                foreach (Formation formation in usableMachine.UserFormations)
                {
                    if (formation?.Team == team)
                        count++;
                }
            }
            catch
            {
            }

            return count;
        }

        private static bool IsEligibleFormationForSiegeMachine(Formation formation, IDetachment detachment)
        {
            if (formation == null ||
                detachment == null ||
                formation.CountOfUnits <= 0 ||
                formation.Detachments.Contains(detachment))
            {
                return false;
            }

            try
            {
                if (formation.GetReadonlyMovementOrderReference().OrderEnum == MovementOrder.MovementOrderEnum.Retreat)
                    return false;
            }
            catch
            {
            }

            try
            {
                int unitCount = formation.Arrangement?.UnitCount ?? 0;
                if (unitCount <= 0)
                    return false;

                return unitCount > 1 || !formation.HasPlayerControlledTroop;
            }
            catch
            {
                return false;
            }
        }

        private static float GetFormationDistanceSquaredToMachine(Formation formation, SiegeWeapon siegeWeapon)
        {
            try
            {
                return formation.CachedAveragePosition.DistanceSquared(siegeWeapon.GameEntity.GlobalPosition.AsVec2);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private static string AutoAssignDetachments(
            Team team,
            SiegeDeploymentHandler siegeDeploymentHandler)
        {
            if (team == null)
                return "Team=<null>";

            string nativeDiagnostics;
            if (siegeDeploymentHandler != null)
            {
                try
                {
                    siegeDeploymentHandler.AutoAssignDetachmentsForDeployment(team);
                    return "Native=True";
                }
                catch (Exception ex)
                {
                    nativeDiagnostics = "Error=" + ex.GetType().Name + ":" + ex.Message;
                }
            }
            else
            {
                nativeDiagnostics = "Handler=<null>";
            }

            return "Native={" + nativeDiagnostics + "} Owned={" + RunOwnedAutoAssignDetachmentsForDeployment(team) + "}";
        }

        private static string RunOwnedAutoAssignDetachmentsForDeployment(Team team)
        {
            Mission mission = team?.Mission ?? Mission.Current;
            if (team == null || mission == null)
                return "Team=" + FormatTeam(team) + " Mission=" + (mission == null ? "<null>" : mission.SceneName);

            List<Formation> formations = null;
            bool originalAllowAiTicking = mission.AllowAiTicking;
            bool originalIsTeleportingAgents = mission.IsTeleportingAgents;
            int detachmentCountBefore = 0;
            int detachmentCountAfter = 0;
            int scannedAgentCount = 0;
            int tickAgentCount = 0;
            int tickAgentErrorCount = 0;
            int usableSlotCount = 0;
            int detachableUnitCount = 0;
            int tickDetachmentsCount = 0;
            int detachedAgentCount = 0;
            string error = string.Empty;
            string firstTickAgentError = string.Empty;

            try
            {
                formations = team.FormationsIncludingEmpty.ToList();
                detachmentCountBefore = CountDetachments(team);
                mission.AllowAiTicking = true;
                mission.IsTeleportingAgents = true;

                if (detachmentCountBefore > 0)
                {
                    foreach (Formation formation in formations)
                    {
                        if (formation == null)
                            continue;

                        formation.ApplyActionOnEachUnit(agent =>
                        {
                            if (agent == null)
                                return;

                            scannedAgentCount++;
                            try
                            {
                                agent.Formation?.Team.DetachmentManager.TickAgent(agent);
                                tickAgentCount++;
                            }
                            catch (Exception ex)
                            {
                                tickAgentErrorCount++;
                                if (string.IsNullOrWhiteSpace(firstTickAgentError))
                                    firstTickAgentError = ex.GetType().Name + ":" + ex.Message;
                            }
                        });
                    }

                    foreach (var detachment in team.DetachmentManager.Detachments)
                    {
                        try
                        {
                            usableSlotCount += detachment.Item1.GetNumberOfUsableSlots();
                        }
                        catch (Exception ex)
                        {
                            error = AppendError(error, "usable-slots", ex);
                        }
                    }

                    foreach (Formation formation in team.FormationsIncludingEmpty)
                    {
                        if (formation == null)
                            continue;

                        try
                        {
                            detachableUnitCount += formation.CountOfDetachableNonPlayerUnits;
                        }
                        catch (Exception ex)
                        {
                            error = AppendError(error, "detachable-units", ex);
                        }
                    }

                    int requiredTickCount = Math.Min(usableSlotCount, detachableUnitCount);
                    for (int i = 0; i < requiredTickCount; i++)
                    {
                        try
                        {
                            team.DetachmentManager.TickDetachments();
                            tickDetachmentsCount++;
                        }
                        catch (Exception ex)
                        {
                            error = AppendError(error, "tick-detachments", ex);
                            break;
                        }
                    }

                    detachedAgentCount = CountAgentsWithDetachment(formations);
                }

                detachmentCountAfter = CountDetachments(team);
            }
            catch (Exception ex)
            {
                error = AppendError(error, "owned-auto-assign", ex);
            }
            finally
            {
                try
                {
                    mission.IsTeleportingAgents = originalIsTeleportingAgents;
                }
                catch
                {
                }

                try
                {
                    mission.AllowAiTicking = originalAllowAiTicking;
                }
                catch
                {
                }
            }

            return "Invoked=True" +
                   " MissionLoadingFinished=" + mission.IsLoadingFinished +
                   " DetachmentsBefore=" + detachmentCountBefore +
                   " DetachmentsAfter=" + detachmentCountAfter +
                   " AgentsScanned=" + scannedAgentCount +
                   " TickAgents=" + tickAgentCount +
                   " TickAgentErrors=" + tickAgentErrorCount +
                   " FirstTickAgentError=" + (string.IsNullOrWhiteSpace(firstTickAgentError) ? "<none>" : firstTickAgentError) +
                   " UsableSlots=" + usableSlotCount +
                   " DetachableUnits=" + detachableUnitCount +
                   " TickDetachments=" + tickDetachmentsCount +
                   " DetachedAgents=" + detachedAgentCount +
                   " RestoredAllowAiTicking=" + originalAllowAiTicking +
                   " RestoredTeleportingAgents=" + originalIsTeleportingAgents +
                   " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static int CountDetachments(Team team)
        {
            if (team?.DetachmentManager?.Detachments == null)
                return 0;

            int count = 0;
            try
            {
                foreach (var _ in team.DetachmentManager.Detachments)
                    count++;
            }
            catch
            {
            }

            return count;
        }

        private static int CountAgentsWithDetachment(IEnumerable<Formation> formations)
        {
            if (formations == null)
                return 0;

            int count = 0;
            try
            {
                foreach (Formation formation in formations)
                {
                    if (formation == null)
                        continue;

                    formation.ApplyActionOnEachUnit(agent =>
                    {
                        if (agent?.Detachment != null)
                            count++;
                    });
                }
            }
            catch
            {
            }

            return count;
        }

        private static string ForceUpdateTeamUnits(Team team)
        {
            if (team == null)
                return "Team=<null>";

            int formationCount = 0;
            int agentCount = 0;
            string error = string.Empty;
            try
            {
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null)
                        continue;

                    formationCount++;
                    formation.ApplyActionOnEachUnit(agent =>
                    {
                        if (agent == null)
                            return;

                        agentCount++;
                        agent.ForceUpdateCachedAndFormationValues(
                            updateOnlyMovement: false,
                            arrangementChangeAllowed: false);
                    });
                }
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ":" + ex.Message;
            }

            return "Formations=" + formationCount +
                   " Agents=" + agentCount +
                   " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static string DestroyDetachmentIfPresent(Team team, SiegeWeapon siegeWeapon)
        {
            if (team == null || siegeWeapon == null)
                return "Team=" + FormatTeam(team) + " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon);

            IDetachment detachment = siegeWeapon as IDetachment;
            if (detachment == null)
                return "Detachment=<null>";

            try
            {
                if (team.DetachmentManager != null && team.DetachmentManager.ContainsDetachment(detachment))
                {
                    team.DetachmentManager.DestroyDetachment(detachment);
                    return "Destroyed=True";
                }

                return "Destroyed=False Contains=False";
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string ReleaseAgentsFromSiegeMachineBeforeDisband(Team team, SiegeWeapon siegeWeapon)
        {
            if (team == null || siegeWeapon == null)
                return "Team=" + FormatTeam(team) + " SiegeWeapon=" + FormatSiegeWeapon(siegeWeapon);

            UsableMachine usableMachine = siegeWeapon as UsableMachine;
            IDetachment detachment = siegeWeapon as IDetachment;
            if (usableMachine == null || detachment == null)
                return "UsableMachine=" + (usableMachine != null) + " Detachment=" + (detachment != null);

            var agentsToRelease = new List<Agent>();
            int standingPointCount = 0;
            int userAgentCount = 0;
            int movingAgentCount = 0;
            int defendingAgentCount = 0;
            int detachedAgentScanCount = 0;
            int detachedAgentMatchCount = 0;
            int duplicateCount = 0;
            int skippedOtherTeamCount = 0;
            int releasedCount = 0;
            int attachedCount = 0;
            int forceUpdateCount = 0;
            string error = string.Empty;

            try
            {
                foreach (StandingPoint standingPoint in usableMachine.StandingPoints)
                {
                    if (standingPoint == null)
                        continue;

                    standingPointCount++;
                    userAgentCount += TryAddAgentForRelease(
                        agentsToRelease,
                        standingPoint.UserAgent,
                        team,
                        ref duplicateCount,
                        ref skippedOtherTeamCount);
                    movingAgentCount += TryAddAgentForRelease(
                        agentsToRelease,
                        standingPoint.MovingAgent,
                        team,
                        ref duplicateCount,
                        ref skippedOtherTeamCount);

                    if (standingPoint.DefendingAgents == null)
                        continue;

                    for (int i = standingPoint.DefendingAgents.Count - 1; i >= 0; i--)
                    {
                        defendingAgentCount += TryAddAgentForRelease(
                            agentsToRelease,
                            standingPoint.DefendingAgents[i],
                            team,
                            ref duplicateCount,
                            ref skippedOtherTeamCount);
                    }
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, "standing-points", ex);
            }

            try
            {
                foreach (Formation formation in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (formation == null)
                        continue;

                    formation.ApplyActionOnEachUnit(agent =>
                    {
                        if (agent == null)
                            return;

                        detachedAgentScanCount++;
                        if (!ReferenceEquals(agent.Detachment, detachment))
                            return;

                        detachedAgentMatchCount += TryAddAgentForRelease(
                            agentsToRelease,
                            agent,
                            team,
                            ref duplicateCount,
                            ref skippedOtherTeamCount);
                    });
                }
            }
            catch (Exception ex)
            {
                error = AppendError(error, "formation-scan", ex);
            }

            foreach (Agent agent in agentsToRelease)
            {
                if (agent == null)
                    continue;

                try
                {
                    Formation formation = agent.Formation;
                    detachment.RemoveAgent(agent);
                    releasedCount++;

                    if (formation != null && agent.IsDetachedFromFormation)
                    {
                        formation.AttachUnit(agent);
                        attachedCount++;
                    }

                    agent.ForceUpdateCachedAndFormationValues(
                        updateOnlyMovement: false,
                        arrangementChangeAllowed: false);
                    forceUpdateCount++;
                }
                catch (Exception ex)
                {
                    error = AppendError(error, "release-agent-" + (agent.Index >= 0 ? agent.Index.ToString() : "unknown"), ex);
                }
            }

            return "StandingPoints=" + standingPointCount +
                   " UserAgents=" + userAgentCount +
                   " MovingAgents=" + movingAgentCount +
                   " DefendingAgents=" + defendingAgentCount +
                   " DetachedScanned=" + detachedAgentScanCount +
                   " DetachedMatches=" + detachedAgentMatchCount +
                   " Selected=" + agentsToRelease.Count +
                   " Duplicates=" + duplicateCount +
                   " SkippedOtherTeam=" + skippedOtherTeamCount +
                   " Released=" + releasedCount +
                   " Attached=" + attachedCount +
                   " ForceUpdated=" + forceUpdateCount +
                   " Error=" + (string.IsNullOrWhiteSpace(error) ? "<none>" : error);
        }

        private static int TryAddAgentForRelease(
            List<Agent> agentsToRelease,
            Agent agent,
            Team team,
            ref int duplicateCount,
            ref int skippedOtherTeamCount)
        {
            if (agentsToRelease == null || agent == null)
                return 0;

            if (team != null && agent.Team != null && agent.Team != team)
            {
                skippedOtherTeamCount++;
                return 0;
            }

            if (agentsToRelease.Contains(agent))
            {
                duplicateCount++;
                return 0;
            }

            agentsToRelease.Add(agent);
            return 1;
        }

        private static string SyncSiegeControllerAfterDeploy(
            BattleSideEnum side,
            SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            if (!TryResolveSiegeWeaponsController(side, out IMissionSiegeWeaponsController weaponsController, out string controllerDiagnostics))
                return controllerDiagnostics;

            object destructionComponent = GetDestructionComponent(siegeWeapon);
            string stateDiagnostics = BuildControllerStateForWeapon(
                weaponsController,
                siegeWeapon,
                destructionComponent,
                out System.Collections.IList allWeapons,
                out System.Collections.IList undeployedWeapons,
                out System.Collections.IDictionary deployedWeapons,
                out _,
                out bool containsDeployedDestructionComponent,
                out bool containsUndeployedWeapon);

            if (containsDeployedDestructionComponent)
                return "AlreadyDeployed=True State={" + stateDiagnostics + "}";

            string nativeDiagnostics = "<skipped>";
            if (containsUndeployedWeapon)
            {
                try
                {
                    weaponsController.OnWeaponDeployed(siegeWeapon);
                    return "Native=True State={" + stateDiagnostics + "}";
                }
                catch (Exception ex)
                {
                    nativeDiagnostics = ex.GetType().Name + ":" + ex.Message;
                }
            }

            string manualDiagnostics = EnsureDeployedWeapon(
                siegeWeapon,
                destructionComponent,
                allWeapons,
                undeployedWeapons,
                deployedWeapons);
            return "Native=" + nativeDiagnostics +
                   " State={" + stateDiagnostics + "}" +
                   " Manual={" + manualDiagnostics + "}";
        }

        private static string SyncSiegeControllerAfterUndeploy(
            BattleSideEnum side,
            SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "SiegeWeapon=<null>";

            if (!TryResolveSiegeWeaponsController(side, out IMissionSiegeWeaponsController weaponsController, out string controllerDiagnostics))
                return controllerDiagnostics;

            object destructionComponent = GetDestructionComponent(siegeWeapon);
            string stateDiagnostics = BuildControllerStateForWeapon(
                weaponsController,
                siegeWeapon,
                destructionComponent,
                out System.Collections.IList allWeapons,
                out System.Collections.IList undeployedWeapons,
                out System.Collections.IDictionary deployedWeapons,
                out MissionSiegeWeapon deployedMissionWeapon,
                out bool containsDeployedDestructionComponent,
                out bool containsUndeployedWeapon);

            string nativeDiagnostics = "<skipped>";
            if (containsDeployedDestructionComponent)
            {
                try
                {
                    weaponsController.OnWeaponUndeployed(siegeWeapon);
                    return "Native=True State={" + stateDiagnostics + "}";
                }
                catch (Exception ex)
                {
                    nativeDiagnostics = ex.GetType().Name + ":" + ex.Message;
                }
            }

            string manualDiagnostics = EnsureUndeployedWeapon(
                siegeWeapon,
                destructionComponent,
                allWeapons,
                undeployedWeapons,
                deployedWeapons,
                deployedMissionWeapon);
            return "Native=" + nativeDiagnostics +
                   " State={" + stateDiagnostics + "}" +
                   " Manual={" + manualDiagnostics + "}" +
                   " UndeployedBefore=" + containsUndeployedWeapon;
        }

        private static bool TryResolveSiegeWeaponsController(
            BattleSideEnum side,
            out IMissionSiegeWeaponsController weaponsController,
            out string diagnostics)
        {
            weaponsController = null;
            diagnostics = string.Empty;
            try
            {
                Mission mission = Mission.Current;
                MissionSiegeEnginesLogic siegeEnginesLogic = mission?.GetMissionBehavior<MissionSiegeEnginesLogic>();
                weaponsController = siegeEnginesLogic?.GetSiegeWeaponsController(side);
                diagnostics = weaponsController == null
                    ? "Controller=<null>"
                    : "Controller=" + weaponsController.GetType().FullName;
                return weaponsController != null;
            }
            catch (Exception ex)
            {
                diagnostics = "ControllerError=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static object GetDestructionComponent(SiegeWeapon siegeWeapon)
        {
            try
            {
                return siegeWeapon?.DestructionComponent;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildControllerStateForWeapon(
            IMissionSiegeWeaponsController weaponsController,
            SiegeWeapon siegeWeapon,
            object destructionComponent,
            out System.Collections.IList allWeapons,
            out System.Collections.IList undeployedWeapons,
            out System.Collections.IDictionary deployedWeapons,
            out MissionSiegeWeapon deployedMissionWeapon,
            out bool containsDeployedDestructionComponent,
            out bool containsUndeployedWeapon)
        {
            allWeapons = SiegeControllerWeaponsField?.GetValue(weaponsController) as System.Collections.IList;
            undeployedWeapons = SiegeControllerUndeployedWeaponsField?.GetValue(weaponsController) as System.Collections.IList;
            deployedWeapons = SiegeControllerDeployedWeaponsField?.GetValue(weaponsController) as System.Collections.IDictionary;
            deployedMissionWeapon = null;
            containsDeployedDestructionComponent = false;
            containsUndeployedWeapon = false;

            SiegeEngineType weaponType = GetSiegeEngineType(siegeWeapon);
            try
            {
                if (deployedWeapons != null &&
                    destructionComponent != null &&
                    deployedWeapons.Contains(destructionComponent))
                {
                    containsDeployedDestructionComponent = true;
                    deployedMissionWeapon = deployedWeapons[destructionComponent] as MissionSiegeWeapon;
                }
            }
            catch
            {
            }

            containsUndeployedWeapon =
                FindMissionSiegeWeaponByType(undeployedWeapons, weaponType) != null;

            return "All=" + (allWeapons?.Count.ToString() ?? "<null>") +
                   " Undeployed=" + (undeployedWeapons?.Count.ToString() ?? "<null>") +
                   " Deployed=" + (deployedWeapons?.Count.ToString() ?? "<null>") +
                   " ContainsDeployed=" + containsDeployedDestructionComponent +
                   " ContainsUndeployedType=" + containsUndeployedWeapon +
                   " Type=" + FormatSiegeEngineType(weaponType);
        }

        private static string EnsureDeployedWeapon(
            SiegeWeapon siegeWeapon,
            object destructionComponent,
            System.Collections.IList allWeapons,
            System.Collections.IList undeployedWeapons,
            System.Collections.IDictionary deployedWeapons)
        {
            if (deployedWeapons == null)
                return "DeployedDictionary=<null>";

            if (destructionComponent == null)
                return "DestructionComponent=<null>";

            SiegeEngineType weaponType = GetSiegeEngineType(siegeWeapon);
            MissionSiegeWeapon missionSiegeWeapon =
                FindMissionSiegeWeaponByType(undeployedWeapons, weaponType) ??
                FindReusableMissionSiegeWeapon(allWeapons, undeployedWeapons, deployedWeapons, weaponType);
            if (missionSiegeWeapon == null)
                return "MissionSiegeWeapon=<null>";

            try
            {
                if (undeployedWeapons != null && ContainsMissionSiegeWeapon(undeployedWeapons, missionSiegeWeapon))
                    undeployedWeapons.Remove(missionSiegeWeapon);

                if (!deployedWeapons.Contains(destructionComponent))
                    deployedWeapons.Add(destructionComponent, missionSiegeWeapon);

                return "Added=True MissionWeapon=" + FormatMissionSiegeWeapon(missionSiegeWeapon, weaponType);
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string EnsureUndeployedWeapon(
            SiegeWeapon siegeWeapon,
            object destructionComponent,
            System.Collections.IList allWeapons,
            System.Collections.IList undeployedWeapons,
            System.Collections.IDictionary deployedWeapons,
            MissionSiegeWeapon preferredMissionWeapon)
        {
            if (undeployedWeapons == null)
                return "UndeployedList=<null>";

            SiegeEngineType weaponType = GetSiegeEngineType(siegeWeapon);
            if (FindMissionSiegeWeaponByType(undeployedWeapons, weaponType) != null)
                return "AlreadyUndeployed=True";

            MissionSiegeWeapon missionSiegeWeapon = preferredMissionWeapon ??
                FindReusableMissionSiegeWeapon(allWeapons, undeployedWeapons, deployedWeapons, weaponType);
            if (missionSiegeWeapon == null)
                return "MissionSiegeWeapon=<null>";

            try
            {
                if (!ContainsMissionSiegeWeapon(undeployedWeapons, missionSiegeWeapon))
                    undeployedWeapons.Add(missionSiegeWeapon);

                if (deployedWeapons != null &&
                    destructionComponent != null &&
                    deployedWeapons.Contains(destructionComponent))
                {
                    deployedWeapons.Remove(destructionComponent);
                }

                return "Added=True MissionWeapon=" + FormatMissionSiegeWeapon(missionSiegeWeapon, weaponType);
            }
            catch (Exception ex)
            {
                return "Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static MissionSiegeWeapon FindReusableMissionSiegeWeapon(
            System.Collections.IList allWeapons,
            System.Collections.IList undeployedWeapons,
            System.Collections.IDictionary deployedWeapons,
            SiegeEngineType weaponType)
        {
            MissionSiegeWeapon fromDeployed =
                FindMissionSiegeWeaponByType(GetDictionaryValues(deployedWeapons), weaponType);
            if (fromDeployed != null)
                return fromDeployed;

            if (allWeapons == null)
                return null;

            foreach (object entry in allWeapons)
            {
                MissionSiegeWeapon missionSiegeWeapon = ExtractMissionSiegeWeapon(entry);
                if (!MissionSiegeWeaponMatches(missionSiegeWeapon, weaponType))
                    continue;

                if (ContainsMissionSiegeWeapon(undeployedWeapons, missionSiegeWeapon) ||
                    ContainsMissionSiegeWeapon(GetDictionaryValues(deployedWeapons), missionSiegeWeapon))
                {
                    continue;
                }

                return missionSiegeWeapon;
            }

            return FindMissionSiegeWeaponByType(allWeapons, weaponType);
        }

        private static System.Collections.IEnumerable GetDictionaryValues(System.Collections.IDictionary dictionary)
        {
            if (dictionary == null)
                yield break;

            foreach (object entry in dictionary)
            {
                if (entry is System.Collections.DictionaryEntry dictionaryEntry)
                    yield return dictionaryEntry.Value;
            }
        }

        private static MissionSiegeWeapon FindMissionSiegeWeaponByType(
            System.Collections.IEnumerable entries,
            SiegeEngineType weaponType)
        {
            if (entries == null)
                return null;

            foreach (object entry in entries)
            {
                MissionSiegeWeapon missionSiegeWeapon = ExtractMissionSiegeWeapon(entry);
                if (MissionSiegeWeaponMatches(missionSiegeWeapon, weaponType))
                    return missionSiegeWeapon;
            }

            return null;
        }

        private static bool ContainsMissionSiegeWeapon(
            System.Collections.IEnumerable entries,
            MissionSiegeWeapon missionSiegeWeapon)
        {
            if (entries == null || missionSiegeWeapon == null)
                return false;

            foreach (object entry in entries)
            {
                if (ReferenceEquals(ExtractMissionSiegeWeapon(entry), missionSiegeWeapon))
                    return true;
            }

            return false;
        }

        private static MissionSiegeWeapon ExtractMissionSiegeWeapon(object entry)
        {
            if (entry == null)
                return null;

            MissionSiegeWeapon missionSiegeWeapon = entry as MissionSiegeWeapon;
            if (missionSiegeWeapon != null)
                return missionSiegeWeapon;

            try
            {
                PropertyInfo valueProperty = entry.GetType().GetProperty(
                    "Value",
                    BindingFlags.Instance | BindingFlags.Public);
                return valueProperty?.GetValue(entry, null) as MissionSiegeWeapon;
            }
            catch
            {
                return null;
            }
        }

        private static bool MissionSiegeWeaponMatches(
            MissionSiegeWeapon missionSiegeWeapon,
            SiegeEngineType weaponType)
        {
            if (missionSiegeWeapon == null || weaponType == null)
                return false;

            if (ReferenceEquals(missionSiegeWeapon.Type, weaponType))
                return true;

            return string.Equals(
                missionSiegeWeapon.Type?.StringId,
                weaponType.StringId,
                StringComparison.Ordinal);
        }

        private static SiegeEngineType GetSiegeEngineType(SiegeWeapon siegeWeapon)
        {
            try
            {
                return siegeWeapon?.GetSiegeEngineType();
            }
            catch
            {
                return null;
            }
        }

        private static string FormatDeploymentPoint(DeploymentPoint deploymentPoint)
        {
            if (deploymentPoint == null)
                return "<null>";

            return "Id=" + deploymentPoint.Id +
                   "/Side=" + deploymentPoint.Side +
                   "/Disabled=" + deploymentPoint.IsDisabled +
                   "/Deployed=" + deploymentPoint.IsDeployed;
        }

        private static string FormatSiegeWeapon(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "<null>";

            return "Id=" + siegeWeapon.Id +
                   "/Side=" + siegeWeapon.Side +
                   "/Disabled=" + siegeWeapon.IsDisabled +
                   "/Type=" + (siegeWeapon.GetSiegeEngineType()?.StringId ?? "<null>");
        }

        private static string FormatSynchedObject(SynchedMissionObject missionObject)
        {
            if (missionObject == null)
                return "<null>";

            return "Id=" + missionObject.Id + "/Type=" + missionObject.GetType().Name;
        }

        private static string FormatTeam(Team team)
        {
            if (team == null)
                return "<null>";

            return team.Side + "#" + team.TeamIndex;
        }

        private static string FormatMissionSiegeWeapon(
            MissionSiegeWeapon missionSiegeWeapon,
            SiegeEngineType requestedType)
        {
            if (missionSiegeWeapon == null)
                return "<null>";

            return "Index=" + missionSiegeWeapon.Index +
                   "/Type=" + FormatSiegeEngineType(missionSiegeWeapon.Type) +
                   "/Exact=" + ReferenceEquals(missionSiegeWeapon.Type, requestedType) +
                   "/Health=" + FormatFloat(missionSiegeWeapon.Health) +
                   "/Initial=" + FormatFloat(missionSiegeWeapon.InitialHealth) +
                   "/Max=" + FormatFloat(missionSiegeWeapon.MaxHealth);
        }

        private static string FormatSiegeEngineType(SiegeEngineType siegeEngineType)
        {
            if (siegeEngineType == null)
                return "<null>";

            string stringId = string.IsNullOrWhiteSpace(siegeEngineType.StringId)
                ? "<empty>"
                : siegeEngineType.StringId;
            return stringId +
                   "/InstanceHash=" +
                   System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(siegeEngineType);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string AppendError(string current, string step, Exception exception)
        {
            string next = (step ?? "unknown") + ":" +
                          (exception == null ? "<null>" : exception.GetType().Name + ":" + exception.Message);
            return string.IsNullOrWhiteSpace(current) ? next : current + "|" + next;
        }
    }
}
