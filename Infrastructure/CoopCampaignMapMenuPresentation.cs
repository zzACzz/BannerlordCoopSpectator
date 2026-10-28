using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    // Detached native EncounterOverlay presentation. No campaign identities,
    // delegates, encyclopedia links or client-side strength calculations.
    // Attached atomically to the menu envelope; native capture/UI gates remain.
    public sealed class CoopCampaignMapEncounterParty
    {
        public string TooltipId { get; set; } = string.Empty;
        public static bool IsTooltipId(string id) => id != null && id.Length == 42 &&
            id.StartsWith("encounter:", StringComparison.Ordinal) &&
            id.Skip(10).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        public string PortraitCode { get; set; } = string.Empty;
        public int PortraitPose { get; set; } = -1;
        public string BannerCode { get; set; } = string.Empty;
        public string NameText { get; set; } = string.Empty;
        public string PartySizeLabel { get; set; } = string.Empty;
        public string DescriptionText { get; set; } = string.Empty;
        public string LocationText { get; set; } = string.Empty;
        public string PowerText { get; set; } = string.Empty;
        public string ProfessionText { get; set; } = string.Empty;
        public int Relation { get; set; } = -101;
        public int ShipCount { get; set; }
        public bool IsPlayer { get; set; }
        public bool IsEnemy { get; set; }
        public bool IsAlly { get; set; }
        public bool IsNeutral { get; set; }
        public bool IsCharacterInPrison { get; set; }
        public bool IsHighlightEnabled { get; set; }
        public List<int> QuestMarkers { get; set; } = new List<int>();

        public bool IsValid() => TooltipId != null && (TooltipId.Length == 0 || IsTooltipId(TooltipId)) &&
            CoopConversationHudCodec.IsPortraitCode(PortraitCode) &&
            PortraitPose >= -1 && PortraitPose <= 7 && (PortraitPose == -1 || !string.IsNullOrEmpty(PortraitCode)) &&
            CoopCampaignMapEncounterPresentation.Banner(BannerCode) &&
            new[] { NameText, PartySizeLabel, DescriptionText, LocationText, PowerText, ProfessionText }
                .All(CoopCampaignMapEncounterPresentation.Text) &&
            // Native HeroVM: -101 means no hero, 101 means the player's hero.
            Relation >= -101 && Relation <= 101 && ShipCount >= 0 &&
            QuestMarkers != null && QuestMarkers.Count <= 32 &&
            QuestMarkers.All(q => q == 1 || q == 2 || q == 4 || q == 8 || q == 16);

        public CoopCampaignMapEncounterParty Clone()
        {
            var copy = (CoopCampaignMapEncounterParty)MemberwiseClone();
            copy.QuestMarkers = QuestMarkers == null ? null : new List<int>(QuestMarkers);
            return copy;
        }
    }

    public sealed class CoopCampaignMapEncounterPresentation
    {
        public const int MaxPartiesPerSide = 256;
        public string TitleText { get; set; } = string.Empty;
        public bool IsSiege { get; set; }
        public bool IsNaval { get; set; }
        public string AttackerBanner { get; set; } = string.Empty;
        public string DefenderBanner { get; set; } = string.Empty;
        public string AttackerCountLabel { get; set; } = string.Empty;
        public string DefenderCountLabel { get; set; } = string.Empty;
        public string AttackerMorale { get; set; } = string.Empty;
        public string DefenderMorale { get; set; } = string.Empty;
        public string AttackerFood { get; set; } = string.Empty;
        public string DefenderFood { get; set; } = string.Empty;
        public string DefenderWallHitPoints { get; set; } = string.Empty;
        public int AttackerShipCount { get; set; }
        public int DefenderShipCount { get; set; }
        public double AttackerPower { get; set; }
        public double DefenderPower { get; set; }
        public double InitialAttackerPower { get; set; }
        public double InitialDefenderPower { get; set; }
        public string AttackerColor { get; set; } = "#FFFFFFFF";
        public string DefenderColor { get; set; } = "#FFFFFFFF";
        public List<CoopCampaignMapEncounterParty> Attackers { get; set; } = new List<CoopCampaignMapEncounterParty>();
        public List<CoopCampaignMapEncounterParty> Defenders { get; set; } = new List<CoopCampaignMapEncounterParty>();
        public List<CoopCampaignCraftedVisualWeapon> VisualWeapons { get; set; } = new List<CoopCampaignCraftedVisualWeapon>();

        public bool IsValid() =>
            new[] { TitleText, AttackerCountLabel, DefenderCountLabel, AttackerMorale,
                DefenderMorale, AttackerFood, DefenderFood, DefenderWallHitPoints }.All(Text) &&
            Banner(AttackerBanner) && Banner(DefenderBanner) &&
            AttackerShipCount >= 0 && DefenderShipCount >= 0 &&
            Power(AttackerPower) && Power(DefenderPower) &&
            Power(InitialAttackerPower) && Power(InitialDefenderPower) &&
            Color(AttackerColor) && Color(DefenderColor) && Parties(Attackers) && Parties(Defenders) &&
            UniqueTooltipIds() && CoopCampaignCraftedVisualWeapon.IsBound(VisualWeapons,
                Attackers.Concat(Defenders).Select(p => p.PortraitCode));

        private bool UniqueTooltipIds()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var party in Attackers.Concat(Defenders))
                if (party.TooltipId.Length != 0 && !seen.Add(party.TooltipId)) return false;
            return true;
        }

        public bool TrySnapshot(out CoopCampaignMapEncounterPresentation snapshot)
        {
            snapshot = null;
            if (!IsValid()) return false;
            var copy = (CoopCampaignMapEncounterPresentation)MemberwiseClone();
            copy.Attackers = Attackers.ConvertAll(p => p.Clone());
            copy.Defenders = Defenders.ConvertAll(p => p.Clone());
            copy.VisualWeapons = VisualWeapons.OrderBy(w => w.Key, StringComparer.Ordinal).Select(w => w.Clone()).ToList();
            snapshot = copy;
            return true;
        }
        internal static bool Text(string s) => s != null && s.Length <= CoopCampaignMapMenuPresentation.MaxTextCharacters;
        internal static bool Banner(string s) => s != null &&
            s.Length <= CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters &&
            s.All(c => (c >= '0' && c <= '9') || c == '.' || c == '-');
        private static bool Color(string s) => s != null && s.Length == 9 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
        private static bool Power(double n) => !double.IsNaN(n) && !double.IsInfinity(n) && n >= 0 && n <= 1e12;
        private static bool Parties(List<CoopCampaignMapEncounterParty> parties) =>
            parties != null && parties.Count <= MaxPartiesPerSide && parties.All(p => p != null && p.IsValid());
    }

    public static class CoopCampaignMapMenuSendPolicy
    {
        // Finish an in-flight snapshot of the same window, then send only the
        // latest revision. Closing/changing windows or sessions preempts it.
        public static bool ShouldPreempt(string currentMenuId, string nextMenuId, bool force) =>
            force || nextMenuId == null || !string.Equals(currentMenuId, nextMenuId, StringComparison.Ordinal);
    }

    public sealed class CoopCampaignMapMenuPlunder
    {
        public string ItemId { get; set; } = string.Empty;
        // Empty means no admitted host tooltip. Never derive this from ItemId.
        public string TooltipId { get; set; } = string.Empty;
        public int Amount { get; set; }
        public static bool IsTooltipId(string value) => value != null &&
            (value.Length == 0 || (value.Length == 40 && value.StartsWith("plunder:", StringComparison.Ordinal) &&
                value.Substring(8).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))));
        public CoopCampaignMapMenuPlunder Clone() => (CoopCampaignMapMenuPlunder)MemberwiseClone();
    }
    // Detached presentation data only. Never transport MenuContext, native menu
    // options, delegates or action requests to the replica.
    public sealed class CoopCampaignMapMenuOption
    {
        public string Text { get; set; } = string.Empty;
        public string Hint { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public int ItemType { get; set; }
        public int BattleSize { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsWaitActive { get; set; }
        public bool IsHighlightEnabled { get; set; }
        public bool IsNavalBattle { get; set; }
        public CoopCampaignMapMenuOption Clone() => (CoopCampaignMapMenuOption)MemberwiseClone();
    }

    public sealed class CoopCampaignMapMenuProgress
    {
        public string Text { get; set; } = string.Empty;
        public string ProgressText { get; set; } = string.Empty;
        // Native GameMenuItemProgressVM publishes a percentage, not a unit fraction.
        public float Progress { get; set; }
        public CoopCampaignMapMenuProgress Clone() => (CoopCampaignMapMenuProgress)MemberwiseClone();
    }

    // Core menu envelope including plunder. Encounter overlays and raid world indicators
    // must be integrated separately before claiming native presentation parity.
    public sealed class CoopCampaignMapMenuPresentation
    {
        public const int MaxOptions = 128;
        public const int MaxProgressItems = 16;
        public const int MaxPlunderItems = 256;
        public const int MaxTextCharacters = 16384;
        public string MenuId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public string Background { get; set; } = string.Empty;
        public bool IsNight { get; set; }
        public bool IsEncounterMenu { get; set; }
        public bool IsInSiegeMode { get; set; }
        public CoopCampaignMapEncounterPresentation Encounter { get; set; }
        public CoopCampaignSettlementMenuPresentation SettlementOverlay { get; set; }
        public CoopCampaignMapSiegeOverlay SiegeOverlay { get; set; }
        public CoopCampaignAftermathPresentation Aftermath { get; set; }
        public CoopCampaignAutoBattlePresentation AutoBattle { get; set; }
        public List<CoopCampaignMapMenuOption> Options { get; set; } = new List<CoopCampaignMapMenuOption>();
        public List<CoopCampaignMapMenuProgress> ProgressItems { get; set; } = new List<CoopCampaignMapMenuProgress>();
        public List<CoopCampaignMapMenuPlunder> PlunderItems { get; set; } = new List<CoopCampaignMapMenuPlunder>();

        public bool IsValid()
        {
            if (!Text(MenuId, 256) || MenuId.Length == 0 || !Text(Title, MaxTextCharacters) ||
                !Text(Context, MaxTextCharacters) || !Text(Background, 256) ||
                Options == null || Options.Count > MaxOptions ||
                ProgressItems == null || ProgressItems.Count > MaxProgressItems ||
                PlunderItems == null || PlunderItems.Count > MaxPlunderItems) return false;
            if (Encounter != null && (!IsEncounterMenu || !Encounter.IsValid())) return false;
            if (SettlementOverlay != null && (IsEncounterMenu || Encounter != null || !SettlementOverlay.IsValid())) return false;
            if (SiegeOverlay != null && (!IsInSiegeMode || !SiegeOverlay.IsValid())) return false;
            if (Aftermath != null && (!Aftermath.IsValid() || IsEncounterMenu || IsInSiegeMode ||
                AutoBattle != null || Encounter != null || SettlementOverlay != null || SiegeOverlay != null || Options.Count != 0 || ProgressItems.Count != 0 || PlunderItems.Count != 0 ||
                MenuId != "aftermath:" + (int)Aftermath.Kind + ":" + Aftermath.WindowId)) return false;
            if (AutoBattle != null && (!AutoBattle.IsValid() || Aftermath != null || IsEncounterMenu || IsInSiegeMode ||
                Encounter != null || SettlementOverlay != null || SiegeOverlay != null || Options.Count != 0 || ProgressItems.Count != 0 || PlunderItems.Count != 0 ||
                MenuId != "autobattle:" + AutoBattle.WindowId)) return false;
            var tooltipIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var loot in PlunderItems)
            {
                if (loot == null || !Text(loot.ItemId, 256) || loot.ItemId.Length == 0 || loot.Amount < 0) return false;
                if (!CoopCampaignMapMenuPlunder.IsTooltipId(loot.TooltipId) ||
                    (loot.TooltipId.Length != 0 && !tooltipIds.Add(loot.TooltipId))) return false;
                foreach (char c in loot.ItemId)
                    if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                        !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
            }
            foreach (var option in Options)
                if (option == null || !Text(option.Text, MaxTextCharacters) ||
                    !Text(option.Hint, MaxTextCharacters) || !Text(option.LeaveType, 256) ||
                    // Native GameMenuItemVM uses -1 when no encounter battle exists.
                    option.BattleSize < -1) return false;
            foreach (var progress in ProgressItems)
                if (progress == null || !Text(progress.Text, MaxTextCharacters) ||
                    !Text(progress.ProgressText, MaxTextCharacters) ||
                    float.IsNaN(progress.Progress) || float.IsInfinity(progress.Progress) ||
                    progress.Progress < 0 || progress.Progress > 100) return false;
            return true;
        }

        // Reject the entire inconsistent capture rather than truncating host choices.
        public CoopCampaignMapMenuPresentation Clone()
        {
            if (!TrySnapshot(out var copy)) throw new ArgumentException("Invalid menu presentation.");
            return copy;
        }

        public bool TrySnapshot(out CoopCampaignMapMenuPresentation snapshot)
        {
            snapshot = null;
            if (!IsValid()) return false;
            var copy = (CoopCampaignMapMenuPresentation)MemberwiseClone();
            copy.Aftermath = Aftermath?.Clone();
            copy.AutoBattle = AutoBattle?.Clone();
            copy.SettlementOverlay = SettlementOverlay?.Clone();
            if (SiegeOverlay != null)
            {
                if (!SiegeOverlay.TrySnapshot(out var siege)) return false;
                copy.SiegeOverlay = siege;
            }
            if (Encounter != null)
            {
                if (!Encounter.TrySnapshot(out var encounter)) return false;
                copy.Encounter = encounter;
            }
            copy.Options = new List<CoopCampaignMapMenuOption>(Options.Count);
            foreach (var item in Options) copy.Options.Add(item.Clone());
            copy.ProgressItems = new List<CoopCampaignMapMenuProgress>(ProgressItems.Count);
            foreach (var item in ProgressItems) copy.ProgressItems.Add(item.Clone());
            copy.PlunderItems = new List<CoopCampaignMapMenuPlunder>(PlunderItems.Count);
            foreach (var item in PlunderItems) copy.PlunderItems.Add(item.Clone());
            snapshot = copy;
            return true;
        }

        private static bool Text(string value, int limit) => value != null && value.Length <= limit;
    }
}
