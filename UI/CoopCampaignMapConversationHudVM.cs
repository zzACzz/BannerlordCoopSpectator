using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.UI
{
    // Fixed stock providers only. No remote provider name/size/path is accepted.
    public sealed class CoopConversationHudImageVM : ViewModel
    {
        private static int _portraitFailureDiagnostics;
        public CoopConversationHudImageVM(string id, bool banner, bool nineGrid = false, int portraitPose = -1)
        {
            TextureProviderName = banner ? "BannerImageTextureProvider" : "CharacterImageTextureProvider";
            AdditionalArgs = banner && nineGrid ? "ninegrid" : string.Empty;
            Id = banner ? SafeBanner(id) : SafePortrait(id);
            if (!banner && !string.IsNullOrEmpty(Id) && portraitPose >= 0 && portraitPose <= 7 &&
                Patches.CoopReplicaPortraitPosePatch.IsInstalled)
            {
                TextureProviderName = "CoopReplicaPortraitTextureProvider";
                AdditionalArgs = portraitPose.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        [DataSourceProperty] public string Id { get; }
        [DataSourceProperty] public string AdditionalArgs { get; }
        [DataSourceProperty] public string TextureProviderName { get; }

        private static string SafeBanner(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            try { var banner = new Banner(id); return banner.BannerCode; }
            catch (Exception) { return string.Empty; }
        }

        internal static string SafePortrait(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            if (!CoopConversationHudCodec.IsPortraitCode(id)) return PortraitUnavailable("structure");
            try
            {
                string[] fields = id.Split(new[] { "@---@" }, StringSplitOptions.None);
                if (!BodyProperties.FromString(fields[2], out var body) ||
                    !InRange(body.Age, 0f, 128f) || !InRange(body.Weight, 0f, 1f) || !InRange(body.Build, 0f, 1f)) return PortraitUnavailable("body");
                var code = CharacterCode.CreateFrom(id);
                if (code.Race >= FaceGen.GetRaceCount() || FaceGen.GetBaseMonsterFromRace(code.Race) == null) return PortraitUnavailable("race");
                // As with the map/tableau actors, campaign-only items may exist
                // exclusively as preloaded static mirrors in multiplayer. Never
                // register new objects or silently strip a missing equipment slot.
                var objects = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                if (objects == null) return PortraitUnavailable("object-manager");
                string unavailableResource = "equipment-code";
                string resolved = CoopConversationHudCodec.ResolvePortraitEquipment(id, itemId =>
                {
                    if (objects.GetObject<ItemObject>(itemId) != null) return itemId;
                    if (ExactCampaignRuntimeItemRegistry.TryResolvePreloadedMirrorItem(itemId, out string mirror, out _) &&
                        objects.GetObject<ItemObject>(mirror) != null) return mirror;
                    unavailableResource = "item:" + itemId;
                    return null;
                }, modifierId =>
                {
                    if (objects.GetObject<ItemModifier>(modifierId) != null) return true;
                    unavailableResource = "modifier:" + modifierId;
                    return false;
                });
                if (string.IsNullOrEmpty(resolved)) return PortraitUnavailable(unavailableResource);
                CharacterCode.CreateFrom(resolved).CalculateEquipment();
                return resolved;
            }
            catch (Exception ex) { return PortraitUnavailable("exception:" + ex.GetType().Name); }
        }
        // Failure-only, explicit verbose mode, bounded per process. No extra
        // native reads, body codes, player IDs or exception messages are logged.
        private static string PortraitUnavailable(string reason)
        {
            if (CoopDebugConfig.VerboseDiagnostics &&
                System.Threading.Interlocked.Increment(ref _portraitFailureDiagnostics) <= 32)
                ModLogger.Info("CoopConversationHudImageVM: portrait unavailable. Reason=" + reason + ".");
            return string.Empty;
        }
        private static bool InRange(float value, float min, float max) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }

    public sealed class CoopConversationHudQuestVM : ViewModel
    {
        public CoopConversationHudQuestVM(int type) { QuestMarkerType = type; }
        [DataSourceProperty] public int QuestMarkerType { get; }
    }

    public sealed class CoopConversationHudPartyVM : ViewModel
    {
        public CoopConversationHudPartyVM(CoopConversationHudParty party)
        {
            HealthyAmount = party.HealthyAmount;
            LeaderVisual = new CoopConversationHudImageVM(party.PortraitCode, false);
            foreach (int type in party.QuestMarkers) Quests.Add(new CoopConversationHudQuestVM(type));
        }
        [DataSourceProperty] public int HealthyAmount { get; }
        [DataSourceProperty] public CoopConversationHudImageVM LeaderVisual { get; }
        [DataSourceProperty] public MBBindingList<CoopConversationHudQuestVM> Quests { get; } = new MBBindingList<CoopConversationHudQuestVM>();
        public void ExecuteShowPartyTooltip() { }
        public void ExecuteHideTooltip() { }
        public override void OnFinalize()
        {
            LeaderVisual.OnFinalize();
            foreach (var quest in Quests) quest.OnFinalize();
            Quests.Clear(); base.OnFinalize();
        }
    }

    // Match precisely the six values consumed by the stock PowerLevelComparer
    // prefab; do not instantiate its ratio calculations on a hidden 0/0 state.
    public sealed class CoopConversationHudPowerVM : ViewModel
    {
        public CoopConversationHudPowerVM(CoopCampaignMapConversationHud hud)
        {
            AttackerBattlePowerValue = hud.AttackerPower; DefenderBattlePowerValue = hud.DefenderPower;
            InitialAttackerBattlePowerValue = hud.InitialAttackerPower; InitialDefenderBattlePowerValue = hud.InitialDefenderPower;
            AttackerColor = hud.AttackerColor; DefenderColor = hud.DefenderColor;
            Hint = new CoopCampaignMapConversationHintVM(GameTexts.FindText("str_power_levels"));
        }
        [DataSourceProperty] public double AttackerBattlePowerValue { get; }
        [DataSourceProperty] public double DefenderBattlePowerValue { get; }
        [DataSourceProperty] public double InitialAttackerBattlePowerValue { get; }
        [DataSourceProperty] public double InitialDefenderBattlePowerValue { get; }
        [DataSourceProperty] public string AttackerColor { get; }
        [DataSourceProperty] public string DefenderColor { get; }
        [DataSourceProperty] public CoopCampaignMapConversationHintVM Hint { get; }
        public override void OnFinalize() { Hint.OnFinalize(); base.OnFinalize(); }
    }
}
