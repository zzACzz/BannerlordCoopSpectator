using System;

namespace CoopSpectator.Infrastructure.Hideout
{
    public enum CoopHideoutBossPhase
    {
        InitialAssault = 0,
        PreparingCinematic = 1,
        Cinematic = 2,
        AwaitingHostChoice = 3,
        Duel = 4,
        AllBattle = 5,
        Completed = 6
    }

    public enum CoopHideoutBossChoice
    {
        None = 0,
        Duel = 1,
        AllBattle = 2
    }

    public enum CoopHideoutBossClientCommandKind
    {
        ReadyForCinematic = 0,
        ChooseDuel = 1,
        ChooseAllBattle = 2
    }

    public sealed class CoopHideoutBossPhaseSession
    {
        public string BattleInstanceId { get; set; }
        public int Revision { get; set; }
        public CoopHideoutBossPhase Phase { get; set; }
        public CoopHideoutBossChoice Choice { get; set; }
        public int HostPeerIndex { get; set; } = -1;
        public int HostAgentIndex { get; set; } = -1;
        public int BossAgentIndex { get; set; } = -1;
        public DateTime DeadlineUtc { get; set; }
        public string Reason { get; set; }

        public CoopHideoutBossPhaseSession Clone()
        {
            return new CoopHideoutBossPhaseSession
            {
                BattleInstanceId = BattleInstanceId,
                Revision = Revision,
                Phase = Phase,
                Choice = Choice,
                HostPeerIndex = HostPeerIndex,
                HostAgentIndex = HostAgentIndex,
                BossAgentIndex = BossAgentIndex,
                DeadlineUtc = DeadlineUtc,
                Reason = Reason
            };
        }
    }

    public static class CoopHideoutBossPhaseContract
    {
        public const int ProtocolVersion = 1;
        public const string ScenarioKind = "Hideout";
        public const string GameModeId = "CoopHideoutDay";
        public const string BossFightEntityTag = "hideout_boss_fight";
        public const int MaximumBattleInstanceIdCharacters = 64;
        public const int MaximumReasonCharacters = 128;
        public const int CinematicReadyTimeoutMilliseconds = 2500;
        public const int CinematicDurationMilliseconds = 8000;
        public const int HostChoiceTimeoutMilliseconds = 20000;

        public static bool IsHideoutScenario(string scenarioKind)
        {
            return string.Equals(
                (scenarioKind ?? string.Empty).Trim(),
                ScenarioKind,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSupportedDayHideoutSceneName(string sceneName)
        {
            return TryNormalizeDayHideoutSceneName(sceneName, out _);
        }

        public static bool TryNormalizeDayHideoutSceneName(
            string sceneName,
            out string normalizedSceneName)
        {
            normalizedSceneName = (sceneName ?? string.Empty).Trim();
            if (normalizedSceneName.Length == 0)
                return false;

            if (string.Equals(normalizedSceneName, "bandit_forest", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedSceneName, "bandit_forest_sv", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalizedSceneName.StartsWith("desert_hideout_", StringComparison.OrdinalIgnoreCase) ||
                   normalizedSceneName.StartsWith("forest_hideout_", StringComparison.OrdinalIgnoreCase) ||
                   normalizedSceneName.StartsWith("hideout_steppe_", StringComparison.OrdinalIgnoreCase) ||
                   normalizedSceneName.StartsWith("mountain_hideout_", StringComparison.OrdinalIgnoreCase) ||
                   normalizedSceneName.StartsWith("sea_bandit_", StringComparison.OrdinalIgnoreCase);
        }

        public static bool CanEnterDayHideoutCampaignBridge(
            bool hasDayController,
            bool hasAmbushController,
            bool hasSelectedRosterContract)
        {
            return hasDayController &&
                   !hasAmbushController &&
                   hasSelectedRosterContract;
        }

        public static int ResolveBossTriggerCount(int initialEnemyCount)
        {
            if (initialEnemyCount <= 0)
                return 0;

            int quarter = (int)Math.Ceiling(initialEnemyCount * 0.25d);
            return Math.Max(1, Math.Min(5, quarter));
        }

        public static bool ShouldPrepareBossPhase(
            int initialEnemyCount,
            int activeEnemyCount,
            bool hostAgentActive,
            bool bossFightEntityAvailable)
        {
            int triggerCount = ResolveBossTriggerCount(initialEnemyCount);
            return triggerCount > 0 &&
                   activeEnemyCount > 0 &&
                   activeEnemyCount <= triggerCount &&
                   hostAgentActive &&
                   bossFightEntityAvailable;
        }

        public static CoopHideoutBossChoice ResolveChoice(CoopHideoutBossClientCommandKind commandKind)
        {
            if (commandKind == CoopHideoutBossClientCommandKind.ChooseDuel)
                return CoopHideoutBossChoice.Duel;
            if (commandKind == CoopHideoutBossClientCommandKind.ChooseAllBattle)
                return CoopHideoutBossChoice.AllBattle;
            return CoopHideoutBossChoice.None;
        }

        public static bool TryTransition(
            CoopHideoutBossPhaseSession session,
            CoopHideoutBossPhase nextPhase,
            DateTime deadlineUtc,
            string reason,
            out string rejectionReason)
        {
            rejectionReason = null;
            if (session == null)
            {
                rejectionReason = "session_missing";
                return false;
            }

            if (!IsTransitionAllowed(session.Phase, nextPhase))
            {
                rejectionReason = "transition_invalid:" + session.Phase + "->" + nextPhase;
                return false;
            }

            session.Phase = nextPhase;
            session.Revision++;
            session.DeadlineUtc = deadlineUtc;
            session.Reason = Bound(reason, MaximumReasonCharacters);
            return true;
        }

        public static bool TryAcceptHostChoice(
            CoopHideoutBossPhaseSession session,
            int senderPeerIndex,
            int expectedRevision,
            CoopHideoutBossClientCommandKind commandKind,
            out CoopHideoutBossChoice acceptedChoice,
            out string rejectionReason)
        {
            acceptedChoice = CoopHideoutBossChoice.None;
            rejectionReason = null;
            if (session == null)
            {
                rejectionReason = "session_missing";
                return false;
            }
            if (session.Phase != CoopHideoutBossPhase.AwaitingHostChoice)
            {
                rejectionReason = "choice_phase_invalid";
                return false;
            }
            if (senderPeerIndex < 0 || senderPeerIndex != session.HostPeerIndex)
            {
                rejectionReason = "choice_sender_not_host";
                return false;
            }
            if (expectedRevision != session.Revision)
            {
                rejectionReason = "choice_revision_stale";
                return false;
            }
            if (session.Choice != CoopHideoutBossChoice.None)
            {
                rejectionReason = "choice_already_committed";
                return false;
            }

            acceptedChoice = ResolveChoice(commandKind);
            if (acceptedChoice == CoopHideoutBossChoice.None)
            {
                rejectionReason = "choice_command_invalid";
                return false;
            }

            session.Choice = acceptedChoice;
            return true;
        }

        public static string Bound(string value, int maximumCharacters)
        {
            string safe = value ?? string.Empty;
            return safe.Length <= maximumCharacters
                ? safe
                : safe.Substring(0, maximumCharacters);
        }

        private static bool IsTransitionAllowed(CoopHideoutBossPhase current, CoopHideoutBossPhase next)
        {
            if (current == next)
                return false;

            switch (current)
            {
                case CoopHideoutBossPhase.InitialAssault:
                    return next == CoopHideoutBossPhase.PreparingCinematic;
                case CoopHideoutBossPhase.PreparingCinematic:
                    return next == CoopHideoutBossPhase.Cinematic ||
                           next == CoopHideoutBossPhase.AllBattle;
                case CoopHideoutBossPhase.Cinematic:
                    return next == CoopHideoutBossPhase.AwaitingHostChoice ||
                           next == CoopHideoutBossPhase.AllBattle;
                case CoopHideoutBossPhase.AwaitingHostChoice:
                    return next == CoopHideoutBossPhase.Duel ||
                           next == CoopHideoutBossPhase.AllBattle;
                case CoopHideoutBossPhase.Duel:
                    return next == CoopHideoutBossPhase.AllBattle ||
                           next == CoopHideoutBossPhase.Completed;
                case CoopHideoutBossPhase.AllBattle:
                    return next == CoopHideoutBossPhase.Completed;
                default:
                    return false;
            }
        }
    }
}
