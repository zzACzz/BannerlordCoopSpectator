using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.Infrastructure
{
    internal readonly struct ScoreboardSideRuntimeState
    {
        public ScoreboardSideRuntimeState(
            BattleSideEnum side,
            int sideScore,
            int botKillCount,
            int botAssistCount,
            int botDeathCount,
            int botAliveCount,
            string source,
            DateTime updatedUtc)
        {
            Side = side;
            SideScore = sideScore;
            BotKillCount = botKillCount;
            BotAssistCount = botAssistCount;
            BotDeathCount = botDeathCount;
            BotAliveCount = botAliveCount;
            Source = source;
            UpdatedUtc = updatedUtc;
        }

        public BattleSideEnum Side { get; }
        public int SideScore { get; }
        public int BotKillCount { get; }
        public int BotAssistCount { get; }
        public int BotDeathCount { get; }
        public int BotAliveCount { get; }
        public string Source { get; }
        public DateTime UpdatedUtc { get; }
    }

    internal static class CoopBattleScoreboardRuntimeState
    {
        private sealed class MissionScoreboardRuntimeStateHolder
        {
            public readonly Dictionary<BattleSideEnum, ScoreboardSideRuntimeState> StatesBySide =
                new Dictionary<BattleSideEnum, ScoreboardSideRuntimeState>();
        }

        private static readonly ConditionalWeakTable<Mission, MissionScoreboardRuntimeStateHolder> _statesByMission =
            new ConditionalWeakTable<Mission, MissionScoreboardRuntimeStateHolder>();

        public static void Reset(Mission mission)
        {
            if (mission == null)
                return;

            _statesByMission.Remove(mission);
        }

        public static void InitializeMission(
            Mission mission,
            string source)
        {
            if (mission == null)
                return;

            Apply(
                mission,
                BattleSideEnum.Defender,
                0,
                0,
                0,
                0,
                ResolveDefaultBotAliveCountForSide(BattleSideEnum.Defender),
                source);
            Apply(
                mission,
                BattleSideEnum.Attacker,
                0,
                0,
                0,
                0,
                ResolveDefaultBotAliveCountForSide(BattleSideEnum.Attacker),
                source);
        }

        public static bool TryGetState(
            Mission mission,
            BattleSideEnum side,
            out ScoreboardSideRuntimeState state)
        {
            state = default;
            if (mission == null || !IsSupportedSide(side))
                return false;

            return _statesByMission.TryGetValue(mission, out MissionScoreboardRuntimeStateHolder holder) &&
                holder.StatesBySide.TryGetValue(side, out state);
        }

        public static bool TryGetOrSeedState(
            Mission mission,
            MissionScoreboardComponent scoreboardComponent,
            BattleSideEnum side,
            string source,
            out ScoreboardSideRuntimeState state)
        {
            state = default;
            if (mission == null || !IsSupportedSide(side))
                return false;

            if (TryGetState(mission, side, out state))
                return true;

            int defaultBotAliveCount = ResolveDefaultBotAliveCountForSide(side);
            Apply(
                mission,
                side,
                0,
                0,
                0,
                0,
                defaultBotAliveCount,
                source);
            return TryGetState(mission, side, out state);
        }

        public static void Apply(
            Mission mission,
            BattleSideEnum side,
            int sideScore,
            int botKillCount,
            int botAssistCount,
            int botDeathCount,
            int botAliveCount,
            string source)
        {
            if (mission == null || !IsSupportedSide(side))
                return;

            ScoreboardSideRuntimeState nextState = new ScoreboardSideRuntimeState(
                side,
                NormalizeScore(sideScore),
                NormalizeStat(botKillCount),
                NormalizeStat(botAssistCount),
                NormalizeStat(botDeathCount),
                NormalizeBotAliveCount(botAliveCount),
                source,
                DateTime.UtcNow);

            MissionScoreboardRuntimeStateHolder holder = _statesByMission.GetOrCreateValue(mission);
            if (holder.StatesBySide.TryGetValue(side, out ScoreboardSideRuntimeState previousState) &&
                previousState.SideScore == nextState.SideScore &&
                previousState.BotKillCount == nextState.BotKillCount &&
                previousState.BotAssistCount == nextState.BotAssistCount &&
                previousState.BotDeathCount == nextState.BotDeathCount &&
                previousState.BotAliveCount == nextState.BotAliveCount)
            {
                return;
            }

            holder.StatesBySide[side] = nextState;

            ModLogger.Info(
                "CoopBattleScoreboardRuntimeState: state updated. " +
                "Mission=" + (mission.SceneName ?? "unknown") +
                " Side=" + side +
                " SideScore=" + nextState.SideScore +
                " BotKills=" + nextState.BotKillCount +
                " BotAssists=" + nextState.BotAssistCount +
                " BotDeaths=" + nextState.BotDeathCount +
                " BotAlive=" + nextState.BotAliveCount +
                " Source=" + (source ?? "unknown"));
        }

        public static void ApplyRoundScores(
            Mission mission,
            MissionScoreboardComponent scoreboardComponent,
            int attackerScore,
            int defenderScore,
            string source)
        {
            if (mission == null)
                return;

            TryGetOrSeedState(mission, scoreboardComponent, BattleSideEnum.Attacker, source, out ScoreboardSideRuntimeState attackerState);
            TryGetOrSeedState(mission, scoreboardComponent, BattleSideEnum.Defender, source, out ScoreboardSideRuntimeState defenderState);

            Apply(
                mission,
                BattleSideEnum.Attacker,
                attackerScore,
                attackerState.BotKillCount,
                attackerState.BotAssistCount,
                attackerState.BotDeathCount,
                attackerState.BotAliveCount,
                source);
            Apply(
                mission,
                BattleSideEnum.Defender,
                defenderScore,
                defenderState.BotKillCount,
                defenderState.BotAssistCount,
                defenderState.BotDeathCount,
                defenderState.BotAliveCount,
                source);
        }

        public static void ApplyBotData(
            Mission mission,
            MissionScoreboardComponent scoreboardComponent,
            BattleSideEnum side,
            int botKillCount,
            int botAssistCount,
            int botDeathCount,
            int botAliveCount,
            string source)
        {
            if (mission == null || !IsSupportedSide(side))
                return;

            TryGetOrSeedState(mission, scoreboardComponent, side, source, out ScoreboardSideRuntimeState currentState);
            Apply(
                mission,
                side,
                currentState.SideScore,
                botKillCount,
                botAssistCount,
                botDeathCount,
                botAliveCount,
                source);
        }

        public static void AdjustBotData(
            Mission mission,
            MissionScoreboardComponent scoreboardComponent,
            BattleSideEnum side,
            int botKillCountDelta,
            int botAssistCountDelta,
            int botDeathCountDelta,
            int botAliveCountDelta,
            string source)
        {
            if (!TryGetOrSeedState(mission, scoreboardComponent, side, source, out ScoreboardSideRuntimeState currentState))
                return;

            Apply(
                mission,
                side,
                currentState.SideScore,
                currentState.BotKillCount + botKillCountDelta,
                currentState.BotAssistCount + botAssistCountDelta,
                currentState.BotDeathCount + botDeathCountDelta,
                currentState.BotAliveCount + botAliveCountDelta,
                source);
        }

        public static void IncrementSideScore(
            Mission mission,
            MissionScoreboardComponent scoreboardComponent,
            BattleSideEnum side,
            string source)
        {
            if (!TryGetOrSeedState(mission, scoreboardComponent, side, source, out ScoreboardSideRuntimeState currentState))
                return;

            Apply(
                mission,
                side,
                currentState.SideScore + 1,
                currentState.BotKillCount,
                currentState.BotAssistCount,
                currentState.BotDeathCount,
                currentState.BotAliveCount,
                source);
        }

        private static bool IsSupportedSide(BattleSideEnum side)
        {
            return side == BattleSideEnum.Attacker || side == BattleSideEnum.Defender;
        }

        private static int ResolveDefaultBotAliveCountForSide(BattleSideEnum side)
        {
            if (side == BattleSideEnum.Attacker)
                return NormalizeBotAliveCount(MultiplayerOptions.OptionType.NumberOfBotsTeam2.GetIntValue());

            if (side == BattleSideEnum.Defender)
                return NormalizeBotAliveCount(MultiplayerOptions.OptionType.NumberOfBotsTeam1.GetIntValue());

            return 0;
        }

        private static int NormalizeStat(int value)
        {
            return Math.Max(-1000, Math.Min(100000, value));
        }

        private static int NormalizeScore(int value)
        {
            return Math.Max(-1000, Math.Min(100000, value));
        }

        private static int NormalizeBotAliveCount(int value)
        {
            return Math.Max(0, Math.Min(100000, value));
        }
    }
}
