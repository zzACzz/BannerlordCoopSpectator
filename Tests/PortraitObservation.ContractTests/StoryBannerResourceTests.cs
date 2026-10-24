using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;

internal static class StoryBannerResourceTests
{
    internal static void Run()
    {
        CoopStoryBannerResources.EnsureForMultiplayer(null);
        var campaign = new Game { GameType = new object() };
        CoopStoryBannerResources.EnsureForMultiplayer(campaign);
        if (campaign.ObjectManager.Count != 0) throw new Exception("Campaign effect registration");
        var game = new Game { GameType = new TaleWorlds.MountAndBlade.MultiplayerGame() };
        CoopStoryBannerResources.EnsureForMultiplayer(game);
        CoopStoryBannerResources.EnsureForMultiplayer(game);
        var effect = game.ObjectManager.GetObject<BannerEffect>("dragon_banner_effect");
        if (game.ObjectManager.Count != 1 || effect == null || effect.Name != "{=!}Not Implemented." ||
            effect.Description != effect.Name || effect.Level1 != 0 || effect.Level2 != 0 || effect.Level3 != 0 ||
            (int)effect.Increment != -1) throw new Exception("Exact StoryMode zero effect or repeated initialization");
        effect.Level1 = 2; // An existing object owned by another module is preserved.
        CoopStoryBannerResources.EnsureForMultiplayer(game);
        if (effect.Level1 != 2) throw new Exception("Existing effect overwritten");
        var secondGame = new Game { GameType = new TaleWorlds.MountAndBlade.MultiplayerGame() };
        CoopStoryBannerResources.EnsureForMultiplayer(secondGame);
        if (ReferenceEquals(effect, secondGame.ObjectManager.GetObject<BannerEffect>("dragon_banner_effect")))
            throw new Exception("Effect retained across game instances");
        Console.WriteLine("Story banner zero effect: campaign isolation, per-game initialization and existing-object preservation passed (managed substitute).");
    }
}
namespace TaleWorlds.Core
{
    public sealed class Game
    {
        public object GameType;
        public TaleWorlds.ObjectSystem.MBObjectManager ObjectManager = new TaleWorlds.ObjectSystem.MBObjectManager();
    }
    public enum EffectIncrementType { None = -1 }
    public sealed class BannerEffect : TaleWorlds.ObjectSystem.MBObjectBase
    {
        public string Name, Description;
        public float Level1, Level2, Level3;
        public EffectIncrementType Increment;
        public BannerEffect(string id) { StringId = id; }
        public void Initialize(string name, string description, float a, float b, float c, EffectIncrementType increment)
        { Name = name; Description = description; Level1 = a; Level2 = b; Level3 = c; Increment = increment; }
    }
}
namespace TaleWorlds.ObjectSystem
{
    public sealed class MBObjectManager
    {
        private readonly Dictionary<string, MBObjectBase> _objects = new Dictionary<string, MBObjectBase>();
        public int Count => _objects.Count;
        public T GetObject<T>(string id) where T : MBObjectBase => _objects.TryGetValue(id, out var value) ? value as T : null;
        public T RegisterPresumedObject<T>(T item) where T : MBObjectBase { _objects.Add(item.StringId, item); return item; }
    }
}
namespace TaleWorlds.MountAndBlade { public sealed class MultiplayerGame { } }
