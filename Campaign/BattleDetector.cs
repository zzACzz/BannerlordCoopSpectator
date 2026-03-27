using System; // Підключаємо базові типи .NET (Exception)
using System.Collections;
using System.Collections.Generic; // Підключаємо List<> для списків у DTO
using CoopSpectator.DedicatedHelper; // SendStartMission / SendEndMission до Dedicated Helper (Етап 3b)
using CoopSpectator.Infrastructure; // Підключаємо логер і UI feedback
using CoopSpectator.Network; // Підключаємо NetworkRole для перевірки ролі
using CoopSpectator.Network.Messages; // Підключаємо DTO + кодек для BATTLE_START:{json}
using TaleWorlds.CampaignSystem; // Підключаємо Campaign (для доступу до поточної кампанії)
using TaleWorlds.CampaignSystem.Party; // Підключаємо MobileParty (партія гравця в кампанії)
using TaleWorlds.MountAndBlade; // Підключаємо Mission (детекція входу в місію/битву)
using System.Reflection; // Reflection для mission-safe fallback ids героїв/лордів із кампанії.
using System.Linq;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Campaign // Тримаємо battle/campaign логіку в одному namespace
{ // Починаємо блок простору імен
    /// <summary> // Документуємо клас
    /// Детектор старту битви/місії у хоста: // Пояснюємо, що ми детектимо
    /// коли `Mission.Current` переходить з null → не-null, // Описуємо основний сигнал
    /// ми відправляємо клієнтам `BATTLE_START:{json}`. // Описуємо результат
    /// </summary> // Завершуємо XML-коментар
    public sealed class BattleDetector // Оголошуємо sealed сервіс-клас (стан + простий tick)
    { // Починаємо блок класу
        private const string SyntheticAllCampaignTroopsBattleId = "synthetic_all_campaign_troops";
        private const string SyntheticLiveHeroesBattleId = "synthetic_live_heroes";
        private const int SyntheticHeroCompanionLimit = 12;
        private const int SyntheticHeroLordLimit = 12;
        private bool _wasInMissionLastTick; // Пам'ятаємо стан попереднього тіку: чи вже була активна місія
        private bool _hasSentBattleStartForThisMission; // Прапорець, щоб не відправляти BATTLE_START багато разів за одну місію
        private string _lastConsumedBattleResultKey;
        private string _lastMissionExitRequestedBattleResultKey;
        private string _lastMissionExitFailedBattleResultKey;
        private string _cachedHostAftermathRewardResultKey;
        private BattleResultWritebackSummary.RewardProjectionSummary _cachedHostAftermathRewardProjection;
        private string _cachedHostAftermathLootResultKey;
        private BattleResultWritebackSummary.LootAftermathSummary _cachedHostAftermathLootSummary;
        private DateTime _missionEnteredUtc;
        private DateTime _nextMissionBattleResultPollUtc;
        private DateTime _nextBattleStartAttemptUtc;
        private DateTime _nextBattleStartWaitLogUtc;
        private static SyntheticRosterMode _syntheticRosterMode;
        private static readonly Dictionary<string, object> CachedDefaultSkillObjects = new Dictionary<string, object>(StringComparer.Ordinal);
        private static readonly Dictionary<string, object> CachedDefaultCharacterAttributeObjects = new Dictionary<string, object>(StringComparer.Ordinal);
        private static List<object> _cachedPerkObjects;

        private enum SyntheticRosterMode
        {
            None = 0,
            AllCampaignTroops = 1,
            LiveHeroes = 2
        }

        private sealed class EncounterPartyWritebackState
        {
            public string PartyId { get; set; }
            public PartyBase PartyBase { get; set; }
            public MobileParty MobileParty { get; set; }
            public TroopRoster MemberRoster { get; set; }
            public CharacterObject CaptainCharacter { get; set; }
            public bool IsMainParty { get; set; }
        }

        private sealed class BattleResultCharacterAggregate
        {
            public string PartyId { get; set; }
            public string IdentityKey { get; set; }
            public string HeroId { get; set; }
            public string OriginalCharacterId { get; set; }
            public string CharacterId { get; set; }
            public string TroopName { get; set; }
            public bool IsHero { get; set; }
            public int SnapshotCount { get; set; }
            public int SnapshotWoundedCount { get; set; }
            public int RemovedCount { get; set; }
            public int KilledCount { get; set; }
            public int UnconsciousCount { get; set; }
            public int RoutedCount { get; set; }
            public int OtherRemovedCount { get; set; }
            public int ScoreHitCount { get; set; }
            public float DamageDealt { get; set; }
            public float DamageTaken { get; set; }

            public int DesiredCount => IsHero
                ? Math.Max(0, SnapshotCount)
                : Math.Max(0, SnapshotCount - Math.Max(0, KilledCount + OtherRemovedCount));

            public int DesiredWoundedCount => IsHero
                ? Math.Max(0, SnapshotWoundedCount)
                : Math.Max(0, Math.Min(DesiredCount, SnapshotWoundedCount + Math.Max(0, UnconsciousCount)));

            public bool HeroShouldBeWounded => IsHero && (KilledCount > 0 || UnconsciousCount > 0 || OtherRemovedCount > 0);
        }

        private sealed class BattleResultWritebackSummary
        {
            public sealed class LootItemEntrySummary
            {
                public string ItemId { get; set; }
                public int Amount { get; set; }
            }

            public sealed class RewardProjectionSummary
            {
                public bool Ready { get; set; }
                public bool UsedCommittedMapEventResults { get; set; }
                public bool UsedMapEventContribution { get; set; }
                public bool MainPartyOnWinnerSide { get; set; }
                public bool UsedSnapshotContribution { get; set; }
                public string Source { get; set; }
                public string WinnerSide { get; set; }
                public string MainPartySide { get; set; }
                public float WinnerRenownValue { get; set; }
                public float WinnerInfluenceValue { get; set; }
                public float ContributionShare { get; set; }
                public float ProjectedRenown { get; set; }
                public float ProjectedInfluence { get; set; }
                public float ProjectedMoraleChange { get; set; }
                public int ProjectedGoldGain { get; set; }
                public int ProjectedGoldLoss { get; set; }
                public int ProjectedGoldDelta => ProjectedGoldGain - ProjectedGoldLoss;
            }

            public sealed class RewardApplySummary
            {
                public bool Attempted { get; set; }
                public bool Applied { get; set; }
                public bool SkippedCommittedResults { get; set; }
                public bool AppliedGold { get; set; }
                public bool AppliedMorale { get; set; }
                public bool AppliedRenown { get; set; }
                public bool AppliedInfluence { get; set; }
                public int GoldDeltaApplied { get; set; }
                public float MoraleApplied { get; set; }
                public float RenownApplied { get; set; }
                public float InfluenceApplied { get; set; }
                public string Source { get; set; }
            }

            public sealed class LootAftermathSummary
            {
                public bool Ready { get; set; }
                public string Source { get; set; }
                public int LootMemberCount { get; set; }
                public int LootPrisonerCount { get; set; }
                public int LootItemStacks { get; set; }
                public int LootItemUnits { get; set; }
                public string LootMemberSample { get; set; }
                public string LootPrisonerSample { get; set; }
                public string LootItemSample { get; set; }
                public List<LootItemEntrySummary> ItemEntries { get; } = new List<LootItemEntrySummary>();
            }

            public sealed class LootApplySummary
            {
                public bool Attempted { get; set; }
                public bool Applied { get; set; }
                public bool SkippedCommittedResults { get; set; }
                public string Source { get; set; }
                public int ItemStacksApplied { get; set; }
                public int ItemUnitsApplied { get; set; }
            }

            public int Aggregates { get; set; }
            public int EncounterParties { get; set; }
            public int ResolvedPartyAggregates { get; set; }
            public int RegularTroopsAdjusted { get; set; }
            public int HeroWoundsApplied { get; set; }
            public int HeroHitPointsAdjusted { get; set; }
            public int HeroHitPointLossApplied { get; set; }
            public int TroopXpApplied { get; set; }
            public float HeroSkillXpApplied { get; set; }
            public float HeroRawXpApplied { get; set; }
            public int UnresolvedPartyAggregates { get; set; }
            public int UnresolvedCombatEvents { get; set; }
            public RewardProjectionSummary RewardProjection { get; } = new RewardProjectionSummary();
            public RewardApplySummary RewardApply { get; } = new RewardApplySummary();
            public LootAftermathSummary LootAftermath { get; } = new LootAftermathSummary();
            public LootApplySummary LootApply { get; } = new LootApplySummary();
            public List<string> AdjustedSamples { get; } = new List<string>();
            public List<string> UnresolvedSamples { get; } = new List<string>();
        }

        private sealed class FallbackCombatXpParticipant
        {
            public CharacterObject Character { get; set; }
            public Hero Hero { get; set; }
            public int Weight { get; set; }
        }

        private static void ResetCombatProfileLookupCaches()
        {
            CachedDefaultSkillObjects.Clear();
            CachedDefaultCharacterAttributeObjects.Clear();
            _cachedPerkObjects = null;
        }

        public static bool IsSyntheticAllCampaignTroopsRosterEnabled()
        {
            return _syntheticRosterMode == SyntheticRosterMode.AllCampaignTroops;
        }

        public static string SetSyntheticAllCampaignTroopsRosterEnabled(bool enabled)
        {
            _syntheticRosterMode = enabled ? SyntheticRosterMode.AllCampaignTroops : SyntheticRosterMode.None;
            string summary = enabled
                ? BuildSyntheticAllCampaignTroopsPreviewSummary()
                : "disabled";
            ModLogger.Info(
                "BattleDetector: synthetic all-campaign-troops test roster " +
                (enabled ? "enabled" : "disabled") +
                ". Summary=" + summary + ".");
            return summary;
        }

        public static bool IsSyntheticLiveHeroesRosterEnabled()
        {
            return _syntheticRosterMode == SyntheticRosterMode.LiveHeroes;
        }

        public static string SetSyntheticLiveHeroesRosterEnabled(bool enabled)
        {
            _syntheticRosterMode = enabled ? SyntheticRosterMode.LiveHeroes : SyntheticRosterMode.None;
            string summary = enabled
                ? BuildSyntheticLiveHeroesPreviewSummary()
                : "disabled";
            ModLogger.Info(
                "BattleDetector: synthetic live-heroes test roster " +
                (enabled ? "enabled" : "disabled") +
                ". Summary=" + summary + ".");
            return summary;
        }

        public static string GetSyntheticRosterStatusSummary()
        {
            switch (_syntheticRosterMode)
            {
                case SyntheticRosterMode.AllCampaignTroops:
                    return "ALL_CAMPAIGN_TROOPS";
                case SyntheticRosterMode.LiveHeroes:
                    return "LIVE_HEROES";
                default:
                    return "OFF";
            }
        }

        public void Tick() // Метод, який треба викликати кожен кадр з головного потоку (наприклад з SubModule.OnApplicationTick)
        { // Починаємо блок методу
            if (GameNetwork.IsClient) // MP-клієнт не повинен керувати dedicated helper під час join/load місії.
            {
                bool isInMissionForClient = Mission.Current != null;
                _wasInMissionLastTick = isInMissionForClient;
                if (!isInMissionForClient)
                {
                    _hasSentBattleStartForThisMission = false;
                    ResetMissionExitState();
                    _nextBattleStartAttemptUtc = DateTime.MinValue;
                    _nextBattleStartWaitLogUtc = DateTime.MinValue;
                }
                return;
            }
            if (Mission.Current != null && _wasInMissionLastTick)
                TryHandleBattleResultMissionExit();

            bool isInMissionNow = Mission.Current != null; // Визначаємо чи зараз є активна місія (битва/сцена)

            if (!isInMissionNow) // Якщо місії немає, значить ми не в битві (або вже вийшли з неї)
            { // Починаємо блок if
                TryConsumeBattleResultWritebackAudit();
                ResetMissionExitState();
                if (_wasInMissionLastTick && ShouldNotifyDedicatedHelper()) // Щойно вийшли з місії — сказати Dedicated Helper end_mission (якщо ми не спектатор-клієнт)
                { // Починаємо блок if
                    // Dedicated mission teardown is now driven by the dedicated process after authoritative battle completion.
                } // Завершуємо блок if
                _wasInMissionLastTick = false; // Оновлюємо стан "було в місії" на false
                _hasSentBattleStartForThisMission = false; // Скидаємо прапорець, щоб наступна місія могла знову надіслати BATTLE_START
                _nextBattleStartAttemptUtc = DateTime.MinValue;
                _nextBattleStartWaitLogUtc = DateTime.MinValue;
                ResetIfMissionEnded(); // Тримаємо стан консистентним
                return; // Виходимо, бо поки що нема старту битви для відправки
            } // Завершуємо блок if

            if (_wasInMissionLastTick) // Якщо ми вже були в місії на попередньому тіку, значить "старт" вже минув
            { // Починаємо блок if
                TryStartMissionForCurrentRole();
                return; // Виходимо, щоб не відправляти повторно
            } // Завершуємо блок if

            _wasInMissionLastTick = true; // Фіксуємо, що ми щойно увійшли в місію
            // Діагностика: лог при вході в місію (битва в кампанії або інша сцена), щоб перевірити чи Tick бачить Mission.Current
            _missionEnteredUtc = DateTime.UtcNow;
            _lastMissionExitRequestedBattleResultKey = null;
            _lastMissionExitFailedBattleResultKey = null;
            _cachedHostAftermathRewardResultKey = null;
            _cachedHostAftermathRewardProjection = null;
            _cachedHostAftermathLootResultKey = null;
            _cachedHostAftermathLootSummary = null;
            _nextMissionBattleResultPollUtc = DateTime.MinValue;
            _nextBattleStartAttemptUtc = DateTime.MinValue;
            _nextBattleStartWaitLogUtc = DateTime.MinValue;
            ModLogger.Info("BattleDetector: mission entered (Mission.Current set). Notifying dedicated if applicable.");
            TryStartMissionForCurrentRole();
        } // Завершуємо блок методу

        private static bool ShouldSendBattleStart() // Перевіряємо чи поточний інстанс гри має право надсилати BATTLE_START
        { // Починаємо блок методу
            if (CoopRuntime.Network == null) // Перевіряємо що NetworkManager ініціалізований
            { // Починаємо блок if
                return false; // Повертаємо false, бо без мережі немає куди надсилати
            } // Завершуємо блок if

            if (!CoopRuntime.Network.IsRunning) // Перевіряємо що мережа реально запущена (сервер активний)
            { // Починаємо блок if
                return false; // Повертаємо false, бо сервер ще не стартував
            } // Завершуємо блок if

            if (CoopRuntime.Network.Role != NetworkRole.Server) // Перевіряємо що ми саме хост/сервер
            { // Починаємо блок if
                return false; // Повертаємо false, бо клієнт не має розсилати battle події
            } // Завершуємо блок if

            return true; // Повертаємо true, бо ми сервер і можемо надсилати BATTLE_START
        } // Завершуємо блок методу

        /// <summary>Чи треба повідомляти Dedicated Helper (start_mission/end_mission). True для хоста кампанії або TCP-сервера, false для спектатор-клієнта.</summary>
        private static bool ShouldNotifyDedicatedHelper()
        {
            if (CoopRuntime.Network == null) return true; // Нема мережі — це кампанія-хост, дедик на цій машині керується тут
            if (CoopRuntime.Network.Role == NetworkRole.Client) return false; // Спектатор не керує дедиком
            return true; // Сервер або інший випадок — керуємо
        }

        private void ResetIfMissionEnded() // Helper: тримаємо внутрішній стан коректним, коли місія завершилась
        { // Починаємо блок методу
            if (Mission.Current != null) // Якщо місія активна, ми нічого не скидаємо
            { // Починаємо блок if
                return; // Виходимо, бо ми все ще в місії
            } // Завершуємо блок if

            _wasInMissionLastTick = false; // Скидаємо "було в місії" — ми вже не в місії
            _hasSentBattleStartForThisMission = false; // Дозволяємо наступній місії знову відправити BATTLE_START
            _nextBattleStartAttemptUtc = DateTime.MinValue;
            _nextBattleStartWaitLogUtc = DateTime.MinValue;
        } // Завершуємо блок методу

        private void TryStartMissionForCurrentRole()
        {
            if (_hasSentBattleStartForThisMission)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (_nextBattleStartAttemptUtc != DateTime.MinValue && nowUtc < _nextBattleStartAttemptUtc)
                return;

            bool started = false;
            if (ShouldSendBattleStart())
                started = TrySendBattleStart();
            else if (ShouldNotifyDedicatedHelper())
                started = TryStartDedicatedMissionForCampaignHost();

            if (started)
            {
                _nextBattleStartAttemptUtc = DateTime.MinValue;
                _nextBattleStartWaitLogUtc = DateTime.MinValue;
            }
            else
            {
                _nextBattleStartAttemptUtc = nowUtc.AddMilliseconds(750);
            }
        }

        private void TryConsumeBattleResultWritebackAudit()
        {
            try
            {
                CoopBattleResultBridgeFile.BattleResultSnapshot result = CoopBattleResultBridgeFile.ReadResult(logRead: false);
                if (result == null)
                    return;

                string resultKey = BuildBattleResultKey(result);
                if (string.Equals(_lastConsumedBattleResultKey, resultKey, StringComparison.Ordinal))
                    return;

                _lastConsumedBattleResultKey = resultKey;

                BattleResultWritebackSummary writebackSummary = ApplyBattleResultWriteback(
                    result,
                    string.Equals(_cachedHostAftermathRewardResultKey, resultKey, StringComparison.Ordinal)
                        ? CloneRewardProjectionSummary(_cachedHostAftermathRewardProjection)
                        : null,
                    string.Equals(_cachedHostAftermathLootResultKey, resultKey, StringComparison.Ordinal)
                        ? CloneLootAftermathSummary(_cachedHostAftermathLootSummary)
                        : null);

                int removedTotal = result.Entries?.Sum(entry => entry?.RemovedCount ?? 0) ?? 0;
                int killedTotal = result.Entries?.Sum(entry => entry?.KilledCount ?? 0) ?? 0;
                int unconsciousTotal = result.Entries?.Sum(entry => entry?.UnconsciousCount ?? 0) ?? 0;
                int activeTotal = result.Entries?.Sum(entry => entry?.ActiveCount ?? 0) ?? 0;
                int combatEventCount = result.CombatEvents?.Count ?? 0;
                int droppedCombatEventCount = result.DroppedCombatEventCount;
                float damageTotal = result.Entries?.Sum(entry => entry?.DamageDealt ?? 0f) ?? 0f;
                int hitTotal = result.Entries?.Sum(entry => entry?.ScoreHitCount ?? 0) ?? 0;
                IEnumerable<string> entrySummary = (result.Entries ?? new List<CoopBattleResultBridgeFile.BattleResultEntrySnapshot>())
                    .Where(entry => entry != null && (entry.RemovedCount > 0 || entry.ActiveCount > 0 || entry.MaterializedSpawnCount > 0 || entry.ScoreHitCount > 0 || entry.DamageDealt > 0.01f))
                    .OrderByDescending(entry => entry.RemovedCount)
                    .ThenByDescending(entry => entry.KillsInflictedCount)
                    .ThenByDescending(entry => entry.DamageDealt)
                    .ThenByDescending(entry => entry.MaterializedSpawnCount)
                    .Take(24)
                    .Select(entry =>
                        (entry.SideId ?? "side") + "/" +
                        (entry.EntryId ?? "entry") +
                        ": Spawned=" + entry.MaterializedSpawnCount +
                        " Active=" + entry.ActiveCount +
                        " Removed=" + entry.RemovedCount +
                        " Killed=" + entry.KilledCount +
                        " Unconscious=" + entry.UnconsciousCount +
                        " Hits=" + entry.ScoreHitCount +
                        " Damage=" + entry.DamageDealt.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                        " KillsBy=" + entry.KillsInflictedCount +
                        (entry.IsHero ? " Hero=True" : string.Empty));

                ModLogger.Info(
                    "BattleDetector: consumed battle_result writeback audit. " +
                    "BattleId=" + (result.BattleId ?? "null") +
                    " WinnerSide=" + (result.WinnerSide ?? "none") +
                    " Entries=" + (result.Entries?.Count ?? 0) +
                    " Removed=" + removedTotal +
                    " Killed=" + killedTotal +
                    " Unconscious=" + unconsciousTotal +
                    " Active=" + activeTotal +
                    " Hits=" + hitTotal +
                    " Damage=" + damageTotal.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                    " CombatEvents=" + combatEventCount +
                " DroppedCombatEvents=" + droppedCombatEventCount +
                " Writeback[Aggregates=" + writebackSummary.Aggregates +
                " EncounterParties=" + writebackSummary.EncounterParties +
                " Resolved=" + writebackSummary.ResolvedPartyAggregates +
                " TroopsAdjusted=" + writebackSummary.RegularTroopsAdjusted +
                " HeroWounds=" + writebackSummary.HeroWoundsApplied +
                " HeroHpAdjusted=" + writebackSummary.HeroHitPointsAdjusted +
                " HeroHpLoss=" + writebackSummary.HeroHitPointLossApplied +
                " TroopXp=" + writebackSummary.TroopXpApplied +
                " HeroSkillXp=" + writebackSummary.HeroSkillXpApplied.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                " HeroRawXp=" + writebackSummary.HeroRawXpApplied.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) +
                " RewardProjection[Ready=" + writebackSummary.RewardProjection.Ready +
                " Source=" + (writebackSummary.RewardProjection.Source ?? "none") +
                " Committed=" + writebackSummary.RewardProjection.UsedCommittedMapEventResults +
                " MapEventContribution=" + writebackSummary.RewardProjection.UsedMapEventContribution +
                " MainPartyOnWinnerSide=" + writebackSummary.RewardProjection.MainPartyOnWinnerSide +
                " WinnerSide=" + (writebackSummary.RewardProjection.WinnerSide ?? "none") +
                " MainPartySide=" + (writebackSummary.RewardProjection.MainPartySide ?? "none") +
                " Share=" + writebackSummary.RewardProjection.ContributionShare.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Renown=" + writebackSummary.RewardProjection.ProjectedRenown.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " Influence=" + writebackSummary.RewardProjection.ProjectedInfluence.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " Morale=" + writebackSummary.RewardProjection.ProjectedMoraleChange.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " GoldDelta=" + writebackSummary.RewardProjection.ProjectedGoldDelta +
                " Gain=" + writebackSummary.RewardProjection.ProjectedGoldGain +
                " Loss=" + writebackSummary.RewardProjection.ProjectedGoldLoss +
                " SnapshotContribution=" + writebackSummary.RewardProjection.UsedSnapshotContribution + "]" +
                " RewardApply[Attempted=" + writebackSummary.RewardApply.Attempted +
                " Applied=" + writebackSummary.RewardApply.Applied +
                " SkippedCommitted=" + writebackSummary.RewardApply.SkippedCommittedResults +
                " Source=" + (writebackSummary.RewardApply.Source ?? "none") +
                " Gold=" + writebackSummary.RewardApply.GoldDeltaApplied +
                " Morale=" + writebackSummary.RewardApply.MoraleApplied.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " Renown=" + writebackSummary.RewardApply.RenownApplied.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " Influence=" + writebackSummary.RewardApply.InfluenceApplied.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "]" +
                " LootAftermath[Ready=" + writebackSummary.LootAftermath.Ready +
                " Source=" + (writebackSummary.LootAftermath.Source ?? "none") +
                " Members=" + writebackSummary.LootAftermath.LootMemberCount +
                " Prisoners=" + writebackSummary.LootAftermath.LootPrisonerCount +
                " ItemStacks=" + writebackSummary.LootAftermath.LootItemStacks +
                " ItemUnits=" + writebackSummary.LootAftermath.LootItemUnits +
                " MemberSample=" + (writebackSummary.LootAftermath.LootMemberSample ?? "none") +
                " PrisonerSample=" + (writebackSummary.LootAftermath.LootPrisonerSample ?? "none") +
                " ItemSample=" + (writebackSummary.LootAftermath.LootItemSample ?? "none") + "]" +
                " LootApply[Attempted=" + writebackSummary.LootApply.Attempted +
                " Applied=" + writebackSummary.LootApply.Applied +
                " SkippedCommitted=" + writebackSummary.LootApply.SkippedCommittedResults +
                " Source=" + (writebackSummary.LootApply.Source ?? "none") +
                " ItemStacks=" + writebackSummary.LootApply.ItemStacksApplied +
                " ItemUnits=" + writebackSummary.LootApply.ItemUnitsApplied + "]" +
                " UnresolvedAggregates=" + writebackSummary.UnresolvedPartyAggregates +
                " UnresolvedEvents=" + writebackSummary.UnresolvedCombatEvents +
                    " AdjustedSamples=[" + string.Join("; ", writebackSummary.AdjustedSamples) + "]" +
                    " UnresolvedSamples=[" + string.Join("; ", writebackSummary.UnresolvedSamples) + "]]" +
                    " Summary=[" + string.Join("; ", entrySummary) + "].");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: failed to consume battle_result audit: " + ex.Message);
            }
        }

        private static BattleResultWritebackSummary ApplyBattleResultWriteback(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            BattleResultWritebackSummary.RewardProjectionSummary cachedRewardProjection = null,
            BattleResultWritebackSummary.LootAftermathSummary cachedLootAftermath = null)
        {
            var summary = new BattleResultWritebackSummary();
            if (result?.Entries == null || result.Entries.Count == 0 || TaleWorlds.CampaignSystem.Campaign.Current == null)
                return summary;

            Dictionary<string, EncounterPartyWritebackState> encounterParties = ResolveEncounterPartyWritebackStates();
            summary.EncounterParties = encounterParties.Count;

            Dictionary<string, BattleResultCharacterAggregate> aggregates = BuildBattleResultCharacterAggregates(result);
            summary.Aggregates = aggregates.Count;

            foreach (BattleResultCharacterAggregate aggregate in aggregates.Values
                         .OrderBy(group => group.PartyId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(group => group.HeroId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(group => group.OriginalCharacterId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(group => group.CharacterId, StringComparer.OrdinalIgnoreCase))
            {
                if (aggregate == null)
                    continue;

                if (!encounterParties.TryGetValue(aggregate.PartyId ?? string.Empty, out EncounterPartyWritebackState partyState) ||
                    partyState?.MemberRoster == null)
                {
                    summary.UnresolvedPartyAggregates++;
                    AddWritebackSample(
                        summary.UnresolvedSamples,
                        "MissingParty:" + (aggregate.PartyId ?? "null") + "/" + (aggregate.TroopName ?? aggregate.IdentityKey ?? "entry"));
                    continue;
                }

                summary.ResolvedPartyAggregates++;
                TryApplyAggregateToPartyRoster(partyState, aggregate, summary);
            }

            TryApplyMainPartyCombatXpWriteback(result, encounterParties, summary);
            TryBuildMainPartyRewardProjection(result, encounterParties, summary, cachedRewardProjection);
            TryApplyMainPartyRewardWriteback(encounterParties, summary);
            TryBuildMainPartyLootAftermathAudit(summary, cachedLootAftermath);
            TryApplyMainPartyItemLootWriteback(encounterParties, summary);
            return summary;
        }

        private static Dictionary<string, EncounterPartyWritebackState> ResolveEncounterPartyWritebackStates()
        {
            var states = new Dictionary<string, EncounterPartyWritebackState>(StringComparer.OrdinalIgnoreCase);

            try
            {
                object battle = TryGetCurrentBattleObject();
                foreach (object sideObject in new[] { TryGetPropertyValue(battle, "AttackerSide"), TryGetPropertyValue(battle, "DefenderSide") })
                {
                    foreach (object partyObject in EnumerateBattleParties(sideObject))
                    {
                        object partyBaseLike = UnwrapPartyBase(partyObject);
                        TryRegisterEncounterPartyWritebackState(states, partyBaseLike);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: failed to enumerate encounter parties for writeback: " + ex.Message);
            }

            TryRegisterEncounterPartyWritebackState(states, MobileParty.MainParty?.Party ?? (object)MobileParty.MainParty);
            return states;
        }

        private static void TryRegisterEncounterPartyWritebackState(
            IDictionary<string, EncounterPartyWritebackState> states,
            object partyBaseLike)
        {
            if (states == null || partyBaseLike == null)
                return;

            object unwrapped = UnwrapPartyBase(partyBaseLike) ?? partyBaseLike;
            PartyBase partyBase = unwrapped as PartyBase;
            MobileParty mobileParty = TryGetPropertyValue(unwrapped, "MobileParty") as MobileParty;
            if (mobileParty == null && partyBase != null)
                mobileParty = partyBase.MobileParty;

            string partyId =
                TryGetStringId(unwrapped) ??
                TryGetStringId(partyBase) ??
                TryGetStringId(mobileParty);
            if (string.IsNullOrWhiteSpace(partyId))
                return;

            TroopRoster memberRoster = TryGetPropertyValue(unwrapped, "MemberRoster") as TroopRoster;
            if (memberRoster == null && partyBase != null)
                memberRoster = partyBase.MemberRoster;
            if (memberRoster == null)
                return;

            Hero captainHero =
                TryResolveHeroObject(TryGetPropertyValue(mobileParty, "LeaderHero")) ??
                TryResolveHeroObject(TryGetPropertyValue(partyBase, "LeaderHero"));

            states[partyId] = new EncounterPartyWritebackState
            {
                PartyId = partyId,
                PartyBase = partyBase ?? mobileParty?.Party,
                MobileParty = mobileParty,
                MemberRoster = memberRoster,
                CaptainCharacter = captainHero?.CharacterObject,
                IsMainParty =
                    string.Equals(partyId, MobileParty.MainParty?.StringId, StringComparison.OrdinalIgnoreCase) ||
                    ReferenceEquals(mobileParty, MobileParty.MainParty) ||
                    ReferenceEquals(partyBase, MobileParty.MainParty?.Party)
            };
        }

        private static Dictionary<string, BattleResultCharacterAggregate> BuildBattleResultCharacterAggregates(
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            var aggregates = new Dictionary<string, BattleResultCharacterAggregate>(StringComparer.OrdinalIgnoreCase);

            foreach (CoopBattleResultBridgeFile.BattleResultEntrySnapshot entry in result.Entries.Where(entry => entry != null))
            {
                string identityKey = BuildBattleResultIdentityKey(entry.PartyId, entry.HeroId, entry.OriginalCharacterId, entry.CharacterId);
                if (string.IsNullOrWhiteSpace(identityKey))
                    continue;

                if (!aggregates.TryGetValue(identityKey, out BattleResultCharacterAggregate aggregate))
                {
                    aggregate = new BattleResultCharacterAggregate
                    {
                        PartyId = entry.PartyId ?? string.Empty,
                        IdentityKey = identityKey,
                        HeroId = entry.HeroId,
                        OriginalCharacterId = entry.OriginalCharacterId,
                        CharacterId = entry.CharacterId,
                        TroopName = entry.TroopName,
                        IsHero = entry.IsHero
                    };
                    aggregates[identityKey] = aggregate;
                }

                aggregate.SnapshotCount += Math.Max(0, entry.SnapshotCount);
                aggregate.SnapshotWoundedCount += Math.Max(0, entry.SnapshotWoundedCount);
                aggregate.RemovedCount += Math.Max(0, entry.RemovedCount);
                aggregate.KilledCount += Math.Max(0, entry.KilledCount);
                aggregate.UnconsciousCount += Math.Max(0, entry.UnconsciousCount);
                aggregate.RoutedCount += Math.Max(0, entry.RoutedCount);
                aggregate.OtherRemovedCount += Math.Max(0, entry.OtherRemovedCount);
                aggregate.ScoreHitCount += Math.Max(0, entry.ScoreHitCount);
                aggregate.DamageDealt += Math.Max(0f, entry.DamageDealt);
                aggregate.DamageTaken += Math.Max(0f, entry.DamageTaken);
            }

            return aggregates;
        }

        private static string BuildBattleResultIdentityKey(string partyId, string heroId, string originalCharacterId, string characterId)
        {
            string identity =
                !string.IsNullOrWhiteSpace(heroId) ? "hero:" + heroId :
                !string.IsNullOrWhiteSpace(originalCharacterId) ? "orig:" + originalCharacterId :
                !string.IsNullOrWhiteSpace(characterId) ? "char:" + characterId :
                null;
            return identity == null ? null : (partyId ?? string.Empty) + "|" + identity;
        }

        private static void TryApplyAggregateToPartyRoster(
            EncounterPartyWritebackState partyState,
            BattleResultCharacterAggregate aggregate,
            BattleResultWritebackSummary summary)
        {
            if (partyState?.MemberRoster == null || aggregate == null)
                return;

            if (!TryResolvePartyRosterCharacter(
                    partyState.MemberRoster,
                    aggregate.HeroId,
                    aggregate.OriginalCharacterId,
                    aggregate.CharacterId,
                    out CharacterObject rosterCharacter,
                    out Hero rosterHero,
                    out int rosterIndex,
                    out int currentCount,
                    out int currentWounded))
            {
                summary.UnresolvedPartyAggregates++;
                AddWritebackSample(
                    summary.UnresolvedSamples,
                    "MissingTroop:" + (partyState.PartyId ?? "null") + "/" + (aggregate.TroopName ?? aggregate.IdentityKey ?? "entry"));
                return;
            }

            if (aggregate.IsHero)
            {
                if (aggregate.HeroShouldBeWounded && TryApplyHeroWoundWriteback(rosterHero, summary))
                {
                    AddWritebackSample(
                        summary.AdjustedSamples,
                        "HeroWounded:" + (rosterHero?.StringId ?? aggregate.HeroId ?? rosterCharacter?.StringId ?? "hero"));
                }
                else if (TryApplyHeroHitPointsWriteback(rosterHero, aggregate, summary, out string heroHitPointSample))
                {
                    AddWritebackSample(summary.AdjustedSamples, heroHitPointSample);
                }

                return;
            }

            int desiredCount = Math.Max(0, aggregate.DesiredCount);
            int desiredWounded = Math.Max(0, Math.Min(desiredCount, aggregate.DesiredWoundedCount));
            bool changed = false;

            if (currentCount != desiredCount)
            {
                partyState.MemberRoster.AddToCounts(rosterCharacter, desiredCount - currentCount, false, 0, 0, true, -1);
                changed = true;
            }

            int updatedIndex = partyState.MemberRoster.FindIndexOfTroop(rosterCharacter);
            if (updatedIndex >= 0)
            {
                int updatedWounded = partyState.MemberRoster.GetElementWoundedNumber(updatedIndex);
                if (updatedWounded != desiredWounded)
                {
                    partyState.MemberRoster.SetElementWoundedNumber(updatedIndex, desiredWounded);
                    changed = true;
                }
            }

            if (!changed)
                return;

            summary.RegularTroopsAdjusted++;
            AddWritebackSample(
                summary.AdjustedSamples,
                (partyState.PartyId ?? "party") + "/" +
                (aggregate.TroopName ?? rosterCharacter?.StringId ?? aggregate.IdentityKey ?? "troop") +
                " Current=" + currentCount + "/" + currentWounded +
                " -> Desired=" + desiredCount + "/" + desiredWounded +
                " Killed=" + aggregate.KilledCount +
                " Wounded=" + aggregate.UnconsciousCount);
        }

        private static bool TryResolvePartyRosterCharacter(
            TroopRoster roster,
            string heroId,
            string originalCharacterId,
            string characterId,
            out CharacterObject rosterCharacter,
            out Hero rosterHero,
            out int rosterIndex,
            out int currentCount,
            out int currentWounded)
        {
            rosterCharacter = null;
            rosterHero = null;
            rosterIndex = -1;
            currentCount = 0;
            currentWounded = 0;

            if (roster == null)
                return false;

            var troopRoster = roster.GetTroopRoster();
            if (troopRoster == null)
                return false;

            for (int i = 0; i < troopRoster.Count; i++)
            {
                TroopRosterElement element = troopRoster[i];
                CharacterObject candidate = element.Character;
                if (candidate == null)
                    continue;

                Hero candidateHero = candidate.HeroObject;
                bool matchesHero =
                    !string.IsNullOrWhiteSpace(heroId) &&
                    string.Equals(candidateHero?.StringId, heroId, StringComparison.OrdinalIgnoreCase);
                bool matchesOriginal =
                    !string.IsNullOrWhiteSpace(originalCharacterId) &&
                    (string.Equals(candidate.OriginalCharacter?.StringId, originalCharacterId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(candidate.StringId, originalCharacterId, StringComparison.OrdinalIgnoreCase));
                bool matchesCharacter =
                    !string.IsNullOrWhiteSpace(characterId) &&
                    string.Equals(candidate.StringId, characterId, StringComparison.OrdinalIgnoreCase);

                if (!matchesHero && !matchesOriginal && !matchesCharacter)
                    continue;

                rosterCharacter = candidate;
                rosterHero = candidateHero;
                rosterIndex = i;
                currentCount = element.Number;
                currentWounded = element.WoundedNumber;
                return true;
            }

            return false;
        }

        private static bool TryApplyHeroWoundWriteback(Hero hero, BattleResultWritebackSummary summary)
        {
            if (hero == null || !hero.IsAlive || hero.IsWounded)
                return false;

            try
            {
                hero.MakeWounded(hero, KillCharacterAction.KillCharacterActionDetail.WoundedInBattle);
                summary.HeroWoundsApplied++;
                return true;
            }
            catch (Exception ex)
            {
                AddWritebackSample(summary.UnresolvedSamples, "HeroWoundFailed:" + (hero.StringId ?? "hero") + "/" + ex.Message);
                return false;
            }
        }

        private static bool TryApplyHeroHitPointsWriteback(
            Hero hero,
            BattleResultCharacterAggregate aggregate,
            BattleResultWritebackSummary summary,
            out string adjustedSample)
        {
            adjustedSample = null;
            if (hero == null || aggregate == null || !hero.IsAlive || hero.IsWounded)
                return false;

            int hitPointLoss = Math.Max(0, (int)Math.Round(aggregate.DamageTaken, MidpointRounding.AwayFromZero));
            if (hitPointLoss <= 0)
                return false;

            try
            {
                int currentHitPoints = Math.Max(0, hero.HitPoints);
                int maxHitPoints = Math.Max(1, hero.MaxHitPoints);
                int desiredHitPoints = Math.Max(1, Math.Min(maxHitPoints, currentHitPoints - hitPointLoss));
                if (desiredHitPoints >= currentHitPoints)
                    return false;

                hero.HitPoints = desiredHitPoints;
                summary.HeroHitPointsAdjusted++;
                summary.HeroHitPointLossApplied += currentHitPoints - desiredHitPoints;
                adjustedSample =
                    "HeroHp:" +
                    (hero.StringId ?? aggregate.HeroId ?? aggregate.CharacterId ?? "hero") +
                    " " + currentHitPoints + "->" + desiredHitPoints +
                    " Loss=" + (currentHitPoints - desiredHitPoints);
                return true;
            }
            catch (Exception ex)
            {
                AddWritebackSample(summary.UnresolvedSamples, "HeroHpFailed:" + (hero.StringId ?? "hero") + "/" + ex.Message);
                return false;
            }
        }

        private static void TryApplyMainPartyCombatXpWriteback(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            IDictionary<string, EncounterPartyWritebackState> encounterParties,
            BattleResultWritebackSummary summary)
        {
            if (result == null || encounterParties == null)
                return;

            string mainPartyId = MobileParty.MainParty?.StringId;
            if (string.IsNullOrWhiteSpace(mainPartyId) ||
                !encounterParties.TryGetValue(mainPartyId, out EncounterPartyWritebackState mainPartyState) ||
                mainPartyState?.MemberRoster == null ||
                mainPartyState.PartyBase == null)
            {
                return;
            }

            CombatXpModel combatXpModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.CombatXpModel;
            if (combatXpModel == null)
                return;

            if (result.CombatEvents == null || result.CombatEvents.Count == 0)
            {
                TryApplyMainPartyFallbackCombatXpWriteback(result, mainPartyState, combatXpModel, summary);
                return;
            }

            Dictionary<string, CoopBattleResultBridgeFile.BattleResultEntrySnapshot> entriesById =
                (result.Entries ?? new List<CoopBattleResultBridgeFile.BattleResultEntrySnapshot>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.EntryId))
                .GroupBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (CoopBattleResultBridgeFile.BattleResultCombatEventSnapshot combatEvent in result.CombatEvents.Where(item => item != null))
            {
                if (!string.Equals(combatEvent.AttackerPartyId, mainPartyId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!entriesById.TryGetValue(combatEvent.AttackerEntryId ?? string.Empty, out CoopBattleResultBridgeFile.BattleResultEntrySnapshot attackerEntry) ||
                    attackerEntry == null)
                {
                    summary.UnresolvedCombatEvents++;
                    continue;
                }

                if (!TryResolvePartyRosterCharacter(
                        mainPartyState.MemberRoster,
                        attackerEntry.HeroId,
                        attackerEntry.OriginalCharacterId,
                        attackerEntry.CharacterId,
                        out CharacterObject attackerCharacter,
                        out Hero attackerHero,
                        out _,
                        out _,
                        out _))
                {
                    summary.UnresolvedCombatEvents++;
                    continue;
                }

                CharacterObject attackedCharacter = TryResolveCharacterObjectFromIds(
                    combatEvent.VictimOriginalCharacterId,
                    combatEvent.VictimCharacterId);
                if (attackedCharacter == null)
                {
                    summary.UnresolvedCombatEvents++;
                    continue;
                }

                int damage = Math.Max(0, (int)Math.Round(combatEvent.Damage, MidpointRounding.AwayFromZero));
                if (damage <= 0)
                    continue;

                float xpFromHit;
                try
                {
                    ExplainedNumber explained = combatXpModel.GetXpFromHit(
                        attackerCharacter,
                        mainPartyState.CaptainCharacter,
                        attackedCharacter,
                        mainPartyState.PartyBase,
                        damage,
                        combatEvent.IsFatal,
                        CombatXpModel.MissionTypeEnum.Battle);
                    xpFromHit = Math.Max(0f, explained.ResultNumber);
                }
                catch (Exception ex)
                {
                    summary.UnresolvedCombatEvents++;
                    AddWritebackSample(summary.UnresolvedSamples, "CombatXpFailed:" + ex.Message);
                    continue;
                }

                if (xpFromHit <= 0.01f)
                    continue;

                if (attackerHero != null)
                {
                    SkillObject hintedSkill = TryResolveSkillObject(combatEvent.WeaponSkillHint);
                    try
                    {
                        if (hintedSkill != null)
                        {
                            attackerHero.HeroDeveloper?.AddSkillXp(hintedSkill, xpFromHit, true, false);
                            if (attackerHero.HeroDeveloper == null)
                                attackerHero.AddSkillXp(hintedSkill, xpFromHit);
                            summary.HeroSkillXpApplied += xpFromHit;
                        }
                        else
                        {
                            summary.UnresolvedCombatEvents++;
                        }
                    }
                    catch (Exception ex)
                    {
                        summary.UnresolvedCombatEvents++;
                        AddWritebackSample(summary.UnresolvedSamples, "HeroXpFailed:" + (attackerHero.StringId ?? "hero") + "/" + ex.Message);
                    }

                    continue;
                }

                int troopXp = Math.Max(0, (int)Math.Round(xpFromHit, MidpointRounding.AwayFromZero));
                if (troopXp <= 0)
                    continue;

                try
                {
                    mainPartyState.MemberRoster.AddXpToTroop(attackerCharacter, troopXp);
                    summary.TroopXpApplied += troopXp;
                }
                catch (Exception ex)
                {
                    summary.UnresolvedCombatEvents++;
                    AddWritebackSample(summary.UnresolvedSamples, "TroopXpFailed:" + (attackerCharacter.StringId ?? "troop") + "/" + ex.Message);
                }
            }
        }

        private static void TryApplyMainPartyFallbackCombatXpWriteback(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            EncounterPartyWritebackState mainPartyState,
            CombatXpModel combatXpModel,
            BattleResultWritebackSummary summary)
        {
            if (result?.Entries == null || mainPartyState?.MemberRoster == null || mainPartyState.PartyBase == null)
                return;

            string mainPartyId = mainPartyState.PartyId;
            if (string.IsNullOrWhiteSpace(mainPartyId))
                return;

            var participants = new List<FallbackCombatXpParticipant>();
            int totalWeight = 0;
            foreach (CoopBattleResultBridgeFile.BattleResultEntrySnapshot entry in result.Entries.Where(item =>
                         item != null &&
                         string.Equals(item.PartyId, mainPartyId, StringComparison.OrdinalIgnoreCase) &&
                         Math.Max(0, item.SnapshotCount) > 0))
            {
                if (!TryResolvePartyRosterCharacter(
                        mainPartyState.MemberRoster,
                        entry.HeroId,
                        entry.OriginalCharacterId,
                        entry.CharacterId,
                        out CharacterObject participantCharacter,
                        out Hero participantHero,
                        out _,
                        out _,
                        out _))
                {
                    continue;
                }

                int weight = Math.Max(1, entry.SnapshotCount);
                participants.Add(new FallbackCombatXpParticipant
                {
                    Character = participantCharacter,
                    Hero = participantHero,
                    Weight = weight
                });
                totalWeight += weight;
            }

            if (participants.Count == 0 || totalWeight <= 0)
                return;

            bool anyApplied = false;
            foreach (CoopBattleResultBridgeFile.BattleResultEntrySnapshot enemyEntry in result.Entries.Where(item =>
                         item != null &&
                         !string.Equals(item.PartyId, mainPartyId, StringComparison.OrdinalIgnoreCase)))
            {
                int casualtyCount = Math.Max(0, enemyEntry.KilledCount + enemyEntry.UnconsciousCount + enemyEntry.OtherRemovedCount);
                if (casualtyCount <= 0)
                    continue;

                CharacterObject attackedCharacter = TryResolveCharacterObjectFromIds(
                    enemyEntry.OriginalCharacterId,
                    enemyEntry.CharacterId,
                    enemyEntry.SpawnTemplateId);
                if (attackedCharacter == null)
                {
                    AddWritebackSample(summary.UnresolvedSamples, "FallbackXpMissingVictim:" + (enemyEntry.TroopName ?? enemyEntry.CharacterId ?? "enemy"));
                    continue;
                }

                int damage = Math.Max(1, TryGetCharacterBaseHitPoints(attackedCharacter));
                foreach (FallbackCombatXpParticipant participant in participants)
                {
                    if (participant?.Character == null)
                        continue;

                    float xpFromCasualty;
                    try
                    {
                        ExplainedNumber explained = combatXpModel.GetXpFromHit(
                            participant.Character,
                            mainPartyState.CaptainCharacter,
                            attackedCharacter,
                            mainPartyState.PartyBase,
                            damage,
                            true,
                            CombatXpModel.MissionTypeEnum.Battle);
                        xpFromCasualty = Math.Max(0f, explained.ResultNumber);
                    }
                    catch (Exception ex)
                    {
                        AddWritebackSample(summary.UnresolvedSamples, "FallbackCombatXpFailed:" + ex.Message);
                        continue;
                    }

                    if (xpFromCasualty <= 0.01f)
                        continue;

                    float weightedXp = xpFromCasualty * casualtyCount * (participant.Weight / (float)totalWeight);
                    if (weightedXp <= 0.01f)
                        continue;

                    if (participant.Hero != null)
                    {
                        SkillObject bestSkill = TryResolveBestCombatSkillObject(participant.Character);
                        if (bestSkill == null)
                        {
                            summary.UnresolvedCombatEvents++;
                            continue;
                        }

                        try
                        {
                            participant.Hero.HeroDeveloper?.AddSkillXp(bestSkill, weightedXp, true, false);
                            if (participant.Hero.HeroDeveloper == null)
                                participant.Hero.AddSkillXp(bestSkill, weightedXp);
                            summary.HeroSkillXpApplied += weightedXp;
                            anyApplied = true;
                        }
                        catch (Exception ex)
                        {
                            summary.UnresolvedCombatEvents++;
                            AddWritebackSample(summary.UnresolvedSamples, "FallbackHeroXpFailed:" + (participant.Hero.StringId ?? "hero") + "/" + ex.Message);
                        }

                        continue;
                    }

                    int troopXp = Math.Max(0, (int)Math.Round(weightedXp, MidpointRounding.AwayFromZero));
                    if (troopXp <= 0)
                        continue;

                    try
                    {
                        mainPartyState.MemberRoster.AddXpToTroop(participant.Character, troopXp);
                        summary.TroopXpApplied += troopXp;
                        anyApplied = true;
                    }
                    catch (Exception ex)
                    {
                        summary.UnresolvedCombatEvents++;
                        AddWritebackSample(summary.UnresolvedSamples, "FallbackTroopXpFailed:" + (participant.Character.StringId ?? "troop") + "/" + ex.Message);
                    }
                }
            }

            if (anyApplied)
                AddWritebackSample(summary.AdjustedSamples, "FallbackCombatXp:main-party-casualty-pool");
        }

        private static void TryBuildMainPartyRewardProjection(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            IDictionary<string, EncounterPartyWritebackState> encounterParties,
            BattleResultWritebackSummary summary,
            BattleResultWritebackSummary.RewardProjectionSummary cachedRewardProjection = null)
        {
            BattleResultWritebackSummary.RewardProjectionSummary projection = summary?.RewardProjection;
            if (projection == null || result == null || encounterParties == null)
                return;

            projection.WinnerSide = result.WinnerSide ?? string.Empty;

            string mainPartyId = MobileParty.MainParty?.StringId;
            if (string.IsNullOrWhiteSpace(mainPartyId) ||
                !encounterParties.TryGetValue(mainPartyId, out EncounterPartyWritebackState mainPartyState) ||
                mainPartyState?.PartyBase == null)
            {
                AddWritebackSample(summary.UnresolvedSamples, "RewardProjectionMissingMainParty");
                return;
            }

            BattleSideEnum winnerSide = TryResolveWinnerSideEnum(result.WinnerSide);
            if (winnerSide == BattleSideEnum.None)
            {
                AddWritebackSample(summary.UnresolvedSamples, "RewardProjectionMissingWinnerSide");
                return;
            }

            MapEvent battle = PlayerEncounter.Battle;
            if (battle == null)
            {
                if (TryPopulateRewardProjectionFromCachedSnapshot(cachedRewardProjection, projection))
                {
                    projection.Ready = true;
                    AddWritebackSample(
                        summary.AdjustedSamples,
                        "RewardProjection:cached Share=" + projection.ContributionShare.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                        " Gold=" + projection.ProjectedGoldDelta);
                    return;
                }

                AddWritebackSample(summary.UnresolvedSamples, "RewardProjectionMissingBattle");
                return;
            }

            MapEventSide winnerMapSide = battle.GetMapEventSide(winnerSide);
            MapEventSide defeatedMapSide = battle.GetMapEventSide(winnerSide.GetOppositeSide());
            BattleRewardModel battleRewardModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.BattleRewardModel;
            PartyMoraleModel partyMoraleModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.PartyMoraleModel;
            if (winnerMapSide == null || defeatedMapSide == null || battleRewardModel == null || partyMoraleModel == null)
            {
                AddWritebackSample(summary.UnresolvedSamples, "RewardProjectionMissingModels");
                return;
            }

            BattleSideEnum mainPartySide = mainPartyState.PartyBase.Side;
            projection.MainPartySide = mainPartySide.ToString();
            projection.MainPartyOnWinnerSide = mainPartySide == winnerSide;
            projection.WinnerRenownValue = Math.Max(0f, winnerMapSide.RenownValue);
            projection.WinnerInfluenceValue = Math.Max(0f, winnerMapSide.InfluenceValue);
            MapEventParty mainPartyMapEventParty = TryFindMapEventParty(
                projection.MainPartyOnWinnerSide ? winnerMapSide : defeatedMapSide,
                mainPartyState.PartyBase);
            projection.ContributionShare = TryResolveRewardContributionShare(
                winnerMapSide,
                mainPartyState.PartyBase,
                projection);

            if (TryPopulateRewardProjectionFromCommittedMapEventParty(mainPartyMapEventParty, projection))
            {
                projection.Ready = true;
                AddWritebackSample(
                    summary.AdjustedSamples,
                    "RewardProjection:" +
                    (projection.MainPartyOnWinnerSide ? "winner" : "loser") +
                    "/committed Share=" + projection.ContributionShare.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                    " Gold=" + projection.ProjectedGoldDelta);
                return;
            }

            if (projection.MainPartyOnWinnerSide)
            {
                try
                {
                    projection.Source = "reward-model-fallback";
                    float victoryFactor = TryCalculateWinnerAftermathScale(battle, defeatedMapSide);
                    projection.ProjectedRenown = Math.Max(
                        0f,
                        battleRewardModel.CalculateRenownGain(
                            mainPartyState.PartyBase,
                            projection.WinnerRenownValue,
                            projection.ContributionShare).ResultNumber * victoryFactor);

                    projection.ProjectedInfluence = Math.Max(
                        0f,
                        battleRewardModel.CalculateInfluenceGain(
                            mainPartyState.PartyBase,
                            projection.WinnerInfluenceValue,
                            projection.ContributionShare).ResultNumber * victoryFactor);

                    projection.ProjectedMoraleChange = battleRewardModel.CalculateMoraleGainVictory(
                        mainPartyState.PartyBase,
                        projection.WinnerRenownValue,
                        projection.ContributionShare,
                        battle).ResultNumber;

                    float goldShare = 0f;
                    foreach (KeyValuePair<MapEventParty, float> chance in battleRewardModel.GetLootGoldChances(winnerMapSide.Parties))
                    {
                        if (chance.Key?.Party == mainPartyState.PartyBase)
                        {
                            goldShare = Math.Max(0f, chance.Value);
                            break;
                        }
                    }

                    int totalPlunderedGold = 0;
                    bool usedCommittedGoldLoss = false;
                    foreach (MapEventParty defeatedParty in defeatedMapSide.Parties)
                    {
                        if (defeatedParty == null)
                            continue;

                        if (defeatedParty.GoldLost > 0)
                        {
                            totalPlunderedGold += Math.Max(0, defeatedParty.GoldLost);
                            usedCommittedGoldLoss = true;
                            continue;
                        }

                        if (defeatedParty.Party == null)
                            continue;

                        totalPlunderedGold += Math.Max(
                            0,
                            battleRewardModel.CalculatePlunderedGoldAmountFromDefeatedParty(defeatedParty.Party));
                    }

                    if (usedCommittedGoldLoss)
                        projection.Source = "reward-model-fallback/committed-gold-loss";

                    projection.ProjectedGoldGain = Math.Max(
                        0,
                        (int)Math.Round(totalPlunderedGold * goldShare, MidpointRounding.AwayFromZero));
                }
                catch (Exception ex)
                {
                    AddWritebackSample(summary.UnresolvedSamples, "RewardProjectionWinnerFailed:" + ex.Message);
                    return;
                }
            }
            else
            {
                try
                {
                    projection.Source = "reward-model-fallback";
                    projection.ProjectedMoraleChange = partyMoraleModel.GetDefeatMoraleChange(mainPartyState.PartyBase);
                    projection.ProjectedGoldLoss = Math.Max(
                        0,
                        battleRewardModel.CalculatePlunderedGoldAmountFromDefeatedParty(mainPartyState.PartyBase));
                }
                catch (Exception ex)
                {
                    AddWritebackSample(summary.UnresolvedSamples, "RewardProjectionDefeatFailed:" + ex.Message);
                    return;
                }
            }

            projection.Ready = true;
            AddWritebackSample(
                summary.AdjustedSamples,
                "RewardProjection:" +
                (projection.MainPartyOnWinnerSide ? "winner" : "loser") +
                " Share=" + projection.ContributionShare.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                " Gold=" + projection.ProjectedGoldDelta);
        }

        private static float TryResolveRewardContributionShare(
            MapEventSide winnerMapSide,
            PartyBase targetParty,
            BattleResultWritebackSummary.RewardProjectionSummary projection)
        {
            if (winnerMapSide == null || targetParty == null)
                return 0f;

            BattleRuntimeState snapshotState = BattleSnapshotRuntimeState.GetState();
            float targetWeight = 0f;
            float totalWeight = 0f;
            bool usedMapEventContribution = false;
            bool usedSnapshotContribution = false;

            foreach (MapEventParty winnerParty in winnerMapSide.Parties)
            {
                PartyBase partyBase = winnerParty?.Party;
                if (partyBase == null)
                    continue;

                float weight = 0f;
                int mapEventContribution = Math.Max(0, winnerParty.ContributionToBattle);
                if (mapEventContribution > 0)
                {
                    weight = mapEventContribution;
                    usedMapEventContribution = true;
                }
                else
                {
                    string partyId = TryGetStringId(partyBase) ?? TryGetStringId(partyBase.MobileParty);
                    if (snapshotState?.PartiesById != null &&
                        !string.IsNullOrWhiteSpace(partyId) &&
                        snapshotState.PartiesById.TryGetValue(partyId, out BattlePartyState snapshotParty) &&
                        snapshotParty != null)
                    {
                        int contribution = Math.Max(0, snapshotParty.Modifiers?.ContributionToBattle ?? 0);
                        if (contribution > 0)
                        {
                            weight = contribution;
                            usedSnapshotContribution = true;
                        }
                        else
                        {
                            weight = Math.Max(1, snapshotParty.TotalManCount);
                        }
                    }
                }

                if (weight <= 0.01f)
                    weight = Math.Max(1, partyBase.MemberRoster?.TotalManCount ?? 0);

                totalWeight += weight;
                if (partyBase == targetParty)
                    targetWeight = weight;
            }

            if (projection != null)
                projection.UsedMapEventContribution = usedMapEventContribution;

            if (projection != null)
                projection.UsedSnapshotContribution = usedSnapshotContribution;

            if (totalWeight <= 0.01f || targetWeight <= 0.01f)
                return 0f;

            return Math.Max(0f, Math.Min(1f, targetWeight / totalWeight));
        }

        private static void TryApplyMainPartyRewardWriteback(
            IDictionary<string, EncounterPartyWritebackState> encounterParties,
            BattleResultWritebackSummary summary)
        {
            BattleResultWritebackSummary.RewardProjectionSummary projection = summary?.RewardProjection;
            BattleResultWritebackSummary.RewardApplySummary rewardApply = summary?.RewardApply;
            if (projection == null || rewardApply == null || encounterParties == null)
                return;

            rewardApply.Attempted = true;
            rewardApply.Source = projection.Source;

            string mainPartyId = MobileParty.MainParty?.StringId;
            if (string.IsNullOrWhiteSpace(mainPartyId) ||
                !encounterParties.TryGetValue(mainPartyId, out EncounterPartyWritebackState mainPartyState) ||
                mainPartyState?.PartyBase == null)
            {
                AddWritebackSample(summary.UnresolvedSamples, "RewardApplyMissingMainParty");
                return;
            }

            if (!projection.Ready)
            {
                AddWritebackSample(summary.UnresolvedSamples, "RewardApplyProjectionNotReady");
                return;
            }

            if (projection.UsedCommittedMapEventResults)
            {
                rewardApply.SkippedCommittedResults = true;
                AddWritebackSample(summary.AdjustedSamples, "RewardApply:skipped-committed");
                return;
            }

            Hero leaderHero = mainPartyState.PartyBase.LeaderHero;
            MobileParty mobileParty = mainPartyState.MobileParty ?? mainPartyState.PartyBase.MobileParty;

            try
            {
                if (projection.ProjectedGoldGain > 0)
                {
                    if (leaderHero != null)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(null, leaderHero, projection.ProjectedGoldGain, disableNotification: true);
                        rewardApply.AppliedGold = true;
                    }
                    else if (mobileParty != null && mobileParty.IsPartyTradeActive)
                    {
                        mobileParty.PartyTradeGold += projection.ProjectedGoldGain;
                        rewardApply.AppliedGold = true;
                    }
                }

                if (projection.ProjectedGoldLoss > 0)
                {
                    if (leaderHero != null)
                    {
                        GiveGoldAction.ApplyBetweenCharacters(leaderHero, null, projection.ProjectedGoldLoss, disableNotification: true);
                        rewardApply.AppliedGold = true;
                    }
                    else if (mobileParty != null && mobileParty.IsPartyTradeActive)
                    {
                        mobileParty.PartyTradeGold -= projection.ProjectedGoldLoss;
                        rewardApply.AppliedGold = true;
                    }
                }

                rewardApply.GoldDeltaApplied = rewardApply.AppliedGold ? projection.ProjectedGoldDelta : 0;

                if (mobileParty != null && Math.Abs(projection.ProjectedMoraleChange) > 0.001f)
                {
                    mobileParty.RecentEventsMorale += projection.ProjectedMoraleChange;
                    rewardApply.AppliedMorale = true;
                    rewardApply.MoraleApplied = projection.ProjectedMoraleChange;
                }

                if (leaderHero != null && projection.ProjectedRenown > 0.001f)
                {
                    GainRenownAction.Apply(leaderHero, projection.ProjectedRenown, doNotNotify: true);
                    rewardApply.AppliedRenown = true;
                    rewardApply.RenownApplied = projection.ProjectedRenown;
                }

                if (leaderHero != null && projection.ProjectedInfluence > 0.001f)
                {
                    GainKingdomInfluenceAction.ApplyForBattle(leaderHero, projection.ProjectedInfluence);
                    rewardApply.AppliedInfluence = true;
                    rewardApply.InfluenceApplied = projection.ProjectedInfluence;
                }
            }
            catch (Exception ex)
            {
                AddWritebackSample(summary.UnresolvedSamples, "RewardApplyFailed:" + ex.Message);
                return;
            }

            rewardApply.Applied =
                rewardApply.AppliedGold ||
                rewardApply.AppliedMorale ||
                rewardApply.AppliedRenown ||
                rewardApply.AppliedInfluence;

            AddWritebackSample(
                summary.AdjustedSamples,
                "RewardApply:" +
                (rewardApply.Applied ? "applied" : "no-op") +
                " Gold=" + rewardApply.GoldDeltaApplied +
                " Morale=" + rewardApply.MoraleApplied.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " Renown=" + rewardApply.RenownApplied.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                " Influence=" + rewardApply.InfluenceApplied.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void TryBuildMainPartyLootAftermathAudit(
            BattleResultWritebackSummary summary,
            BattleResultWritebackSummary.LootAftermathSummary cachedLootAftermath = null)
        {
            BattleResultWritebackSummary.LootAftermathSummary lootSummary = summary?.LootAftermath;
            if (lootSummary == null)
                return;

            try
            {
                MapEvent battle = PlayerEncounter.Battle;
                PartyBase mainParty = MobileParty.MainParty?.Party;
                if (battle == null || mainParty == null)
                {
                    if (TryPopulateLootAftermathFromCachedSnapshot(cachedLootAftermath, lootSummary))
                    {
                        AddWritebackSample(
                            summary.AdjustedSamples,
                            "LootAftermath:cached M=" + lootSummary.LootMemberCount +
                            " P=" + lootSummary.LootPrisonerCount +
                            " I=" + lootSummary.LootItemStacks + "/" + lootSummary.LootItemUnits);
                    }
                    return;
                }

                MapEventSide mainPartySide = battle.GetMapEventSide(mainParty.Side);
                MapEventParty mainPartyMapEventParty = TryFindMapEventParty(mainPartySide, mainParty);
                if (mainPartyMapEventParty == null)
                    return;

                lootSummary.Source = "map-event-party-loot-rosters";
                lootSummary.LootMemberCount = TryCountTroopRosterMembers(mainPartyMapEventParty.RosterToReceiveLootMembers, out string memberSample);
                lootSummary.LootPrisonerCount = TryCountTroopRosterMembers(mainPartyMapEventParty.RosterToReceiveLootPrisoners, out string prisonerSample);
                TryCountItemRoster(mainPartyMapEventParty.RosterToReceiveLootItems, lootSummary.ItemEntries, out int itemStacks, out int itemUnits, out string itemSample);
                lootSummary.LootItemStacks = itemStacks;
                lootSummary.LootItemUnits = itemUnits;
                lootSummary.LootMemberSample = memberSample;
                lootSummary.LootPrisonerSample = prisonerSample;
                lootSummary.LootItemSample = itemSample;
                lootSummary.Ready =
                    lootSummary.LootMemberCount > 0 ||
                    lootSummary.LootPrisonerCount > 0 ||
                    lootSummary.LootItemStacks > 0;

                if (lootSummary.Ready)
                {
                    AddWritebackSample(
                        summary.AdjustedSamples,
                        "LootAftermath:M=" + lootSummary.LootMemberCount +
                        " P=" + lootSummary.LootPrisonerCount +
                        " I=" + lootSummary.LootItemStacks + "/" + lootSummary.LootItemUnits);
                }
            }
            catch (Exception ex)
            {
                AddWritebackSample(summary.UnresolvedSamples, "LootAftermathAuditFailed:" + ex.Message);
            }
        }

        private static int TryCountTroopRosterMembers(TroopRoster roster, out string sample)
        {
            sample = null;
            if (roster == null)
                return 0;

            int total = 0;
            try
            {
                var parts = new List<string>();
                foreach (TroopRosterElement element in roster.GetTroopRoster())
                {
                    CharacterObject character = element.Character;
                    int number = Math.Max(0, element.Number);
                    if (character == null || number <= 0)
                        continue;

                    total += number;
                    if (parts.Count < 3)
                        parts.Add((character.StringId ?? character.Name?.ToString() ?? "troop") + "x" + number);
                }

                if (parts.Count > 0)
                    sample = string.Join(",", parts);
            }
            catch
            {
                return total;
            }

            return total;
        }

        private static void TryCountItemRoster(
            object itemRoster,
            ICollection<BattleResultWritebackSummary.LootItemEntrySummary> itemEntries,
            out int stackCount,
            out int unitCount,
            out string sample)
        {
            stackCount = 0;
            unitCount = 0;
            sample = null;
            itemEntries?.Clear();

            IEnumerable enumerable = itemRoster as IEnumerable;
            if (enumerable == null)
                return;

            var parts = new List<string>();
            foreach (object element in enumerable)
            {
                if (element == null)
                    continue;

                int amount = Math.Max(0, TryGetIntProperty(element, "Amount"));
                if (amount <= 0)
                    continue;

                object equipmentElement = TryGetPropertyValue(element, "EquipmentElement");
                string itemId = TryGetEquipmentElementItemId(equipmentElement);
                if (string.IsNullOrWhiteSpace(itemId))
                    itemId = TryGetStringId(TryGetPropertyValue(equipmentElement, "Item"));
                if (string.IsNullOrWhiteSpace(itemId))
                    itemId = "item";

                stackCount++;
                unitCount += amount;
                itemEntries?.Add(new BattleResultWritebackSummary.LootItemEntrySummary
                {
                    ItemId = itemId,
                    Amount = amount
                });
                if (parts.Count < 3)
                    parts.Add(itemId + "x" + amount);
            }

            if (parts.Count > 0)
                sample = string.Join(",", parts);
        }

        private static void TryApplyMainPartyItemLootWriteback(
            IDictionary<string, EncounterPartyWritebackState> encounterParties,
            BattleResultWritebackSummary summary)
        {
            BattleResultWritebackSummary.LootAftermathSummary lootSummary = summary?.LootAftermath;
            BattleResultWritebackSummary.LootApplySummary lootApply = summary?.LootApply;
            if (lootSummary == null || lootApply == null || encounterParties == null)
                return;

            lootApply.Attempted = true;
            lootApply.Source = lootSummary.Source;

            string mainPartyId = MobileParty.MainParty?.StringId;
            if (string.IsNullOrWhiteSpace(mainPartyId) ||
                !encounterParties.TryGetValue(mainPartyId, out EncounterPartyWritebackState mainPartyState))
            {
                AddWritebackSample(summary.UnresolvedSamples, "LootApplyMissingMainParty");
                return;
            }

            if (!lootSummary.Ready)
            {
                AddWritebackSample(summary.UnresolvedSamples, "LootApplyAftermathNotReady");
                return;
            }

            if (lootSummary.ItemEntries == null || lootSummary.ItemEntries.Count == 0 || lootSummary.LootItemUnits <= 0)
            {
                AddWritebackSample(summary.AdjustedSamples, "LootApply:no-items");
                return;
            }

            if (!string.IsNullOrWhiteSpace(lootSummary.Source) &&
                !lootSummary.Source.StartsWith("cached-host-aftermath/", StringComparison.OrdinalIgnoreCase))
            {
                lootApply.SkippedCommittedResults = true;
                AddWritebackSample(summary.AdjustedSamples, "LootApply:skipped-live-map-event");
                return;
            }

            MobileParty targetParty = mainPartyState.MobileParty ?? MobileParty.MainParty;
            ItemRoster itemRoster = targetParty?.ItemRoster;
            if (itemRoster == null)
            {
                AddWritebackSample(summary.UnresolvedSamples, "LootApplyMissingItemRoster");
                return;
            }

            try
            {
                foreach (BattleResultWritebackSummary.LootItemEntrySummary entry in lootSummary.ItemEntries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId) || entry.Amount <= 0)
                        continue;

                    ItemObject itemObject = null;
                    try
                    {
                        itemObject = MBObjectManager.Instance?.GetObject<ItemObject>(entry.ItemId);
                    }
                    catch
                    {
                        itemObject = null;
                    }

                    if (itemObject == null)
                    {
                        AddWritebackSample(summary.UnresolvedSamples, "MissingLootItem:" + entry.ItemId);
                        continue;
                    }

                    itemRoster.AddToCounts(itemObject, entry.Amount);
                    lootApply.ItemStacksApplied++;
                    lootApply.ItemUnitsApplied += entry.Amount;
                }
            }
            catch (Exception ex)
            {
                AddWritebackSample(summary.UnresolvedSamples, "LootApplyFailed:" + ex.Message);
                return;
            }

            lootApply.Applied = lootApply.ItemUnitsApplied > 0;
            AddWritebackSample(
                summary.AdjustedSamples,
                "LootApply:" +
                (lootApply.Applied ? "applied" : "no-op") +
                " Items=" + lootApply.ItemStacksApplied + "/" + lootApply.ItemUnitsApplied);
        }

        private static bool TryPopulateLootAftermathFromCachedSnapshot(
            BattleResultWritebackSummary.LootAftermathSummary cachedLootAftermath,
            BattleResultWritebackSummary.LootAftermathSummary targetLootAftermath)
        {
            if (cachedLootAftermath == null || targetLootAftermath == null || !cachedLootAftermath.Ready)
                return false;

            targetLootAftermath.Ready = true;
            targetLootAftermath.Source = "cached-host-aftermath/" + (cachedLootAftermath.Source ?? "unknown");
            targetLootAftermath.LootMemberCount = cachedLootAftermath.LootMemberCount;
            targetLootAftermath.LootPrisonerCount = cachedLootAftermath.LootPrisonerCount;
            targetLootAftermath.LootItemStacks = cachedLootAftermath.LootItemStacks;
            targetLootAftermath.LootItemUnits = cachedLootAftermath.LootItemUnits;
            targetLootAftermath.LootMemberSample = cachedLootAftermath.LootMemberSample;
            targetLootAftermath.LootPrisonerSample = cachedLootAftermath.LootPrisonerSample;
            targetLootAftermath.LootItemSample = cachedLootAftermath.LootItemSample;
            targetLootAftermath.ItemEntries.Clear();
            foreach (BattleResultWritebackSummary.LootItemEntrySummary entry in cachedLootAftermath.ItemEntries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId) || entry.Amount <= 0)
                    continue;

                targetLootAftermath.ItemEntries.Add(new BattleResultWritebackSummary.LootItemEntrySummary
                {
                    ItemId = entry.ItemId,
                    Amount = entry.Amount
                });
            }
            return true;
        }

        private static MapEventParty TryFindMapEventParty(MapEventSide side, PartyBase targetParty)
        {
            if (side == null || targetParty == null)
                return null;

            foreach (MapEventParty party in side.Parties)
            {
                if (party?.Party == targetParty)
                    return party;
            }

            return null;
        }

        private static bool TryPopulateRewardProjectionFromCachedSnapshot(
            BattleResultWritebackSummary.RewardProjectionSummary cachedProjection,
            BattleResultWritebackSummary.RewardProjectionSummary targetProjection)
        {
            if (cachedProjection == null || targetProjection == null || !cachedProjection.Ready)
                return false;

            targetProjection.Ready = true;
            targetProjection.UsedCommittedMapEventResults = cachedProjection.UsedCommittedMapEventResults;
            targetProjection.UsedMapEventContribution = cachedProjection.UsedMapEventContribution;
            targetProjection.MainPartyOnWinnerSide = cachedProjection.MainPartyOnWinnerSide;
            targetProjection.UsedSnapshotContribution = cachedProjection.UsedSnapshotContribution;
            targetProjection.Source = "cached-host-aftermath/" + (cachedProjection.Source ?? "unknown");
            targetProjection.WinnerSide = cachedProjection.WinnerSide;
            targetProjection.MainPartySide = cachedProjection.MainPartySide;
            targetProjection.WinnerRenownValue = cachedProjection.WinnerRenownValue;
            targetProjection.WinnerInfluenceValue = cachedProjection.WinnerInfluenceValue;
            targetProjection.ContributionShare = cachedProjection.ContributionShare;
            targetProjection.ProjectedRenown = cachedProjection.ProjectedRenown;
            targetProjection.ProjectedInfluence = cachedProjection.ProjectedInfluence;
            targetProjection.ProjectedMoraleChange = cachedProjection.ProjectedMoraleChange;
            targetProjection.ProjectedGoldGain = cachedProjection.ProjectedGoldGain;
            targetProjection.ProjectedGoldLoss = cachedProjection.ProjectedGoldLoss;
            return true;
        }

        private static bool TryPopulateRewardProjectionFromCommittedMapEventParty(
            MapEventParty mainPartyMapEventParty,
            BattleResultWritebackSummary.RewardProjectionSummary projection)
        {
            if (mainPartyMapEventParty == null || projection == null)
                return false;

            bool hasCommittedWinnerRewards =
                mainPartyMapEventParty.GainedRenown > 0.001f ||
                mainPartyMapEventParty.GainedInfluence > 0.001f ||
                mainPartyMapEventParty.PlunderedGold > 0 ||
                Math.Abs(mainPartyMapEventParty.MoraleChange) > 0.001f;

            bool hasCommittedLoserRewards =
                mainPartyMapEventParty.GoldLost > 0 ||
                Math.Abs(mainPartyMapEventParty.MoraleChange) > 0.001f;

            bool hasCommittedAftermath = projection.MainPartyOnWinnerSide
                ? hasCommittedWinnerRewards
                : hasCommittedLoserRewards;
            if (!hasCommittedAftermath)
                return false;

            projection.UsedCommittedMapEventResults = true;
            projection.Source = "committed-map-event-party";
            projection.ProjectedRenown = Math.Max(0f, mainPartyMapEventParty.GainedRenown);
            projection.ProjectedInfluence = Math.Max(0f, mainPartyMapEventParty.GainedInfluence);
            projection.ProjectedMoraleChange = mainPartyMapEventParty.MoraleChange;
            projection.ProjectedGoldGain = Math.Max(0, mainPartyMapEventParty.PlunderedGold);
            projection.ProjectedGoldLoss = Math.Max(0, mainPartyMapEventParty.GoldLost);
            return true;
        }

        private static float TryCalculateWinnerAftermathScale(MapEvent battle, MapEventSide defeatedMapSide)
        {
            if (battle == null || defeatedMapSide == null)
                return 1f;

            try
            {
                float defeatedSideStartingStrength = battle.StrengthOfSide[(int)defeatedMapSide.MissionSide];
                if (defeatedSideStartingStrength <= 0.01f)
                    return 1f;

                float defeatedSideRemainingStrength = 0f;
                foreach (MapEventParty defeatedParty in defeatedMapSide.Parties)
                {
                    if (defeatedParty?.Party == null)
                        continue;

                    defeatedSideRemainingStrength += defeatedParty.Party.GetCustomStrength(
                        defeatedParty.Party.Side,
                        MapEvent.PowerCalculationContext.PlainBattle);
                }

                float aftermathScale = Math.Max(0f, defeatedSideStartingStrength - defeatedSideRemainingStrength) / defeatedSideStartingStrength;
                return Math.Max(0f, Math.Min(1f, aftermathScale));
            }
            catch
            {
                return 1f;
            }
        }

        private static CharacterObject TryResolveCharacterObjectFromIds(params string[] ids)
        {
            if (ids == null)
                return null;

            foreach (string id in ids)
            {
                CharacterObject resolved = TryResolveCharacterObjectById(id);
                if (resolved != null)
                    return resolved;
            }

            return null;
        }

        private static CharacterObject TryResolveCharacterObjectById(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || MBObjectManager.Instance == null)
                return null;

            try
            {
                return MBObjectManager.Instance.GetObject<BasicCharacterObject>(id) as CharacterObject;
            }
            catch
            {
                return null;
            }
        }

        private static SkillObject TryResolveSkillObject(string skillHint)
        {
            if (string.IsNullOrWhiteSpace(skillHint))
                return null;

            if (TryGetDefaultSkillObject(skillHint) is SkillObject directSkill)
                return directSkill;

            try
            {
                return MBObjectManager.Instance?.GetObject<SkillObject>(skillHint);
            }
            catch
            {
                return null;
            }
        }

        private static SkillObject TryResolveBestCombatSkillObject(CharacterObject character)
        {
            if (character == null)
                return null;

            string[] candidateSkills = { "OneHanded", "TwoHanded", "Polearm", "Bow", "Crossbow", "Throwing" };
            string bestSkillName = null;
            int bestSkillValue = -1;
            foreach (string candidateSkill in candidateSkills)
            {
                int candidateValue = TryGetCharacterSkillValue(character, candidateSkill);
                if (candidateValue <= bestSkillValue)
                    continue;

                bestSkillValue = candidateValue;
                bestSkillName = candidateSkill;
            }

            return TryResolveSkillObject(bestSkillName);
        }

        private static void AddWritebackSample(ICollection<string> samples, string sample, int maxCount = 12)
        {
            if (samples == null || string.IsNullOrWhiteSpace(sample) || samples.Count >= maxCount)
                return;

            samples.Add(sample);
        }

        private static BattleResultWritebackSummary.RewardProjectionSummary CloneRewardProjectionSummary(
            BattleResultWritebackSummary.RewardProjectionSummary source)
        {
            if (source == null)
                return null;

            return new BattleResultWritebackSummary.RewardProjectionSummary
            {
                Ready = source.Ready,
                UsedCommittedMapEventResults = source.UsedCommittedMapEventResults,
                UsedMapEventContribution = source.UsedMapEventContribution,
                MainPartyOnWinnerSide = source.MainPartyOnWinnerSide,
                UsedSnapshotContribution = source.UsedSnapshotContribution,
                Source = source.Source,
                WinnerSide = source.WinnerSide,
                MainPartySide = source.MainPartySide,
                WinnerRenownValue = source.WinnerRenownValue,
                WinnerInfluenceValue = source.WinnerInfluenceValue,
                ContributionShare = source.ContributionShare,
                ProjectedRenown = source.ProjectedRenown,
                ProjectedInfluence = source.ProjectedInfluence,
                ProjectedMoraleChange = source.ProjectedMoraleChange,
                ProjectedGoldGain = source.ProjectedGoldGain,
                ProjectedGoldLoss = source.ProjectedGoldLoss
            };
        }

        private static BattleResultWritebackSummary.LootAftermathSummary CloneLootAftermathSummary(
            BattleResultWritebackSummary.LootAftermathSummary source)
        {
            if (source == null)
                return null;

            var clone = new BattleResultWritebackSummary.LootAftermathSummary
            {
                Ready = source.Ready,
                Source = source.Source,
                LootMemberCount = source.LootMemberCount,
                LootPrisonerCount = source.LootPrisonerCount,
                LootItemStacks = source.LootItemStacks,
                LootItemUnits = source.LootItemUnits,
                LootMemberSample = source.LootMemberSample,
                LootPrisonerSample = source.LootPrisonerSample,
                LootItemSample = source.LootItemSample
            };

            foreach (BattleResultWritebackSummary.LootItemEntrySummary entry in source.ItemEntries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId) || entry.Amount <= 0)
                    continue;

                clone.ItemEntries.Add(new BattleResultWritebackSummary.LootItemEntrySummary
                {
                    ItemId = entry.ItemId,
                    Amount = entry.Amount
                });
            }

            return clone;
        }

        private void TryHandleBattleResultMissionExit()
        {
            Mission mission = Mission.Current;
            if (mission == null || GameNetwork.IsClient || TaleWorlds.CampaignSystem.Campaign.Current == null)
                return;

            if (DateTime.UtcNow < _nextMissionBattleResultPollUtc)
                return;

            _nextMissionBattleResultPollUtc = DateTime.UtcNow.AddMilliseconds(250);

            CoopBattleResultBridgeFile.BattleResultSnapshot result = CoopBattleResultBridgeFile.ReadResult(logRead: false);
            if (result == null)
                return;

            string resultKey = BuildBattleResultKey(result);
            if (string.IsNullOrEmpty(resultKey))
                return;

            if (result.UpdatedUtc <= _missionEnteredUtc)
                return;

            if (string.Equals(_lastMissionExitRequestedBattleResultKey, resultKey, StringComparison.Ordinal))
                return;

            TryCacheHostAftermathRewardProjection(result, resultKey);
            TryCacheHostAftermathLootSummary(result, resultKey);

            bool encounterPrepared = TryPrepareAuthoritativeEncounterResultBridge(result);
            bool exitRequested = TryRequestLocalMissionExit(mission, "campaign battle_result bridge");
            if (exitRequested)
            {
                bool dedicatedEndMissionRequested = false;
                if (ShouldNotifyDedicatedHelper())
                {
                    try
                    {
                        dedicatedEndMissionRequested = DedicatedServerCommands.SendEndMission();
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info("DedicatedServerCommands.SendEndMission: " + ex.Message);
                    }
                }

                _lastMissionExitRequestedBattleResultKey = resultKey;
                _lastMissionExitFailedBattleResultKey = null;
                ModLogger.Info(
                    "BattleDetector: battle_result detected during active campaign mission. " +
                    "Requested local mission exit. " +
                    "DedicatedEndMissionRequested=" + dedicatedEndMissionRequested + " " +
                    "EncounterPrepared=" + encounterPrepared + " " +
                    "BattleId=" + (result.BattleId ?? "null") +
                    " WinnerSide=" + (result.WinnerSide ?? "none") +
                    " MissionScene=" + SafeMissionSceneName(mission) + ".");
                return;
            }

            if (string.Equals(_lastMissionExitFailedBattleResultKey, resultKey, StringComparison.Ordinal))
                return;

            _lastMissionExitFailedBattleResultKey = resultKey;
            ModLogger.Info(
                "BattleDetector: battle_result detected during active campaign mission, but local mission exit request failed. " +
                "BattleId=" + (result.BattleId ?? "null") +
                " WinnerSide=" + (result.WinnerSide ?? "none") +
                " MissionScene=" + SafeMissionSceneName(mission) + ".");
        }

        private void TryCacheHostAftermathRewardProjection(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            string resultKey)
        {
            if (result == null || string.IsNullOrWhiteSpace(resultKey))
                return;

            try
            {
                Dictionary<string, EncounterPartyWritebackState> encounterParties = ResolveEncounterPartyWritebackStates();
                var summary = new BattleResultWritebackSummary();
                TryBuildMainPartyRewardProjection(result, encounterParties, summary);
                if (!summary.RewardProjection.Ready)
                    return;

                _cachedHostAftermathRewardResultKey = resultKey;
                _cachedHostAftermathRewardProjection = CloneRewardProjectionSummary(summary.RewardProjection);

                ModLogger.Info(
                    "BattleDetector: cached host aftermath reward projection before mission exit. " +
                    "BattleId=" + (result.BattleId ?? "null") +
                    " WinnerSide=" + (result.WinnerSide ?? "none") +
                    " Source=" + (summary.RewardProjection.Source ?? "none") +
                    " GoldDelta=" + summary.RewardProjection.ProjectedGoldDelta +
                    " Renown=" + summary.RewardProjection.ProjectedRenown.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " Influence=" + summary.RewardProjection.ProjectedInfluence.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " Morale=" + summary.RewardProjection.ProjectedMoraleChange.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: failed to cache host aftermath reward projection: " + ex.Message);
            }
        }

        private void TryCacheHostAftermathLootSummary(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            string resultKey)
        {
            if (result == null || string.IsNullOrWhiteSpace(resultKey))
                return;

            try
            {
                var summary = new BattleResultWritebackSummary();
                TryBuildMainPartyLootAftermathAudit(summary);
                if (!summary.LootAftermath.Ready)
                    return;

                _cachedHostAftermathLootResultKey = resultKey;
                _cachedHostAftermathLootSummary = CloneLootAftermathSummary(summary.LootAftermath);

                ModLogger.Info(
                    "BattleDetector: cached host aftermath loot summary before mission exit. " +
                    "BattleId=" + (result.BattleId ?? "null") +
                    " WinnerSide=" + (result.WinnerSide ?? "none") +
                    " Source=" + (summary.LootAftermath.Source ?? "none") +
                    " Members=" + summary.LootAftermath.LootMemberCount +
                    " Prisoners=" + summary.LootAftermath.LootPrisonerCount +
                    " ItemStacks=" + summary.LootAftermath.LootItemStacks +
                    " ItemUnits=" + summary.LootAftermath.LootItemUnits + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: failed to cache host aftermath loot summary: " + ex.Message);
            }
        }

        private static bool TryPrepareAuthoritativeEncounterResultBridge(CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            if (result == null)
                return false;

            try
            {
                PlayerEncounter encounter = PlayerEncounter.Current;
                if (encounter == null || PlayerEncounter.Battle == null)
                {
                    ModLogger.Info(
                        "BattleDetector: authoritative encounter result bridge skipped because PlayerEncounter/Battle is null. " +
                        "BattleId=" + (result.BattleId ?? "null") +
                        " WinnerSide=" + (result.WinnerSide ?? "none") + ".");
                    return false;
                }

                BattleState winnerBattleState = TryResolveWinnerBattleState(result.WinnerSide);
                if (winnerBattleState == BattleState.None)
                {
                    ModLogger.Info(
                        "BattleDetector: authoritative encounter result bridge skipped because WinnerSide could not be resolved. " +
                        "BattleId=" + (result.BattleId ?? "null") +
                        " WinnerSide=" + (result.WinnerSide ?? "none") + ".");
                    return false;
                }

                CampaignBattleResult campaignBattleResult = CampaignBattleResult.GetResult(winnerBattleState, enemyRetreated: true);
                if (campaignBattleResult == null)
                {
                    ModLogger.Info(
                        "BattleDetector: authoritative encounter result bridge skipped because CampaignBattleResult.GetResult returned null. " +
                        "BattleId=" + (result.BattleId ?? "null") +
                        " WinnerSide=" + (result.WinnerSide ?? "none") + ".");
                    return false;
                }

                PlayerEncounter.CampaignBattleResult = campaignBattleResult;
                bool continueReset = TrySetMemberValue(encounter, "_doesBattleContinue", false);

                ModLogger.Info(
                    "BattleDetector: prepared authoritative encounter result bridge. " +
                    "BattleId=" + (result.BattleId ?? "null") +
                    " WinnerSide=" + (result.WinnerSide ?? "none") +
                    " PlayerSide=" + PlayerEncounter.Current.PlayerSide +
                    " CampaignResult[Victory=" + campaignBattleResult.PlayerVictory +
                    " Defeat=" + campaignBattleResult.PlayerDefeat +
                    " EnemyPulledBack=" + campaignBattleResult.EnemyPulledBack +
                    " EnemyRetreated=" + campaignBattleResult.EnemyRetreated + "] " +
                    "ContinueReset=" + continueReset + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: failed to prepare authoritative encounter result bridge: " + ex.Message);
                return false;
            }
        }

        private static BattleState TryResolveWinnerBattleState(string winnerSide)
        {
            if (string.IsNullOrWhiteSpace(winnerSide))
                return BattleState.None;

            if (string.Equals(winnerSide, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase))
                return BattleState.AttackerVictory;

            if (string.Equals(winnerSide, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase))
                return BattleState.DefenderVictory;

            return BattleState.None;
        }

        private static BattleSideEnum TryResolveWinnerSideEnum(string winnerSide)
        {
            if (string.IsNullOrWhiteSpace(winnerSide))
                return BattleSideEnum.None;

            if (string.Equals(winnerSide, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;

            if (string.Equals(winnerSide, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static bool TryRequestLocalMissionExit(Mission mission, string source)
        {
            if (mission == null)
                return false;

            foreach (string methodName in new[] { "EndMission", "OnEndMissionRequest", "OnEndMissionInternal" })
            {
                try
                {
                    MethodInfo method = mission.GetType().GetMethod(
                        methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        Type.EmptyTypes,
                        null);
                    if (method == null)
                        continue;

                    method.Invoke(mission, null);
                    ModLogger.Info(
                        "BattleDetector: requested local mission exit via reflection. " +
                        "Method=" + methodName +
                        " Source=" + (source ?? "unknown") + ".");
                    return true;
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "BattleDetector: local mission exit method failed. " +
                        "Method=" + methodName +
                        " Source=" + (source ?? "unknown") +
                        " Error=" + ex.Message);
                }
            }

            return false;
        }

        private static string BuildBattleResultKey(CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            if (result == null)
                return null;

            return (result.BattleId ?? "null") + "|" + result.UpdatedUtc.ToString("O");
        }

        private static string SafeMissionSceneName(Mission mission)
        {
            if (mission == null)
                return string.Empty;

            try
            {
                return mission.SceneName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void ResetMissionExitState()
        {
            _missionEnteredUtc = DateTime.MinValue;
            _lastMissionExitRequestedBattleResultKey = null;
            _lastMissionExitFailedBattleResultKey = null;
            _cachedHostAftermathRewardResultKey = null;
            _cachedHostAftermathRewardProjection = null;
            _cachedHostAftermathLootResultKey = null;
            _cachedHostAftermathLootSummary = null;
            _nextMissionBattleResultPollUtc = DateTime.MinValue;
        }

        private bool TrySendBattleStart() // Формуємо повідомлення та надсилаємо клієнтам (best-effort)
        { // Починаємо блок методу
            if (_hasSentBattleStartForThisMission) // Перевіряємо guard-прапорець
            { // Починаємо блок if
                return true; // Виходимо, бо вже відправили для цієї місії
            } // Завершуємо блок if

            try // Захищаємо збір даних від винятків, щоб мод не крашив гру
            { // Починаємо блок try
                BattleStartMessage payload = BuildBattleStartPayload(); // Будуємо DTO з даними про битву/місію
                if (!IsBattleStartPayloadReady(payload, "network-host", out _))
                    return false;

                CoopBattleResultBridgeFile.ClearResult("BattleDetector.TrySendBattleStart");
                TryWriteBattleRosterFile(payload); // Варіант A: зберігаємо roster у спільний файл для dedicated на цьому ж ПК.
                string wireMessage = BattleStartMessageCodec.BuildBattleStartMessage(payload); // Серіалізуємо DTO в `BATTLE_START:{json}`
                CoopRuntime.Network.BroadcastToClients(wireMessage); // Відправляємо повідомлення всім підключеним клієнтам
                _hasSentBattleStartForThisMission = true;
                UiFeedback.ShowMessageDeferred("Host: BATTLE_START sent to clients."); // Даємо короткий UI-індикатор для дебагу/перевірки
                ModLogger.Info("BATTLE_START broadcasted."); // Логуємо факт відправки в лог гри
                try { DedicatedServerCommands.SendStartMission(); } catch (Exception ex) { ModLogger.Info("DedicatedServerCommands.SendStartMission: " + ex.Message); } // Етап 3b: Dedicated Helper переводить у mission mode (поки stub)
                return true;
            } // Завершуємо блок try
            catch (Exception ex) // Ловимо будь-які винятки (API changes/null, тощо)
            { // Починаємо блок catch
                ModLogger.Error("Failed to send BATTLE_START.", ex); // Логуємо помилку, щоб її можна було діагностувати
                return false;
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private bool TryStartDedicatedMissionForCampaignHost()
        {
            try
            {
                BattleStartMessage payload = BuildBattleStartPayload();
                if (!IsBattleStartPayloadReady(payload, "campaign-host", out _))
                    return false;

                ModLogger.Info("BattleDetector: not TCP host — sending start_mission to dedicated (campaign host path).");
                CoopBattleResultBridgeFile.ClearResult("BattleDetector.Tick campaign-host start_mission");
                TryWriteBattleRosterFile(payload); // Для host-only campaign path теж пишемо battle_roster.json перед start_mission.
                bool sent = DedicatedServerCommands.SendStartMission();
                _hasSentBattleStartForThisMission = true;
                ModLogger.Info("BattleDetector: SendStartMission() returned " + sent + ".");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("DedicatedServerCommands.SendStartMission: " + ex.Message);
                return false;
            }
        }

        private static void TryWriteBattleRosterFile(BattleStartMessage payload)
        {
            if (payload == null || payload.Troops == null || payload.Troops.Count == 0)
            {
                ModLogger.Info("BattleDetector: skipping battle_roster.json write (payload troops empty).");
                return;
            }

            try
            {
                var troopIds = new List<string>();
                foreach (TroopStackInfo troop in payload.Troops)
                {
                    if (troop == null || string.IsNullOrWhiteSpace(troop.CharacterId))
                        continue;
                    troopIds.Add(troop.CharacterId);
                }

                bool wrote = BattleRosterFileHelper.WriteRoster(troopIds, payload.Snapshot);
                ModLogger.Info("BattleDetector: battle_roster.json write result = " + wrote + " (troop ids collected = " + troopIds.Count + ", snapshot sides = " + (payload.Snapshot?.Sides?.Count ?? 0) + ").");
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleDetector: failed to write battle_roster.json.", ex);
            }
        }

        private bool IsBattleStartPayloadReady(BattleStartMessage payload, string source, out string readinessSummary)
        {
            BattleSnapshotMessage snapshot = payload?.Snapshot;
            List<BattleSideSnapshotMessage> sides = snapshot?.Sides?
                .Where(side => side != null)
                .ToList() ?? new List<BattleSideSnapshotMessage>();
            int populatedSideCount = 0;
            bool hasAttacker = false;
            bool hasDefender = false;

            foreach (BattleSideSnapshotMessage side in sides)
            {
                int troopCount = side?.Troops?.Count ?? 0;
                if (troopCount <= 0)
                    continue;

                populatedSideCount++;
                if (string.Equals(side.SideId, "attacker", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(side.SideText, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase))
                {
                    hasAttacker = true;
                }
                else if (string.Equals(side.SideId, "defender", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(side.SideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase))
                {
                    hasDefender = true;
                }
            }

            readinessSummary =
                "BattleId=" + (snapshot?.BattleId ?? "null") +
                " SnapshotSides=" + sides.Count +
                " PopulatedSides=" + populatedSideCount +
                " HasAttacker=" + hasAttacker +
                " HasDefender=" + hasDefender +
                " Troops=" + (payload?.Troops?.Count ?? 0);

            bool ready = sides.Count >= 2 && populatedSideCount >= 2 && hasAttacker && hasDefender;
            if (!ready && DateTime.UtcNow >= _nextBattleStartWaitLogUtc)
            {
                _nextBattleStartWaitLogUtc = DateTime.UtcNow.AddSeconds(2);
                ModLogger.Info(
                    "BattleDetector: delaying battle start until authoritative snapshot is ready. " +
                    "Source=" + (source ?? "unknown") + " " +
                    readinessSummary + ".");
            }

            return ready;
        }

        private static BattleStartMessage BuildBattleStartPayload() // Будуємо мінімальні дані про старт битви/місії
        { // Починаємо блок методу
            // Mission entry/load can recreate the underlying MBObject instances for skills/attributes.
            // Re-resolve them for each payload build so synthetic snapshots do not keep stale references
            // and collapse all combat-profile values to zero on the second build.
            ResetCombatProfileLookupCaches();

            BattleStartMessage message = new BattleStartMessage(); // Створюємо DTO

            // 1) Map scene name (best-effort) // Пояснюємо блок
            message.MapScene = TryGetMapSceneNameSafe(); // Пишемо назву сцени мапи (або "unknown")

            // 2) Map position (campaign world) // Пояснюємо блок
            float x = 0f; // Дефолтне значення X
            float y = 0f; // Дефолтне значення Y

            try // Пробуємо взяти позицію партії хоста
            { // Починаємо блок try
                var pos = MobileParty.MainParty.GetPosition2D; // Беремо позицію партії гравця на мапі (Vec2)
                x = pos.x; // Записуємо X
                y = pos.y; // Записуємо Y
            } // Завершуємо блок try
            catch (Exception) // Якщо ми не в кампанії або API недоступний — лишаємо 0/0
            { // Починаємо блок catch
            } // Завершуємо блок catch

            message.MapX = x; // Записуємо X у DTO
            message.MapY = y; // Записуємо Y у DTO

            // 3) Player side (best-effort) // Пояснюємо блок
            message.PlayerSide = TryGetPlayerSideTextSafe(); // Пишемо "Attacker/Defender/Unknown" як текст

            // 4) Extended battle snapshot (best-effort) // Пояснюємо блок
            message.Snapshot =
                _syntheticRosterMode == SyntheticRosterMode.AllCampaignTroops
                    ? BuildSyntheticAllCampaignTroopsSnapshot(message.MapScene, message.PlayerSide)
                    : _syntheticRosterMode == SyntheticRosterMode.LiveHeroes
                        ? BuildSyntheticLiveHeroesSnapshot(message.MapScene, message.PlayerSide)
                        : BuildBattleSnapshotSafe(message.MapScene, message.PlayerSide);
            BattleSnapshotRuntimeState.SetCurrent(message.Snapshot, "host-battle-detector");

            // 5) Legacy fields for transitional clients/runtime // Пояснюємо блок
            message.Troops = BuildLegacyTroopsFromSnapshot(message.Snapshot) ?? BuildPartyTroopStacksSafe();
            message.ArmySize = BuildLegacyArmySizeFromSnapshot(message.Snapshot);
            if (message.ArmySize <= 0)
                message.ArmySize = TryGetArmySizeSafe(); // Записуємо сумарну кількість людей (для UI і тестів)

            return message; // Повертаємо сформований DTO
        } // Завершуємо блок методу

        private static BattleSnapshotMessage BuildSyntheticAllCampaignTroopsSnapshot(string mapScene, string playerSideText)
        {
            List<BasicCharacterObject> characters = CollectSyntheticAllCampaignTroops();
            if (characters.Count == 0)
            {
                ModLogger.Info("BattleDetector: synthetic all-campaign-troops snapshot had no eligible characters, falling back to live snapshot.");
                return BuildBattleSnapshotSafe(mapScene, playerSideText);
            }

            var snapshot = new BattleSnapshotMessage
            {
                BattleId = SyntheticAllCampaignTroopsBattleId,
                BattleType = "SyntheticAllCampaignTroops",
                MapScene = mapScene,
                PlayerSide = playerSideText
            };

            var attackerSide = new BattleSideSnapshotMessage
            {
                SideId = "attacker",
                SideText = nameof(BattleSideEnum.Attacker),
                IsPlayerSide = string.Equals(playerSideText, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase)
            };
            var defenderSide = new BattleSideSnapshotMessage
            {
                SideId = "defender",
                SideText = nameof(BattleSideEnum.Defender),
                IsPlayerSide = string.Equals(playerSideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase)
            };

            Dictionary<string, List<BasicCharacterObject>> groupsByCulture = characters
                .GroupBy(character => TryGetCultureId(character) ?? "neutral_culture", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(character => character.StringId, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

            var orderedGroups = groupsByCulture
                .OrderByDescending(group => group.Value.Count)
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int attackerAssigned = 0;
            int defenderAssigned = 0;
            var rosterCharacters = characters.Cast<object>().ToList();
            foreach (KeyValuePair<string, List<BasicCharacterObject>> group in orderedGroups)
            {
                BattleSideSnapshotMessage targetSide = attackerAssigned <= defenderAssigned ? attackerSide : defenderSide;
                string canonicalCultureId = group.Key;
                string partyId = "synthetic_" + targetSide.SideId + "_" + canonicalCultureId;
                string partyName = "Synthetic " + canonicalCultureId + " roster";

                var partySnapshot = new BattlePartySnapshotMessage
                {
                    PartyId = partyId,
                    PartyName = partyName,
                    IsMainParty = false
                };

                foreach (BasicCharacterObject character in group.Value)
                {
                    if (character == null || string.IsNullOrWhiteSpace(character.StringId))
                        continue;

                    string originalCharacterId = character.StringId;
                    string spawnTemplateId = GetMissionSafeCharacterId(character, rosterCharacters);
                    var troop = new TroopStackInfo
                    {
                        EntryId = targetSide.SideId + "|" + partyId + "|" + originalCharacterId,
                        SideId = targetSide.SideId,
                        PartyId = partyId,
                        CharacterId = spawnTemplateId,
                        OriginalCharacterId = originalCharacterId,
                        SpawnTemplateId = spawnTemplateId,
                        TroopName = character.Name?.ToString() ?? originalCharacterId,
                        CultureId = TryGetCultureId(character),
                        Tier = TryGetIntProperty(character, "Tier"),
                        IsMounted = TryGetBoolProperty(character, "IsMounted"),
                        IsRanged = TryGetCharacterIsRanged(character),
                        HasShield = TryGetCharacterHasShield(character),
                        HasThrown = TryGetCharacterHasThrown(character),
                        IsHero = false,
                        Count = 1,
                        WoundedCount = 0
                    };
                    ApplyCombatEquipmentSnapshot(troop, character);
                    ApplyCombatProfileSnapshot(troop, character);
                    partySnapshot.Troops.Add(troop);
                    targetSide.Troops.Add(troop);
                }

                if (partySnapshot.Troops.Count <= 0)
                    continue;

                partySnapshot.TotalManCount = partySnapshot.Troops.Count;
                targetSide.Parties.Add(partySnapshot);
                if (ReferenceEquals(targetSide, attackerSide))
                    attackerAssigned += partySnapshot.TotalManCount;
                else
                    defenderAssigned += partySnapshot.TotalManCount;
            }

            attackerSide.TotalManCount = attackerSide.Troops.Count;
            defenderSide.TotalManCount = defenderSide.Troops.Count;

            if (attackerSide.Parties.Count > 0)
                snapshot.Sides.Add(attackerSide);
            if (defenderSide.Parties.Count > 0)
                snapshot.Sides.Add(defenderSide);

            ModLogger.Info(
                "BattleDetector: using synthetic all-campaign-troops snapshot. " +
                "Characters=" + characters.Count +
                " AttackerEntries=" + attackerSide.Troops.Count +
                " DefenderEntries=" + defenderSide.Troops.Count + ".");
            LogSnapshotMappings("synthetic-all-campaign-troops", snapshot);
            return snapshot;
        }

        private static BattleSnapshotMessage BuildSyntheticLiveHeroesSnapshot(string mapScene, string playerSideText)
        {
            List<Hero> attackerHeroes = CollectSyntheticPlayerHeroes();
            List<Hero> defenderLords = CollectSyntheticLordHeroes();
            if (attackerHeroes.Count == 0 || defenderLords.Count == 0)
            {
                ModLogger.Info("BattleDetector: synthetic live-heroes snapshot had no eligible heroes, falling back to live snapshot.");
                return BuildBattleSnapshotSafe(mapScene, playerSideText);
            }

            var snapshot = new BattleSnapshotMessage
            {
                BattleId = SyntheticLiveHeroesBattleId,
                BattleType = "SyntheticLiveHeroes",
                MapScene = mapScene,
                PlayerSide = playerSideText
            };

            var attackerSide = new BattleSideSnapshotMessage
            {
                SideId = "attacker",
                SideText = nameof(BattleSideEnum.Attacker),
                IsPlayerSide = string.Equals(playerSideText, nameof(BattleSideEnum.Attacker), StringComparison.OrdinalIgnoreCase)
            };
            var defenderSide = new BattleSideSnapshotMessage
            {
                SideId = "defender",
                SideText = nameof(BattleSideEnum.Defender),
                IsPlayerSide = string.Equals(playerSideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase)
            };

            List<object> rosterCharacters = attackerHeroes.Select(hero => (object)hero.CharacterObject)
                .Concat(defenderLords.Select(hero => (object)hero.CharacterObject))
                .Where(character => character != null)
                .ToList();

            AddSyntheticHeroParty(attackerSide, "synthetic_attacker_player_heroes", "Synthetic player heroes", attackerHeroes, rosterCharacters);
            AddSyntheticHeroParty(defenderSide, "synthetic_defender_lords", "Synthetic lords", defenderLords, rosterCharacters);

            attackerSide.TotalManCount = attackerSide.Troops.Count;
            defenderSide.TotalManCount = defenderSide.Troops.Count;

            if (attackerSide.Parties.Count > 0)
                snapshot.Sides.Add(attackerSide);
            if (defenderSide.Parties.Count > 0)
                snapshot.Sides.Add(defenderSide);

            ModLogger.Info(
                "BattleDetector: using synthetic live-heroes snapshot. " +
                "AttackerEntries=" + attackerSide.Troops.Count +
                " DefenderEntries=" + defenderSide.Troops.Count + ".");
            LogSnapshotMappings("synthetic-live-heroes", snapshot);
            return snapshot;
        }

        private static void AddSyntheticHeroParty(
            BattleSideSnapshotMessage targetSide,
            string partyId,
            string partyName,
            List<Hero> heroes,
            List<object> rosterCharacters)
        {
            if (targetSide == null || heroes == null || heroes.Count == 0)
                return;

            var partySnapshot = new BattlePartySnapshotMessage
            {
                PartyId = partyId,
                PartyName = partyName,
                IsMainParty = false
            };

            foreach (Hero hero in heroes)
            {
                TaleWorlds.CampaignSystem.CharacterObject character = hero?.CharacterObject;
                if (character == null || string.IsNullOrWhiteSpace(character.StringId))
                    continue;

                string originalCharacterId = character.StringId;
                string spawnTemplateId = GetMissionSafeCharacterId(character, rosterCharacters);
                var troop = new TroopStackInfo
                {
                    EntryId = targetSide.SideId + "|" + partyId + "|" + originalCharacterId,
                    SideId = targetSide.SideId,
                    PartyId = partyId,
                    CharacterId = spawnTemplateId,
                    OriginalCharacterId = originalCharacterId,
                    SpawnTemplateId = spawnTemplateId,
                    TroopName = character.Name?.ToString() ?? originalCharacterId,
                    CultureId = TryGetCultureId(character),
                    Tier = TryGetIntProperty(character, "Tier"),
                    IsMounted = TryGetBoolProperty(character, "IsMounted"),
                    IsRanged = TryGetCharacterIsRanged(character),
                    HasShield = TryGetCharacterHasShield(character),
                    HasThrown = TryGetCharacterHasThrown(character),
                    IsHero = true,
                    Count = 1,
                    WoundedCount = 0
                };
                ApplyCombatEquipmentSnapshot(troop, character);
                ApplyCombatProfileSnapshot(troop, character);
                ApplyHeroIdentitySnapshot(troop, character);
                partySnapshot.Troops.Add(troop);
                targetSide.Troops.Add(troop);
            }

            if (partySnapshot.Troops.Count <= 0)
                return;

            partySnapshot.TotalManCount = partySnapshot.Troops.Count;
            targetSide.Parties.Add(partySnapshot);
        }

        private static string BuildSyntheticLiveHeroesPreviewSummary()
        {
            List<Hero> playerHeroes = CollectSyntheticPlayerHeroes();
            List<Hero> lords = CollectSyntheticLordHeroes();
            int companionCount = Math.Max(0, playerHeroes.Count - (Hero.MainHero != null ? 1 : 0));
            return "main_hero=" + (Hero.MainHero != null ? 1 : 0) +
                   " companions=" + companionCount +
                   " lords=" + lords.Count;
        }

        private static List<Hero> CollectSyntheticPlayerHeroes()
        {
            var results = new List<Hero>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(Hero hero)
            {
                if (hero == null || !hero.IsAlive || hero.CharacterObject == null || string.IsNullOrWhiteSpace(hero.StringId))
                    return;
                if (!seenIds.Add(hero.StringId))
                    return;
                results.Add(hero);
            }

            TryAdd(Hero.MainHero);

            Clan playerClan = Hero.MainHero?.Clan ?? Clan.PlayerClan;
            IEnumerable<Hero> companions = playerClan?.Companions ?? Enumerable.Empty<Hero>();
            foreach (Hero companion in companions
                         .Where(hero => hero != null && hero.IsAlive)
                         .OrderBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                         .Take(SyntheticHeroCompanionLimit))
            {
                TryAdd(companion);
            }

            return results;
        }

        private static List<Hero> CollectSyntheticLordHeroes()
        {
            IEnumerable<Hero> source = Hero.AllAliveHeroes ?? Enumerable.Empty<Hero>();
            string mainHeroId = Hero.MainHero?.StringId;
            string playerClanId = Hero.MainHero?.Clan?.StringId;

            return source
                .Where(hero =>
                    hero != null &&
                    hero.IsAlive &&
                    hero.CharacterObject != null &&
                    hero.IsLord &&
                    !string.Equals(hero.StringId, mainHeroId, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(hero.Clan?.StringId, playerClanId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(hero => hero.Clan?.StringId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .Take(SyntheticHeroLordLimit)
                .ToList();
        }

        private static List<BasicCharacterObject> CollectSyntheticAllCampaignTroops()
        {
            var results = new List<BasicCharacterObject>();
            try
            {
                MethodInfo getObjectTypeList = typeof(MBObjectManager).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "GetObjectTypeList", StringComparison.Ordinal) &&
                        method.IsGenericMethodDefinition &&
                        method.GetParameters().Length == 0);
                if (getObjectTypeList == null || MBObjectManager.Instance == null)
                    return results;

                object objectList = getObjectTypeList.MakeGenericMethod(typeof(BasicCharacterObject)).Invoke(MBObjectManager.Instance, null);
                if (!(objectList is System.Collections.IEnumerable enumerable))
                    return results;

                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object item in enumerable)
                {
                    if (!(item is BasicCharacterObject character))
                        continue;

                    if (!IsSyntheticAllCampaignTroopCandidate(character))
                        continue;

                    if (!seenIds.Add(character.StringId))
                        continue;

                    results.Add(character);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: failed to collect synthetic campaign troop roster: " + ex.Message);
            }

            return results
                .OrderBy(character => TryGetCultureId(character) ?? "neutral_culture", StringComparer.OrdinalIgnoreCase)
                .ThenBy(character => TryGetIntProperty(character, "Tier"))
                .ThenBy(character => character.StringId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSyntheticAllCampaignTroopCandidate(BasicCharacterObject character)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.StringId))
                return false;

            if (character.IsHero)
                return false;

            string id = character.StringId;
            if (id.StartsWith("mp_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("multiplayer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("dummy_", StringComparison.OrdinalIgnoreCase) ||
                id.IndexOf("template", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (TryResolvePrimaryCombatEquipment(character) == null)
                return false;

            int tier = TryGetIntProperty(character, "Tier");
            bool isMounted = TryGetBoolProperty(character, "IsMounted");
            bool isRanged = TryGetCharacterIsRanged(character);
            bool hasShield = TryGetCharacterHasShield(character);
            bool hasThrown = TryGetCharacterHasThrown(character);

            return tier > 0 || isMounted || isRanged || hasShield || hasThrown;
        }

        private static string BuildSyntheticAllCampaignTroopsPreviewSummary()
        {
            List<BasicCharacterObject> characters = CollectSyntheticAllCampaignTroops();
            if (characters.Count == 0)
                return "eligible=0";

            Dictionary<string, int> byCulture = characters
                .GroupBy(character => TryGetCultureId(character) ?? "neutral_culture", StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            return "eligible=" + characters.Count + " cultures=[" +
                   string.Join(", ", byCulture.Select(pair => pair.Key + "=" + pair.Value)) + "]";
        }

        private static string TryGetMapSceneNameSafe() // Best-effort: намагаємось отримати назву сцени карти кампанії
        { // Починаємо блок методу
            try // Обгортаємо в try/catch, бо MapSceneWrapper може бути недоступний у деяких контекстах
            { // Починаємо блок try
                if (TaleWorlds.CampaignSystem.Campaign.Current == null) // Якщо кампанія не створена (наприклад, ми в меню) — повертаємо "unknown"
                { // Починаємо блок if
                    return "unknown"; // Повертаємо fallback
                } // Завершуємо блок if

                object mapScene = TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper; // Беремо map-scene wrapper як object (щоб не залежати від конкретного інтерфейсу)

                if (mapScene == null) // Якщо wrapper відсутній — повертаємо fallback
                { // Починаємо блок if
                    return "unknown"; // Fallback
                } // Завершуємо блок if

                Type mapSceneType = mapScene.GetType(); // Отримуємо runtime-тип wrapper'а

                // 1) Пробуємо метод GetMapSceneName() (як у старих прикладах) // Пояснюємо першу спробу
                var getNameMethod = mapSceneType.GetMethod("GetMapSceneName", Type.EmptyTypes); // Шукаємо метод без параметрів

                if (getNameMethod != null) // Якщо метод знайдено — пробуємо викликати
                { // Починаємо блок if
                    object value = getNameMethod.Invoke(mapScene, null); // Викликаємо метод reflection'ом
                    string text = value != null ? value.ToString() : null; // Перетворюємо результат в string

                    if (!string.IsNullOrEmpty(text)) // Якщо рядок валідний — повертаємо його
                    { // Починаємо блок if
                        return text; // Повертаємо назву сцени
                    } // Завершуємо блок if
                } // Завершуємо блок if

                // 2) Пробуємо властивості з типових назв: SceneName / Name / MapSceneName // Пояснюємо другу спробу
                var nameProperty = // Оголошуємо змінну з першим знайденим property
                    mapSceneType.GetProperty("SceneName") ?? // Варіант 1
                    mapSceneType.GetProperty("Name") ?? // Варіант 2
                    mapSceneType.GetProperty("MapSceneName"); // Варіант 3

                if (nameProperty != null) // Якщо property знайдено — читаємо значення
                { // Починаємо блок if
                    object value = nameProperty.GetValue(mapScene, null); // Читаємо значення property
                    string text = value != null ? value.ToString() : null; // Перетворюємо в string

                    if (!string.IsNullOrEmpty(text)) // Якщо рядок валідний — повертаємо його
                    { // Починаємо блок if
                        return text; // Повертаємо назву сцени
                    } // Завершуємо блок if
                } // Завершуємо блок if

                return mapScene.ToString(); // Останній fallback — ToString() типу wrapper'а
            } // Завершуємо блок try
            catch (Exception) // Якщо API/контекст не підходить — повертаємо "unknown"
            { // Починаємо блок catch
                return "unknown"; // Повертаємо fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static int TryGetArmySizeSafe() // Best-effort: намагаємось отримати загальний розмір партії
        { // Починаємо блок методу
            try // Захищаємо доступ до MobileParty на випадок, якщо ми не в кампанії
            { // Починаємо блок try
                return MobileParty.MainParty.MemberRoster.TotalManCount; // Повертаємо загальну кількість людей у ростері
            } // Завершуємо блок try
            catch (Exception) // Якщо щось не доступно — повертаємо 0
            { // Починаємо блок catch
                return 0; // Fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static List<TroopStackInfo> BuildPartyTroopStacksSafe() // Будуємо список стеків військ з партії (best-effort)
        { // Починаємо блок методу
            List<TroopStackInfo> troops = new List<TroopStackInfo>(); // Створюємо список результатів

            try // Захищаємо доступ до ростеру, щоб не крашнути гру
            { // Починаємо блок try
                var rosterElements = new List<TaleWorlds.CampaignSystem.Roster.TroopRosterElement>();
                var rosterCharacters = new List<object>();
                string partyId = MobileParty.MainParty?.StringId ?? "main_party";
                foreach (var rosterElement in MobileParty.MainParty.MemberRoster.GetTroopRoster())
                {
                    rosterElements.Add(rosterElement);
                    if (rosterElement.Character != null)
                        rosterCharacters.Add(rosterElement.Character);
                }

                foreach (var element in rosterElements) // Проходимо по елементах ростеру партії
                { // Починаємо блок foreach
                    if (element.Character == null) // Перевіряємо що є Character (тип юнита)
                    { // Починаємо блок if
                        continue; // Пропускаємо некоректні елементи
                    } // Завершуємо блок if

                    int expandedWounded = 0;
                    try
                    {
                        expandedWounded = element.WoundedNumber;
                    }
                    catch (Exception)
                    {
                    }

                    troops.AddRange(BuildTroopStacksForCharacterVariants(
                        element.Character,
                        null,
                        partyId,
                        element.Number,
                        expandedWounded,
                        rosterCharacters,
                        assignExplicitEntryIds: false));
                } // Завершуємо блок foreach
            } // Завершуємо блок try
            catch (Exception ex) // Якщо ростер недоступний — лог і повертаємо те, що є
            { // Починаємо блок catch
                ModLogger.Error("Failed to build troop roster for BATTLE_START.", ex); // Логуємо помилку
            } // Завершуємо блок catch

            LogTroopStackMappings("main-party-roster", troops);
            return troops; // Повертаємо список (може бути порожнім — це ок для MVP)
        } // Завершуємо блок методу

        private static BattleSnapshotMessage BuildBattleSnapshotSafe(string mapScene, string playerSideText)
        {
            try
            {
                object battle = TryGetCurrentBattleObject();
                if (battle == null)
                    return BuildFallbackBattleSnapshot(mapScene, playerSideText);

                ModLogger.Info("BattleDetector: building live battle snapshot from battle type = " + battle.GetType().FullName + ".");

                var snapshot = new BattleSnapshotMessage
                {
                    BattleId = battle.GetType().FullName,
                    BattleType = battle.GetType().Name,
                    MapScene = mapScene,
                    PlayerSide = playerSideText
                };

                BattleSideSnapshotMessage attackerSide = BuildBattleSideSnapshot(
                    TryGetPropertyValue(battle, "AttackerSide"),
                    "attacker",
                    nameof(BattleSideEnum.Attacker),
                    playerSideText);
                BattleSideSnapshotMessage defenderSide = BuildBattleSideSnapshot(
                    TryGetPropertyValue(battle, "DefenderSide"),
                    "defender",
                    nameof(BattleSideEnum.Defender),
                    playerSideText);

                ModLogger.Info(
                    "BattleDetector: live battle side build result. " +
                    "AttackerBuilt=" + (attackerSide != null) +
                    " DefenderBuilt=" + (defenderSide != null) + ".");

                if (attackerSide != null)
                    snapshot.Sides.Add(attackerSide);
                if (defenderSide != null)
                    snapshot.Sides.Add(defenderSide);

                if (snapshot.Sides.Count == 0)
                    return BuildFallbackBattleSnapshot(mapScene, playerSideText);

                LogSnapshotMappings("live-battle", snapshot);
                return snapshot;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: extended battle snapshot build failed: " + ex.Message);
                return BuildFallbackBattleSnapshot(mapScene, playerSideText);
            }
        }

        private static BattleSnapshotMessage BuildFallbackBattleSnapshot(string mapScene, string playerSideText)
        {
            var snapshot = new BattleSnapshotMessage
            {
                BattleId = "fallback",
                BattleType = "Unknown",
                MapScene = mapScene,
                PlayerSide = playerSideText
            };

            var mainPartySide = new BattleSideSnapshotMessage
            {
                SideId = string.Equals(playerSideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase) ? "defender" : "attacker",
                SideText = string.Equals(playerSideText, nameof(BattleSideEnum.Defender), StringComparison.OrdinalIgnoreCase)
                    ? nameof(BattleSideEnum.Defender)
                    : nameof(BattleSideEnum.Attacker),
                LeaderPartyId = MobileParty.MainParty?.StringId ?? "main_party",
                SideMorale = TryGetFloatProperty(MobileParty.MainParty, "Morale"),
                IsPlayerSide = true
            };

            var mainParty = new BattlePartySnapshotMessage
            {
                PartyId = MobileParty.MainParty?.StringId ?? "main_party",
                PartyName = MobileParty.MainParty?.Name?.ToString() ?? "Main Party",
                IsMainParty = true,
                Modifiers = TryBuildPartyModifierSnapshot(MobileParty.MainParty, MobileParty.MainParty?.Party),
                Troops = BuildPartyTroopStacksSafe()
            };
            mainParty.TotalManCount = mainParty.Troops?.Sum(t => t?.Count ?? 0) ?? 0;
            mainPartySide.Parties.Add(mainParty);
            mainPartySide.Troops.AddRange(mainParty.Troops ?? new List<TroopStackInfo>());
            mainPartySide.TotalManCount = mainParty.TotalManCount;
            snapshot.Sides.Add(mainPartySide);

            LogSnapshotMappings("fallback", snapshot);
            return snapshot;
        }

        private static BattleSideSnapshotMessage BuildBattleSideSnapshot(object sideObject, string sideId, string sideText, string playerSideText)
        {
            if (sideObject == null)
            {
                ModLogger.Info("BattleDetector: live side snapshot skipped because side object is null. SideId=" + (sideId ?? "unknown") + ".");
                return null;
            }

            ModLogger.Info(
                "BattleDetector: building side snapshot. " +
                "SideId=" + (sideId ?? "unknown") +
                " SideType=" + sideObject.GetType().FullName + ".");

            var sideSnapshot = new BattleSideSnapshotMessage
            {
                SideId = sideId,
                SideText = sideText,
                LeaderPartyId = TryResolveSideLeaderPartyId(sideObject),
                SideMorale = TryGetSideMorale(sideObject),
                IsPlayerSide = string.Equals(playerSideText, sideText, StringComparison.OrdinalIgnoreCase)
            };

            HashSet<string> seenPartyIds = new HashSet<string>(StringComparer.Ordinal);
            int enumeratedPartyCount = 0;
            foreach (object partyObject in EnumerateBattleParties(sideObject))
            {
                enumeratedPartyCount++;
                BattlePartySnapshotMessage partySnapshot = BuildBattlePartySnapshot(partyObject, sideId);
                if (partySnapshot == null)
                    continue;

                string partyId = string.IsNullOrWhiteSpace(partySnapshot.PartyId)
                    ? "party_" + seenPartyIds.Count
                    : partySnapshot.PartyId;
                if (!seenPartyIds.Add(partyId))
                    continue;

                partySnapshot.PartyId = partyId;
                sideSnapshot.Parties.Add(partySnapshot);
                if (partySnapshot.Troops != null && partySnapshot.Troops.Count > 0)
                    sideSnapshot.Troops.AddRange(partySnapshot.Troops);
            }

            sideSnapshot.TotalManCount = sideSnapshot.Parties.Sum(p => p?.TotalManCount ?? 0);
            ModLogger.Info(
                "BattleDetector: side snapshot built. " +
                "SideId=" + (sideId ?? "unknown") +
                " EnumeratedPartyObjects=" + enumeratedPartyCount +
                " SnapshotParties=" + sideSnapshot.Parties.Count +
                " TotalTroopStacks=" + sideSnapshot.Troops.Count + ".");
            return sideSnapshot.Parties.Count > 0 ? sideSnapshot : null;
        }

        private static BattlePartySnapshotMessage BuildBattlePartySnapshot(object partyObject, string sideId)
        {
            object partyBase = UnwrapPartyBase(partyObject);
            if (partyBase == null)
            {
                ModLogger.Info(
                    "BattleDetector: party snapshot skipped because party base unwrap failed. " +
                    "SideId=" + (sideId ?? "unknown") +
                    " PartyObjectType=" + (partyObject?.GetType().FullName ?? "null") + ".");
                return null;
            }

            List<TroopStackInfo> troops = BuildTroopStacksFromPartySafe(partyBase, sideId);
            if (troops.Count == 0)
            {
                ModLogger.Info(
                    "BattleDetector: party snapshot skipped because troop stack build returned 0 entries. " +
                    "SideId=" + (sideId ?? "unknown") +
                    " PartyBaseType=" + partyBase.GetType().FullName +
                    " PartyId=" + (TryGetStringId(partyBase) ?? TryGetStringId(TryGetPropertyValue(partyBase, "MobileParty")) ?? "null") + ".");
                return null;
            }

            string partyId = TryGetStringId(partyBase) ?? TryGetStringId(TryGetPropertyValue(partyBase, "MobileParty"));
            string partyName = TryGetPartyName(partyBase);
            bool isMainParty = ReferenceEquals(partyBase, MobileParty.MainParty?.Party);

            return new BattlePartySnapshotMessage
            {
                PartyId = partyId,
                PartyName = partyName,
                IsMainParty = isMainParty,
                TotalManCount = troops.Sum(t => t?.Count ?? 0),
                Modifiers = TryBuildPartyModifierSnapshot(partyObject, partyBase),
                Troops = troops
            };
        }

        private static string TryResolveSideLeaderPartyId(object sideObject)
        {
            object leaderParty = TryGetPropertyValue(sideObject, "LeaderParty");
            return TryGetStringId(leaderParty) ?? TryGetStringId(TryGetPropertyValue(leaderParty, "MobileParty"));
        }

        private static float TryGetSideMorale(object sideObject)
        {
            float directValue = TryGetFloatProperty(sideObject, "SideMorale");
            if (directValue > 0.01f)
                return directValue;

            return TryConvertToFloat(TryInvokeMethod(sideObject, "GetSideMorale"));
        }

        private static BattlePartyModifierSnapshotMessage TryBuildPartyModifierSnapshot(object partyObject, object partyBase)
        {
            object mobileParty = TryResolveMobileParty(partyObject, partyBase);
            object leaderHeroObject = TryGetPropertyValue(mobileParty, "LeaderHero");
            object ownerHeroObject = TryGetPropertyValue(mobileParty, "Owner");
            object scoutHeroObject = TryGetPropertyValue(mobileParty, "EffectiveScout");
            object quartermasterHeroObject = TryGetPropertyValue(mobileParty, "EffectiveQuartermaster");
            object engineerHeroObject = TryGetPropertyValue(mobileParty, "EffectiveEngineer");
            object surgeonHeroObject = TryGetPropertyValue(mobileParty, "EffectiveSurgeon");

            Hero leaderHero = TryResolveHeroObject(leaderHeroObject);
            Hero ownerHero = TryResolveHeroObject(ownerHeroObject);
            Hero scoutHero = TryResolveHeroObject(scoutHeroObject);
            Hero quartermasterHero = TryResolveHeroObject(quartermasterHeroObject);
            Hero engineerHero = TryResolveHeroObject(engineerHeroObject);
            Hero surgeonHero = TryResolveHeroObject(surgeonHeroObject);

            var modifiers = new BattlePartyModifierSnapshotMessage
            {
                LeaderHeroId = TryGetHeroId(leaderHeroObject),
                OwnerHeroId = TryGetHeroId(ownerHeroObject),
                ScoutHeroId = TryGetHeroId(scoutHeroObject),
                QuartermasterHeroId = TryGetHeroId(quartermasterHeroObject),
                EngineerHeroId = TryGetHeroId(engineerHeroObject),
                SurgeonHeroId = TryGetHeroId(surgeonHeroObject),
                Morale = TryGetFloatProperty(mobileParty, "Morale"),
                RecentEventsMorale = TryGetFloatProperty(mobileParty, "RecentEventsMorale"),
                MoraleChange = TryGetFloatProperty(partyObject, "MoraleChange"),
                ContributionToBattle = TryGetIntProperty(partyObject, "ContributionToBattle"),
                LeaderLeadershipSkill = TryGetCharacterSkillValue(leaderHero, "Leadership"),
                LeaderTacticsSkill = TryGetCharacterSkillValue(leaderHero, "Tactics"),
                ScoutScoutingSkill = TryGetCharacterSkillValue(scoutHero, "Scouting"),
                QuartermasterStewardSkill = TryGetCharacterSkillValue(quartermasterHero, "Steward"),
                EngineerEngineeringSkill = TryGetCharacterSkillValue(engineerHero, "Engineering"),
                SurgeonMedicineSkill = TryGetCharacterSkillValue(surgeonHero, "Medicine"),
                PartyLeaderPerkIds = TryGetHeroPerkIdsByPartyRoles(leaderHero, "PartyLeader"),
                ArmyCommanderPerkIds = TryGetHeroPerkIdsByPartyRoles(leaderHero, "ArmyCommander"),
                CaptainPerkIds = TryGetHeroPerkIdsByPartyRoles(leaderHero, "Captain"),
                ScoutPerkIds = TryGetHeroPerkIdsByPartyRoles(scoutHero, "Scout"),
                QuartermasterPerkIds = TryGetHeroPerkIdsByPartyRoles(quartermasterHero, "Quartermaster"),
                EngineerPerkIds = TryGetHeroPerkIdsByPartyRoles(engineerHero, "Engineer"),
                SurgeonPerkIds = TryGetHeroPerkIdsByPartyRoles(surgeonHero, "Surgeon")
            };

            return HasPartyModifierData(modifiers) ? modifiers : null;
        }

        private static object TryResolveMobileParty(object partyObject, object partyBase)
        {
            if (partyBase is MobileParty directMobileParty)
                return directMobileParty;

            return TryGetPropertyValue(partyBase, "MobileParty")
                ?? TryGetPropertyValue(partyObject, "MobileParty")
                ?? TryGetPropertyValue(TryGetPropertyValue(partyObject, "Party"), "MobileParty")
                ?? TryGetPropertyValue(TryGetPropertyValue(partyObject, "PartyBase"), "MobileParty");
        }

        private static IEnumerable<object> EnumerateBattleParties(object sideObject)
        {
            if (sideObject == null)
                yield break;

            foreach (string propertyName in new[] { "Parties", "BattleParties", "PartiesOnSide", "MemberParties", "MapEventPartySides", "PartyBases", "MobileParties" })
            {
                object collection = TryGetPropertyValue(sideObject, propertyName);
                if (!(collection is System.Collections.IEnumerable enumerable) || collection is string)
                    continue;

                foreach (object item in enumerable)
                {
                    if (item != null)
                        yield return item;
                }

                ModLogger.Info(
                    "BattleDetector: enumerated battle parties via property '" + propertyName + "'. " +
                    "SideType=" + sideObject.GetType().FullName + ".");

                yield break;
            }

            if (sideObject is System.Collections.IEnumerable selfEnumerable && !(sideObject is string))
            {
                foreach (object item in selfEnumerable)
                {
                    if (item != null)
                        yield return item;
                }

                ModLogger.Info(
                    "BattleDetector: enumerated battle parties via self-enumerable side object. " +
                    "SideType=" + sideObject.GetType().FullName + ".");
            }
        }

        private static object UnwrapPartyBase(object partyObject)
        {
            if (partyObject == null)
                return null;

            foreach (string propertyName in new[] { "Party", "PartyBase", "MobileParty", "MapEventParty", "PartyComponent" })
            {
                object nested = TryGetPropertyValue(partyObject, propertyName);
                if (nested == null)
                    continue;

                object nestedPartyBase = TryGetPropertyValue(nested, "Party") ?? TryGetPropertyValue(nested, "PartyBase");
                if (nestedPartyBase != null)
                    return nestedPartyBase;

                if (TryGetPropertyValue(nested, "MemberRoster") != null || TryGetPropertyValue(nested, "Roster") != null)
                    return nested;
            }

            return TryGetPropertyValue(partyObject, "MemberRoster") != null || TryGetPropertyValue(partyObject, "Roster") != null
                ? partyObject
                : null;
        }

        private static List<TroopStackInfo> BuildTroopStacksFromPartySafe(object partyBase, string sideId)
        {
            var troops = new List<TroopStackInfo>();
            if (partyBase == null)
                return troops;

            try
            {
                object memberRoster = TryGetPropertyValue(partyBase, "MemberRoster");
                if (memberRoster == null)
                {
                    ModLogger.Info(
                        "BattleDetector: party troop build missing MemberRoster. " +
                        "SideId=" + (sideId ?? "unknown") +
                        " PartyBaseType=" + partyBase.GetType().FullName + ".");
                    return troops;
                }

                object troopRoster = TryInvokeMethod(memberRoster, "GetTroopRoster");
                if (troopRoster == null)
                {
                    troopRoster = TryGetPropertyValue(memberRoster, "TroopRoster") ?? TryGetPropertyValue(memberRoster, "_roster");
                }
                if (!(troopRoster is System.Collections.IEnumerable enumerable))
                {
                    ModLogger.Info(
                        "BattleDetector: party troop build missing enumerable troop roster. " +
                        "SideId=" + (sideId ?? "unknown") +
                        " PartyBaseType=" + partyBase.GetType().FullName +
                        " MemberRosterType=" + memberRoster.GetType().FullName +
                        " TroopRosterType=" + (troopRoster?.GetType().FullName ?? "null") + ".");
                    return troops;
                }

                var rosterCharacters = new List<object>();
                var rosterElements = new List<object>();
                foreach (object element in enumerable)
                {
                    if (element == null)
                        continue;

                    rosterElements.Add(element);
                    object character = TryGetPropertyValue(element, "Character");
                    if (character != null)
                        rosterCharacters.Add(character);
                }

                string partyId = TryGetStringId(partyBase) ?? TryGetStringId(TryGetPropertyValue(partyBase, "MobileParty"));
                foreach (object element in rosterElements)
                {
                    object character = TryGetPropertyValue(element, "Character");
                    if (character == null)
                        continue;

                    troops.AddRange(BuildTroopStacksForCharacterVariants(
                        character,
                        sideId,
                        partyId,
                        TryGetIntProperty(element, "Number"),
                        TryGetIntProperty(element, "WoundedNumber"),
                        rosterCharacters,
                        assignExplicitEntryIds: true));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: failed to build side party troop stacks: " + ex.Message);
            }

            string partyIdForLog = TryGetStringId(partyBase) ?? TryGetStringId(TryGetPropertyValue(partyBase, "MobileParty")) ?? "party";
            LogTroopStackMappings("side-party " + (sideId ?? "unknown") + "/" + partyIdForLog, troops);
            return troops;
        }

        private sealed class CombatEquipmentVariantSnapshot
        {
            public string Signature { get; set; }
            public int SampledCount { get; set; }
            public string CombatItem0Id { get; set; }
            public string CombatItem1Id { get; set; }
            public string CombatItem2Id { get; set; }
            public string CombatItem3Id { get; set; }
            public string CombatHeadId { get; set; }
            public string CombatBodyId { get; set; }
            public string CombatLegId { get; set; }
            public string CombatGlovesId { get; set; }
            public string CombatCapeId { get; set; }
            public string CombatHorseId { get; set; }
            public string CombatHorseHarnessId { get; set; }
        }

        private static List<TroopStackInfo> BuildTroopStacksForCharacterVariants(
            object characterObject,
            string sideId,
            string partyId,
            int totalCount,
            int woundedCount,
            List<object> rosterCharacters,
            bool assignExplicitEntryIds)
        {
            var troops = new List<TroopStackInfo>();
            if (characterObject == null || totalCount <= 0)
                return troops;

            string originalCharacterId = TryGetStringId(characterObject);
            string spawnTemplateId = GetMissionSafeCharacterId(characterObject, rosterCharacters);
            List<CombatEquipmentVariantSnapshot> equipmentVariants = BuildCombatEquipmentVariants(characterObject, totalCount);
            bool hasSampledVariantCounts = equipmentVariants.Any(variant => variant != null && variant.SampledCount > 0);
            int activeVariantCount = Math.Max(1, Math.Min(totalCount, equipmentVariants.Count > 0 ? equipmentVariants.Count : 1));
            int clampedWoundedCount = Math.Max(0, Math.Min(totalCount, woundedCount));
            int assignedTroops = 0;
            int assignedWounded = 0;

            for (int variantIndex = 0; variantIndex < activeVariantCount; variantIndex++)
            {
                CombatEquipmentVariantSnapshot variant =
                    equipmentVariants.Count > variantIndex ? equipmentVariants[variantIndex] : null;
                int variantTroopCount = hasSampledVariantCounts && variant != null && variant.SampledCount > 0
                    ? variant.SampledCount
                    : GetDistributedVariantCount(totalCount, activeVariantCount, variantIndex);
                if (variantTroopCount <= 0)
                    continue;

                int variantWoundedCount = hasSampledVariantCounts
                    ? GetDistributedVariantWoundedCount(
                        clampedWoundedCount,
                        totalCount,
                        variantTroopCount,
                        assignedTroops,
                        assignedWounded)
                    : Math.Min(
                        variantTroopCount,
                        GetDistributedVariantCount(clampedWoundedCount, activeVariantCount, variantIndex));

                var stack = new TroopStackInfo
                {
                    SideId = sideId,
                    PartyId = partyId,
                    CharacterId = spawnTemplateId,
                    OriginalCharacterId = originalCharacterId,
                    SpawnTemplateId = spawnTemplateId,
                    CultureId = TryGetCultureId(characterObject),
                    HasShield = TryGetCharacterHasShield(characterObject),
                    HasThrown = TryGetCharacterHasThrown(characterObject),
                    IsRanged = TryGetCharacterIsRanged(characterObject),
                    TroopName = TryGetPropertyValue(characterObject, "Name")?.ToString() ?? TryGetStringId(characterObject),
                    Tier = TryGetIntProperty(characterObject, "Tier"),
                    IsMounted = TryGetBoolProperty(characterObject, "IsMounted"),
                    IsHero = TryGetBoolProperty(characterObject, "IsHero"),
                    Count = variantTroopCount,
                    WoundedCount = variantWoundedCount
                };

                if (assignExplicitEntryIds)
                    stack.EntryId = BuildVariantAwareEntryId(sideId, partyId, spawnTemplateId, variantIndex, activeVariantCount);

                if (variant != null)
                    ApplyCombatEquipmentSnapshot(stack, variant);
                else
                    ApplyCombatEquipmentSnapshot(stack, characterObject);

                ApplyCombatProfileSnapshot(stack, characterObject);
                ApplyHeroIdentitySnapshot(stack, characterObject);
                troops.Add(stack);
                assignedTroops += variantTroopCount;
                assignedWounded += variantWoundedCount;
            }

            return troops;
        }

        private static int GetDistributedVariantCount(int total, int bucketCount, int bucketIndex)
        {
            if (total <= 0 || bucketCount <= 0 || bucketIndex < 0 || bucketIndex >= bucketCount)
                return 0;

            int baseCount = total / bucketCount;
            int remainder = total % bucketCount;
            return baseCount + (bucketIndex < remainder ? 1 : 0);
        }

        private static int GetDistributedVariantWoundedCount(
            int totalWounded,
            int totalCount,
            int variantTroopCount,
            int assignedTroops,
            int assignedWounded)
        {
            if (totalWounded <= 0 || totalCount <= 0 || variantTroopCount <= 0)
                return 0;

            int remainingWounded = Math.Max(0, totalWounded - assignedWounded);
            int remainingTroops = Math.Max(0, totalCount - assignedTroops);
            if (remainingWounded == 0 || remainingTroops == 0)
                return 0;

            if (assignedTroops + variantTroopCount >= totalCount)
                return Math.Min(variantTroopCount, remainingWounded);

            int distributed = (int)Math.Round(
                (double)remainingWounded * variantTroopCount / remainingTroops,
                MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(variantTroopCount, Math.Min(remainingWounded, distributed)));
        }

        private static string BuildVariantAwareEntryId(string sideId, string partyId, string spawnTemplateId, int variantIndex, int variantCount)
        {
            string canonicalSideId = string.IsNullOrWhiteSpace(sideId) ? "unknown" : sideId;
            string canonicalPartyId = string.IsNullOrWhiteSpace(partyId) ? "party" : partyId;
            string canonicalCharacterId = string.IsNullOrWhiteSpace(spawnTemplateId) ? "unknown" : spawnTemplateId;
            if (variantCount <= 1)
                return canonicalSideId + "|" + canonicalPartyId + "|" + canonicalCharacterId;

            return canonicalSideId + "|" + canonicalPartyId + "|" + canonicalCharacterId + "|variant-" + (variantIndex + 1);
        }

        private static List<CombatEquipmentVariantSnapshot> BuildCombatEquipmentVariants(object characterObject, int totalCount)
        {
            List<CombatEquipmentVariantSnapshot> sampledVariants = BuildRandomBattleEquipmentVariants(characterObject, totalCount);
            if (sampledVariants.Count > 0)
                return sampledVariants;

            var allEquipments = EnumerateCharacterEquipments(characterObject)
                .Where(equipment => equipment != null)
                .ToList();
            if (allEquipments.Count == 0)
                return new List<CombatEquipmentVariantSnapshot>();

            List<object> preferredEquipments = allEquipments
                .Where(equipment => !TryGetBoolProperty(equipment, "IsCivilian"))
                .ToList();
            if (preferredEquipments.Count == 0)
                preferredEquipments = allEquipments;

            var variants = new List<CombatEquipmentVariantSnapshot>();
            var seenSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object equipment in preferredEquipments)
            {
                CombatEquipmentVariantSnapshot variant = BuildCombatEquipmentVariantSnapshot(equipment);
                string signature = variant?.Signature ?? string.Empty;
                if (!seenSignatures.Add(signature))
                    continue;

                variants.Add(variant);
            }

            return variants;
        }

        private static List<CombatEquipmentVariantSnapshot> BuildRandomBattleEquipmentVariants(object characterObject, int totalCount)
        {
            var variants = new List<CombatEquipmentVariantSnapshot>();
            if (characterObject == null || totalCount <= 0)
                return variants;

            var bySignature = new Dictionary<string, CombatEquipmentVariantSnapshot>(StringComparer.OrdinalIgnoreCase);
            for (int sampleIndex = 0; sampleIndex < totalCount; sampleIndex++)
            {
                object equipment = TryResolveRandomBattleEquipment(characterObject);
                if (equipment == null)
                    break;

                CombatEquipmentVariantSnapshot variant = BuildCombatEquipmentVariantSnapshot(equipment);
                if (variant == null)
                    continue;

                string signature = variant.Signature ?? string.Empty;
                if (bySignature.TryGetValue(signature, out CombatEquipmentVariantSnapshot existing))
                {
                    existing.SampledCount++;
                    continue;
                }

                variant.SampledCount = 1;
                bySignature[signature] = variant;
                variants.Add(variant);
            }

            if (variants.Sum(variant => variant?.SampledCount ?? 0) != totalCount)
                return new List<CombatEquipmentVariantSnapshot>();

            return variants;
        }

        private static CombatEquipmentVariantSnapshot BuildCombatEquipmentVariantSnapshot(object equipment)
        {
            if (equipment == null)
                return null;

            var variant = new CombatEquipmentVariantSnapshot
            {
                CombatItem0Id = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Weapon0),
                CombatItem1Id = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Weapon1),
                CombatItem2Id = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Weapon2),
                CombatItem3Id = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Weapon3),
                CombatHeadId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Head),
                CombatBodyId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Body),
                CombatLegId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Leg),
                CombatGlovesId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Gloves),
                CombatCapeId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Cape),
                CombatHorseId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.Horse),
                CombatHorseHarnessId = TryGetEquipmentSlotItemId(equipment, EquipmentIndex.HorseHarness)
            };

            variant.Signature = string.Join("|", new[]
            {
                variant.CombatItem0Id ?? string.Empty,
                variant.CombatItem1Id ?? string.Empty,
                variant.CombatItem2Id ?? string.Empty,
                variant.CombatItem3Id ?? string.Empty,
                variant.CombatHeadId ?? string.Empty,
                variant.CombatBodyId ?? string.Empty,
                variant.CombatLegId ?? string.Empty,
                variant.CombatGlovesId ?? string.Empty,
                variant.CombatCapeId ?? string.Empty,
                variant.CombatHorseId ?? string.Empty,
                variant.CombatHorseHarnessId ?? string.Empty
            });
            return variant;
        }

        private static void LogTroopStackMappings(string source, List<TroopStackInfo> troops)
        {
            if (troops == null || troops.Count == 0)
                return;

            IEnumerable<string> mappings = troops
                .Where(troop => troop != null)
                .Take(24)
                .Select(troop =>
                    (troop.OriginalCharacterId ?? "null") +
                    " -> " +
                    ((!string.IsNullOrWhiteSpace(troop.SpawnTemplateId) ? troop.SpawnTemplateId : troop.CharacterId) ?? "null") +
                    " x" + troop.Count +
                    " culture=" + (troop.CultureId ?? "null") +
                    " ranged=" + troop.IsRanged +
                    " shield=" + troop.HasShield +
                    " thrown=" + troop.HasThrown +
                    FormatHeroIdentitySummary(troop) +
                    FormatCombatProfileSummary(troop) +
                    FormatCombatEquipmentSummary(troop));

            ModLogger.Info(
                "BattleDetector: troop stack mapping summary (" + (source ?? "unknown") + ") = [" +
                string.Join("; ", mappings) +
                "].");
        }

        private static void LogSnapshotMappings(string source, BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return;

            IEnumerable<string> mappings = snapshot.Sides
                .Where(side => side?.Troops != null)
                .SelectMany(side => side.Troops)
                .Where(troop => troop != null)
                .Take(32)
                .Select(troop =>
                    (troop.SideId ?? "side") + "/" +
                    (troop.PartyId ?? "party") + "/" +
                    (troop.EntryId ?? "entry") + ": " +
                    (troop.OriginalCharacterId ?? "null") +
                    " -> " +
                    ((!string.IsNullOrWhiteSpace(troop.SpawnTemplateId) ? troop.SpawnTemplateId : troop.CharacterId) ?? "null") +
                    " culture=" + (troop.CultureId ?? "null") +
                    " ranged=" + troop.IsRanged +
                    " shield=" + troop.HasShield +
                    " thrown=" + troop.HasThrown +
                    FormatHeroIdentitySummary(troop) +
                    FormatCombatProfileSummary(troop) +
                    FormatCombatEquipmentSummary(troop));

            ModLogger.Info(
                "BattleDetector: snapshot mapping summary (" + (source ?? "unknown") + ") = [" +
                string.Join("; ", mappings) +
                "].");

            IEnumerable<string> partyModifierMappings = snapshot.Sides
                .Where(side => side?.Parties != null)
                .SelectMany(side => side.Parties.Select(party => FormatPartyModifierSummary(side, party)))
                .Where(summary => !string.IsNullOrWhiteSpace(summary))
                .Take(24);
            if (partyModifierMappings.Any())
            {
                ModLogger.Info(
                    "BattleDetector: party modifier summary (" + (source ?? "unknown") + ") = [" +
                    string.Join("; ", partyModifierMappings) +
                    "].");
            }
        }

        private static string FormatPartyModifierSummary(BattleSideSnapshotMessage side, BattlePartySnapshotMessage party)
        {
            if (side == null || party == null)
                return null;

            BattlePartyModifierSnapshotMessage modifiers = party.Modifiers;
            bool hasAnyData =
                !string.IsNullOrWhiteSpace(side.LeaderPartyId) ||
                Math.Abs(side.SideMorale) > 0.01f ||
                HasPartyModifierData(modifiers);
            if (!hasAnyData)
                return null;

            var parts = new List<string>
            {
                (side.SideId ?? "side") + "/" + (party.PartyId ?? "party")
            };

            if (!string.IsNullOrWhiteSpace(side.LeaderPartyId) &&
                string.Equals(side.LeaderPartyId, party.PartyId, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("LeaderParty=True");
            }

            if (Math.Abs(side.SideMorale) > 0.01f)
                parts.Add("SideMorale=" + side.SideMorale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

            if (modifiers != null)
            {
                AddPartyModifierSummaryPart(parts, "Leader", modifiers.LeaderHeroId);
                AddPartyModifierSummaryPart(parts, "Owner", modifiers.OwnerHeroId);
                AddPartyModifierSummaryPart(parts, "Scout", modifiers.ScoutHeroId);
                AddPartyModifierSummaryPart(parts, "QM", modifiers.QuartermasterHeroId);
                AddPartyModifierSummaryPart(parts, "Eng", modifiers.EngineerHeroId);
                AddPartyModifierSummaryPart(parts, "Surg", modifiers.SurgeonHeroId);

                if (Math.Abs(modifiers.Morale) > 0.01f)
                    parts.Add("Morale=" + modifiers.Morale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                if (Math.Abs(modifiers.RecentEventsMorale) > 0.01f)
                    parts.Add("Recent=" + modifiers.RecentEventsMorale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                if (Math.Abs(modifiers.MoraleChange) > 0.01f)
                    parts.Add("MoraleChange=" + modifiers.MoraleChange.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                if (modifiers.ContributionToBattle > 0)
                    parts.Add("Contribution=" + modifiers.ContributionToBattle);

                if (modifiers.LeaderLeadershipSkill > 0 || modifiers.LeaderTacticsSkill > 0)
                    parts.Add("LeaderSkills=" + modifiers.LeaderLeadershipSkill + "/" + modifiers.LeaderTacticsSkill);
                if (modifiers.ScoutScoutingSkill > 0)
                    parts.Add("ScoutSkill=" + modifiers.ScoutScoutingSkill);
                if (modifiers.QuartermasterStewardSkill > 0)
                    parts.Add("QMSkill=" + modifiers.QuartermasterStewardSkill);
                if (modifiers.EngineerEngineeringSkill > 0)
                    parts.Add("EngSkill=" + modifiers.EngineerEngineeringSkill);
                if (modifiers.SurgeonMedicineSkill > 0)
                    parts.Add("SurgSkill=" + modifiers.SurgeonMedicineSkill);

                string rolePerks = FormatRolePerkCountSummary(modifiers);
                if (!string.IsNullOrWhiteSpace(rolePerks))
                    parts.Add("RolePerks[" + rolePerks + "]");
            }

            return string.Join(" ", parts);
        }

        private static void AddPartyModifierSummaryPart(List<string> parts, string label, string value)
        {
            if (parts == null || string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                return;

            parts.Add(label + "=" + value);
        }

        private static string FormatRolePerkCountSummary(BattlePartyModifierSnapshotMessage modifiers)
        {
            if (modifiers == null)
                return string.Empty;

            var parts = new List<string>();
            AddRolePerkCountSummaryPart(parts, "PL", modifiers.PartyLeaderPerkIds);
            AddRolePerkCountSummaryPart(parts, "AC", modifiers.ArmyCommanderPerkIds);
            AddRolePerkCountSummaryPart(parts, "Cap", modifiers.CaptainPerkIds);
            AddRolePerkCountSummaryPart(parts, "Sc", modifiers.ScoutPerkIds);
            AddRolePerkCountSummaryPart(parts, "QM", modifiers.QuartermasterPerkIds);
            AddRolePerkCountSummaryPart(parts, "Eng", modifiers.EngineerPerkIds);
            AddRolePerkCountSummaryPart(parts, "Surg", modifiers.SurgeonPerkIds);
            return string.Join(",", parts);
        }

        private static void AddRolePerkCountSummaryPart(List<string> parts, string label, List<string> perkIds)
        {
            if (parts == null || string.IsNullOrWhiteSpace(label) || perkIds == null || perkIds.Count <= 0)
                return;

            parts.Add(label + "=" + perkIds.Count);
        }

        private static bool HasPartyModifierData(BattlePartyModifierSnapshotMessage modifiers)
        {
            if (modifiers == null)
                return false;

            return
                !string.IsNullOrWhiteSpace(modifiers.LeaderHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.OwnerHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.ScoutHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.QuartermasterHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.EngineerHeroId) ||
                !string.IsNullOrWhiteSpace(modifiers.SurgeonHeroId) ||
                Math.Abs(modifiers.Morale) > 0.01f ||
                Math.Abs(modifiers.RecentEventsMorale) > 0.01f ||
                Math.Abs(modifiers.MoraleChange) > 0.01f ||
                modifiers.ContributionToBattle > 0 ||
                modifiers.LeaderLeadershipSkill > 0 ||
                modifiers.LeaderTacticsSkill > 0 ||
                modifiers.ScoutScoutingSkill > 0 ||
                modifiers.QuartermasterStewardSkill > 0 ||
                modifiers.EngineerEngineeringSkill > 0 ||
                modifiers.SurgeonMedicineSkill > 0 ||
                (modifiers.PartyLeaderPerkIds?.Count ?? 0) > 0 ||
                (modifiers.ArmyCommanderPerkIds?.Count ?? 0) > 0 ||
                (modifiers.CaptainPerkIds?.Count ?? 0) > 0 ||
                (modifiers.ScoutPerkIds?.Count ?? 0) > 0 ||
                (modifiers.QuartermasterPerkIds?.Count ?? 0) > 0 ||
                (modifiers.EngineerPerkIds?.Count ?? 0) > 0 ||
                (modifiers.SurgeonPerkIds?.Count ?? 0) > 0;
        }

        private static List<TroopStackInfo> BuildLegacyTroopsFromSnapshot(BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return null;

            BattleSideSnapshotMessage playerSide = snapshot.Sides.FirstOrDefault(side => side != null && side.IsPlayerSide);
            BattleSideSnapshotMessage fallbackSide = playerSide ?? snapshot.Sides.FirstOrDefault(side => side != null);
            return fallbackSide?.Troops;
        }

        private static int BuildLegacyArmySizeFromSnapshot(BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return 0;

            BattleSideSnapshotMessage playerSide = snapshot.Sides.FirstOrDefault(side => side != null && side.IsPlayerSide);
            BattleSideSnapshotMessage fallbackSide = playerSide ?? snapshot.Sides.FirstOrDefault(side => side != null);
            return fallbackSide?.TotalManCount ?? 0;
        }

        private static object TryGetCurrentBattleObject()
        {
            try
            {
                var campaignAssembly = typeof(TaleWorlds.CampaignSystem.Campaign).Assembly;
                Type typeA = campaignAssembly.GetType("TaleWorlds.CampaignSystem.Encounters.PlayerEncounter");
                Type typeB = campaignAssembly.GetType("TaleWorlds.CampaignSystem.PlayerEncounter");
                Type playerEncounterType = typeA ?? typeB;
                if (playerEncounterType == null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject failed because PlayerEncounter type was not found.");
                    return null;
                }

                object currentEncounter = TryGetStaticPropertyValue(playerEncounterType, "Current");
                object directBattle = TryGetStaticPropertyValue(playerEncounterType, "Battle");
                if (directBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via PlayerEncounter.Battle.");
                    return directBattle;
                }

                object encounteredBattle = TryGetStaticPropertyValue(playerEncounterType, "EncounteredBattle");
                if (encounteredBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via PlayerEncounter.EncounteredBattle.");
                    return encounteredBattle;
                }

                object currentEncounterBattle = TryGetPropertyValue(currentEncounter, "Battle") ?? TryGetPropertyValue(currentEncounter, "_mapEvent");
                if (currentEncounterBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via PlayerEncounter.Current.Battle/_mapEvent.");
                    return currentEncounterBattle;
                }

                object currentEncounteredParty = TryGetPropertyValue(currentEncounter, "EncounteredParty") ?? TryGetPropertyValue(currentEncounter, "_encounteredParty");
                object currentEncounterPartyBattle =
                    TryGetPropertyValue(currentEncounteredParty, "MapEvent") ??
                    TryGetPropertyValue(TryGetPropertyValue(currentEncounteredParty, "MobileParty"), "MapEvent");
                if (currentEncounterPartyBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via encountered party MapEvent.");
                    return currentEncounterPartyBattle;
                }

                object mainPartyBattle = TryGetPropertyValue(MobileParty.MainParty, "MapEvent");
                if (mainPartyBattle != null)
                {
                    ModLogger.Info("BattleDetector: TryGetCurrentBattleObject resolved via MobileParty.MainParty.MapEvent.");
                    return mainPartyBattle;
                }

                ModLogger.Info(
                    "BattleDetector: TryGetCurrentBattleObject returned null. " +
                    "HasCurrentEncounter=" + (currentEncounter != null) +
                    " HasEncounteredParty=" + (currentEncounteredParty != null) +
                    " HasMainPartyMapEvent=" + (mainPartyBattle != null));
                return null;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleDetector: TryGetCurrentBattleObject failed: " + ex.Message);
                return null;
            }
        }

        private static object TryGetStaticPropertyValue(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return property?.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static Type TryGetKnownGameType(params string[] fullNames)
        {
            if (fullNames == null || fullNames.Length == 0)
                return null;

            foreach (string fullName in fullNames)
            {
                if (string.IsNullOrWhiteSpace(fullName))
                    continue;

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        Type resolvedType = assembly.GetType(fullName, false);
                        if (resolvedType != null)
                            return resolvedType;
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static object TryInvokeMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            try
            {
                MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                return method?.Invoke(instance, null);
            }
            catch
            {
                return null;
            }
        }

        private static object TryInvokeMethod(object instance, string methodName, object argument)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            try
            {
                Type argumentType = argument?.GetType();
                MethodInfo method = instance.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(candidate =>
                    {
                        if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                            return false;

                        ParameterInfo[] parameters = candidate.GetParameters();
                        if (parameters.Length != 1)
                            return false;

                        if (argument == null)
                            return !parameters[0].ParameterType.IsValueType ||
                                   Nullable.GetUnderlyingType(parameters[0].ParameterType) != null;

                        return parameters[0].ParameterType.IsInstanceOfType(argument) ||
                               parameters[0].ParameterType.IsAssignableFrom(argumentType);
                    });

                return method?.Invoke(instance, new[] { argument });
            }
            catch
            {
                return null;
            }
        }

        private static int TryConvertToInt(object value)
        {
            if (value == null)
                return 0;

            try
            {
                switch (value)
                {
                    case int intValue:
                        return intValue;
                    case short shortValue:
                        return shortValue;
                    case long longValue:
                        return longValue > int.MaxValue ? int.MaxValue : (int)longValue;
                    case float floatValue:
                        return (int)Math.Round(floatValue);
                    case double doubleValue:
                        return (int)Math.Round(doubleValue);
                    case decimal decimalValue:
                        return (int)Math.Round(decimalValue);
                    case byte byteValue:
                        return byteValue;
                    case sbyte sbyteValue:
                        return sbyteValue;
                    case uint uintValue:
                        return uintValue > int.MaxValue ? int.MaxValue : (int)uintValue;
                    case ushort ushortValue:
                        return ushortValue;
                    case ulong ulongValue:
                        return ulongValue > int.MaxValue ? int.MaxValue : (int)ulongValue;
                    default:
                        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                return 0;
            }
        }

        private static float TryConvertToFloat(object value)
        {
            if (value == null)
                return 0f;

            try
            {
                switch (value)
                {
                    case float floatValue:
                        return floatValue;
                    case double doubleValue:
                        return (float)doubleValue;
                    case decimal decimalValue:
                        return (float)decimalValue;
                    case int intValue:
                        return intValue;
                    case long longValue:
                        return longValue;
                    default:
                        return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                return 0f;
            }
        }

        private static string TryGetPartyName(object partyBase)
        {
            object directName = TryGetPropertyValue(partyBase, "Name");
            if (directName != null)
                return directName.ToString();

            object mobileParty = TryGetPropertyValue(partyBase, "MobileParty");
            object mobilePartyName = TryGetPropertyValue(mobileParty, "Name");
            if (mobilePartyName != null)
                return mobilePartyName.ToString();

            return TryGetStringId(partyBase) ?? "Unknown Party";
        }

        private static string TryGetPlayerSideTextSafe() // Best-effort: намагаємось визначити сторону гравця в поточному encounter
        { // Починаємо блок методу
            // Ми не хардкодимо PlayerEncounter тип напряму, // Пояснюємо чому reflection
            // щоб зменшити ризик несумісності API між версіями гри. // Пояснюємо мету
            try // Обгортаємо в try/catch, бо reflection може впасти
            { // Починаємо блок try
                var campaignAssembly = typeof(TaleWorlds.CampaignSystem.Campaign).Assembly; // Беремо збірку TaleWorlds.CampaignSystem, де зазвичай лежать encounter типи

                // Найчастіші повні імена для PlayerEncounter в Bannerlord. // Пояснюємо список кандидатів
                Type typeA = campaignAssembly.GetType("TaleWorlds.CampaignSystem.Encounters.PlayerEncounter"); // Варіант 1: через namespace Encounters
                Type typeB = campaignAssembly.GetType("TaleWorlds.CampaignSystem.PlayerEncounter"); // Варіант 2: прямо в CampaignSystem
                Type playerEncounterType = typeA ?? typeB; // Беремо перший знайдений варіант

                if (playerEncounterType == null) // Якщо тип не знайдений — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                // Очікуємо static property Battle. // Пояснюємо очікувану структуру
                var battleProperty = playerEncounterType.GetProperty("Battle"); // Беремо property "Battle"

                if (battleProperty == null) // Якщо property немає — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                object battle = battleProperty.GetValue(null, null); // Читаємо static property (екземпляр не потрібен)

                if (battle == null) // Якщо battle null — значить encounter не в стані battle або ще не створений
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                // Очікуємо property PlayerSide на battle-об'єкті. // Пояснюємо очікування
                var playerSideProperty = battle.GetType().GetProperty("PlayerSide"); // Беремо property "PlayerSide"

                if (playerSideProperty == null) // Якщо немає — повертаємо Unknown
                { // Починаємо блок if
                    return "Unknown"; // Fallback
                } // Завершуємо блок if

                object sideValue = playerSideProperty.GetValue(battle, null); // Читаємо значення PlayerSide (зазвичай enum)

                return sideValue != null ? sideValue.ToString() : "Unknown"; // Повертаємо enum.ToString() або Unknown
            } // Завершуємо блок try
            catch (Exception) // Якщо reflection не вдався — не ламаємо мод, а просто повертаємо Unknown
            { // Починаємо блок catch
                return "Unknown"; // Fallback
            } // Завершуємо блок catch
        } // Завершуємо блок методу

        private static string GetMissionSafeCharacterId(object characterObject, List<object> rosterCharacters)
        {
            if (characterObject == null)
                return null;

            string originalId = TryGetStringId(characterObject);
            bool isHero = TryGetBoolProperty(characterObject, "IsHero");
            if (isHero)
            {
                string heroRoleSafeId = TryResolveHeroRoleMissionSafeCharacterId(characterObject as TaleWorlds.CampaignSystem.CharacterObject);
                if (!string.IsNullOrWhiteSpace(heroRoleSafeId) && !string.Equals(heroRoleSafeId, originalId, StringComparison.Ordinal))
                {
                    ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to hero-role runtime template '" + heroRoleSafeId + "'.");
                    return heroRoleSafeId;
                }
            }

            if (!isHero)
            {
                string banditFallbackId = TryResolveBanditMissionSafeCharacterId(characterObject);
                if (!string.IsNullOrWhiteSpace(banditFallbackId) && !string.Equals(banditFallbackId, originalId, StringComparison.Ordinal))
                {
                    ModLogger.Info("BattleDetector: mapped bandit campaign troop id '" + originalId + "' to mission-safe bandit fallback '" + banditFallbackId + "'.");
                    return banditFallbackId;
                }
            }

            string multiplayerSafeId = TryResolveMultiplayerSafeCharacterId(characterObject as TaleWorlds.CampaignSystem.CharacterObject);
            if (!string.IsNullOrWhiteSpace(multiplayerSafeId) && !string.Equals(multiplayerSafeId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped campaign troop id '" + originalId + "' to multiplayer-safe id '" + multiplayerSafeId + "'.");
                return multiplayerSafeId;
            }

            if (!isHero)
            {
                string genericFallbackId = TryResolveGenericMissionSafeCharacterId(characterObject);
                if (!string.IsNullOrWhiteSpace(genericFallbackId) && !string.Equals(genericFallbackId, originalId, StringComparison.Ordinal))
                {
                    ModLogger.Info("BattleDetector: mapped unsupported campaign troop id '" + originalId + "' to generic mission-safe fallback '" + genericFallbackId + "'.");
                    return genericFallbackId;
                }

                return originalId;
            }

            LogHeroRosterContext(originalId, rosterCharacters);

            string rosterSurrogateId = TryFindRosterSurrogateId(characterObject, rosterCharacters);
            if (!string.IsNullOrWhiteSpace(rosterSurrogateId) && !string.Equals(rosterSurrogateId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to roster surrogate '" + rosterSurrogateId + "'.");
                return rosterSurrogateId;
            }

            string typedHeroFallbackId = TryResolveTypedHeroFallbackId(characterObject as TaleWorlds.CampaignSystem.CharacterObject);
            if (!string.IsNullOrWhiteSpace(typedHeroFallbackId) && !string.Equals(typedHeroFallbackId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to typed fallback '" + typedHeroFallbackId + "'.");
                return typedHeroFallbackId;
            }

            object originalCharacter = TryGetPropertyValue(characterObject, "OriginalCharacter");
            string originalCharacterId = TryGetStringId(originalCharacter);
            if (!string.IsNullOrWhiteSpace(originalCharacterId) && !string.Equals(originalCharacterId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to OriginalCharacter '" + originalCharacterId + "'.");
                return originalCharacterId;
            }

            object templateCharacter = TryGetPropertyValue(characterObject, "Template");
            string templateCharacterId = TryGetStringId(templateCharacter);
            if (!string.IsNullOrWhiteSpace(templateCharacterId) && !string.Equals(templateCharacterId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to Template '" + templateCharacterId + "'.");
                return templateCharacterId;
            }

            object culture = TryGetPropertyValue(characterObject, "Culture");
            string cultureFallbackId = TryResolveCultureTroopId(culture);
            if (!string.IsNullOrWhiteSpace(cultureFallbackId) && !string.Equals(cultureFallbackId, originalId, StringComparison.Ordinal))
            {
                ModLogger.Info("BattleDetector: mapped hero troop id '" + originalId + "' to culture fallback '" + cultureFallbackId + "'.");
                return cultureFallbackId;
            }

            const string guaranteedVanillaFallbackId = "imperial_infantryman";
            ModLogger.Info("BattleDetector: no mission-safe fallback found for hero troop id '" + originalId + "'. Using guaranteed vanilla fallback '" + guaranteedVanillaFallbackId + "'.");
            return guaranteedVanillaFallbackId;
        }

        private static string TryResolveBanditMissionSafeCharacterId(object characterObject)
        {
            string originalId = TryGetStringId(characterObject);
            if (string.IsNullOrWhiteSpace(originalId))
                return null;

            string normalized = originalId.Trim().ToLowerInvariant();
            if (normalized.Contains("looter"))
                return "mp_coop_looter_troop";
            if (normalized.Contains("sea_raider"))
                return "mp_heavy_infantry_sturgia_troop";
            if (normalized.Contains("forest_bandit"))
                return "mp_light_ranged_battania_troop";
            if (normalized.Contains("mountain_bandit"))
                return "mp_light_infantry_vlandia_troop";
            if (normalized.Contains("desert_bandit"))
                return "mp_skirmisher_aserai_troop";
            if (normalized.Contains("steppe_bandit"))
                return "mp_horse_archer_khuzait_troop";
            if (normalized.Contains("bandit"))
                return "mp_skirmisher_empire_troop";

            return null;
        }

        private static string TryResolveGenericMissionSafeCharacterId(object characterObject)
        {
            if (characterObject == null)
                return null;

            bool isMounted = TryGetBoolProperty(characterObject, "IsMounted");
            bool isRanged = TryGetCharacterIsRanged(characterObject);
            bool hasShield = TryGetCharacterHasShield(characterObject);
            bool hasThrown = TryGetCharacterHasThrown(characterObject);
            int tier = TryGetIntProperty(characterObject, "Tier");
            string cultureToken = TryMapCultureToMultiplayerToken(TryGetCultureId(characterObject));

            if (isMounted)
                return !string.IsNullOrWhiteSpace(cultureToken)
                    ? "mp_light_cavalry_" + cultureToken + "_troop"
                    : "mp_coop_light_cavalry_sturgia_troop";

            if (isRanged)
            {
                if (!string.IsNullOrWhiteSpace(cultureToken))
                    return tier >= 4
                        ? "mp_heavy_ranged_" + cultureToken + "_troop"
                        : "mp_light_ranged_" + cultureToken + "_troop";

                return tier >= 4
                    ? "mp_heavy_ranged_vlandia_troop"
                    : "mp_light_ranged_empire_troop";
            }

            if (!string.IsNullOrWhiteSpace(cultureToken))
            {
                if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) && tier < 4 && hasThrown)
                    return "mp_skirmisher_empire_troop";

                if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) && tier >= 4)
                    return "mp_coop_heavy_infantry_empire_troop";

                return NormalizeKnownMissionSafeTemplateId(
                    tier >= 4
                        ? "mp_heavy_infantry_" + cultureToken + "_troop"
                        : "mp_light_infantry_" + cultureToken + "_troop");
            }

            return tier >= 4
                ? "mp_coop_heavy_infantry_empire_troop"
                : "mp_heavy_infantry_empire_troop";
        }

        private static string TryFindRosterSurrogateId(object heroCharacter, List<object> rosterCharacters)
        {
            if (heroCharacter == null || rosterCharacters == null || rosterCharacters.Count == 0)
                return null;

            bool heroMounted = TryGetBoolProperty(heroCharacter, "IsMounted");
            int heroTier = TryGetIntProperty(heroCharacter, "Tier");
            string heroCultureId = TryGetStringId(TryGetPropertyValue(heroCharacter, "Culture"));

            object bestCandidate = null;
            int bestScore = int.MinValue;

            foreach (object candidate in rosterCharacters)
            {
                if (candidate == null || TryGetBoolProperty(candidate, "IsHero"))
                    continue;

                string candidateId = TryGetStringId(candidate);
                if (string.IsNullOrWhiteSpace(candidateId))
                    continue;

                int score = 0;
                bool candidateMounted = TryGetBoolProperty(candidate, "IsMounted");
                int candidateTier = TryGetIntProperty(candidate, "Tier");
                string candidateCultureId = TryGetStringId(TryGetPropertyValue(candidate, "Culture"));

                if (candidateMounted == heroMounted)
                    score += 50;
                if (!string.IsNullOrWhiteSpace(heroCultureId) && string.Equals(heroCultureId, candidateCultureId, StringComparison.Ordinal))
                    score += 100;

                score -= Math.Abs(candidateTier - heroTier) * 5;

                if (bestCandidate == null || score > bestScore)
                {
                    bestCandidate = candidate;
                    bestScore = score;
                }
            }

            return TryGetStringId(bestCandidate);
        }

        private static string TryResolveTypedHeroFallbackId(TaleWorlds.CampaignSystem.CharacterObject heroCharacter)
        {
            if (heroCharacter == null)
                return null;

            TaleWorlds.Core.BasicCharacterObject[] candidates =
            {
                heroCharacter.OriginalCharacter,
                heroCharacter.Culture?.BasicTroop,
                heroCharacter.Culture?.EliteBasicTroop
            };

            foreach (TaleWorlds.Core.BasicCharacterObject candidate in candidates)
            {
                if (candidate == null)
                    continue;

                string candidateId = candidate.StringId;
                if (!string.IsNullOrWhiteSpace(candidateId))
                    return candidateId;
            }

            return null;
        }

        private static string TryResolveHeroRoleMissionSafeCharacterId(TaleWorlds.CampaignSystem.CharacterObject heroCharacter)
        {
            if (heroCharacter == null || !heroCharacter.IsHero)
                return null;

            string heroRole = TryGetHeroRole(heroCharacter);
            if (string.IsNullOrWhiteSpace(heroRole))
                return null;

            string cultureId = TryGetCultureId(heroCharacter) ?? heroCharacter.Culture?.StringId;
            string cultureToken = TryMapCultureToMultiplayerToken(cultureId);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return null;

            bool isMounted = heroCharacter.IsMounted;
            bool isRanged = heroCharacter.IsRanged;
            bool hasShield = TryGetCharacterHasShield(heroCharacter);
            bool hasThrown = TryGetCharacterHasThrown(heroCharacter);
            int effectiveTier = ComputeHeroRuntimeTier(heroCharacter, heroRole);

            string troopTemplateId = TryResolveRoleAwareTroopTemplateId(
                cultureToken,
                heroRole,
                isMounted,
                isRanged,
                hasShield,
                hasThrown,
                effectiveTier);

            return TryConvertTroopTemplateToHeroTemplate(troopTemplateId);
        }

        private static int ComputeHeroRuntimeTier(TaleWorlds.CampaignSystem.CharacterObject heroCharacter, string heroRole)
        {
            int tier = heroCharacter?.Tier ?? 0;
            int heroLevel = TryGetHeroLevel(heroCharacter);

            if (heroLevel >= 24)
                tier = Math.Max(tier, 5);
            else if (heroLevel >= 16)
                tier = Math.Max(tier, 4);
            else if (heroLevel >= 8)
                tier = Math.Max(tier, 3);

            if (string.Equals(heroRole, "lord", StringComparison.OrdinalIgnoreCase))
                return Math.Max(tier, 5);

            if (string.Equals(heroRole, "companion", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(heroRole, "wanderer", StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(tier, 4);
            }

            if (string.Equals(heroRole, "player", StringComparison.OrdinalIgnoreCase))
                return Math.Max(tier, 3);

            return Math.Max(tier, 1);
        }

        private static string TryResolveRoleAwareTroopTemplateId(
            string cultureToken,
            string heroRole,
            bool isMounted,
            bool isRanged,
            bool hasShield,
            bool hasThrown,
            int tier)
        {
            if (string.IsNullOrWhiteSpace(cultureToken))
                return null;

            bool isLord = string.Equals(heroRole, "lord", StringComparison.OrdinalIgnoreCase);

            if (isMounted)
            {
                string cavalryTemplateId = (isLord ? "mp_heavy_cavalry_" : "mp_light_cavalry_") + cultureToken + "_troop";
                return NormalizeKnownMissionSafeTemplateId(cavalryTemplateId);
            }

            if (isRanged)
            {
                string rangedTemplateId = ((isLord || tier >= 4) ? "mp_heavy_ranged_" : "mp_light_ranged_") + cultureToken + "_troop";
                return NormalizeKnownMissionSafeTemplateId(rangedTemplateId);
            }

            if (hasThrown)
                return NormalizeKnownMissionSafeTemplateId("mp_skirmisher_" + cultureToken + "_troop");

            if (string.Equals(cultureToken, "empire", StringComparison.OrdinalIgnoreCase))
            {
                if (isLord || tier >= 4)
                    return "mp_coop_heavy_infantry_empire_troop";

                if (hasShield)
                    return "mp_coop_light_infantry_empire_troop";
            }

            if (isLord || tier >= 5)
                return NormalizeKnownMissionSafeTemplateId("mp_shock_infantry_" + cultureToken + "_troop");

            return NormalizeKnownMissionSafeTemplateId(
                (tier >= 4 ? "mp_heavy_infantry_" : "mp_light_infantry_") + cultureToken + "_troop");
        }

        private static string TryConvertTroopTemplateToHeroTemplate(string troopTemplateId)
        {
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return null;

            string normalizedTroopTemplateId = NormalizeKnownMissionSafeTemplateId(troopTemplateId);
            if (string.IsNullOrWhiteSpace(normalizedTroopTemplateId))
                return null;

            if (normalizedTroopTemplateId.EndsWith("_hero", StringComparison.Ordinal))
                return normalizedTroopTemplateId;

            if (!normalizedTroopTemplateId.EndsWith("_troop", StringComparison.Ordinal))
                return normalizedTroopTemplateId;

            string heroTemplateId = normalizedTroopTemplateId.Substring(0, normalizedTroopTemplateId.Length - "_troop".Length) + "_hero";
            if (heroTemplateId.StartsWith("mp_", StringComparison.Ordinal))
                return heroTemplateId;

            try
            {
                BasicCharacterObject heroTemplate = MBObjectManager.Instance.GetObject<BasicCharacterObject>(heroTemplateId);
                if (heroTemplate != null)
                    return heroTemplate.StringId;
            }
            catch
            {
            }

            return normalizedTroopTemplateId;
        }

        private static string TryResolveMultiplayerSafeCharacterId(TaleWorlds.CampaignSystem.CharacterObject character)
        {
            if (character == null)
                return null;

            string cultureId = character.Culture?.StringId;
            string cultureToken = TryMapCultureToMultiplayerToken(cultureId);
            if (string.IsNullOrWhiteSpace(cultureToken))
                return null;

            bool isMounted = character.IsMounted;
            bool isRanged = character.IsRanged;
            bool hasShield = TryGetCharacterHasShield(character);
            bool hasThrown = TryGetCharacterHasThrown(character);
            int tier = character.Tier;

            string coopControlTroopId = TryResolveCoopControlTroopId(cultureToken, isMounted, isRanged, tier);
            if (!string.IsNullOrWhiteSpace(coopControlTroopId))
                return coopControlTroopId;

            if (!isMounted && !isRanged && string.Equals(cultureToken, "empire", StringComparison.Ordinal) && tier < 4)
            {
                if (hasThrown)
                    return "mp_skirmisher_empire_troop";
            }

            var candidates = new List<string>();
            if (isMounted)
            {
                if (isRanged && string.Equals(cultureToken, "khuzait", StringComparison.Ordinal))
                    candidates.Add("mp_horse_archer_khuzait_troop");

                candidates.Add((tier >= 4 ? "mp_heavy_cavalry_" : "mp_light_cavalry_") + cultureToken + "_troop");
                candidates.Add("mp_light_cavalry_" + cultureToken + "_troop");
                candidates.Add("mp_heavy_cavalry_" + cultureToken + "_troop");
            }
            else if (isRanged)
            {
                candidates.Add((tier >= 4 ? "mp_heavy_ranged_" : "mp_light_ranged_") + cultureToken + "_troop");
                candidates.Add("mp_light_ranged_" + cultureToken + "_troop");
                candidates.Add("mp_heavy_ranged_" + cultureToken + "_troop");
                candidates.Add("mp_skirmisher_" + cultureToken + "_troop");
            }
            else
            {
                if (tier >= 5)
                    candidates.Add("mp_shock_infantry_" + cultureToken + "_troop");

                if (tier >= 4)
                    candidates.Add("mp_heavy_infantry_" + cultureToken + "_troop");

                candidates.Add("mp_light_infantry_" + cultureToken + "_troop");
                if (tier < 4)
                    candidates.Add("mp_heavy_infantry_" + cultureToken + "_troop");
                candidates.Add("mp_shock_infantry_" + cultureToken + "_troop");
                candidates.Add("mp_skirmisher_" + cultureToken + "_troop");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                if (!seen.Add(candidate))
                    continue;
                return NormalizeKnownMissionSafeTemplateId(candidate);
            }

            return null;
        }

        private static string NormalizeKnownMissionSafeTemplateId(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return candidate;

            switch (candidate)
            {
                case "mp_light_infantry_empire_troop":
                    return "mp_coop_light_infantry_empire_troop";
                case "mp_heavy_infantry_aserai_troop":
                    return "mp_shock_infantry_aserai_troop";
                default:
                    return candidate;
            }
        }

        private static string TryResolveCoopControlTroopId(string cultureToken, bool isMounted, bool isRanged, int tier)
        {
            if (string.IsNullOrWhiteSpace(cultureToken))
                return null;

            if (isMounted)
                return "mp_coop_light_cavalry_" + cultureToken + "_troop";

            if (string.Equals(cultureToken, "empire", StringComparison.Ordinal) && !isMounted && !isRanged && tier >= 4)
                return "mp_coop_heavy_infantry_empire_troop";

            return null;
        }

        private static string TryMapCultureToMultiplayerToken(string cultureId)
        {
            if (string.IsNullOrWhiteSpace(cultureId))
                return null;

            string normalized = cultureId.Trim().ToLowerInvariant();
            if (normalized.Contains("looter") || normalized.Contains("looters"))
                return "empire";
            if (normalized.Contains("sea_raider"))
                return "sturgia";
            if (normalized.Contains("forest_bandit"))
                return "battania";
            if (normalized.Contains("mountain_bandit"))
                return "vlandia";
            if (normalized.Contains("desert_bandit"))
                return "aserai";
            if (normalized.Contains("steppe_bandit"))
                return "khuzait";
            if (normalized.Contains("bandit"))
                return "empire";
            if (normalized.Contains("empire") || normalized.StartsWith("imperial"))
                return "empire";
            if (normalized.Contains("aserai"))
                return "aserai";
            if (normalized.Contains("battania") || normalized.Contains("battania") || normalized.StartsWith("battanian"))
                return "battania";
            if (normalized.Contains("khuzait"))
                return "khuzait";
            if (normalized.Contains("sturgia") || normalized.StartsWith("sturgian"))
                return "sturgia";
            if (normalized.Contains("vlandia") || normalized.StartsWith("vlandian"))
                return "vlandia";

            return null;
        }

        private static void LogHeroRosterContext(string heroId, List<object> rosterCharacters)
        {
            if (string.IsNullOrWhiteSpace(heroId) || rosterCharacters == null || rosterCharacters.Count == 0)
                return;

            try
            {
                List<string> candidates = new List<string>();
                foreach (object candidate in rosterCharacters)
                {
                    if (candidate == null)
                        continue;

                    string candidateId = TryGetStringId(candidate);
                    bool isHero = TryGetBoolProperty(candidate, "IsHero");
                    int tier = TryGetIntProperty(candidate, "Tier");
                    bool mounted = TryGetBoolProperty(candidate, "IsMounted");
                    candidates.Add((isHero ? "hero:" : "troop:") + candidateId + "/tier=" + tier + "/mounted=" + mounted);
                }

                ModLogger.Info("BattleDetector: hero '" + heroId + "' roster candidates = [" + string.Join(", ", candidates) + "].");
            }
            catch
            {
            }
        }

        private static string TryResolveCultureTroopId(object culture)
        {
            if (culture == null)
                return null;

            string[] candidateProperties =
            {
                "BasicTroop",
                "EliteBasicTroop",
                "MeleeMilitiaTroop",
                "RangedMilitiaTroop",
                "MeleeEliteMilitiaTroop",
                "RangedEliteMilitiaTroop"
            };

            foreach (string propertyName in candidateProperties)
            {
                object troop = TryGetPropertyValue(culture, propertyName);
                string troopId = TryGetStringId(troop);
                if (!string.IsNullOrWhiteSpace(troopId))
                    return troopId;
            }

            return null;
        }

        private static object TryGetPropertyValue(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(instance, null);

                FieldInfo field = instance.GetType().GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field?.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySetMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            try
            {
                PropertyInfo property = instance.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, value, null);
                    return true;
                }

                FieldInfo field = instance.GetType().GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string TryGetStringId(object instance)
        {
            if (instance is TaleWorlds.ObjectSystem.MBObjectBase mbObject)
                return mbObject.StringId;

            object value = TryGetPropertyValue(instance, "StringId");
            return value?.ToString();
        }

        private static bool TryGetBoolProperty(object instance, string propertyName)
        {
            object value = TryGetPropertyValue(instance, propertyName);
            return value is bool b && b;
        }

        private static string TryGetCultureId(object instance)
        {
            if (instance is BasicCharacterObject basicCharacter)
                return basicCharacter.Culture?.StringId;

            object culture = TryGetPropertyValue(instance, "Culture");
            return TryGetStringId(culture);
        }

        private static void ApplyCombatProfileSnapshot(TroopStackInfo troop, object characterObject)
        {
            if (troop == null || characterObject == null)
                return;

            troop.AttributeVigor = TryGetCharacterAttributeValue(characterObject, "Vigor");
            troop.AttributeControl = TryGetCharacterAttributeValue(characterObject, "Control");
            troop.AttributeEndurance = TryGetCharacterAttributeValue(characterObject, "Endurance");
            troop.SkillOneHanded = TryGetCharacterSkillValue(characterObject, "OneHanded");
            troop.SkillTwoHanded = TryGetCharacterSkillValue(characterObject, "TwoHanded");
            troop.SkillPolearm = TryGetCharacterSkillValue(characterObject, "Polearm");
            troop.SkillBow = TryGetCharacterSkillValue(characterObject, "Bow");
            troop.SkillCrossbow = TryGetCharacterSkillValue(characterObject, "Crossbow");
            troop.SkillThrowing = TryGetCharacterSkillValue(characterObject, "Throwing");
            troop.SkillRiding = TryGetCharacterSkillValue(characterObject, "Riding");
            troop.SkillAthletics = TryGetCharacterSkillValue(characterObject, "Athletics");
            BackfillDerivedCombatAttributes(troop);
            troop.BaseHitPoints = TryGetCharacterBaseHitPoints(characterObject);
            troop.PerkIds = TryGetCharacterPerkIds(characterObject);
        }

        private static void BackfillDerivedCombatAttributes(TroopStackInfo troop)
        {
            if (troop == null)
                return;

            if (troop.AttributeVigor <= 0)
            {
                troop.AttributeVigor = DeriveCombatAttributeFromSkills(
                    troop.SkillOneHanded,
                    troop.SkillTwoHanded,
                    troop.SkillPolearm);
            }

            if (troop.AttributeControl <= 0)
            {
                troop.AttributeControl = DeriveCombatAttributeFromSkills(
                    troop.SkillBow,
                    troop.SkillCrossbow,
                    troop.SkillThrowing);
            }

            if (troop.AttributeEndurance <= 0)
            {
                troop.AttributeEndurance = DeriveCombatAttributeFromSkills(
                    troop.SkillRiding,
                    troop.SkillAthletics);
            }
        }

        private static int DeriveCombatAttributeFromSkills(params int[] skillValues)
        {
            if (skillValues == null || skillValues.Length == 0)
                return 0;

            int maxSkill = skillValues.Max();
            if (maxSkill <= 0)
                return 0;

            int derivedValue = 1 + (int)Math.Round(maxSkill / 40f, MidpointRounding.AwayFromZero);
            return Math.Max(1, Math.Min(10, derivedValue));
        }

        private static void ApplyHeroIdentitySnapshot(TroopStackInfo troop, object characterObject)
        {
            if (troop == null || characterObject == null)
                return;

            troop.HeroId = TryGetHeroId(characterObject);
            troop.HeroRole = TryGetHeroRole(characterObject);
            troop.HeroOccupationId = TryGetHeroOccupationId(characterObject);
            troop.HeroClanId = TryGetHeroClanId(characterObject);
            troop.HeroTemplateId = TryGetHeroTemplateId(characterObject);
            troop.HeroBodyProperties = TryGetHeroBodyProperties(characterObject);
            troop.HeroLevel = TryGetHeroLevel(characterObject);
            troop.HeroAge = TryGetHeroAge(characterObject);
            troop.HeroIsFemale = TryGetHeroIsFemale(characterObject);
        }

        private static int TryGetCharacterAttributeValue(object instance, string attributeName)
        {
            object attributeObject = TryGetDefaultCharacterAttributeObject(attributeName);
            if (attributeObject == null)
                return 0;

            foreach (object source in EnumerateCombatProfileSources(instance))
            {
                int value = TryConvertToInt(TryInvokeMethod(source, "GetAttributeValue", attributeObject));
                if (value > 0)
                    return value;
            }

            return 0;
        }

        private static int TryGetCharacterSkillValue(object instance, string skillName)
        {
            object skillObject = TryGetDefaultSkillObject(skillName);
            if (skillObject == null)
                return 0;

            foreach (object source in EnumerateCombatProfileSources(instance))
            {
                int value = TryConvertToInt(TryInvokeMethod(source, "GetSkillValue", skillObject));
                if (value > 0)
                    return value;
            }

            return 0;
        }

        private static int TryGetCharacterBaseHitPoints(object instance)
        {
            foreach (object source in EnumerateCombatProfileSources(instance))
            {
                int maxHitPoints = TryGetIntProperty(source, "MaxHitPoints");
                if (maxHitPoints > 0)
                    return maxHitPoints;

                maxHitPoints = TryConvertToInt(TryInvokeMethod(source, "MaxHitPoints"));
                if (maxHitPoints > 0)
                    return maxHitPoints;

                int hitPoints = TryGetIntProperty(source, "HitPoints");
                if (hitPoints > 0)
                    return hitPoints;
            }

            return 0;
        }

        private static List<string> TryGetCharacterPerkIds(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero == null)
                return new List<string>();

            List<object> perkObjects = GetCachedPerkObjects();
            if (perkObjects.Count == 0)
                return new List<string>();

            var perkIds = new List<string>();
            foreach (object perkObject in perkObjects)
            {
                if (!TryHeroHasPerk(hero, perkObject))
                    continue;

                string perkId = TryGetStringId(perkObject);
                if (!string.IsNullOrWhiteSpace(perkId))
                    perkIds.Add(perkId);
            }

            return perkIds;
        }

        private static List<string> TryGetHeroPerkIdsByPartyRoles(Hero hero, params string[] roleNames)
        {
            if (hero == null || roleNames == null || roleNames.Length == 0)
                return new List<string>();

            List<object> perkObjects = GetCachedPerkObjects();
            if (perkObjects.Count == 0)
                return new List<string>();

            var perkIds = new List<string>();
            foreach (object perkObject in perkObjects)
            {
                if (!PerkMatchesAnyPartyRole(perkObject, roleNames) || !TryHeroHasPerk(hero, perkObject))
                    continue;

                string perkId = TryGetStringId(perkObject);
                if (!string.IsNullOrWhiteSpace(perkId))
                    perkIds.Add(perkId);
            }

            return perkIds;
        }

        private static bool PerkMatchesAnyPartyRole(object perkObject, params string[] roleNames)
        {
            if (perkObject == null || roleNames == null || roleNames.Length == 0)
                return false;

            string primaryRole = TryGetPropertyValue(perkObject, "PrimaryRole")?.ToString();
            string secondaryRole = TryGetPropertyValue(perkObject, "SecondaryRole")?.ToString();
            for (int i = 0; i < roleNames.Length; i++)
            {
                string roleName = roleNames[i];
                if (string.IsNullOrWhiteSpace(roleName))
                    continue;

                if (string.Equals(primaryRole, roleName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(secondaryRole, roleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<object> GetCachedPerkObjects()
        {
            if (_cachedPerkObjects != null)
                return _cachedPerkObjects;

            var perkObjects = new List<object>();
            try
            {
                Type perkObjectType = TryGetKnownGameType("TaleWorlds.CampaignSystem.CharacterDevelopment.PerkObject");
                if (perkObjectType != null && MBObjectManager.Instance != null)
                {
                    object allPerks = TryGetStaticPropertyValue(perkObjectType, "All");
                    if (allPerks is System.Collections.IEnumerable allPerksEnumerable)
                    {
                        foreach (object perkObject in allPerksEnumerable)
                        {
                            if (perkObject != null)
                                perkObjects.Add(perkObject);
                        }
                    }

                    MethodInfo getObjectTypeListMethod = MBObjectManager.Instance.GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(method =>
                            string.Equals(method.Name, "GetObjectTypeList", StringComparison.Ordinal) &&
                            method.IsGenericMethodDefinition &&
                            method.GetParameters().Length == 0);
                    if (perkObjects.Count == 0 && getObjectTypeListMethod != null)
                    {
                        object result = getObjectTypeListMethod.MakeGenericMethod(perkObjectType).Invoke(MBObjectManager.Instance, null);
                        if (result is System.Collections.IEnumerable enumerable)
                        {
                            foreach (object perkObject in enumerable)
                            {
                                if (perkObject != null)
                                    perkObjects.Add(perkObject);
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            _cachedPerkObjects = perkObjects;
            return _cachedPerkObjects;
        }

        private static bool TryHeroHasPerk(Hero hero, object perkObject)
        {
            if (hero == null || perkObject == null)
                return false;

            foreach (object source in new object[]
                     {
                         hero,
                         TryGetPropertyValue(hero, "HeroDeveloper") ?? TryGetPropertyValue(hero, "Developer"),
                         hero.CharacterObject
                     })
            {
                if (source == null)
                    continue;

                object result = TryInvokeMethod(source, "GetPerkValue", perkObject);
                if (result is bool hasPerk)
                    return hasPerk;
            }

            return false;
        }

        private static IEnumerable<object> EnumerateCombatProfileSources(object instance)
        {
            if (instance == null)
                yield break;

            var yielded = new HashSet<object>();
            foreach (object source in new object[]
                     {
                         instance,
                         TryGetPropertyValue(instance, "CharacterObject"),
                         TryGetPropertyValue(instance, "OriginalCharacter")
                     })
            {
                if (source != null && yielded.Add(source))
                    yield return source;
            }

            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
            {
                if (yielded.Add(hero))
                    yield return hero;

                if (hero.CharacterObject != null && yielded.Add(hero.CharacterObject))
                    yield return hero.CharacterObject;

                if (hero.CharacterObject?.OriginalCharacter != null && yielded.Add(hero.CharacterObject.OriginalCharacter))
                    yield return hero.CharacterObject.OriginalCharacter;
            }
        }

        private static object TryGetDefaultSkillObject(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                return null;

            if (CachedDefaultSkillObjects.TryGetValue(skillName, out object cached))
                return cached;

            Type defaultSkillsType = TryGetKnownGameType(
                "TaleWorlds.Core.DefaultSkills",
                "TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultSkills");
            object skillObject = TryGetStaticPropertyValue(defaultSkillsType, skillName);
            CachedDefaultSkillObjects[skillName] = skillObject;
            return skillObject;
        }

        private static object TryGetDefaultCharacterAttributeObject(string attributeName)
        {
            if (string.IsNullOrWhiteSpace(attributeName))
                return null;

            if (CachedDefaultCharacterAttributeObjects.TryGetValue(attributeName, out object cached))
                return cached;

            Type defaultCharacterAttributesType = TryGetKnownGameType(
                "TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultCharacterAttributes",
                "TaleWorlds.Core.DefaultCharacterAttributes");
            object attributeObject = TryGetStaticPropertyValue(defaultCharacterAttributesType, attributeName);
            CachedDefaultCharacterAttributeObjects[attributeName] = attributeObject;
            return attributeObject;
        }

        private static Hero TryResolveHeroObject(object instance)
        {
            if (instance is Hero hero)
                return hero;

            if (instance is CharacterObject characterObject)
                return characterObject.HeroObject;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            return heroObject as Hero;
        }

        private static string TryGetHeroId(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return hero.StringId;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            return TryGetStringId(heroObject);
        }

        private static string TryGetHeroRole(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero == null)
                return null;

            bool isMainHero = false;
            try
            {
                isMainHero = Hero.MainHero != null && ReferenceEquals(hero, Hero.MainHero);
            }
            catch
            {
                isMainHero = TryGetBoolProperty(hero, "IsMainHero");
            }

            if (isMainHero || hero.IsHumanPlayerCharacter)
                return "player";

            if (hero.IsPlayerCompanion || hero.CompanionOf != null)
                return "companion";

            if (hero.IsLord)
                return "lord";

            if (hero.IsWanderer)
                return "wanderer";

            string occupationId = NormalizeHeroIdentityToken(hero.Occupation.ToString());
            return string.IsNullOrWhiteSpace(occupationId)
                ? "hero"
                : occupationId;
        }

        private static string TryGetHeroOccupationId(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return NormalizeHeroIdentityToken(hero.Occupation.ToString());

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            object occupation = TryGetPropertyValue(heroObject, "Occupation");
            return NormalizeHeroIdentityToken(occupation?.ToString());
        }

        private static string TryGetHeroClanId(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return hero.Clan?.StringId;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            object clan = TryGetPropertyValue(heroObject, "Clan");
            return TryGetStringId(clan);
        }

        private static string TryGetHeroTemplateId(object instance)
        {
            if (instance is CharacterObject characterObject)
            {
                string templateId = TryGetStringId(TryGetPropertyValue(characterObject, "Template"));
                if (!string.IsNullOrWhiteSpace(templateId))
                    return templateId;

                string originalCharacterId = characterObject.OriginalCharacter?.StringId;
                if (!string.IsNullOrWhiteSpace(originalCharacterId))
                    return originalCharacterId;

                if (!string.IsNullOrWhiteSpace(characterObject.StringId))
                    return characterObject.StringId;
            }

            Hero hero = TryResolveHeroObject(instance);
            if (hero?.CharacterObject != null)
            {
                string heroTemplateId = TryGetStringId(TryGetPropertyValue(hero.CharacterObject, "Template"));
                if (!string.IsNullOrWhiteSpace(heroTemplateId))
                    return heroTemplateId;

                if (hero.CharacterObject.OriginalCharacter != null)
                    return hero.CharacterObject.OriginalCharacter.StringId;

                if (!string.IsNullOrWhiteSpace(hero.CharacterObject.StringId))
                    return hero.CharacterObject.StringId;
            }

            object originalCharacter = TryGetPropertyValue(instance, "OriginalCharacter");
            string originalCharacterIdFallback = TryGetStringId(originalCharacter);
            if (!string.IsNullOrWhiteSpace(originalCharacterIdFallback))
                return originalCharacterIdFallback;

            string templateIdFallback = TryGetStringId(TryGetPropertyValue(instance, "Template"));
            if (!string.IsNullOrWhiteSpace(templateIdFallback))
                return templateIdFallback;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            object heroCharacter = TryGetPropertyValue(heroObject, "CharacterObject");
            string heroCharacterTemplateId = TryGetStringId(TryGetPropertyValue(heroCharacter, "Template"));
            if (!string.IsNullOrWhiteSpace(heroCharacterTemplateId))
                return heroCharacterTemplateId;

            object heroOriginalCharacter = TryGetPropertyValue(heroCharacter, "OriginalCharacter");
            string heroOriginalCharacterId = TryGetStringId(heroOriginalCharacter);
            if (!string.IsNullOrWhiteSpace(heroOriginalCharacterId))
                return heroOriginalCharacterId;

            string heroCharacterId = TryGetStringId(heroCharacter);
            if (!string.IsNullOrWhiteSpace(heroCharacterId))
                return heroCharacterId;

            return TryGetHeroId(instance);
        }

        private static int TryGetHeroLevel(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return TryGetIntProperty(hero, "Level");

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            return TryGetIntProperty(heroObject, "Level");
        }

        private static string TryGetHeroBodyProperties(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
            {
                object heroBodyProperties = TryGetPropertyValue(hero, "BodyProperties");
                if (heroBodyProperties is BodyProperties bodyProperties)
                    return bodyProperties.ToString();
            }

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            object reflectedBodyProperties = TryGetPropertyValue(heroObject, "BodyProperties");
            return reflectedBodyProperties?.ToString();
        }

        private static float TryGetHeroAge(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return TryGetFloatProperty(hero, "Age");

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            return TryGetFloatProperty(heroObject, "Age");
        }

        private static bool TryGetHeroIsFemale(object instance)
        {
            Hero hero = TryResolveHeroObject(instance);
            if (hero != null)
                return TryGetBoolProperty(hero, "IsFemale") || TryGetBoolProperty(hero.CharacterObject, "IsFemale");

            if (instance is CharacterObject characterObject)
                return characterObject.IsFemale;

            object heroObject = TryGetPropertyValue(instance, "HeroObject") ?? TryGetPropertyValue(instance, "Hero");
            if (TryGetBoolProperty(heroObject, "IsFemale"))
                return true;

            object heroCharacter = TryGetPropertyValue(heroObject, "CharacterObject");
            return TryGetBoolProperty(heroCharacter, "IsFemale");
        }

        private static string NormalizeHeroIdentityToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = new List<char>(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsWhiteSpace(current) || current == '-')
                {
                    if (normalized.Count > 0 && normalized[normalized.Count - 1] != '_')
                        normalized.Add('_');
                    continue;
                }

                if (char.IsUpper(current) && i > 0)
                {
                    char previous = value[i - 1];
                    char next = i + 1 < value.Length ? value[i + 1] : '\0';
                    if (char.IsLower(previous) || (next != '\0' && char.IsLower(next)))
                        normalized.Add('_');
                }

                normalized.Add(char.ToLowerInvariant(current));
            }

            return new string(normalized.ToArray()).Trim('_');
        }

        private static void ApplyCombatEquipmentSnapshot(TroopStackInfo troop, object characterObject)
        {
            if (troop == null || characterObject == null)
                return;

            object equipment = TryResolvePrimaryCombatEquipment(characterObject);
            if (equipment == null)
                return;

            ApplyCombatEquipmentSnapshot(troop, BuildCombatEquipmentVariantSnapshot(equipment));
        }

        private static void ApplyCombatEquipmentSnapshot(TroopStackInfo troop, CombatEquipmentVariantSnapshot equipment)
        {
            if (troop == null || equipment == null)
                return;

            troop.CombatItem0Id = equipment.CombatItem0Id;
            troop.CombatItem1Id = equipment.CombatItem1Id;
            troop.CombatItem2Id = equipment.CombatItem2Id;
            troop.CombatItem3Id = equipment.CombatItem3Id;
            troop.CombatHeadId = equipment.CombatHeadId;
            troop.CombatBodyId = equipment.CombatBodyId;
            troop.CombatLegId = equipment.CombatLegId;
            troop.CombatGlovesId = equipment.CombatGlovesId;
            troop.CombatCapeId = equipment.CombatCapeId;
            troop.CombatHorseId = equipment.CombatHorseId;
            troop.CombatHorseHarnessId = equipment.CombatHorseHarnessId;
        }

        private static object TryResolvePrimaryCombatEquipment(object instance)
        {
            if (instance == null)
                return null;

            foreach (string propertyName in new[] { "FirstBattleEquipment", "BattleEquipment", "SecondBattleEquipment", "Equipment" })
            {
                object equipment = TryGetPropertyValue(instance, propertyName);
                if (equipment != null && !TryGetBoolProperty(equipment, "IsCivilian"))
                    return equipment;
            }

            foreach (object equipment in EnumerateCharacterEquipments(instance))
            {
                if (equipment != null && !TryGetBoolProperty(equipment, "IsCivilian"))
                    return equipment;
            }

            foreach (object equipment in EnumerateCharacterEquipments(instance))
            {
                if (equipment != null)
                    return equipment;
            }

            return null;
        }

        private static object TryResolveRandomBattleEquipment(object instance)
        {
            if (instance == null)
                return null;

            foreach (string propertyName in new[] { "RandomBattleEquipment", "GetRandomEquipment" })
            {
                object equipment = TryGetPropertyValue(instance, propertyName);
                if (equipment != null && !TryGetBoolProperty(equipment, "IsCivilian"))
                    return equipment;
            }

            object invokedEquipment = TryInvokeMethod(instance, "GetRandomEquipment");
            if (invokedEquipment != null && !TryGetBoolProperty(invokedEquipment, "IsCivilian"))
                return invokedEquipment;

            return null;
        }

        private static string TryGetEquipmentSlotItemId(object equipment, EquipmentIndex slot)
        {
            if (equipment == null)
                return null;

            try
            {
                MethodInfo getEquipmentFromSlot = equipment.GetType().GetMethod(
                    "GetEquipmentFromSlot",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(EquipmentIndex) },
                    null);

                if (getEquipmentFromSlot != null)
                {
                    object equipmentElement = getEquipmentFromSlot.Invoke(equipment, new object[] { slot });
                    string itemId = TryGetEquipmentElementItemId(equipmentElement);
                    if (!string.IsNullOrWhiteSpace(itemId))
                        return itemId;
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo getEquipmentElement = equipment.GetType().GetMethod(
                    "GetEquipmentElement",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(EquipmentIndex) },
                    null);

                if (getEquipmentElement != null)
                {
                    object equipmentElement = getEquipmentElement.Invoke(equipment, new object[] { slot });
                    string itemId = TryGetEquipmentElementItemId(equipmentElement);
                    if (!string.IsNullOrWhiteSpace(itemId))
                        return itemId;
                }
            }
            catch
            {
            }

            try
            {
                PropertyInfo indexer = equipment.GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(property =>
                    {
                        ParameterInfo[] parameters = property.GetIndexParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(EquipmentIndex);
                    });

                if (indexer != null)
                {
                    object equipmentElement = indexer.GetValue(equipment, new object[] { slot });
                    string itemId = TryGetEquipmentElementItemId(equipmentElement);
                    if (!string.IsNullOrWhiteSpace(itemId))
                        return itemId;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string TryGetEquipmentElementItemId(object equipmentElement)
        {
            if (equipmentElement == null)
                return null;

            object item = TryGetPropertyValue(equipmentElement, "Item") ?? TryGetPropertyValue(equipmentElement, "ItemObject");
            return TryGetStringId(item);
        }

        private static string FormatCombatEquipmentSummary(TroopStackInfo troop)
        {
            if (troop == null)
                return string.Empty;

            List<string> parts = new List<string>();
            AddCombatEquipmentSummaryPart(parts, "Item0", troop.CombatItem0Id);
            AddCombatEquipmentSummaryPart(parts, "Item1", troop.CombatItem1Id);
            AddCombatEquipmentSummaryPart(parts, "Item2", troop.CombatItem2Id);
            AddCombatEquipmentSummaryPart(parts, "Item3", troop.CombatItem3Id);
            AddCombatEquipmentSummaryPart(parts, "Head", troop.CombatHeadId);
            AddCombatEquipmentSummaryPart(parts, "Body", troop.CombatBodyId);
            AddCombatEquipmentSummaryPart(parts, "Leg", troop.CombatLegId);
            AddCombatEquipmentSummaryPart(parts, "Gloves", troop.CombatGlovesId);
            AddCombatEquipmentSummaryPart(parts, "Cape", troop.CombatCapeId);
            AddCombatEquipmentSummaryPart(parts, "Horse", troop.CombatHorseId);
            AddCombatEquipmentSummaryPart(parts, "HorseHarness", troop.CombatHorseHarnessId);

            return parts.Count == 0
                ? " eq=[]"
                : " eq=[" + string.Join(", ", parts) + "]";
        }

        private static string FormatHeroIdentitySummary(TroopStackInfo troop)
        {
            if (troop == null)
                return string.Empty;

            bool hasHeroIdentity =
                !string.IsNullOrWhiteSpace(troop.HeroId) ||
                !string.IsNullOrWhiteSpace(troop.HeroRole) ||
                !string.IsNullOrWhiteSpace(troop.HeroOccupationId) ||
                !string.IsNullOrWhiteSpace(troop.HeroClanId);

            if (!hasHeroIdentity)
                return troop.IsHero ? " hero_role=hero" : string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(troop.HeroId))
                parts.Add("hero_id=" + troop.HeroId);
            if (!string.IsNullOrWhiteSpace(troop.HeroRole))
                parts.Add("hero_role=" + troop.HeroRole);
            if (!string.IsNullOrWhiteSpace(troop.HeroOccupationId))
                parts.Add("occupation=" + troop.HeroOccupationId);
            if (!string.IsNullOrWhiteSpace(troop.HeroClanId))
                parts.Add("clan=" + troop.HeroClanId);
            if (!string.IsNullOrWhiteSpace(troop.HeroTemplateId))
                parts.Add("template=" + troop.HeroTemplateId);
            if (!string.IsNullOrWhiteSpace(troop.HeroBodyProperties))
                parts.Add("body=present");
            if (troop.HeroLevel > 0)
                parts.Add("level=" + troop.HeroLevel);
            if (troop.HeroAge > 0.01f)
                parts.Add("age=" + troop.HeroAge.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            parts.Add("female=" + troop.HeroIsFemale);

            return parts.Count == 0
                ? string.Empty
                : " " + string.Join(" ", parts);
        }

        private static string FormatCombatProfileSummary(TroopStackInfo troop)
        {
            if (troop == null)
                return string.Empty;

            bool hasCombatProfile =
                troop.AttributeVigor > 0 ||
                troop.AttributeControl > 0 ||
                troop.AttributeEndurance > 0 ||
                troop.SkillOneHanded > 0 ||
                troop.SkillTwoHanded > 0 ||
                troop.SkillPolearm > 0 ||
                troop.SkillBow > 0 ||
                troop.SkillCrossbow > 0 ||
                troop.SkillThrowing > 0 ||
                troop.SkillRiding > 0 ||
                troop.SkillAthletics > 0 ||
                troop.BaseHitPoints > 0 ||
                (troop.PerkIds != null && troop.PerkIds.Count > 0);

            if (!hasCombatProfile)
                return string.Empty;

            var parts = new List<string>
            {
                "attr=" + troop.AttributeVigor + "/" + troop.AttributeControl + "/" + troop.AttributeEndurance,
                "skills=" + string.Join("/",
                    troop.SkillOneHanded,
                    troop.SkillTwoHanded,
                    troop.SkillPolearm,
                    troop.SkillBow,
                    troop.SkillCrossbow,
                    troop.SkillThrowing,
                    troop.SkillRiding,
                    troop.SkillAthletics),
                "hp=" + troop.BaseHitPoints
            };

            if (troop.PerkIds != null && troop.PerkIds.Count > 0)
            {
                IEnumerable<string> samplePerks = troop.PerkIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Take(3);
                string perkSample = string.Join(",", samplePerks);
                if (troop.PerkIds.Count > 3)
                    perkSample += ",...";

                parts.Add("perks=" + troop.PerkIds.Count + (string.IsNullOrWhiteSpace(perkSample) ? string.Empty : "[" + perkSample + "]"));
            }

            return " profile=[" + string.Join(" ", parts) + "]";
        }

        private static void AddCombatEquipmentSummaryPart(List<string> parts, string label, string itemId)
        {
            if (parts == null || string.IsNullOrWhiteSpace(itemId))
                return;

            parts.Add(label + "=" + itemId);
        }

        private static bool TryGetCharacterIsRanged(object instance)
        {
            if (instance is BasicCharacterObject basicCharacter)
                return basicCharacter.IsRanged;

            return TryGetBoolProperty(instance, "IsRanged");
        }

        private static bool TryGetCharacterHasShield(object instance)
        {
            return TryCharacterHasWeaponClass(instance, WeaponClass.SmallShield) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.LargeShield);
        }

        private static bool TryGetCharacterHasThrown(object instance)
        {
            return TryCharacterHasWeaponClass(instance, WeaponClass.Javelin) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.ThrowingAxe) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.ThrowingKnife) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.Stone) ||
                   TryCharacterHasWeaponClass(instance, WeaponClass.SlingStone);
        }

        private static bool TryCharacterHasWeaponClass(object instance, WeaponClass weaponClass)
        {
            foreach (object equipment in EnumerateCharacterEquipments(instance))
            {
                if (equipment == null)
                    continue;

                try
                {
                    MethodInfo hasWeaponOfClass = equipment.GetType().GetMethod(
                        "HasWeaponOfClass",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(WeaponClass) },
                        null);

                    if (hasWeaponOfClass == null)
                        continue;

                    object result = hasWeaponOfClass.Invoke(equipment, new object[] { weaponClass });
                    if (result is bool hasWeapon && hasWeapon)
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static IEnumerable<object> EnumerateCharacterEquipments(object instance)
        {
            if (instance == null)
                yield break;

            var yielded = new HashSet<object>();
            foreach (string propertyName in new[] { "Equipment", "BattleEquipment", "FirstBattleEquipment", "SecondBattleEquipment", "CivilianEquipment" })
            {
                object equipment = TryGetPropertyValue(instance, propertyName);
                if (equipment != null && yielded.Add(equipment))
                    yield return equipment;
            }

            object allEquipments = TryGetPropertyValue(instance, "AllEquipments");
            if (allEquipments is System.Collections.IEnumerable enumerable && !(allEquipments is string))
            {
                foreach (object equipment in enumerable)
                {
                    if (equipment != null && yielded.Add(equipment))
                        yield return equipment;
                }
            }
        }

        private static int TryGetIntProperty(object instance, string propertyName)
        {
            object value = TryGetPropertyValue(instance, propertyName);
            return value is int i ? i : 0;
        }

        private static float TryGetFloatProperty(object instance, string propertyName)
        {
            object value = TryGetPropertyValue(instance, propertyName);
            if (value is float f)
                return f;
            if (value is double d)
                return (float)d;
            if (value is decimal m)
                return (float)m;
            if (value is int i)
                return i;
            if (value is long l)
                return l;

            try
            {
                return value != null ? Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture) : 0f;
            }
            catch
            {
                return 0f;
            }
        }

    } // Завершуємо блок класу
} // Завершуємо блок простору імен

