using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    // Resource data only. No XML, native objects, file paths or command names.
    // Fingerprints deliberately match the existing battle crafting contract.
    public sealed class CoopCampaignCraftedVisualPiece
    {
        public string Id, Type;
        public int Scale;
        public CoopCampaignCraftedVisualPiece Clone() => (CoopCampaignCraftedVisualPiece)MemberwiseClone();
    }

    public sealed class CoopCampaignCraftedVisualWeapon
    {
        public const int MaximumDefinitions = 256, MaximumPieces = 12;
        public string Key, OriginalId, MirrorId, Name, TemplateId, CultureId, ModifierGroupId, DesignHash;
        public List<CoopCampaignCraftedVisualPiece> Pieces = new List<CoopCampaignCraftedVisualPiece>();
        public bool IsValid()
        {
            if (!Identifier(OriginalId) || !Identifier(TemplateId) || !Identifier(CultureId, true) ||
                !Identifier(ModifierGroupId, true) || !Text(Name, 256) || !Text(DesignHash, 512) ||
                Pieces == null || Pieces.Count == 0 || Pieces.Count > MaximumPieces) return false;
            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var piece in Pieces)
                if (piece == null || !Identifier(piece.Id) || !Identifier(piece.Type) ||
                    piece.Scale < 0 || piece.Scale > 1000 || !types.Add(piece.Type)) return false;
            string expected = CalculateKey();
            return Key == expected && MirrorId == "cs_crafted_" + expected.Substring(3);
        }
        public string CalculateKey()
        {
            var parts = new List<string> { OriginalId ?? "", TemplateId ?? "", ModifierGroupId ?? "",
                CultureId ?? "", Name ?? "", DesignHash ?? "" };
            foreach (var piece in OrderedPieces())
                parts.Add(piece.Type + ":" + piece.Id + ":" + piece.Scale.ToString(CultureInfo.InvariantCulture));
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                foreach (char ch in string.Join("|", parts)) { hash ^= char.ToLowerInvariant(ch); hash *= 1099511628211UL; }
                return "cw_" + hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }
        public IEnumerable<CoopCampaignCraftedVisualPiece> OrderedPieces() => Pieces
            .OrderBy(p => p.Type, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase);
        public CoopCampaignCraftedVisualWeapon Clone()
        {
            var copy = (CoopCampaignCraftedVisualWeapon)MemberwiseClone();
            copy.Pieces = OrderedPieces().Select(p => p.Clone()).ToList(); return copy;
        }
        public bool SameDefinition(CoopCampaignCraftedVisualWeapon other) => other != null &&
            Key == other.Key && OriginalId == other.OriginalId && MirrorId == other.MirrorId && Name == other.Name &&
            TemplateId == other.TemplateId && CultureId == other.CultureId && ModifierGroupId == other.ModifierGroupId &&
            DesignHash == other.DesignHash && Pieces.Count == other.Pieces.Count && OrderedPieces().Zip(other.OrderedPieces(),
                (a, b) => a.Id == b.Id && a.Type == b.Type && a.Scale == b.Scale).All(equal => equal);
        public static bool IsBound(IReadOnlyList<CoopCampaignCraftedVisualWeapon> definitions, IEnumerable<string> portraits)
        {
            if (definitions == null || definitions.Count > MaximumDefinitions || portraits == null) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var originals = new HashSet<string>(StringComparer.Ordinal);
            var required = new HashSet<string>(StringComparer.Ordinal);
            foreach (string portrait in portraits)
            {
                if (!CoopConversationHudCodec.IsPortraitCode(portrait)) return false;
                if (portrait.Length == 0) continue;
                foreach (var slot in portrait.Split(new[] { "@---@" }, StringSplitOptions.None)[1].Split('+').Skip(1))
                {
                    string id = slot.Split('-')[1];
                    if (id.StartsWith("cs_crafted_", StringComparison.Ordinal)) required.Add(id);
                }
            }
            foreach (var definition in definitions)
                if (definition == null || !definition.IsValid() || !keys.Add(definition.Key) ||
                    !originals.Add(definition.OriginalId) || !required.Remove(definition.MirrorId)) return false;
            // Every declared object is visible, and every referenced crafted
            // alias has its exact definition in this same presentation.
            return required.Count == 0;
        }
        internal static bool Identifier(string text, bool empty = false) => text != null && text.Length <= 128 &&
            (empty || text.Length != 0) && text.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' ||
                c >= '0' && c <= '9' || c == '_');
        internal static bool Text(string text, int max) => text != null && text.Length <= max && text.IndexOf('\0') < 0;
    }

    // Pure readiness bookkeeping. Only an admitted mission-tick adapter supplies
    // native preparation. There is no timer or implicit acknowledgement by delay.
    public sealed class CoopCampaignCraftedVisualPreparation
    {
        private readonly Dictionary<string, CoopCampaignCraftedVisualWeapon> _ready =
            new Dictionary<string, CoopCampaignCraftedVisualWeapon>(StringComparer.Ordinal);
        public bool TryPrepare(CoopCampaignMapEncounterPresentation encounter,
            Func<CoopCampaignCraftedVisualWeapon, bool> prepare, int budget)
        {
            if (encounter == null) return true;
            if (!encounter.IsValid() || budget < 0) return false;
            return TryPrepare(encounter.VisualWeapons, encounter.Attackers.Concat(encounter.Defenders).Select(p => p.PortraitCode), prepare, budget);
        }
        public bool TryPrepare(IReadOnlyList<CoopCampaignCraftedVisualWeapon> definitions, IEnumerable<string> portraits,
            Func<CoopCampaignCraftedVisualWeapon, bool> prepare, int budget)
        {
            if (budget < 0 || !CoopCampaignCraftedVisualWeapon.IsBound(definitions, portraits)) return false;
            // Check all identities before any native action. A reused fingerprint
            // cannot replace a resource that an existing view may still own.
            foreach (var definition in definitions)
                if (_ready.TryGetValue(definition.Key, out var previous) && !previous.SameDefinition(definition)) return false;
            foreach (var definition in definitions)
            {
                if (_ready.ContainsKey(definition.Key)) continue;
                if (prepare == null || budget-- <= 0 || !prepare(definition.Clone())) return false;
                _ready.Add(definition.Key, definition.Clone());
            }
            return true;
        }
    }
}
