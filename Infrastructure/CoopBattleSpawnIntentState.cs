using System;

namespace CoopSpectator.Infrastructure
{
    internal sealed class CoopBattleSpawnIntentSnapshot
    {
        public bool IsRequested { get; set; }
        public string Source { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    internal static class CoopBattleSpawnIntentState
    {
        private static readonly CoopBattleSpawnIntentSnapshot _current = new CoopBattleSpawnIntentSnapshot
        {
            IsRequested = false,
            Source = "uninitialized",
            UpdatedUtc = DateTime.MinValue
        };

        public static void Reset()
        {
            _current.IsRequested = false;
            _current.Source = "reset";
            _current.UpdatedUtc = DateTime.UtcNow;
        }

        public static CoopBattleSpawnIntentSnapshot GetCurrent()
        {
            return new CoopBattleSpawnIntentSnapshot
            {
                IsRequested = _current.IsRequested,
                Source = _current.Source,
                UpdatedUtc = _current.UpdatedUtc
            };
        }

        public static void RequestSpawn(string source)
        {
            _current.IsRequested = true;
            _current.Source = source ?? "unknown";
            _current.UpdatedUtc = DateTime.UtcNow;

            ModLogger.Info(
                "CoopBattleSpawnIntentState: spawn requested. " +
                "Source=" + _current.Source);
        }

        public static void Clear(string source)
        {
            if (!_current.IsRequested)
                return;

            _current.IsRequested = false;
            _current.Source = source ?? "unknown";
            _current.UpdatedUtc = DateTime.UtcNow;

            ModLogger.Info(
                "CoopBattleSpawnIntentState: spawn intent cleared. " +
                "Source=" + _current.Source);
        }
    }
}
