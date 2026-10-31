using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Presentation only: never carry a MobileParty/Hero id or recompute strength.
    public sealed class CoopConversationHudParty
    {
        public string PortraitCode { get; set; } = string.Empty;
        public int HealthyAmount { get; set; }
        public List<int> QuestMarkers { get; set; } = new List<int>();
        public CoopConversationHudParty Clone() => new CoopConversationHudParty
        {
            PortraitCode = PortraitCode, HealthyAmount = HealthyAmount,
            QuestMarkers = QuestMarkers == null ? null : new List<int>(QuestMarkers)
        };
    }

    public sealed class CoopCampaignMapConversationHud
    {
        public bool IsAggressive { get; set; }
        public CoopConversationHudParty AttackerLeader { get; set; }
        public CoopConversationHudParty DefenderLeader { get; set; }
        public List<CoopConversationHudParty> AttackerParties { get; set; } = new List<CoopConversationHudParty>();
        public List<CoopConversationHudParty> DefenderParties { get; set; } = new List<CoopConversationHudParty>();
        public double AttackerPower { get; set; }
        public double DefenderPower { get; set; }
        public double InitialAttackerPower { get; set; }
        public double InitialDefenderPower { get; set; }
        public string AttackerColor { get; set; } = string.Empty;
        public string DefenderColor { get; set; } = string.Empty;
        public bool IsBannerEnabled { get; set; }
        public string BannerCode { get; set; } = string.Empty;
        public bool BannerNineGrid { get; set; }
        public string FactionHint { get; set; } = string.Empty;

        public CoopCampaignMapConversationHud Clone()
        {
            var copy = (CoopCampaignMapConversationHud)MemberwiseClone();
            copy.AttackerLeader = AttackerLeader?.Clone();
            copy.DefenderLeader = DefenderLeader?.Clone();
            copy.AttackerParties = AttackerParties?.ConvertAll(p => p?.Clone());
            copy.DefenderParties = DefenderParties?.ConvertAll(p => p?.Clone());
            return copy;
        }
    }

    public static class CoopConversationHudCodec
    {
        public const int MaxPartiesPerSide = 32;
        public const int MaxPortraitCharacters = 4096;
        public const int MaxPayloadBytes = 300000;
        private const int Magic = 0x43484431;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool IsValid(CoopCampaignMapConversationHud h)
        {
            if (h == null) return true;
            if (h.AttackerParties == null || h.DefenderParties == null ||
                h.AttackerParties.Count > MaxPartiesPerSide || h.DefenderParties.Count > MaxPartiesPerSide ||
                h.FactionHint == null || h.FactionHint.Length > 512 || !Banner(h.BannerCode)) return false;
            if (!h.IsBannerEnabled && (h.BannerCode.Length != 0 || h.BannerNineGrid || h.FactionHint.Length != 0)) return false;
            if (!h.IsAggressive)
                return h.AttackerLeader == null && h.DefenderLeader == null &&
                    h.AttackerParties.Count == 0 && h.DefenderParties.Count == 0 &&
                    h.AttackerPower == 0 && h.DefenderPower == 0 && h.InitialAttackerPower == 0 &&
                    h.InitialDefenderPower == 0 && h.AttackerColor == string.Empty && h.DefenderColor == string.Empty;
            return Party(h.AttackerLeader) && Party(h.DefenderLeader) &&
                h.AttackerParties.All(Party) && h.DefenderParties.All(Party) &&
                Power(h.AttackerPower) && Power(h.DefenderPower) &&
                Power(h.InitialAttackerPower) && Power(h.InitialDefenderPower) &&
                Color(h.AttackerColor) && Color(h.DefenderColor);
        }

        private static bool Power(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0 && v <= 1e12;
        private static bool Color(string s) => s != null && s.Length == 9 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
        private static bool Banner(string s) => s != null && s.Length <= CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters &&
            s.All(c => (c >= '0' && c <= '9') || c == '.' || c == '-');
        private static bool Party(CoopConversationHudParty p) => p != null && p.HealthyAmount >= 0 && p.HealthyAmount <= 1000000 &&
            IsPortraitCode(p.PortraitCode) && p.QuestMarkers != null && p.QuestMarkers.Count <= 2 &&
            p.QuestMarkers.All(q => q == 1 || q == 2 || q == 4 || q == 8 || q == 16);

        // Check the installed CharacterCode/Equipment parser's structure before
        // any native provider sees it. Body/race/item availability is checked in UI.
        public static bool IsPortraitCode(string s)
        {
            if (s == null || s.Length > MaxPortraitCharacters) return false;
            if (s.Length == 0) return true; // Preserve the host's hidden-hero silhouette.
            string[] f = s.Split(new[] { "@---@" }, StringSplitOptions.None);
            if (f.Length != 10 || f[0].Length != 0 || f[9].Length != 0 ||
                f[2].Length == 0 || f[2].Length > CoopCampaignMapPrototypeContract.MaxBodyPropertiesCharacters ||
                (f[3] != "0" && f[3] != "1") || (f[4] != "0" && f[4] != "1") ||
                !Integer(f[5], 0, 10) || !uint.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                !uint.TryParse(f[7], NumberStyles.None, CultureInfo.InvariantCulture, out _) || !Integer(f[8], 0, 255)) return false;
            return IsEquipmentCode(f[1]);
        }
        public static bool IsEquipmentCode(string code)
        {
            if (code == null || code.Length > MaxPortraitCharacters) return false;
            string[] slots = code.Split('+');
            if (slots.Length != 13 || slots[0].Length != 0) return false;
            for (int i = 0; i < 12; i++)
            {
                string[] slot = slots[i + 1].Split('-');
                if (slot.Length != 3 || slot[0] != i.ToString(CultureInfo.InvariantCulture) ||
                    !ItemId(slot[1]) || !ItemId(slot[2])) return false;
            }
            return true;
        }
        private static bool Integer(string s, int min, int max) =>
            int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max;
        private static bool ItemId(string s) => s == "@null" || (s.Length > 0 && s.Length <= 128 &&
            s.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'));

        // Resolve only equipment identifiers, preserving the host's body/colors/
        // formation exactly. Missing resources must not create partial portraits.
        public static string ResolvePortraitEquipment(string code, Func<string, string> resolveItem,
            Func<string, bool> hasModifier)
        {
            return resolveItem == null ? string.Empty :
                ResolvePortraitEquipmentForSlots(code, (slot, id) => resolveItem(id), hasModifier);
        }

        public static string ResolvePortraitEquipmentForSlots(string code, Func<int, string, string> resolveItem,
            Func<string, bool> hasModifier)
        {
            if (string.IsNullOrEmpty(code) || !IsPortraitCode(code) || resolveItem == null || hasModifier == null)
                return string.Empty;
            string[] fields = code.Split(new[] { "@---@" }, StringSplitOptions.None);
            fields[1] = ResolveEquipmentCodeForSlots(fields[1], resolveItem, hasModifier);
            if (fields[1].Length == 0) return string.Empty;
            string result = string.Join("@---@", fields);
            return IsPortraitCode(result) ? result : string.Empty;
        }

        public static string ResolveEquipmentCode(string code, Func<string, string> resolveItem,
            Func<string, bool> hasModifier)
        {
            return resolveItem == null ? string.Empty :
                ResolveEquipmentCodeForSlots(code, (slot, id) => resolveItem(id), hasModifier);
        }

        public static string ResolveEquipmentCodeForSlots(string code, Func<int, string, string> resolveItem,
            Func<string, bool> hasModifier)
        {
            if (!IsEquipmentCode(code) || resolveItem == null || hasModifier == null) return string.Empty;
            string[] slots = code.Split('+');
            for (int i = 1; i < slots.Length; i++)
            {
                string[] parts = slots[i].Split('-');
                if (parts[1] != "@null")
                {
                    string resolved = resolveItem(i - 1, parts[1]);
                    if (string.IsNullOrEmpty(resolved) || resolved == "@null" || !ItemId(resolved)) return string.Empty;
                    parts[1] = resolved;
                }
                if (parts[2] != "@null" && !hasModifier(parts[2])) return string.Empty;
                slots[i] = string.Join("-", parts);
            }
            string result = string.Join("+", slots);
            return IsEquipmentCode(result) ? result : string.Empty;
        }

        public static bool Equivalent(CoopCampaignMapConversationHud a, CoopCampaignMapConversationHud b)
        {
            if (ReferenceEquals(a, b)) return true;
            return a != null && b != null && a.IsAggressive == b.IsAggressive && a.IsBannerEnabled == b.IsBannerEnabled &&
                a.BannerCode == b.BannerCode && a.BannerNineGrid == b.BannerNineGrid && a.FactionHint == b.FactionHint &&
                a.AttackerPower == b.AttackerPower && a.DefenderPower == b.DefenderPower &&
                a.InitialAttackerPower == b.InitialAttackerPower && a.InitialDefenderPower == b.InitialDefenderPower &&
                a.AttackerColor == b.AttackerColor && a.DefenderColor == b.DefenderColor &&
                SameParty(a.AttackerLeader, b.AttackerLeader) && SameParty(a.DefenderLeader, b.DefenderLeader) &&
                SameList(a.AttackerParties, b.AttackerParties) && SameList(a.DefenderParties, b.DefenderParties);
        }
        private static bool SameParty(CoopConversationHudParty a, CoopConversationHudParty b) => ReferenceEquals(a, b) ||
            (a != null && b != null && a.PortraitCode == b.PortraitCode && a.HealthyAmount == b.HealthyAmount &&
             a.QuestMarkers != null && b.QuestMarkers != null && a.QuestMarkers.SequenceEqual(b.QuestMarkers));
        private static bool SameList(List<CoopConversationHudParty> a, List<CoopConversationHudParty> b) =>
            a != null && b != null && a.Count == b.Count && a.Zip(b, SameParty).All(v => v);

        public static bool TrySerialize(CoopCampaignMapConversationHud h, out byte[] bytes)
        {
            bytes = null;
            if (!IsValid(h)) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var w = new BinaryWriter(stream, Utf8, true))
                {
                    w.Write(Magic); w.Write(h != null);
                    if (h != null)
                    {
                        w.Write(h.IsAggressive); w.Write(h.IsBannerEnabled);
                        Text(w, h.BannerCode); w.Write(h.BannerNineGrid); Text(w, h.FactionHint);
                        if (h.IsAggressive)
                        {
                            w.Write(h.AttackerPower); w.Write(h.DefenderPower);
                            w.Write(h.InitialAttackerPower); w.Write(h.InitialDefenderPower);
                            Text(w, h.AttackerColor); Text(w, h.DefenderColor);
                            WriteParty(w, h.AttackerLeader); WriteParty(w, h.DefenderLeader);
                            WriteParties(w, h.AttackerParties); WriteParties(w, h.DefenderParties);
                        }
                    }
                    w.Flush(); if (stream.Length > MaxPayloadBytes) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (ArgumentException) { return false; }
        }

        public static bool TryDeserialize(byte[] bytes, out CoopCampaignMapConversationHud h)
        {
            h = null;
            if (bytes == null || bytes.Length < 5 || bytes.Length > MaxPayloadBytes) return false;
            try
            {
                using (var s = new MemoryStream(bytes, false))
                using (var r = new BinaryReader(s, Utf8, true))
                {
                    if (r.ReadInt32() != Magic) return false;
                    CoopCampaignMapConversationHud value = null;
                    if (Bool(r))
                    {
                        value = new CoopCampaignMapConversationHud
                        {
                            IsAggressive = Bool(r), IsBannerEnabled = Bool(r),
                            BannerCode = Text(r, CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters),
                            BannerNineGrid = Bool(r), FactionHint = Text(r, 512)
                        };
                        if (value.IsAggressive)
                        {
                            value.AttackerPower = r.ReadDouble(); value.DefenderPower = r.ReadDouble();
                            value.InitialAttackerPower = r.ReadDouble(); value.InitialDefenderPower = r.ReadDouble();
                            value.AttackerColor = Text(r, 9); value.DefenderColor = Text(r, 9);
                            value.AttackerLeader = ReadParty(r); value.DefenderLeader = ReadParty(r);
                            value.AttackerParties = ReadParties(r); value.DefenderParties = ReadParties(r);
                        }
                    }
                    if (s.Position != s.Length || !IsValid(value)) return false;
                    h = value; return true;
                }
            }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }
        private static void Text(BinaryWriter w, string s) => CoopCampaignMapConversationTableauCodec.WriteText(w, s);
        private static string Text(BinaryReader r, int max) => CoopCampaignMapConversationTableauCodec.ReadText(r, max);
        private static bool Bool(BinaryReader r) => CoopCampaignMapConversationTableauCodec.ReadBoolean(r);
        private static void WriteParty(BinaryWriter w, CoopConversationHudParty p)
        {
            Text(w, p.PortraitCode); w.Write(p.HealthyAmount); w.Write(p.QuestMarkers.Count);
            foreach (int q in p.QuestMarkers) w.Write(q);
        }
        private static CoopConversationHudParty ReadParty(BinaryReader r)
        {
            var p = new CoopConversationHudParty { PortraitCode = Text(r, MaxPortraitCharacters), HealthyAmount = r.ReadInt32() };
            int n = r.ReadInt32(); if (n < 0 || n > 2) throw new InvalidDataException();
            for (int i = 0; i < n; i++) p.QuestMarkers.Add(r.ReadInt32());
            return p;
        }
        private static void WriteParties(BinaryWriter w, List<CoopConversationHudParty> parties)
        { w.Write(parties.Count); foreach (var p in parties) WriteParty(w, p); }
        private static List<CoopConversationHudParty> ReadParties(BinaryReader r)
        {
            int n = r.ReadInt32(); if (n < 0 || n > MaxPartiesPerSide) throw new InvalidDataException();
            var list = new List<CoopConversationHudParty>();
            for (int i = 0; i < n; i++) list.Add(ReadParty(r));
            return list;
        }
    }
}
