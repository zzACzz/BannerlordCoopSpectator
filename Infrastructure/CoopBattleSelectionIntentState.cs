using System;
using TaleWorlds.Core;

namespace CoopSpectator.Infrastructure
{
    internal sealed class CoopBattleSelectionIntentSnapshot
    {
        public BattleSideEnum Side { get; set; }
        public string TroopOrEntryId { get; set; }
        public string Source { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    internal static class CoopBattleSelectionIntentState
    {
        private static readonly CoopBattleSelectionIntentSnapshot _current = new CoopBattleSelectionIntentSnapshot
        {
            Side = BattleSideEnum.None,
            TroopOrEntryId = null,
            Source = "uninitialized",
            UpdatedUtc = DateTime.MinValue
        };

        public static void Reset()
        {
            _current.Side = BattleSideEnum.None;
            _current.TroopOrEntryId = null;
            _current.Source = "reset";
            _current.UpdatedUtc = DateTime.UtcNow;
        }

        public static CoopBattleSelectionIntentSnapshot GetCurrent()
        {
            return new CoopBattleSelectionIntentSnapshot
            {
                Side = _current.Side,
                TroopOrEntryId = _current.TroopOrEntryId,
                Source = _current.Source,
                UpdatedUtc = _current.UpdatedUtc
            };
        }

        public static void UpdateSide(BattleSideEnum side, string source)
        {
            _current.Side = side;
            _current.Source = source;
            _current.UpdatedUtc = DateTime.UtcNow;

            ModLogger.Info(
                "CoopBattleSelectionIntentState: side updated. " +
                "Side=" + side +
                " Source=" + source);
        }

        public static void UpdateTroopOrEntry(string troopOrEntryId, string source)
        {
            _current.TroopOrEntryId = string.IsNullOrWhiteSpace(troopOrEntryId) ? null : troopOrEntryId.Trim();
            _current.Source = source;
            _current.UpdatedUtc = DateTime.UtcNow;

            ModLogger.Info(
                "CoopBattleSelectionIntentState: troop updated. " +
                "TroopOrEntryId=" + (_current.TroopOrEntryId ?? "null") +
                " Source=" + source);
        }
    }
}
