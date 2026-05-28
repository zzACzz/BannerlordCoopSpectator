using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellSpawnFrameBehavior : SpawnFrameBehaviorBase
    {
        public override MatrixFrame GetSpawnFrame(Team team, bool hasMount, bool isInitialSpawn)
        {
            return GetSpawnFrameFromSpawnPoints((IList<GameEntity>)SpawnPoints.ToList(), team, hasMount);
        }
    }
}
