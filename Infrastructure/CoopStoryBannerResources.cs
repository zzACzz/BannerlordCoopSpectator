using TaleWorlds.Core;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopStoryBannerResources
    {
        // Called after native default objects and before Items XML is loaded.
        // Mirrors StoryModeBannerEffects' exact zero-valued fragment effect.
        internal static void EnsureForMultiplayer(Game game)
        {
            if (game?.GameType?.GetType().FullName != "TaleWorlds.MountAndBlade.MultiplayerGame") return;
            var objects = game.ObjectManager;
            if (objects.GetObject<BannerEffect>("dragon_banner_effect") != null) return;
            var effect = objects.RegisterPresumedObject(new BannerEffect("dragon_banner_effect"));
            effect.Initialize("{=!}Not Implemented.", "{=!}Not Implemented.", 0f, 0f, 0f, (EffectIncrementType)(-1));
        }
    }
}
