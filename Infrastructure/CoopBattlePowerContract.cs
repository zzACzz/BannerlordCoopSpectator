using System;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopBattlePowerState
    {
        public string BattleInstanceId { get; set; } = string.Empty;

        public int Revision { get; set; }

        public bool IsAvailable { get; set; }

        public int InitialAttackerPower { get; set; }

        public int CurrentAttackerPower { get; set; }

        public int InitialDefenderPower { get; set; }

        public int CurrentDefenderPower { get; set; }

        public CoopBattlePowerState Clone()
        {
            return new CoopBattlePowerState
            {
                BattleInstanceId = BattleInstanceId,
                Revision = Revision,
                IsAvailable = IsAvailable,
                InitialAttackerPower = InitialAttackerPower,
                CurrentAttackerPower = CurrentAttackerPower,
                InitialDefenderPower = InitialDefenderPower,
                CurrentDefenderPower = CurrentDefenderPower
            };
        }
    }

    public static class CoopBattlePowerContract
    {
        public const int ProtocolVersion = 1;
        public const int PowerScale = 1000;
        public const int MaximumBattleInstanceIdCharacters = 96;

        public static int CalculateUnitPower(
            int tier,
            bool isHero,
            int heroLevel,
            bool isMounted)
        {
            int effectiveTier = isHero
                ? Math.Max(0, heroLevel) / 4 + 1
                : Math.Max(0, tier);
            double roleMultiplier = isHero
                ? 1.5d
                : isMounted
                    ? 1.2d
                    : 1d;
            double power =
                (2d + effectiveTier) *
                (8d + effectiveTier) *
                0.02d *
                roleMultiplier;
            return QuantizePower(power);
        }

        public static int CalculateAvailableStackPower(
            int count,
            int woundedCount,
            int tier,
            bool isHero,
            int heroLevel,
            bool isMounted)
        {
            int availableCount = Math.Max(
                0,
                Math.Max(0, count) - Math.Max(0, woundedCount));
            return MultiplyClamped(
                CalculateUnitPower(tier, isHero, heroLevel, isMounted),
                availableCount);
        }

        public static int QuantizePower(double power)
        {
            if (double.IsNaN(power) || power <= 0d)
                return 0;
            if (double.IsPositiveInfinity(power))
                return int.MaxValue;

            double scaled = Math.Round(
                power * PowerScale,
                MidpointRounding.AwayFromZero);
            if (scaled >= int.MaxValue)
                return int.MaxValue;
            return (int)scaled;
        }

        public static int SubtractClamped(int currentPower, int removedPower)
        {
            return Math.Max(0, Math.Max(0, currentPower) - Math.Max(0, removedPower));
        }

        public static int AddClamped(int currentPower, int addedPower)
        {
            long sum = (long)Math.Max(0, currentPower) + Math.Max(0, addedPower);
            return sum >= int.MaxValue ? int.MaxValue : (int)sum;
        }

        public static bool CanRender(CoopBattlePowerState state)
        {
            return state?.IsAvailable == true &&
                   state.InitialAttackerPower > 0 &&
                   state.InitialDefenderPower > 0 &&
                   (state.CurrentAttackerPower > 0 ||
                    state.CurrentDefenderPower > 0);
        }

        public static string BoundBattleInstanceId(string value)
        {
            string normalized = value ?? string.Empty;
            return normalized.Length <= MaximumBattleInstanceIdCharacters
                ? normalized
                : normalized.Substring(0, MaximumBattleInstanceIdCharacters);
        }

        private static int MultiplyClamped(int value, int multiplier)
        {
            long product = (long)Math.Max(0, value) * Math.Max(0, multiplier);
            return product >= int.MaxValue ? int.MaxValue : (int)product;
        }
    }
}
