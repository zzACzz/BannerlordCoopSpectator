using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactSiegeLadderMerlonVisualParityRuntime
    {
        public static void TryRestoreAfterRead(
            DestructableComponent destructable,
            (BaseSynchedMissionObjectReadableRecord,
                ISynchedMissionObjectReadableRecord) readableRecord,
            bool allowVisibilityUpdate)
        {
            Mission mission = Mission.Current;
            if (destructable == null ||
                mission == null ||
                !GameNetwork.IsClient ||
                GameNetwork.IsServer ||
                !CoopSiegeLadderInteractionRuntime.IsExactSiegeAssaultContext(mission) ||
                destructable.Id.Id < 0 ||
                destructable.CreatedAtRuntime ||
                !destructable.GameEntity.IsValid ||
                !(readableRecord.Item2 is DestructableComponent.DestructableComponentRecord
                    destructableRecord))
            {
                return;
            }

            bool isDeclaredIndestructibleMerlon =
                TryResolveEligibleNativeLadderWallRelation(
                    mission,
                    destructable.GameEntity,
                    out bool hasEligibleTargetWall);

            WeakGameEntity root = destructable.GameEntity;
            BodyFlags bodyFlags = root.BodyFlag;
            BodyFlags physicsDescriptionBodyFlags = root.PhysicsDescBodyFlag;
            bool hasPhysicsDescription = physicsDescriptionBodyFlags != BodyFlags.None;
            bool rootPhysicsEnabled =
                hasPhysicsDescription &&
                (bodyFlags & BodyFlags.Disabled) == 0 &&
                (bodyFlags & BodyFlags.DontTransferToPhysicsEngine) == 0;

            var input = new ExactSiegeLadderMerlonVisualParityInput(
                isExactCampaignSiegeAssault: true,
                isRemoteClient: true,
                isDestructableComponent: true,
                isSceneObject: true,
                isDeclaredIndestructibleMerlon: isDeclaredIndestructibleMerlon,
                hasEligibleTargetWall: hasEligibleTargetWall,
                allowVisibilityUpdate: allowVisibilityUpdate,
                serverRequestedVisible: readableRecord.Item1.SetVisibilityExcludeParents,
                serverMarkedDisabled: readableRecord.Item1.IsDisabled,
                serverHitPoint: destructableRecord.HitPoint,
                serverDestructionState: destructableRecord.DestructionState,
                localMarkedDisabled: destructable.IsDisabled,
                localCanBeDestroyedInitially: destructable.CanBeDestroyedInitially,
                localIsDestroyed: destructable.IsDestroyed,
                localRootHasPhysicsDescription: hasPhysicsDescription,
                localRootVisible: root.GetVisibilityExcludeParents(),
                localRootPhysicsEnabled: rootPhysicsEnabled);

            if (!ExactSiegeLadderMerlonVisualParityContract
                    .ShouldRestoreVisibleDisabledIndestructibleMerlon(input))
            {
                return;
            }

            root.SetVisibilityExcludeParents(true);
            root.SetPhysicsState(isEnabled: true, setChildren: false);
        }

        private static bool TryResolveEligibleNativeLadderWallRelation(
            Mission mission,
            WeakGameEntity merlonEntity,
            out bool hasEligibleTargetWall)
        {
            hasEligibleTargetWall = false;
            if (!merlonEntity.IsValid)
                return false;

            bool isDeclaredIndestructibleMerlon = false;
            WeakGameEntity matchedWallEntity = WeakGameEntity.Invalid;
            IEnumerable<SiegeLadder> ladders =
                mission.MissionObjects.FindAllWithType<SiegeLadder>();
            foreach (SiegeLadder ladder in ladders)
            {
                if (ladder == null ||
                    string.IsNullOrWhiteSpace(ladder.IndestructibleMerlonsTag) ||
                    !merlonEntity.HasTag(ladder.IndestructibleMerlonsTag))
                {
                    continue;
                }

                isDeclaredIndestructibleMerlon = true;
                if (!(ladder.TargetCastlePosition is WallSegment wallSegment) ||
                    !wallSegment.GameEntity.IsValid ||
                    !IsEntityInsideWall(merlonEntity, wallSegment.GameEntity))
                {
                    continue;
                }

                if (wallSegment.IsDisabled ||
                    wallSegment.IsBreachedWall ||
                    !wallSegment.GameEntity.IsVisibleIncludeParents())
                {
                    return isDeclaredIndestructibleMerlon;
                }

                if (matchedWallEntity.IsValid && matchedWallEntity != wallSegment.GameEntity)
                {
                    hasEligibleTargetWall = false;
                    return isDeclaredIndestructibleMerlon;
                }

                matchedWallEntity = wallSegment.GameEntity;
                hasEligibleTargetWall = true;
            }

            return isDeclaredIndestructibleMerlon;
        }

        private static bool IsEntityInsideWall(
            WeakGameEntity entity,
            WeakGameEntity wallEntity)
        {
            for (WeakGameEntity current = entity;
                 current.IsValid;
                 current = current.Parent)
            {
                if (current == wallEntity)
                    return true;
            }

            return false;
        }
    }
}
