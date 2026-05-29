using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellMissionScoreboardComponent : MissionScoreboardComponent
    {
        private static readonly FieldInfo RoundPreEndingEventField = typeof(MultiplayerRoundComponent)
            .GetField("OnPreRoundEnding", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo SetPeerAsMvpMethod = typeof(MissionScoreboardComponent)
            .GetMethod("SetPeerAsMVP", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Dictionary<int, int> _lastRoundScoreByPeerIndex = new Dictionary<int, int>();

        public ListedShellMissionScoreboardComponent()
            : base(new ListedShellScoreboardData())
        {
        }

        public override void AfterStart()
        {
            base.AfterStart();

            if (!GameNetwork.IsServerOrRecorder)
                return;

            MultiplayerRoundComponent roundComponent =
                Mission?.GetMissionBehavior<MissionMultiplayerGameModeBaseClient>()?.RoundComponent as MultiplayerRoundComponent;
            if (roundComponent == null)
                return;

            TryReplaceNativePreRoundEndingHandler(roundComponent);
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

        private void TryReplaceNativePreRoundEndingHandler(MultiplayerRoundComponent roundComponent)
        {
            try
            {
                MulticastDelegate currentDelegate = RoundPreEndingEventField?.GetValue(roundComponent) as MulticastDelegate;
                if (currentDelegate != null)
                {
                    foreach (Delegate handler in currentDelegate.GetInvocationList())
                    {
                        if (!ReferenceEquals(handler.Target, this) ||
                            !string.Equals(handler.Method?.Name, "OnPreRoundEnding", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        roundComponent.OnPreRoundEnding -= (Action)handler;
                    }
                }

                roundComponent.OnPreRoundEnding -= HandleListedShellPreRoundEnding;
                roundComponent.OnPreRoundEnding += HandleListedShellPreRoundEnding;
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellMissionScoreboardComponent: failed to replace native pre-round-ending MVP handler: " + ex.Message);
            }
        }

        private void HandleListedShellPreRoundEnding()
        {
            try
            {
                KeyValuePair<MissionPeer, int> bestAttacker = default;
                KeyValuePair<MissionPeer, int> bestDefender = default;

                MissionScoreboardSide[] sides = Sides;
                if (sides == null)
                    return;

                for (int i = 0; i < sides.Length; i++)
                {
                    MissionScoreboardSide side = sides[i];
                    if (side == null)
                        continue;

                    KeyValuePair<MissionPeer, int> bestForSide = CalculateBestListedShellMvpForSide(side);
                    if (side.Side == BattleSideEnum.Attacker)
                    {
                        if (bestAttacker.Key == null || bestAttacker.Value < bestForSide.Value)
                            bestAttacker = bestForSide;
                    }
                    else if (side.Side == BattleSideEnum.Defender)
                    {
                        if (bestDefender.Key == null || bestDefender.Value < bestForSide.Value)
                            bestDefender = bestForSide;
                    }
                }

                TrySetListedShellPeerAsMvp(bestAttacker.Key);
                TrySetListedShellPeerAsMvp(bestDefender.Key);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellMissionScoreboardComponent: listed-shell MVP selection failed: " + ex.Message);
            }
        }

        private KeyValuePair<MissionPeer, int> CalculateBestListedShellMvpForSide(MissionScoreboardSide side)
        {
            KeyValuePair<MissionPeer, int> result = default;
            IEnumerable<MissionPeer> players = side?.Players;
            if (players == null)
                return result;

            foreach (MissionPeer player in players)
            {
                if (player == null)
                    continue;

                int currentScore = ResolveRuntimeBackedScore(player);
                int peerIndex = player.GetNetworkPeer()?.Index ?? -1;
                int previousScore = 0;
                if (peerIndex >= 0)
                    _lastRoundScoreByPeerIndex.TryGetValue(peerIndex, out previousScore);

                int delta = currentScore - previousScore;
                if (peerIndex >= 0)
                    _lastRoundScoreByPeerIndex[peerIndex] = currentScore;

                if (result.Key == null || result.Value < delta)
                    result = new KeyValuePair<MissionPeer, int>(player, delta);
            }

            return result;
        }

        private static int ResolveRuntimeBackedScore(MissionPeer player)
        {
            if (player != null &&
                CoopBattlePeerStatsRuntimeState.TryGetState(player, out PeerStatsRuntimeState runtimeState))
            {
                return runtimeState.Score;
            }

            return player?.Score ?? 0;
        }

        private void TrySetListedShellPeerAsMvp(MissionPeer peer)
        {
            if (peer == null || SetPeerAsMvpMethod == null)
                return;

            try
            {
                SetPeerAsMvpMethod.Invoke(this, new object[] { peer });
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellMissionScoreboardComponent: failed to set listed-shell MVP through native scoreboard contract: " + ex.Message);
            }
        }
    }
}
