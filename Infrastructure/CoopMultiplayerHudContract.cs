using System;

namespace CoopSpectator.Infrastructure
{
    public static class CoopMultiplayerHudContract
    {
        public const string NativeHudMovieName = "HUDExtension";

        public static bool ShouldSuppressNativeTeamBanners(
            string movieName,
            bool isNativeHudViewModel,
            bool isCoopBattlePowerMission)
        {
            return isNativeHudViewModel &&
                isCoopBattlePowerMission &&
                string.Equals(
                    movieName,
                    NativeHudMovieName,
                    StringComparison.Ordinal);
        }

        public static bool IsExpectedNativeTeamBannerLayout(
            bool headerIsListPanel,
            int headerChildCount,
            int allyBannerChildCount,
            float allyBannerWidth,
            float allyBannerHeight,
            int enemyBannerChildCount,
            float enemyBannerWidth,
            float enemyBannerHeight)
        {
            return headerIsListPanel &&
                headerChildCount == 5 &&
                allyBannerChildCount == 1 &&
                enemyBannerChildCount == 1 &&
                IsExpectedBannerDimension(allyBannerWidth) &&
                IsExpectedBannerDimension(allyBannerHeight) &&
                IsExpectedBannerDimension(enemyBannerWidth) &&
                IsExpectedBannerDimension(enemyBannerHeight);
        }

        private static bool IsExpectedBannerDimension(float value)
        {
            return !float.IsNaN(value) &&
                !float.IsInfinity(value) &&
                Math.Abs(value - 50f) <= 0.01f;
        }
    }
}
