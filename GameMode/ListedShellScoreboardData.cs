using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellScoreboardData : IScoreboardData
    {
        private static readonly TDMScoreboardData NativeScoreboardData = new TDMScoreboardData();

        public MissionScoreboardComponent.ScoreboardHeader[] GetScoreboardHeaders()
        {
            MissionScoreboardComponent.ScoreboardHeader[] headers = NativeScoreboardData.GetScoreboardHeaders();
            if (headers == null || headers.Length < 8)
                return headers ?? Array.Empty<MissionScoreboardComponent.ScoreboardHeader>();

            headers[4] = CreatePlayerStatHeader("kill", state => state.KillCount, bot => bot.KillCount);
            headers[5] = CreatePlayerStatHeader("death", state => state.DeathCount, bot => bot.DeathCount);
            headers[6] = CreatePlayerStatHeader("assist", state => state.AssistCount, bot => bot.AssistCount);
            headers[7] = CreatePlayerStatHeader("score", state => state.Score, bot => bot.Score);
            return headers;
        }

        private static MissionScoreboardComponent.ScoreboardHeader CreatePlayerStatHeader(
            string id,
            Func<PeerStatsRuntimeState, int> runtimeGetter,
            Func<BotData, int> botGetter)
        {
            return new MissionScoreboardComponent.ScoreboardHeader(
                id,
                missionPeer => ResolveRuntimeBackedPlayerStat(missionPeer, runtimeGetter).ToString(),
                botData => (botData != null ? botGetter(botData) : 0).ToString());
        }

        private static int ResolveRuntimeBackedPlayerStat(
            MissionPeer missionPeer,
            Func<PeerStatsRuntimeState, int> runtimeGetter)
        {
            if (missionPeer == null)
                return 0;

            if (runtimeGetter != null &&
                CoopBattlePeerStatsRuntimeState.TryGetState(missionPeer, out PeerStatsRuntimeState runtimeState))
            {
                return runtimeGetter(runtimeState);
            }

            return 0;
        }
    }
}
