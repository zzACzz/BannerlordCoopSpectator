namespace CoopSpectator.Infrastructure
{
    internal enum CoopCommanderDeploymentReadiness
    {
        Ready,
        WaitingForMission,
        WaitingForSide,
        WaitingForVillageBoundary,
        WaitingForTeam,
        MissingRequiredBannerBearerLogic,
        MissingRequiredOrderController,
        WaitingForCommanderEntry,
        WaitingForCommanderAgent,
        WaitingForCommanderArmy
    }

    internal static class CoopCommanderDeploymentReadinessContract
    {
        public static CoopCommanderDeploymentReadiness EvaluatePrerequisites(
            bool hasMission,
            bool hasSide,
            bool isVillageBoundaryReady,
            bool hasTeam,
            bool hasBannerBearerLogic,
            bool hasOrderController,
            bool hasCommanderEntry,
            bool hasCommanderAgent)
        {
            if (!hasMission)
                return CoopCommanderDeploymentReadiness.WaitingForMission;
            if (!hasSide)
                return CoopCommanderDeploymentReadiness.WaitingForSide;
            if (!isVillageBoundaryReady)
                return CoopCommanderDeploymentReadiness.WaitingForVillageBoundary;
            if (!hasTeam)
                return CoopCommanderDeploymentReadiness.WaitingForTeam;
            if (!hasBannerBearerLogic)
                return CoopCommanderDeploymentReadiness.MissingRequiredBannerBearerLogic;
            if (!hasOrderController)
                return CoopCommanderDeploymentReadiness.MissingRequiredOrderController;
            if (!hasCommanderEntry)
                return CoopCommanderDeploymentReadiness.WaitingForCommanderEntry;
            if (!hasCommanderAgent)
                return CoopCommanderDeploymentReadiness.WaitingForCommanderAgent;

            return CoopCommanderDeploymentReadiness.Ready;
        }

        public static CoopCommanderDeploymentReadiness EvaluateArmy(
            int formationsWithUnits,
            int selectableFormationsWithUnits,
            int physicalClassUnitCount)
        {
            return formationsWithUnits > 0 &&
                   selectableFormationsWithUnits > 0 &&
                   physicalClassUnitCount > 0
                ? CoopCommanderDeploymentReadiness.Ready
                : CoopCommanderDeploymentReadiness.WaitingForCommanderArmy;
        }

        public static bool ShouldReplaceCurrentPresentation(
            CoopCommanderDeploymentReadiness readiness)
        {
            return readiness == CoopCommanderDeploymentReadiness.Ready;
        }

        public static bool IsTransientWait(
            CoopCommanderDeploymentReadiness readiness)
        {
            switch (readiness)
            {
                case CoopCommanderDeploymentReadiness.WaitingForMission:
                case CoopCommanderDeploymentReadiness.WaitingForSide:
                case CoopCommanderDeploymentReadiness.WaitingForVillageBoundary:
                case CoopCommanderDeploymentReadiness.WaitingForTeam:
                case CoopCommanderDeploymentReadiness.WaitingForCommanderEntry:
                case CoopCommanderDeploymentReadiness.WaitingForCommanderAgent:
                case CoopCommanderDeploymentReadiness.WaitingForCommanderArmy:
                    return true;
                default:
                    return false;
            }
        }
    }
}
