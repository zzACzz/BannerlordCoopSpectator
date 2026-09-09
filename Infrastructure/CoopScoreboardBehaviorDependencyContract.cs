namespace CoopSpectator.Infrastructure
{
    public enum CoopScoreboardBehaviorDependencyAction
    {
        None = 0,
        InsertServerBridge = 1
    }

    public static class CoopScoreboardBehaviorDependencyContract
    {
        public static CoopScoreboardBehaviorDependencyAction Resolve(
            bool isServerOrRecorder,
            bool hasScoreboard,
            bool hasGameModeClient)
        {
            return isServerOrRecorder && hasScoreboard && !hasGameModeClient
                ? CoopScoreboardBehaviorDependencyAction.InsertServerBridge
                : CoopScoreboardBehaviorDependencyAction.None;
        }

        public static bool IsSatisfied(
            bool isServerOrRecorder,
            bool hasScoreboard,
            bool hasGameModeClient)
        {
            return Resolve(isServerOrRecorder, hasScoreboard, hasGameModeClient) ==
                   CoopScoreboardBehaviorDependencyAction.None;
        }
    }
}
