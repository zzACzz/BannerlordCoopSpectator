using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellSpawnFrameBehavior : SpawnFrameBehaviorBase
    {
        internal static bool TryResolveSpawnFrame(
            Mission mission,
            Team team,
            bool hasMount,
            bool isInitialSpawn,
            out MatrixFrame spawnFrame)
        {
            spawnFrame = MatrixFrame.Identity;
            if (mission?.Scene == null || team == null)
                return false;

            var behavior = new ListedShellSpawnFrameBehavior
            {
                SpawnPoints = mission.Scene.FindEntitiesWithTag("spawnpoint")
            };
            spawnFrame = behavior.GetSpawnFrame(team, hasMount, isInitialSpawn);
            return !spawnFrame.IsIdentity;
        }

        public override MatrixFrame GetSpawnFrame(Team team, bool hasMount, bool isInitialSpawn)
        {
            IList<GameEntity> spawnPoints = SpawnPoints?.ToList();
            if (spawnPoints == null || spawnPoints.Count == 0)
                return MatrixFrame.Identity;

            return GetSpawnFrameFromSpawnPoints(spawnPoints, team, hasMount);
        }
    }
}
