using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    // Prepared visible native overlay fields only, not settlement simulation.
    public sealed class CoopCampaignSettlementMenuPresentation
    {
        public static readonly string[] LabelNames = { "SettlementNameLbl", "RemainingFoodText", "MilitasLbl", "GarrisonLbl",
            "CrimeLbl", "ProsperityLbl", "LoyaltyLbl", "SecurityLbl", "WallsLbl", "ShipyardLbl" };
        public static readonly string[] NumberNames = { "WallsLevel", "ProsperityChangeAmount", "MilitiaChangeAmount",
            "GarrisonChangeAmount", "GarrisonAmount", "CrimeChangeAmount", "LoyaltyChangeAmount", "SecurityChangeAmount", "FoodChangeAmount" };
        public static readonly string[] FlagNames = { "IsFortification", "IsCrimeEnabled", "CanAssignMembers", "IsNoGarrisonWarning",
            "IsCrimeLabelHighlightEnabled", "IsLoyaltyRebellionWarning", "IsShipyardEnabled", "IsInitializationOver", "IsInfoBarExtended" };
        public static readonly string[] HintNames = { "RemainingFoodHint", "MilitasHint", "GarrisonHint", "ProsperityHint",
            "LoyaltyHint", "SecurityHint", "WallsHint", "CrimeHint", "CharacterFilterHint", "PartyFilterHint", "AssignMembersHint", "ShipyardHint" };
        public const int MaxHints = 12 + 2 * CoopCampaignMapEncounterPresentation.MaxPartiesPerSide;
        public Dictionary<string, string> HintTokens = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> Labels = LabelNames.ToDictionary(n => n, n => string.Empty, StringComparer.Ordinal);
        public Dictionary<string, int> Numbers = NumberNames.ToDictionary(n => n, n => 0, StringComparer.Ordinal);
        public Dictionary<string, bool> Flags = FlagNames.ToDictionary(n => n, n => false, StringComparer.Ordinal);
        public string BannerCode = string.Empty;
        public List<CoopCampaignMapEncounterParty> Characters = new List<CoopCampaignMapEncounterParty>();
        public List<CoopCampaignMapEncounterParty> Parties = new List<CoopCampaignMapEncounterParty>();
        public List<CoopCampaignCraftedVisualWeapon> VisualWeapons = new List<CoopCampaignCraftedVisualWeapon>();
        public bool IsValid() => Keys(Labels, LabelNames) && Keys(Numbers, NumberNames) && Keys(Flags, FlagNames) &&
            Labels.Values.All(s => s != null && s.Length <= 2048 && s.IndexOf('\0') < 0) &&
            Numbers["WallsLevel"] >= 0 && Numbers["WallsLevel"] <= 32 && Numbers["GarrisonAmount"] >= 0 &&
            CoopCampaignMapEncounterPresentation.Banner(BannerCode) && ValidList(Characters) && ValidList(Parties) &&
            CoopCampaignCraftedVisualWeapon.IsBound(VisualWeapons, Characters.Concat(Parties).Select(p => p.PortraitCode)) && ValidHints();
        private bool ValidHints()
        {
            if (HintTokens == null || HintTokens.Count > MaxHints) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in HintTokens)
                if (!IsKnownHintKey(entry.Key, Characters.Count, Parties.Count) || !IsHintAvailable(entry.Key) ||
                    !IsHintToken(entry.Value) || !seen.Add(entry.Value)) return false;
            return true;
        }
        public static bool IsHintToken(string value) => value != null && value.Length == 45 &&
            value.StartsWith("settlementui:", StringComparison.Ordinal) &&
            value.Skip(13).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) &&
            value.Skip(13).Any(c => c != '0');
        public static bool IsKnownHintKey(string key, int characters, int parties) =>
            HintNames.Contains(key) || IsRowKey(key, "Character_", characters) || IsRowKey(key, "Party_", parties);
        private static bool IsRowKey(string key, string prefix, int count) => key != null && key.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(key.Substring(prefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int index) && index >= 0 && index < count &&
            key == prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public bool IsHintAvailable(string key)
        {
            if (!Flags["IsInitializationOver"]) return false;
            switch (key)
            {
                case "RemainingFoodHint": case "GarrisonHint": case "LoyaltyHint": case "SecurityHint": case "WallsHint":
                    return Flags["IsFortification"];
                case "CrimeHint": return Flags["IsCrimeEnabled"];
                case "ShipyardHint": return Flags["IsShipyardEnabled"];
                default: return true;
            }
        }
        private static bool Keys<T>(Dictionary<string, T> values, string[] names) =>
            values != null && values.Count == names.Length && names.All(values.ContainsKey);
        private static bool ValidList(List<CoopCampaignMapEncounterParty> list) =>
            list != null && list.Count <= CoopCampaignMapEncounterPresentation.MaxPartiesPerSide &&
            list.All(p => p != null && p.IsValid() && p.TooltipId.Length == 0);
        public CoopCampaignSettlementMenuPresentation Clone()
        {
            if (!IsValid()) throw new ArgumentException("Invalid settlement overlay.");
            var copy = (CoopCampaignSettlementMenuPresentation)MemberwiseClone();
            copy.Labels = new Dictionary<string, string>(Labels, StringComparer.Ordinal);
            copy.Numbers = new Dictionary<string, int>(Numbers, StringComparer.Ordinal);
            copy.Flags = new Dictionary<string, bool>(Flags, StringComparer.Ordinal);
            copy.HintTokens = new Dictionary<string, string>(HintTokens, StringComparer.Ordinal);
            copy.Characters = Characters.ConvertAll(p => p.Clone()); copy.Parties = Parties.ConvertAll(p => p.Clone());
            copy.VisualWeapons = VisualWeapons.Select(w => w.Clone()).ToList(); return copy;
        }
    }

    // Current owner-thread capabilities. Never resolve a settlement/hero by a
    // client supplied game ID; only an exact displayed source can own a token.
    internal sealed class CoopCampaignSettlementHintIdentity
    {
        private object _context, _overlay;
        private string _session, _menu;
        private Dictionary<string, object> _sources = new Dictionary<string, object>(StringComparer.Ordinal);
        private Dictionary<string, string> _tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        internal void Clear()
        { _context = _overlay = null; _session = _menu = null; _sources.Clear(); _tokens.Clear(); }
        internal bool TryUpdate(object context, object overlay, string session, string menu,
            IReadOnlyDictionary<string, object> sources, out Dictionary<string, string> tokens)
        {
            tokens = null;
            if (context == null || overlay == null || !Guid.TryParseExact(session, "N", out var id) || id == Guid.Empty ||
                string.IsNullOrEmpty(menu) || menu.Length > 256 || sources == null || sources.Count > CoopCampaignSettlementMenuPresentation.MaxHints ||
                sources.Any(e => e.Value == null || !CoopCampaignSettlementMenuPresentation.IsKnownHintKey(e.Key, 256, 256)))
            { Clear(); return false; }
            bool same = ReferenceEquals(context, _context) && ReferenceEquals(overlay, _overlay) &&
                session == _session && menu == _menu;
            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in sources)
                next.Add(entry.Key, same && _sources.TryGetValue(entry.Key, out var old) && ReferenceEquals(old, entry.Value)
                    ? _tokens[entry.Key] : "settlementui:" + Guid.NewGuid().ToString("N"));
            _context = context; _overlay = overlay; _session = session; _menu = menu;
            _sources = sources.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal); _tokens = next;
            tokens = new Dictionary<string, string>(next, StringComparer.Ordinal); return true;
        }
        internal bool TryResolve(object context, object overlay, string session, string menu, string token,
            out string key, out object source)
        {
            key = null; source = null;
            if (!ReferenceEquals(context, _context) || !ReferenceEquals(overlay, _overlay) ||
                session != _session || menu != _menu || !CoopCampaignSettlementMenuPresentation.IsHintToken(token)) return false;
            foreach (var entry in _tokens) if (entry.Value == token)
            { key = entry.Key; source = _sources[key]; return true; }
            return false;
        }
    }
}
