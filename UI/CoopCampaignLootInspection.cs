using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    // Local inspection only. No campaign command or inventory mutation delegate.
    internal sealed class CoopCampaignLootInspection
    {
        private readonly CoopCampaignAftermathVM _root;
        private CoopCampaignAftermathVM _card = new CoopCampaignAftermathVM();
        private bool _replaceCard = true;
        private readonly CoopCampaignAftermathVM _visual = new CoopCampaignAftermathVM();
        private string _visualInterest;
        private CoopCampaignMapTooltipPayload _appliedVisual;
        private string _visualHostVersion;
        private static readonly string[] VisualFields = { "MainCharacter", "CharacterWeapon1Slot", "CharacterWeapon2Slot",
            "CharacterWeapon3Slot", "CharacterWeapon4Slot", "CharacterBannerSlot", "CharacterHelmSlot", "CharacterTorsoSlot",
            "CharacterBootSlot", "CharacterGloveSlot", "CharacterCloakSlot", "CharacterMountSlot", "CharacterMountArmorSlot",
            "CurrentCharacterArmArmor", "CurrentCharacterBodyArmor", "CurrentCharacterHeadArmor", "CurrentCharacterLegArmor",
            "CurrentCharacterHorseArmor", "CurrentCharacterTotalEncumbrance" };
        private readonly string _window;
        private string _row, _hero, _interest;
        private int _mode = 1;
        private int _slot = -1;
        private CoopCampaignMapTooltipPayload _applied;
        private readonly Action<string, bool> _setInterest;
        private readonly Action _clearInterest;
        private readonly Func<CoopCampaignMapTooltipPayload> _currentPayload;
        private readonly bool _trace = string.Equals(Environment.GetEnvironmentVariable(
            "COOPSPECTATOR_AFTERMATH_INSPECTION_DIAGNOSTICS"), "1", StringComparison.Ordinal);
        private int _traceBudget = 32;
        private string _traceStage;
        private void Trace(string stage)
        {
            // Explicit opt-in, bounded transitions only. Never inspect native
            // state or log row/window/hero identifiers or received values.
            if (!_trace || _traceBudget <= 0 || stage == _traceStage) return;
            _traceStage = stage; _traceBudget--;
            ModLogger.Info("Replica loot inspection: " + stage);
        }
        internal CoopCampaignLootInspection(CoopCampaignAftermathVM root, string window)
            : this(root, window, CoopCampaignMapPrototypeNetworkController.SetClientTooltipInterest,
                CoopCampaignMapPrototypeNetworkController.ClearClientTooltip,
                () => CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip) { }

        // Explicit channel dependency permits deterministic inspection lifetime
        // tests without creating a mission, GameNetwork or native game state.
        internal CoopCampaignLootInspection(CoopCampaignAftermathVM root, string window,
            Action<string, bool> setInterest, Action clearInterest, Func<CoopCampaignMapTooltipPayload> currentPayload)
        {
            _setInterest = setInterest ?? throw new ArgumentNullException(nameof(setInterest));
            _clearInterest = clearInterest ?? throw new ArgumentNullException(nameof(clearInterest));
            _currentPayload = currentPayload ?? throw new ArgumentNullException(nameof(currentPayload));
            _root = root; _window = window;
            _root.SetLocal("ItemMenu", _card);
            _root.SetLocal("ReplicaTooltipVisible", false);
            _root.SetLocal("ReplicaHeroVisualVisible", false);
            _root.BindLocalHandlers(Command, Setter);
            Trace("session-created");
        }
        private MBBindingList<CoopCampaignAftermathVM> Heroes =>
            (_root.GetPropertyValue("CharacterList") as CoopCampaignAftermathVM)?.GetPropertyValue("ItemList") as MBBindingList<CoopCampaignAftermathVM>;
        private static string Id(CoopCampaignAftermathVM row) => row?.GetPropertyValue("ReplicaRowId") as string;
        private bool Setter(CoopCampaignAftermathVM source, string name, object value)
        {
            if (ReferenceEquals(source, _root.GetPropertyValue("CharacterList")) && name == "SelectedIndex" && value is int index)
            { SelectHero(index); return true; }
            return false;
        }
        private bool Command(CoopCampaignAftermathVM source, string name, object[] args)
        {
            if (ReferenceEquals(source, _root))
            {
                if (name == "ProcessItemTooltip")
                {
                    var item = args != null && args.Length > 0 ? args[0] as CoopCampaignAftermathVM : null;
                    _slot = item?.GetPropertyValue("ReplicaSlotIndex") is int slot && slot >= 0 && slot < 12 ? slot : -1;
                    _row = _slot >= 0 ? null : Id(item);
                    Trace(_slot >= 0 ? "hover-equipment-slot" : _row == null ? "hover-without-row" : "hover-row-admitted");
                    Invalidate(); return true;
                }
                if (name == "ResetSelectedItem" || name == "ExecuteClearSelectedItem")
                { _row = null; _slot = -1; Invalidate(); return true; }
                if (name == "ExecuteSelectBattleOutfit" || name == "ExecuteSelectCivilianOutfit" || name == "ExecuteSelectStealthOutfit")
                {
                    int mode = name == "ExecuteSelectBattleOutfit" ? 1 : name == "ExecuteSelectCivilianOutfit" ? 0 : 2;
                    if (_mode != mode) { _mode = mode; InvalidateVisual(); Invalidate(); }
                    return true;
                }
            }
            if (ReferenceEquals(source, _root.GetPropertyValue("CharacterList")))
            {
                if (name == "ExecuteSelectPreviousItem" || name == "ExecuteSelectNextItem")
                {
                    var heroes = Heroes; if (heroes == null || heroes.Count == 0) return true;
                    int index = 0; for (int i = 0; i < heroes.Count; i++) if (Id(heroes[i]) == _hero) index = i;
                    SelectHero((index + (name == "ExecuteSelectNextItem" ? 1 : heroes.Count - 1)) % heroes.Count); return true;
                }
            }
            return false;
        }
        private void SelectHero(int index)
        {
            var heroes = Heroes;
            if (heroes == null || index < 0 || index >= heroes.Count) return;
            if (_hero == Id(heroes[index])) return;
            _hero = Id(heroes[index]); InvalidateVisual(); Invalidate();
        }
        private void InvalidateVisual()
        {
            _visualInterest = null; _appliedVisual = null; _visualHostVersion = null;
            SetVisualVisible(false);
        }
        private void SetVisualVisible(bool visible)
        {
            // The tableau establishes its own MainCharacter binding context.
            // Parent-relative widget paths are not VM property paths.
            (_root.GetPropertyValue("MainCharacter") as CoopCampaignAftermathVM)?.SetLocal("ReplicaHeroVisualVisible", visible);
            _root.SetLocal("ReplicaHeroVisualVisible", visible);
        }
        private void SetCardVisible(bool visible)
        {
            // InventoryTooltip's prefab root changes DataSource to ItemMenu.
            _card.SetLocal("ReplicaTooltipVisible", visible);
            _root.SetLocal("ReplicaTooltipVisible", visible);
        }
        private bool TickVisual(CoopCampaignAftermathPresentation state, bool refresh)
        {
            // Native focus fields change on hover, including in the background
            // host window. Only the selected hero's actual equipment version may
            // revoke its independent preview; a new focus snapshot must not.
            string version = null;
            var heroes = Heroes;
            if (heroes != null) foreach (var hero in heroes)
                if (Id(hero) == _hero) version = hero.GetPropertyValue("ReplicaLoadoutVersion") as string;
            if (string.IsNullOrEmpty(version)) { InvalidateVisual(); return false; }
            if (_appliedVisual != null && version != _visualHostVersion) { InvalidateVisual(); Invalidate(); }
            if (_hero == null) { InvalidateVisual(); return false; }
            var target = new CoopCampaignAftermathLoadoutTarget { WindowId = _window, HeroId = _hero, EquipmentMode = _mode };
            string id = target.Encode();
            if (!CoopCampaignAftermathLoadoutTarget.IsDisclosed(state, id)) { InvalidateVisual(); return false; }
            if (!refresh && _visualInterest == id && _appliedVisual != null) return true;
            _setInterest(id, false);
            var payload = _currentPayload();
            if (payload?.Kind != CoopCampaignMapTooltipKind.AftermathLoadout || payload.EntityId != id ||
                !target.MatchesPresentation(payload.AftermathCard) ||
                !payload.AftermathCard.Root.Properties.TryGetValue("ReplicaLoadoutVersion", out var replyVersion) ||
                replyVersion.Kind != CoopCampaignPresentationValueKind.Text || replyVersion.Text != version)
                return _visualInterest == id && _appliedVisual != null;
            if (!ReferenceEquals(payload, _appliedVisual))
            {
                _visual.Apply(payload.AftermathCard.Root);
                foreach (string field in VisualFields) _root.SetLocal(field, _visual.GetPropertyValue(field));
                _appliedVisual = payload; _visualInterest = id; _visualHostVersion = version;
            }
            SetVisualVisible(true);
            return true;
        }
        private void Invalidate()
        {
            _interest = null; _applied = null;
            _replaceCard = true;
            SetCardVisible(false);
            _clearInterest();
        }
        internal void Tick(CoopCampaignAftermathPresentation state)
        {
            var heroes = Heroes; int selected = -1;
            if (heroes != null) for (int i = 0; i < heroes.Count; i++) if (Id(heroes[i]) == _hero) selected = i;
            if (selected < 0)
            { _hero = null; InvalidateVisual(); if (heroes?.Count > 0) { selected = 0; _hero = Id(heroes[0]); } Invalidate(); }
            if (_root.GetPropertyValue("CharacterList") is CoopCampaignAftermathVM selector) selector.SetLocal("SelectedIndex", selected);
            _root.SetLocal("EquipmentMode", _mode);
            _root.SetLocal("IsBattleMode", _mode == 1); _root.SetLocal("IsCivilianMode", _mode == 0); _root.SetLocal("IsStealthMode", _mode == 2);
            if (!TickVisual(state, _row == null && _slot < 0)) { SetCardVisible(false); return; }
            string next = null;
            if ((_row != null || _slot >= 0) && _hero != null)
            {
                next = new CoopCampaignAftermathInspectionTarget { WindowId = _window, RowId = _row, HeroId = _hero, EquipmentMode = _mode, EquipmentSlot = _slot }.Encode();
                if (!CoopCampaignAftermathInspectionTarget.IsDisclosed(state, next)) next = null;
            }
            if (next == null) { Trace(_row == null ? "idle-no-hover" : _hero == null ? "missing-hero" : "row-not-disclosed"); if (_interest != null) Invalidate(); return; }
            _interest = next;
            _setInterest(next, false);
            var payload = _currentPayload();
            bool valid = payload?.Kind == CoopCampaignMapTooltipKind.AftermathItem && payload.EntityId == next &&
                payload.AftermathCard?.WindowId == _window;
            Trace(valid ? "matching-card" : payload == null ? "waiting-no-payload" : "waiting-unmatched-payload");
            if (valid && !ReferenceEquals(payload, _applied))
            {
                if (_replaceCard)
                {
                    // Stock ItemMenuVM creates fresh property rows for another
                    // item. Reusing widgets from a different row shape retains
                    // native size-policy state (e.g. subtext -> two columns).
                    var previous = _card;
                    _card = new CoopCampaignAftermathVM();
                    _card.Apply(payload.AftermathCard.Root);
                    _root.SetLocal("ItemMenu", _card);
                    previous.OnFinalize();
                    _replaceCard = false;
                }
                else _card.Apply(payload.AftermathCard.Root);
                _applied = payload;
            }
            SetCardVisible(valid);
        }
        internal void Suspend() { Invalidate(); InvalidateVisual(); }
        internal void Dispose()
        {
            Invalidate(); InvalidateVisual(); _root.BindLocalHandlers(null, null); _root.ClearLocal("ItemMenu");
            foreach (string field in VisualFields) _root.ClearLocal(field);
            _card.OnFinalize(); _visual.OnFinalize();
        }
    }
}
