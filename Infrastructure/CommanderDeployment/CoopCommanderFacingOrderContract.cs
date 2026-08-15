namespace CoopSpectator.Infrastructure
{
    internal enum CoopCommanderFacingOrderDecision
    {
        FaceEnemy,
        FaceDirection,
        Suppress
    }

    internal static class CoopCommanderFacingOrderContract
    {
        public static CoopCommanderFacingOrderDecision Evaluate(
            bool isFacingEnemyActive,
            bool hasWorldPosition,
            bool isWorldPositionValid)
        {
            if (!isFacingEnemyActive)
                return CoopCommanderFacingOrderDecision.FaceEnemy;

            return hasWorldPosition && isWorldPositionValid
                ? CoopCommanderFacingOrderDecision.FaceDirection
                : CoopCommanderFacingOrderDecision.Suppress;
        }
    }
}
