namespace CoopSpectator.Infrastructure
{
    internal readonly struct ExactSiegeLadderMerlonVisualParityInput
    {
        public ExactSiegeLadderMerlonVisualParityInput(
            bool isExactCampaignSiegeAssault,
            bool isRemoteClient,
            bool isDestructableComponent,
            bool isSceneObject,
            bool isDeclaredIndestructibleMerlon,
            bool hasEligibleTargetWall,
            bool allowVisibilityUpdate,
            bool serverRequestedVisible,
            bool serverMarkedDisabled,
            float serverHitPoint,
            int serverDestructionState,
            bool localMarkedDisabled,
            bool localCanBeDestroyedInitially,
            bool localIsDestroyed,
            bool localRootHasPhysicsDescription,
            bool localRootVisible,
            bool localRootPhysicsEnabled)
        {
            IsExactCampaignSiegeAssault = isExactCampaignSiegeAssault;
            IsRemoteClient = isRemoteClient;
            IsDestructableComponent = isDestructableComponent;
            IsSceneObject = isSceneObject;
            IsDeclaredIndestructibleMerlon = isDeclaredIndestructibleMerlon;
            HasEligibleTargetWall = hasEligibleTargetWall;
            AllowVisibilityUpdate = allowVisibilityUpdate;
            ServerRequestedVisible = serverRequestedVisible;
            ServerMarkedDisabled = serverMarkedDisabled;
            ServerHitPoint = serverHitPoint;
            ServerDestructionState = serverDestructionState;
            LocalMarkedDisabled = localMarkedDisabled;
            LocalCanBeDestroyedInitially = localCanBeDestroyedInitially;
            LocalIsDestroyed = localIsDestroyed;
            LocalRootHasPhysicsDescription = localRootHasPhysicsDescription;
            LocalRootVisible = localRootVisible;
            LocalRootPhysicsEnabled = localRootPhysicsEnabled;
        }

        public bool IsExactCampaignSiegeAssault { get; }
        public bool IsRemoteClient { get; }
        public bool IsDestructableComponent { get; }
        public bool IsSceneObject { get; }
        public bool IsDeclaredIndestructibleMerlon { get; }
        public bool HasEligibleTargetWall { get; }
        public bool AllowVisibilityUpdate { get; }
        public bool ServerRequestedVisible { get; }
        public bool ServerMarkedDisabled { get; }
        public float ServerHitPoint { get; }
        public int ServerDestructionState { get; }
        public bool LocalMarkedDisabled { get; }
        public bool LocalCanBeDestroyedInitially { get; }
        public bool LocalIsDestroyed { get; }
        public bool LocalRootHasPhysicsDescription { get; }
        public bool LocalRootVisible { get; }
        public bool LocalRootPhysicsEnabled { get; }
    }

    internal static class ExactSiegeLadderMerlonVisualParityContract
    {
        public static bool ShouldRestoreVisibleDisabledIndestructibleMerlon(
            ExactSiegeLadderMerlonVisualParityInput input)
        {
            return
                input.IsExactCampaignSiegeAssault &&
                input.IsRemoteClient &&
                input.IsDestructableComponent &&
                input.IsSceneObject &&
                input.IsDeclaredIndestructibleMerlon &&
                input.HasEligibleTargetWall &&
                input.AllowVisibilityUpdate &&
                input.ServerRequestedVisible &&
                input.ServerMarkedDisabled &&
                input.ServerHitPoint > 0f &&
                input.ServerDestructionState == 0 &&
                input.LocalMarkedDisabled &&
                !input.LocalCanBeDestroyedInitially &&
                !input.LocalIsDestroyed &&
                input.LocalRootHasPhysicsDescription &&
                (!input.LocalRootVisible || !input.LocalRootPhysicsEnabled);
        }
    }
}
