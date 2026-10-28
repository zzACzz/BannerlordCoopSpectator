using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;
using TaleWorlds.Core.ViewModelCollection.Information;

namespace CoopSpectator.Campaign
{
    internal static class CoopCampaignAftermathCapture
    {
        private static object _owner;
        private static string _windowId;
        private static readonly CoopCampaignAftermathRowRegistry Rows = new CoopCampaignAftermathRowRegistry();
        private static readonly Dictionary<Type, PropertyInfo[]> Bindings = new Dictionary<Type, PropertyInfo[]>();
        private static readonly Dictionary<string, string> VisualAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly bool CaptureDiagnostics = string.Equals(Environment.GetEnvironmentVariable(
            "COOPSPECTATOR_AFTERMATH_CAPTURE_DIAGNOSTICS"), "1", StringComparison.Ordinal);

        // Native Loot modes are shared by field/village/hideout/final siege aftermath.
        // Ordinary inventory, trade, garrison and ransom screens are outside this increment.
        internal static bool IsAftermathState(TaleWorlds.Core.GameState state)
        {
            if (state is TaleWorlds.CampaignSystem.GameState.PartyState party)
                return party.PartyScreenMode == Helpers.PartyScreenHelper.PartyScreenMode.Loot;
            if (state is TaleWorlds.CampaignSystem.GameState.InventoryState inventory)
                return inventory.InventoryMode == Helpers.InventoryScreenHelper.InventoryMode.Loot;
            return false;
        }

        internal static bool TryCapture(out CoopCampaignMapMenuPresentation menu, out string reason)
        {
            menu = null;
            reason = "aftermath-state-not-loot";
            try
            {
                var state = Game.Current?.GameStateManager?.ActiveState;
                if (!IsAftermathState(state)) { Reset(); return false; }
                bool party = state.GetType().Name == "PartyState";
                reason = "aftermath-top-screen-mismatch";
                var screen = ScreenManager.TopScreen;
                string expected = party ? "SandBox.GauntletUI.GauntletPartyScreen" : "SandBox.GauntletUI.GauntletInventoryScreen";
                if (screen?.GetType().FullName != expected) { Reset(); return false; }
                reason = "aftermath-data-source-mismatch";
                var source = screen.GetType().GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(screen) as ViewModel;
                string sourceType = party ? "TaleWorlds.CampaignSystem.ViewModelCollection.Party.PartyVM" :
                    "TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.SPInventoryVM";
                if (source?.GetType().FullName != sourceType) { Reset(); return false; }
                if (!ReferenceEquals(_owner, source)) { _owner = source; _windowId = Guid.NewGuid().ToString("N"); VisualAliases.Clear(); }
                int budget = CoopCampaignAftermathCodec.MaximumValues;
                Rows.Begin(_windowId);
                reason = "aftermath-binding-capture";
                var root = Capture(source, 0, ref budget, new HashSet<object>(ReferenceComparer.Instance));
                // The same crafted identity must be used by the selected model
                // and every row portrait, not only independent Loot previews.
                // Resolve current campaign objects once per owned screen; native
                // creation stays at the existing battle materialization boundary.
                CoopCampaignAftermathEquipmentProjection.ApplyResolvedAliases(root, ResolveVisualItem);
                // Revalidate the exact screen/state after reading prepared bindings.
                reason = "aftermath-screen-changed";
                if (!ReferenceEquals(state, Game.Current?.GameStateManager?.ActiveState) ||
                    !ReferenceEquals(screen, ScreenManager.TopScreen)) { Reset(); return false; }
                var presentation = new CoopCampaignAftermathPresentation {
                    Kind = party ? CoopCampaignAftermathKind.Prisoners : CoopCampaignAftermathKind.Loot,
                    WindowId = _windowId, Root = root };
                reason = "aftermath-tree-invalid";
                if (!CoopCampaignAftermathCodec.TryEncode(presentation, out _, out string rejection))
                {
                    if (CaptureDiagnostics) reason += ":" + rejection;
                    Reset(); return false;
                }
                menu = new CoopCampaignMapMenuPresentation {
                    MenuId = "aftermath:" + (int)presentation.Kind + ":" + _windowId, Aftermath = presentation };
                reason = "aftermath-menu-invalid";
                if (!menu.IsValid()) { Reset(); menu = null; return false; }
                Rows.Complete();
                reason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                // Bounded stage/type only, surfaced through the publisher's existing
                // throttled failure report. Never log binding values or native objects.
                reason += ":" + (ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException.GetType().Name : ex.GetType().Name);
                Reset(); return false;
            }
        }

        internal static bool TryResolveLootRow(string window, string rowId, out object owner, out object row)
            => TryResolveLootMember(window, rowId, false, out owner, out row);

        internal static bool TryResolvePrisonerRow(string window, string rowId, out object owner, out object row)
        {
            owner = null; row = null;
            if (_owner == null || window != _windowId ||
                !(Game.Current?.GameStateManager?.ActiveState is TaleWorlds.CampaignSystem.GameState.PartyState state) ||
                !IsAftermathState(state)) return false;
            var screen = ScreenManager.TopScreen;
            if (screen?.GetType().FullName != "SandBox.GauntletUI.GauntletPartyScreen" ||
                !ReferenceEquals(_owner, screen.GetType().GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(screen)) ||
                !Rows.TryResolve(window, rowId, out var current) ||
                !(current is TaleWorlds.CampaignSystem.ViewModelCollection.Party.PartyCharacterVM candidate) ||
                !(_owner is TaleWorlds.CampaignSystem.ViewModelCollection.Party.PartyVM party) ||
                candidate.Number <= 0 || candidate.Character == null) return false;
            int matches = 0;
            foreach (var list in new[] { party.OtherPartyTroops, party.OtherPartyPrisoners, party.MainPartyTroops, party.MainPartyPrisoners })
                foreach (var entry in list) if (ReferenceEquals(entry, candidate)) matches++;
            if (matches != 1) return false;
            owner = _owner; row = current; return true;
        }

        internal static bool TryResolveLootHero(string window, string rowId, out object owner, out object row)
            => TryResolveLootMember(window, rowId, true, out owner, out row);

        private static bool TryResolveLootMember(string window, string rowId, bool hero, out object owner, out object row)
        {
            owner = null; row = null;
            if (_owner == null || window != _windowId ||
                !(Game.Current?.GameStateManager?.ActiveState is TaleWorlds.CampaignSystem.GameState.InventoryState state) ||
                !IsAftermathState(state)) return false;
            var screen = ScreenManager.TopScreen;
            if (screen?.GetType().FullName != "SandBox.GauntletUI.GauntletInventoryScreen" ||
                !ReferenceEquals(_owner, screen.GetType().GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(screen)) ||
                !Rows.TryResolve(window, rowId, out var current)) return false;
            var inventory = _owner as TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.SPInventoryVM;
            if (inventory == null) return false;
            if (hero)
            {
                if (!(current is TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.InventoryCharacterSelectorItemVM candidate) ||
                    inventory.CharacterList?.ItemList == null || !inventory.CharacterList.ItemList.Contains(candidate) ||
                    candidate.Hero == null || !candidate.Hero.IsAlive) return false;
            }
            else
            {
                if (!(current is TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.SPItemVM candidate)) return false;
                // Publication tokens are insufficient after a live transfer. Require
                // current membership, not only the preceding capture's registry.
                bool present = inventory.LeftItemListVM.Contains(candidate) || inventory.RightItemListVM.Contains(candidate);
                if (!present)
                    foreach (string slot in new[] { "CharacterHelmSlot", "CharacterCloakSlot", "CharacterTorsoSlot",
                        "CharacterGloveSlot", "CharacterBootSlot", "CharacterMountSlot", "CharacterMountArmorSlot",
                        "CharacterWeapon1Slot", "CharacterWeapon2Slot", "CharacterWeapon3Slot", "CharacterWeapon4Slot", "CharacterBannerSlot" })
                        if (ReferenceEquals(typeof(TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.SPInventoryVM)
                            .GetProperty(slot)?.GetValue(inventory), candidate)) { present = true; break; }
                if (!present || candidate.ItemCount <= 0 || candidate.ItemRosterElement.EquipmentElement.IsEmpty) return false;
            }
            owner = _owner; row = current; return true;
        }

        internal static CoopCampaignPresentationValue CapturePrepared(ViewModel source, bool registerRows = true)
        {
            int budget = CoopCampaignAftermathCodec.MaximumValues;
            return Capture(source, 0, ref budget, new HashSet<object>(ReferenceComparer.Instance), registerRows);
        }
        internal static CoopCampaignPresentationValue CapturePreparedWithPolicy(ViewModel source, Func<Type, bool> policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            int budget = CoopCampaignAftermathCodec.MaximumValues;
            return Capture(source, 0, ref budget, new HashSet<object>(ReferenceComparer.Instance), false, policy);
        }

        internal static string LootLoadoutVersion(TaleWorlds.CampaignSystem.Hero hero)
        {
            // Managed equipment/perk/faction reads only. Do not include screen
            // focus, hover, host selection or native tableau state in this key.
            var character = hero.CharacterObject;
            var text = new StringBuilder();
            foreach (var equipment in new[] { character.FirstCivilianEquipment, character.FirstBattleEquipment, character.FirstStealthEquipment })
            {
                string code = equipment?.CalculateEquipmentCode() ?? string.Empty;
                text.Append(code.Length).Append(':').Append(code).Append(';');
            }
            text.Append(hero.MapFaction?.Color ?? 0).Append(';').Append(hero.MapFaction?.Color2 ?? 0).Append(';');
            text.Append(character.GetPerkValue(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultPerks.Athletics.FormFittingArmor));
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private static CoopCampaignPresentationValue Capture(object source, int depth, ref int budget, HashSet<object> ancestors, bool registerRows = true, Func<Type, bool> policy = null)
        {
            if (depth > CoopCampaignAftermathCodec.MaximumDepth || --budget < 0) throw new InvalidOperationException("Aftermath capture bound");
            var value = new CoopCampaignPresentationValue();
            if (source == null) return value;
            Type type = source.GetType();
            if (source is string text) { value.Kind = CoopCampaignPresentationValueKind.Text; value.Text = text; return value; }
            if (source is bool flag) { value.Kind = CoopCampaignPresentationValueKind.Boolean; value.Number = flag ? 1 : 0; return value; }
            if (source is int || source is byte || source is short || source is sbyte || source is ushort || type.IsEnum)
            { value.Kind = CoopCampaignPresentationValueKind.Integer; value.Number = Convert.ToInt32(source, CultureInfo.InvariantCulture); return value; }
            if (source is uint) { value.Kind = CoopCampaignPresentationValueKind.Unsigned; value.Number = (uint)source; return value; }
            if (source is float) { value.Kind = CoopCampaignPresentationValueKind.Float; value.Number = (float)source; return value; }
            if (source is double) { value.Kind = CoopCampaignPresentationValueKind.Double; value.Number = (double)source; return value; }
            if (source is Color color) { value.Kind = CoopCampaignPresentationValueKind.Color; value.Number = color.ToUnsignedInteger(); return value; }
            if (!ancestors.Add(source)) return value; // Never traverse a back-reference to a UI owner.
            try
            {
                if (source is IMBBindingList list)
                {
                    if (list.Count > CoopCampaignAftermathCodec.MaximumItems) throw new InvalidOperationException("Aftermath list bound");
                    value.Kind = CoopCampaignPresentationValueKind.List; value.Items = new List<CoopCampaignPresentationValue>();
                    for (int i = 0; i < list.Count; i++) value.Items.Add(Capture(list[i], depth + 1, ref budget, ancestors, registerRows, policy));
                    return value;
                }
                if (!(source is ViewModel) || !(policy ?? Allowed)(type)) return value;
                value = CoopCampaignPresentationValue.Node();
                // The explicit scoreboard policy admits this exact prepared hint.
                // HintText is a field, not a DataSourceProperty. Do not call its
                // hover commands or evaluate any BasicTooltipViewModel delegate.
                if (policy != null && type == typeof(HintViewModel) && source is HintViewModel hint)
                    value.Properties.Add("ReplicaHintText", Capture(hint.HintText?.ToString() ?? string.Empty,
                        depth + 1, ref budget, ancestors, false, policy));
                if (registerRows && (type.FullName == "TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.SPItemVM" ||
                    type.FullName == "TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.InventoryCharacterSelectorItemVM" ||
                    type.FullName == "TaleWorlds.CampaignSystem.ViewModelCollection.Party.PartyCharacterVM"))
                    value.Properties.Add("ReplicaRowId", Capture(Rows.Observe(source), depth + 1, ref budget, ancestors));
                if (source is TaleWorlds.CampaignSystem.ViewModelCollection.Inventory.InventoryCharacterSelectorItemVM heroRow && heroRow.Hero != null)
                    value.Properties.Add("ReplicaLoadoutVersion", Capture(LootLoadoutVersion(heroRow.Hero), depth + 1, ref budget, ancestors));
                if (!Bindings.TryGetValue(type, out var properties))
                {
                    properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead &&
                            p.IsDefined(typeof(DataSourceProperty), true)).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
                    Bindings.Add(type, properties);
                }
                foreach (var property in properties)
                {
                    // Tooltip delegates and campaign/native models are never evaluated.
                    Type child = property.PropertyType;
                    if (!(child.IsPrimitive || child.IsEnum || child == typeof(string) ||
                        typeof(ViewModel).IsAssignableFrom(child) || typeof(IMBBindingList).IsAssignableFrom(child))) continue;
                    value.Properties.Add(property.Name, Capture(property.GetValue(source), depth + 1, ref budget, ancestors, registerRows, policy));
                }
                if (source is TooltipProperty row)
                {
                    // Native TooltipProperty deliberately has no DataSourceProperty
                    // attributes. Read its prepared backing values; never call its
                    // RefreshValue/RefreshDefinition delegates during replication.
                    value.Properties["DefinitionLabel"] = Capture(row.DefinitionLabel, depth + 1, ref budget, ancestors);
                    value.Properties["ValueLabel"] = Capture(row.ValueLabel, depth + 1, ref budget, ancestors);
                    value.Properties["TextHeight"] = Capture(row.TextHeight, depth + 1, ref budget, ancestors);
                    value.Properties["TextColor"] = Capture(row.TextColor, depth + 1, ref budget, ancestors);
                    value.Properties["PropertyModifier"] = Capture(row.PropertyModifier, depth + 1, ref budget, ancestors);
                    value.Properties["OnlyShowWhenExtended"] = Capture(row.OnlyShowWhenExtended, depth + 1, ref budget, ancestors);
                    value.Properties["OnlyShowWhenNotExtended"] = Capture(row.OnlyShowWhenNotExtended, depth + 1, ref budget, ancestors);
                }
                return value;
            }
            finally { ancestors.Remove(source); }
        }

        private static bool Allowed(Type type)
        {
            // PartyVM.SelectedCharacter is declared in the parent namespace,
            // unlike PartyCharacterVM rows. InputKeyItemVM is shared by both screens.
            if (type.FullName == "TaleWorlds.CampaignSystem.ViewModelCollection.HeroViewModel" ||
                type.FullName == "TaleWorlds.CampaignSystem.ViewModelCollection.Input.InputKeyItemVM") return true;
            string ns = type.Namespace ?? string.Empty;
            return ns.StartsWith("TaleWorlds.CampaignSystem.ViewModelCollection.Party", StringComparison.Ordinal) ||
                ns.StartsWith("TaleWorlds.CampaignSystem.ViewModelCollection.Inventory", StringComparison.Ordinal) ||
                ns.StartsWith("TaleWorlds.Core.ViewModelCollection", StringComparison.Ordinal) ||
                ns.StartsWith("TaleWorlds.Library", StringComparison.Ordinal);
        }
        private static string ResolveVisualItem(string id)
        {
            if (VisualAliases.TryGetValue(id, out string alias)) return alias;
            var item = (Game.Current?.ObjectManager ?? TaleWorlds.ObjectSystem.MBObjectManager.Instance)?.GetObject<ItemObject>(id);
            alias = BattleDetector.TryGetCraftedVisualMirrorId(item, out string crafted) ? crafted : id;
            VisualAliases.Add(id, alias); return alias;
        }
        internal static void Reset() { _owner = null; _windowId = null; Rows.Clear(); VisualAliases.Clear(); }
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
