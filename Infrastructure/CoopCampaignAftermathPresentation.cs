using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    // Fits the existing 96-character entity correlation field without relaxing
    // its bound. Three opaque identities plus bounded view-only parameters.
    public sealed class CoopCampaignAftermathInspectionTarget
    {
        public string WindowId, RowId, HeroId;
        public int EquipmentMode, Usage, ComparisonOffset;
        public int EquipmentSlot = -1;
        public string Encode()
        {
            if (!CoopCampaignAftermathComparisonPolicy.IsValidSelection(EquipmentMode, Usage, int.MaxValue, ComparisonOffset))
                throw new ArgumentException("Invalid inspection selection");
            if (EquipmentSlot != -1)
            {
                if (EquipmentSlot < 0 || EquipmentSlot >= 12) throw new ArgumentException("Invalid equipment slot");
                return "gear:" + Pack(WindowId) + ":" + Pack(HeroId) + ":" +
                    EquipmentMode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    EquipmentSlot.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    Usage.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                    ComparisonOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return "loot:" + Pack(WindowId) + ":" + Pack(RowId) + ":" + Pack(HeroId) + ":" +
                EquipmentMode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                Usage.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                ComparisonOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        public static bool TryParse(string text, out CoopCampaignAftermathInspectionTarget target)
        {
            target = null;
            if (text == null || text.Length > 96) return false;
            var parts = text.Split(':');
            if (parts.Length == 7 && parts[0] == "gear")
            {
                if (!int.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int gearMode) ||
                    !int.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int slot) ||
                    !int.TryParse(parts[5], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int gearUsage) ||
                    !int.TryParse(parts[6], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int gearOffset)) return false;
                try
                {
                    var gear = new CoopCampaignAftermathInspectionTarget { WindowId = Unpack(parts[1]), HeroId = Unpack(parts[2]),
                        EquipmentMode = gearMode, EquipmentSlot = slot, Usage = gearUsage, ComparisonOffset = gearOffset };
                    if (gear.Encode() != text) return false;
                    target = gear; return true;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is FormatException) { return false; }
            }
            if (parts.Length != 7 || parts[0] != "loot" ||
                !int.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int mode) ||
                !int.TryParse(parts[5], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int usage) ||
                !int.TryParse(parts[6], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int offset)) return false;
            try
            {
                var parsed = new CoopCampaignAftermathInspectionTarget { WindowId = Unpack(parts[1]), RowId = Unpack(parts[2]),
                    HeroId = Unpack(parts[3]), EquipmentMode = mode, Usage = usage, ComparisonOffset = offset };
                if (parsed.Encode() != text) return false;
                target = parsed; return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException) { return false; }
        }
        internal static string Pack(string id)
        {
            if (id?.Length != 32 || !Guid.TryParseExact(id, "N", out var guid) || guid == Guid.Empty)
                throw new ArgumentException("Invalid inspection identity");
            return Convert.ToBase64String(guid.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        internal static string Unpack(string text)
        {
            if (text.Length != 22) throw new ArgumentException("Invalid inspection identity");
            return new Guid(Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + "==")).ToString("N");
        }
        public static bool IsDisclosed(CoopCampaignAftermathPresentation state, string text)
        {
            if (!TryParse(text, out var target) || state?.Kind != CoopCampaignAftermathKind.Loot ||
                state.WindowId != target.WindowId || !state.IsValid()) return false;
            var root = state.Root;
            if (target.EquipmentSlot >= 0)
                return CoopCampaignAftermathLoadoutTarget.IsDisclosed(state,
                    new CoopCampaignAftermathLoadoutTarget { WindowId = target.WindowId, HeroId = target.HeroId, EquipmentMode = target.EquipmentMode }.Encode());
            bool row = ContainsList(root, "LeftItemListVM", target.RowId) || ContainsList(root, "RightItemListVM", target.RowId);
            foreach (string slot in new[] { "CharacterHelmSlot", "CharacterCloakSlot", "CharacterTorsoSlot", "CharacterGloveSlot",
                "CharacterBootSlot", "CharacterMountSlot", "CharacterMountArmorSlot", "CharacterWeapon1Slot", "CharacterWeapon2Slot",
                "CharacterWeapon3Slot", "CharacterWeapon4Slot", "CharacterBannerSlot" })
                if (root.Properties.TryGetValue(slot, out var value) && HasId(value, target.RowId)) row = true;
            return row && root.Properties.TryGetValue("CharacterList", out var selector) &&
                ContainsList(selector, "ItemList", target.HeroId);
        }
        private static bool ContainsList(CoopCampaignPresentationValue node, string key, string id)
        {
            if (node?.Kind != CoopCampaignPresentationValueKind.Node || !node.Properties.TryGetValue(key, out var list) ||
                list.Kind != CoopCampaignPresentationValueKind.List) return false;
            return list.Items.Count(item => HasId(item, id)) == 1;
        }
        private static bool HasId(CoopCampaignPresentationValue node, string id) =>
            node?.Kind == CoopCampaignPresentationValueKind.Node && node.Properties.TryGetValue("ReplicaRowId", out var value) &&
            value.Kind == CoopCampaignPresentationValueKind.Text && value.Text == id;
    }

    // A hero preview does not depend on an item hover. Kept separate from item
    // interests so an empty loot list cannot prevent independent hero inspection.
    // Native/peer lifetime admission remains mandatory in addition to parsing.
    public sealed class CoopCampaignAftermathLoadoutTarget
    {
        public string WindowId, HeroId;
        public int EquipmentMode;
        public bool MatchesPresentation(CoopCampaignAftermathPresentation state) =>
            state?.Kind == CoopCampaignAftermathKind.Loot && state.WindowId == WindowId &&
            state.Root?.Kind == CoopCampaignPresentationValueKind.Node &&
            state.Root.Properties.TryGetValue("HeroRowId", out var hero) && hero.Kind == CoopCampaignPresentationValueKind.Text && hero.Text == HeroId &&
            state.Root.Properties.TryGetValue("EquipmentMode", out var mode) && mode.Kind == CoopCampaignPresentationValueKind.Integer && mode.Number == EquipmentMode &&
            state.Root.Properties.TryGetValue("ReplicaLoadoutVersion", out var version) && version.Kind == CoopCampaignPresentationValueKind.Text &&
            !string.IsNullOrEmpty(version.Text) && version.Text.Length <= 64 &&
            state.Root.Properties.TryGetValue("MainCharacter", out var model) && model.Kind == CoopCampaignPresentationValueKind.Node;
        public string Encode()
        {
            if (EquipmentMode < 0 || EquipmentMode > 2) throw new ArgumentException("Invalid equipment mode");
            return "loadout:" + CoopCampaignAftermathInspectionTarget.Pack(WindowId) + ":" +
                CoopCampaignAftermathInspectionTarget.Pack(HeroId) + ":" +
                EquipmentMode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        public static bool TryParse(string text, out CoopCampaignAftermathLoadoutTarget target)
        {
            target = null;
            if (text == null || text.Length > 96) return false;
            var parts = text.Split(':');
            if (parts.Length != 4 || parts[0] != "loadout" || !int.TryParse(parts[3],
                System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int mode)) return false;
            try
            {
                var parsed = new CoopCampaignAftermathLoadoutTarget {
                    WindowId = CoopCampaignAftermathInspectionTarget.Unpack(parts[1]),
                    HeroId = CoopCampaignAftermathInspectionTarget.Unpack(parts[2]), EquipmentMode = mode };
                if (parsed.Encode() != text) return false;
                target = parsed; return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException) { return false; }
        }
        public static bool IsDisclosed(CoopCampaignAftermathPresentation state, string text)
        {
            if (!TryParse(text, out var target) || state?.Kind != CoopCampaignAftermathKind.Loot ||
                state.WindowId != target.WindowId || !state.IsValid() ||
                !state.Root.Properties.TryGetValue("CharacterList", out var selector) ||
                selector.Kind != CoopCampaignPresentationValueKind.Node ||
                !selector.Properties.TryGetValue("ItemList", out var list) || list.Kind != CoopCampaignPresentationValueKind.List) return false;
            return list.Items.Count(row => row?.Kind == CoopCampaignPresentationValueKind.Node &&
                row.Properties.TryGetValue("ReplicaRowId", out var id) &&
                id.Kind == CoopCampaignPresentationValueKind.Text && id.Text == target.HeroId) == 1;
        }
    }

    // Foundation for independent prisoner/troop inspection. Not network-admitted
    // until its native provider and client lifetime are connected coherently.
    public sealed class CoopCampaignAftermathTroopTarget
    {
        public string WindowId, RowId;
        public string Encode() => "troop:" + CoopCampaignAftermathInspectionTarget.Pack(WindowId) + ":" +
            CoopCampaignAftermathInspectionTarget.Pack(RowId);
        public static bool TryParse(string text, out CoopCampaignAftermathTroopTarget target)
        {
            target = null;
            if (text == null || text.Length > 96) return false;
            var parts = text.Split(':');
            if (parts.Length != 3 || parts[0] != "troop") return false;
            try
            {
                var parsed = new CoopCampaignAftermathTroopTarget { WindowId = CoopCampaignAftermathInspectionTarget.Unpack(parts[1]),
                    RowId = CoopCampaignAftermathInspectionTarget.Unpack(parts[2]) };
                if (parsed.Encode() != text) return false;
                target = parsed; return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException) { return false; }
        }
        public static bool IsDisclosed(CoopCampaignAftermathPresentation state, string text)
        {
            if (!TryParse(text, out var target) || state?.Kind != CoopCampaignAftermathKind.Prisoners ||
                state.WindowId != target.WindowId || !state.IsValid()) return false;
            int matches = 0;
            foreach (string name in new[] { "OtherPartyTroops", "OtherPartyPrisoners", "MainPartyTroops", "MainPartyPrisoners" })
            {
                if (!state.Root.Properties.TryGetValue(name, out var list) || list.Kind != CoopCampaignPresentationValueKind.List) continue;
                foreach (var row in list.Items)
                    if (row?.Kind == CoopCampaignPresentationValueKind.Node &&
                        row.Properties.TryGetValue("ReplicaRowId", out var id) && id.Kind == CoopCampaignPresentationValueKind.Text && id.Text == target.RowId)
                    {
                        if (!row.Properties.TryGetValue("Number", out var count) || count.Kind != CoopCampaignPresentationValueKind.Integer || count.Number <= 0) return false;
                        matches++;
                    }
            }
            return matches == 1;
        }
    }

    public static class CoopCampaignAftermathComparisonPolicy
    {
        // Native order: exact lock identity, same item type, alternate weapon
        // class match, then other compatible items. Stable slot order breaks ties.
        public static int Priority(bool exactIdentity, bool sameType, bool alternateClass) =>
            exactIdentity ? 0 : sameType ? 1 : alternateClass ? 2 : 3;
        public static bool IsValidSelection(int mode, int usage, int usageCount, int offset) =>
            mode >= 0 && mode <= 2 && usage >= 0 && usageCount >= 0 &&
            (usageCount == 0 ? usage == 0 : usage < usageCount) && offset >= 0 && offset < 4;
    }

    // Window-local object identity, never a list index or item name. No native
    // object leaves the host; completed captures revoke rows no longer present.
    public sealed class CoopCampaignAftermathRowRegistry
    {
        private readonly Dictionary<object, string> _ids = new Dictionary<object, string>(ReferenceIdentity.Instance);
        private readonly HashSet<object> _seen = new HashSet<object>(ReferenceIdentity.Instance);
        private string _window;
        public void Begin(string window)
        {
            if (!string.Equals(_window, window, StringComparison.Ordinal)) { Clear(); _window = window; }
            _seen.Clear();
        }
        public string Observe(object row)
        {
            if (row == null || string.IsNullOrEmpty(_window)) throw new InvalidOperationException("No aftermath row/window");
            if (!_ids.TryGetValue(row, out string id)) _ids.Add(row, id = Guid.NewGuid().ToString("N"));
            _seen.Add(row);
            return id;
        }
        public void Complete()
        {
            foreach (object row in _ids.Keys.Where(row => !_seen.Contains(row)).ToArray()) _ids.Remove(row);
        }
        public bool TryResolve(string window, string id, out object row)
        {
            row = null;
            if (!string.Equals(window, _window, StringComparison.Ordinal) || string.IsNullOrEmpty(id)) return false;
            foreach (var pair in _ids)
                if (_seen.Contains(pair.Key) && string.Equals(pair.Value, id, StringComparison.Ordinal)) { row = pair.Key; return true; }
            return false;
        }
        public void Clear() { _window = null; _ids.Clear(); _seen.Clear(); }
        private sealed class ReferenceIdentity : IEqualityComparer<object>
        {
            internal static readonly ReferenceIdentity Instance = new ReferenceIdentity();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    public enum CoopCampaignAftermathKind : byte { Prisoners = 1, Loot = 2 }
    public enum CoopCampaignPresentationValueKind : byte { Null, Boolean, Integer, Unsigned, Float, Double, Text, Node, List, Color }

    // Presentation values only. No native types, commands, delegates or campaign objects.
    public sealed class CoopCampaignPresentationValue
    {
        public CoopCampaignPresentationValueKind Kind { get; set; }
        public string Text { get; set; } = string.Empty;
        public double Number { get; set; }
        public Dictionary<string, CoopCampaignPresentationValue> Properties { get; set; }
        public List<CoopCampaignPresentationValue> Items { get; set; }

        public static CoopCampaignPresentationValue Node() => new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.Node,
            Properties = new Dictionary<string, CoopCampaignPresentationValue>(StringComparer.Ordinal) };

    }

    public sealed class CoopCampaignAftermathPresentation
    {
        public CoopCampaignAftermathKind Kind { get; set; }
        public string WindowId { get; set; }
        public CoopCampaignPresentationValue Root { get; set; }
        public bool IsValid() => CoopCampaignAftermathCodec.TryEncode(this, out _);
        public CoopCampaignAftermathPresentation Clone()
        {
            if (!CoopCampaignAftermathCodec.TryEncode(this, out var bytes) ||
                !CoopCampaignAftermathCodec.TryDecode(bytes, out var copy)) throw new ArgumentException("Invalid aftermath presentation");
            return copy;
        }
    }

    public static class CoopCampaignAftermathCodec
    {
        private const int Magic = 0x43414632; // CAF2: typed UI color
        public const int MaximumBytes = 786432;
        public const int MaximumValues = 65536;
        public const int MaximumDepth = 12;
        public const int MaximumItems = 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryEncode(CoopCampaignAftermathPresentation source, out byte[] bytes)
        {
            bytes = null;
            if (source == null || (source.Kind != CoopCampaignAftermathKind.Prisoners && source.Kind != CoopCampaignAftermathKind.Loot) ||
                source.WindowId == null || source.WindowId.Length != 32 || !source.WindowId.All(IsHex) ||
                source.Root?.Kind != CoopCampaignPresentationValueKind.Node) return false;
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Utf8))
                {
                    writer.Write(Magic); writer.Write((byte)source.Kind); WriteText(writer, source.WindowId, 32);
                    int budget = MaximumValues;
                    Write(writer, source.Root, 0, ref budget, new HashSet<CoopCampaignPresentationValue>());
                    writer.Flush();
                    if (stream.Length > MaximumBytes) return false;
                    bytes = stream.ToArray(); return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is OverflowException) { return false; }
        }

        public static bool TryDecode(byte[] bytes, out CoopCampaignAftermathPresentation result)
        {
            result = null;
            if (bytes == null || bytes.Length < 46 || bytes.Length > MaximumBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadInt32() != Magic) return false;
                    var kind = (CoopCampaignAftermathKind)reader.ReadByte();
                    string id = ReadText(reader, 32);
                    int budget = MaximumValues;
                    var root = Read(reader, 0, ref budget);
                    if (stream.Position != stream.Length || root.Kind != CoopCampaignPresentationValueKind.Node ||
                        (kind != CoopCampaignAftermathKind.Prisoners && kind != CoopCampaignAftermathKind.Loot) ||
                        id.Length != 32 || !id.All(IsHex)) return false;
                    result = new CoopCampaignAftermathPresentation { Kind = kind, WindowId = id, Root = root };
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is InvalidDataException || ex is OverflowException) { return false; }
        }

        private static void Write(BinaryWriter writer, CoopCampaignPresentationValue value, int depth, ref int budget,
            HashSet<CoopCampaignPresentationValue> ancestors)
        {
            if (value == null || depth > MaximumDepth || --budget < 0 || !ancestors.Add(value)) throw new InvalidDataException();
            writer.Write((byte)value.Kind);
            switch (value.Kind)
            {
                case CoopCampaignPresentationValueKind.Null: break;
                case CoopCampaignPresentationValueKind.Boolean:
                case CoopCampaignPresentationValueKind.Integer:
                case CoopCampaignPresentationValueKind.Unsigned:
                case CoopCampaignPresentationValueKind.Color:
                case CoopCampaignPresentationValueKind.Float:
                case CoopCampaignPresentationValueKind.Double:
                    ValidateNumber(value.Kind, value.Number); writer.Write(value.Number); break;
                case CoopCampaignPresentationValueKind.Text: WriteText(writer, value.Text, 32768); break;
                case CoopCampaignPresentationValueKind.Node:
                    if (value.Properties == null || value.Properties.Count > 256) throw new InvalidDataException();
                    writer.Write(value.Properties.Count);
                    foreach (var property in value.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        if (!IsPropertyName(property.Key)) throw new InvalidDataException();
                        WriteText(writer, property.Key, 128); Write(writer, property.Value, depth + 1, ref budget, ancestors);
                    }
                    break;
                case CoopCampaignPresentationValueKind.List:
                    if (value.Items == null || value.Items.Count > MaximumItems) throw new InvalidDataException();
                    writer.Write(value.Items.Count);
                    foreach (var item in value.Items) Write(writer, item, depth + 1, ref budget, ancestors);
                    break;
                default: throw new InvalidDataException();
            }
            ancestors.Remove(value);
            if (writer.BaseStream.Length > MaximumBytes) throw new InvalidDataException();
        }

        private static CoopCampaignPresentationValue Read(BinaryReader reader, int depth, ref int budget)
        {
            if (depth > MaximumDepth || --budget < 0) throw new InvalidDataException();
            var result = new CoopCampaignPresentationValue { Kind = (CoopCampaignPresentationValueKind)reader.ReadByte() };
            switch (result.Kind)
            {
                case CoopCampaignPresentationValueKind.Null: break;
                case CoopCampaignPresentationValueKind.Boolean:
                case CoopCampaignPresentationValueKind.Integer:
                case CoopCampaignPresentationValueKind.Unsigned:
                case CoopCampaignPresentationValueKind.Color:
                case CoopCampaignPresentationValueKind.Float:
                case CoopCampaignPresentationValueKind.Double:
                    result.Number = reader.ReadDouble(); ValidateNumber(result.Kind, result.Number); break;
                case CoopCampaignPresentationValueKind.Text: result.Text = ReadText(reader, 32768); break;
                case CoopCampaignPresentationValueKind.Node:
                    int count = ReadCount(reader, 256);
                    result.Properties = new Dictionary<string, CoopCampaignPresentationValue>(StringComparer.Ordinal);
                    string previous = null;
                    for (int i = 0; i < count; i++)
                    {
                        string key = ReadText(reader, 128);
                        if (!IsPropertyName(key) || (previous != null && string.CompareOrdinal(previous, key) >= 0)) throw new InvalidDataException();
                        result.Properties.Add(key, Read(reader, depth + 1, ref budget)); previous = key;
                    }
                    break;
                case CoopCampaignPresentationValueKind.List:
                    int items = ReadCount(reader, MaximumItems); result.Items = new List<CoopCampaignPresentationValue>();
                    for (int i = 0; i < items; i++) result.Items.Add(Read(reader, depth + 1, ref budget));
                    break;
                default: throw new InvalidDataException();
            }
            return result;
        }

        private static void ValidateNumber(CoopCampaignPresentationValueKind kind, double n)
        {
            if (double.IsNaN(n) || double.IsInfinity(n) ||
                (kind == CoopCampaignPresentationValueKind.Boolean && n != 0 && n != 1) ||
                (kind == CoopCampaignPresentationValueKind.Integer && (n < int.MinValue || n > int.MaxValue || n != Math.Truncate(n))) ||
                ((kind == CoopCampaignPresentationValueKind.Unsigned || kind == CoopCampaignPresentationValueKind.Color) &&
                    (n < 0 || n > uint.MaxValue || n != Math.Truncate(n))) ||
                (kind == CoopCampaignPresentationValueKind.Float && (n < -float.MaxValue || n > float.MaxValue))) throw new InvalidDataException();
        }
        public static bool IsPropertyName(string name) => name != null && name.Length > 0 && name.Length <= 128 &&
            ((name[0] >= 'A' && name[0] <= 'Z') || (name[0] >= 'a' && name[0] <= 'z') || name[0] == '_') &&
            name.All(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_');
        private static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
        private static int ReadCount(BinaryReader reader, int max)
        { int count = reader.ReadInt32(); if (count < 0 || count > max) throw new InvalidDataException(); return count; }
        private static void WriteText(BinaryWriter writer, string text, int max)
        {
            if (text == null || text.Length > max) throw new InvalidDataException();
            byte[] bytes = Utf8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static string ReadText(BinaryReader reader, int max)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > max * 4 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
            string text = Utf8.GetString(reader.ReadBytes(count));
            if (text.Length > max) throw new InvalidDataException(); return text;
        }
    }
}
