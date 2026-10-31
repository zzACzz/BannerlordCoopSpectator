using System;
using System.Collections.Generic;
using System.Globalization;
using CoopSpectator.Infrastructure;
using TaleWorlds.Library;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.UI
{
    internal interface ICoopCampaignAftermathInspection
    {
        void Tick(CoopCampaignAftermathPresentation state);
        void Suspend();
        void Dispose();
    }
    // Gauntlet's XML binding engine consumes IViewModel. Reimplement its access
    // interface instead of constructing native PartyVM/InventoryVM on the client.
    // Native commands and setter delegates never exist on these detached nodes.
    public sealed class CoopCampaignAftermathVM : ViewModel, IViewModel, ViewModel.IViewModelGetterInterface
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _local = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly HashSet<string> _rejectedWrites = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Tuple<string, string, string>> _resourceCache =
            new Dictionary<string, Tuple<string, string, string>>(StringComparer.Ordinal);
        public Func<CoopCampaignAftermathVM, string, object[], bool> LocalCommand { get; set; }
        public Func<CoopCampaignAftermathVM, string, object, bool> LocalSetter { get; set; }
        public void SetLocal(string key, object value)
        { if (_local.TryGetValue(key, out var prior) && Equals(prior, value)) return; _local[key] = value; OnPropertyChanged(key); }
        public void ClearLocal(string key) { if (_local.Remove(key)) OnPropertyChanged(key); }
        public void BindLocalHandlers(Func<CoopCampaignAftermathVM, string, object[], bool> command,
            Func<CoopCampaignAftermathVM, string, object, bool> setter)
        {
            LocalCommand = command; LocalSetter = setter;
            foreach (var value in _values.Values)
            {
                if (value is CoopCampaignAftermathVM child) child.BindLocalHandlers(command, setter);
                else if (value is MBBindingList<CoopCampaignAftermathVM> list)
                    foreach (var row in list) row.BindLocalHandlers(command, setter);
            }
        }

        public void Apply(CoopCampaignPresentationValue node)
        {
            var remaining = new HashSet<string>(_values.Keys, StringComparer.Ordinal);
            if (node?.Kind == CoopCampaignPresentationValueKind.Node)
            foreach (var pair in node.Properties)
            {
                remaining.Remove(pair.Key);
                _values.TryGetValue(pair.Key, out var prior);
                object next;
                if (pair.Value.Kind == CoopCampaignPresentationValueKind.Node)
                {
                    var child = prior as CoopCampaignAftermathVM ?? new CoopCampaignAftermathVM();
                    child.Apply(pair.Value); next = child;
                }
                else if (pair.Value.Kind == CoopCampaignPresentationValueKind.List)
                {
                    var list = prior as MBBindingList<CoopCampaignAftermathVM> ?? new MBBindingList<CoopCampaignAftermathVM>();
                    ApplyList(list, pair.Value.Items);
                    next = list;
                }
                else next = ResolveResource(node, pair.Key, Scalar(pair.Value));
                if (!ReferenceEquals(prior, next) && !Equals(prior, next))
                {
                    FinalizeValue(prior); _values[pair.Key] = next; OnPropertyChanged(pair.Key);
                }
            }
            foreach (string key in remaining)
            { FinalizeValue(_values[key]); _values.Remove(key); _resourceCache.Remove(key); OnPropertyChanged(key); }
            if (LocalCommand != null || LocalSetter != null) BindLocalHandlers(LocalCommand, LocalSetter);
        }

        private static void ApplyList(MBBindingList<CoopCampaignAftermathVM> list, List<CoopCampaignPresentationValue> items)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            bool keyed = items.Count > 0;
            bool anyKey = false;
            foreach (var item in items)
            {
                string id = RowId(item);
                anyKey |= id != null;
                keyed &= id != null;
                if (id != null && !ids.Add(id)) throw new ArgumentException("Duplicate aftermath row identity");
            }
            if (anyKey && !keyed) throw new ArgumentException("Mixed aftermath row identity");
            if (keyed)
            {
                // Remove revoked rows before inserting/reordering current ones. A
                // surviving VM must never be rebound to a different item by index.
                for (int i = list.Count - 1; i >= 0; i--)
                    if (!(list[i].GetPropertyValue("ReplicaRowId") is string id) || !ids.Contains(id))
                    { list[i].OnFinalize(); list.RemoveAt(i); }
                for (int i = 0; i < items.Count; i++)
                {
                    string id = RowId(items[i]);
                    int found = -1;
                    for (int j = i; j < list.Count; j++)
                        if (Equals(list[j].GetPropertyValue("ReplicaRowId"), id)) { found = j; break; }
                    CoopCampaignAftermathVM row;
                    if (found < 0) { row = new CoopCampaignAftermathVM(); list.Insert(i, row); }
                    else
                    {
                        row = list[found];
                        if (found != i) { list.RemoveAt(found); list.Insert(i, row); }
                    }
                    row.Apply(items[i]);
                }
                return;
            }
            // Ordinary property/flag lists have no semantic row identity.
            // Never recycle an identified item VM into such a list.
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].GetPropertyValue("ReplicaRowId") != null)
                { list[i].OnFinalize(); list.RemoveAt(i); }
            while (list.Count > items.Count) { list[list.Count - 1].OnFinalize(); list.RemoveAt(list.Count - 1); }
            while (list.Count < items.Count) list.Add(new CoopCampaignAftermathVM());
            for (int i = 0; i < list.Count; i++) list[i].Apply(items[i]);
        }

        private static string RowId(CoopCampaignPresentationValue node)
        {
            if (node?.Kind != CoopCampaignPresentationValueKind.Node ||
                !node.Properties.TryGetValue("ReplicaRowId", out var id)) return null;
            if (id.Kind != CoopCampaignPresentationValueKind.Text || id.Text?.Length != 32 ||
                !Guid.TryParseExact(id.Text, "N", out _)) throw new ArgumentException("Invalid aftermath row identity");
            return id.Text;
        }

        private static object Scalar(CoopCampaignPresentationValue value)
        {
            switch (value.Kind)
            {
                case CoopCampaignPresentationValueKind.Boolean: return value.Number != 0;
                case CoopCampaignPresentationValueKind.Integer: return (int)value.Number;
                case CoopCampaignPresentationValueKind.Unsigned: return (uint)value.Number;
                case CoopCampaignPresentationValueKind.Color: return Color.FromUint((uint)value.Number);
                case CoopCampaignPresentationValueKind.Float: return (float)value.Number;
                case CoopCampaignPresentationValueKind.Double: return value.Number;
                case CoopCampaignPresentationValueKind.Text: return value.Text;
                default: return null;
            }
        }
        private object ResolveResource(CoopCampaignPresentationValue node, string key, object value)
        {
            if (!(value is string text) || text.Length == 0) return value;
            string providerName = string.Empty;
            if (key != "EquipmentCode")
            {
                if (key != "Id" || !node.Properties.TryGetValue("TextureProviderName", out var provider) ||
                    provider.Kind != CoopCampaignPresentationValueKind.Text ||
                    (provider.Text != "ItemImageTextureProvider" && provider.Text != "CharacterImageTextureProvider")) return value;
                providerName = provider.Text;
            }
            if (_resourceCache.TryGetValue(key, out var cached) && cached.Item1 == text && cached.Item2 == providerName)
                return cached.Item3;
            string resolved = key == "EquipmentCode" ? CoopConversationHudCodec.ResolveEquipmentCodeForSlots(text, ResolveEquipmentItem, HasModifier) :
                providerName == "ItemImageTextureProvider" ? ResolveItem(text) ?? string.Empty : CoopConversationHudImageVM.SafePortrait(text);
            // Static preloaded resources only; stable snapshots must not repeatedly
            // enter object/portrait resolution. Cache lifetime is this owned node.
            _resourceCache[key] = Tuple.Create(text, providerName, resolved);
            return resolved;
        }
        private static string ResolveItem(string id)
        {
            var objects = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
            if (objects == null) return null;
            return CoopCampaignVisualItemResolver.TryResolveImage(objects, id, out var item) ? item.StringId : null;
        }
        private static string ResolveEquipmentItem(int slot, string id) =>
            CoopCampaignVisualItemResolver.TryResolve(Game.Current?.ObjectManager ?? MBObjectManager.Instance,
                id, slot, out var item) ? item.StringId : null;
        private static bool HasModifier(string id) =>
            (Game.Current?.ObjectManager ?? MBObjectManager.Instance)?.GetObject<ItemModifier>(id) != null;
        public new object GetPropertyValue(string name) => _local.TryGetValue(name, out var local) ? local :
            _values.TryGetValue(name, out var value) ? value : null;
        public new object GetPropertyValue(string name, PropertyTypeFeeder feeder) => GetPropertyValue(name);
        public new Type GetPropertyType(string name) => GetPropertyValue(name)?.GetType() ?? typeof(object);
        public bool IsValueSynced(string name) => true;
        public new void SetPropertyValue(string name, object value)
        {
            if (_rejectedWrites.Contains(name)) return;
            if (LocalSetter?.Invoke(this, name, value) == true || Equals(GetPropertyValue(name), value)) return;
            // Native widgets may coerce a selection/size while handling our
            // notification and synchronously write it back. Restore display at
            // most once; never recurse through that two-way binding feedback.
            _rejectedWrites.Add(name);
            try { OnPropertyChanged(name); }
            finally { _rejectedWrites.Remove(name); }
        }
        public new void ExecuteCommand(string name, object[] parameters) { LocalCommand?.Invoke(this, name, parameters); }
        // XML GauntletView dispatches through ViewModel (nonvirtual), not
        // IViewModel. Expose only inspection methods for its reflection lookup.
        // Transfer/equip/buy/sell/Done methods intentionally do not exist.
        public void ProcessItemTooltip(CoopCampaignAftermathVM item) => ExecuteCommand(nameof(ProcessItemTooltip), new object[] { item });
        public void ResetSelectedItem() => ExecuteCommand(nameof(ResetSelectedItem), Array.Empty<object>());
        public void ExecuteClearSelectedItem() => ExecuteCommand(nameof(ExecuteClearSelectedItem), Array.Empty<object>());
        public void ExecuteSelectBattleOutfit() => ExecuteCommand(nameof(ExecuteSelectBattleOutfit), Array.Empty<object>());
        public void ExecuteSelectCivilianOutfit() => ExecuteCommand(nameof(ExecuteSelectCivilianOutfit), Array.Empty<object>());
        public void ExecuteSelectStealthOutfit() => ExecuteCommand(nameof(ExecuteSelectStealthOutfit), Array.Empty<object>());
        public void ExecuteSelectPreviousItem() => ExecuteCommand(nameof(ExecuteSelectPreviousItem), Array.Empty<object>());
        public void ExecuteSelectNextItem() => ExecuteCommand(nameof(ExecuteSelectNextItem), Array.Empty<object>());
        public void ExecuteSetSelected() => ExecuteCommand(nameof(ExecuteSetSelected), Array.Empty<object>());
        public void ExecuteSetFocused() => ExecuteCommand(nameof(ExecuteSetFocused), Array.Empty<object>());
        public void ExecuteSetUnfocused() => ExecuteCommand(nameof(ExecuteSetUnfocused), Array.Empty<object>());
        public void ExecuteClearSelectedCharacterTuple() => ExecuteCommand(nameof(ExecuteClearSelectedCharacterTuple), Array.Empty<object>());
        public void ExecuteBeginHint() => ExecuteCommand(nameof(ExecuteBeginHint), Array.Empty<object>());
        public void ExecuteEndHint() => ExecuteCommand(nameof(ExecuteEndHint), Array.Empty<object>());
        public new object GetViewModelAtPath(BindingPath path, bool isList) => GetViewModelAtPath(path);
        public new object GetViewModelAtPath(BindingPath path)
        {
            // Match ViewModel's root-node convention; list indexes are path nodes.
            object current = this;
            var remainder = path?.SubPath;
            while (remainder != null)
            {
                if (current is CoopCampaignAftermathVM vm) current = vm.GetPropertyValue(remainder.FirstNode);
                else if (current is IMBBindingList list && int.TryParse(remainder.FirstNode, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int index) && index >= 0 && index < list.Count) current = list[index];
                else return null;
                remainder = remainder.SubPath;
            }
            return current;
        }
        public override void OnFinalize()
        {
            foreach (object value in _values.Values) FinalizeValue(value);
            _values.Clear(); _local.Clear(); _rejectedWrites.Clear(); _resourceCache.Clear(); LocalCommand = null; LocalSetter = null; base.OnFinalize();
        }
        private static void FinalizeValue(object value)
        {
            if (value is CoopCampaignAftermathVM vm) vm.OnFinalize();
            else if (value is MBBindingList<CoopCampaignAftermathVM> list)
            { foreach (var item in list) item.OnFinalize(); list.Clear(); }
        }
    }
}
