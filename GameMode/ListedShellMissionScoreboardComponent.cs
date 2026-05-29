using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellMissionScoreboardComponent : MissionScoreboardComponent
    {
        public ListedShellMissionScoreboardComponent()
            : base(new ListedShellScoreboardData())
        {
        }

        public override void OnScoreHit(
            Agent affectedAgent,
            Agent affectorAgent,
            WeaponComponentData attackerWeapon,
            bool isBlocked,
            bool isSiegeEngineHit,
            in Blow blow,
            in AttackCollisionData collisionData,
            float damagedHp,
            float hitDistance,
            float shotDifficulty)
        {
            if (ListedShellLobbyRuntime.TryHandleListedShellScoreHit(
                this,
                affectedAgent,
                affectorAgent,
                isBlocked,
                damagedHp))
            {
                return;
            }

            base.OnScoreHit(
                affectedAgent,
                affectorAgent,
                attackerWeapon,
                isBlocked,
                isSiegeEngineHit,
                blow,
                collisionData,
                damagedHp,
                hitDistance,
                shotDifficulty);
        }
    }
}
