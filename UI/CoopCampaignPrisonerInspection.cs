using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    // Local selection only. No PartyVM callback, campaign setter, transfer,
    // upgrade, recruit, execution, conversation or confirmation command exists.
    internal sealed class CoopCampaignPrisonerInspection : ICoopCampaignAftermathInspection
    {
        private static readonly string[] Lists = { "OtherPartyTroops", "OtherPartyPrisoners", "MainPartyTroops", "MainPartyPrisoners" };
        private static readonly string[] Fields = { "SelectedCharacter", "CurrentCharacterLevelLbl", "CurrentCharacterWageLbl",
            "CurrentCharacterTier", "IsCurrentCharacterFormationEnabled", "IsCurrentCharacterWageEnabled" };
        private readonly CoopCampaignAftermathVM _root, _preview = new CoopCampaignAftermathVM(), _empty = new CoopCampaignAftermathVM();
        private readonly string _window;
        private readonly Action<string, bool> _set;
        private readonly Action _clear;
        private readonly Func<CoopCampaignMapTooltipPayload> _current;
        private string _row;
        private bool _initialized;
        private CoopCampaignMapTooltipPayload _applied;

        internal CoopCampaignPrisonerInspection(CoopCampaignAftermathVM root, string window) : this(root, window,
            CoopCampaignMapPrototypeNetworkController.SetClientTooltipInterest,
            CoopCampaignMapPrototypeNetworkController.ClearClientTooltip,
            () => CoopCampaignMapPrototypeNetworkController.CurrentClientTooltip) { }

        internal CoopCampaignPrisonerInspection(CoopCampaignAftermathVM root, string window,
            Action<string, bool> set, Action clear, Func<CoopCampaignMapTooltipPayload> current)
        {
            _root = root; _window = window; _set = set; _clear = clear; _current = current;
            _empty.SetLocal("IsTableauEnabled", false);
            _root.BindLocalHandlers(Command, Setter);
            HidePreview();
        }
        private static string Id(CoopCampaignAftermathVM row) => row?.GetPropertyValue("ReplicaRowId") as string;
        private IEnumerable<CoopCampaignAftermathVM> Rows()
        {
            foreach (string list in Lists)
                if (_root.GetPropertyValue(list) is MBBindingList<CoopCampaignAftermathVM> rows)
                    foreach (var row in rows) yield return row;
        }
        private void HidePreview()
        {
            _applied = null; _clear();
            _root.SetLocal("SelectedCharacter", _empty);
            _root.SetLocal("CurrentCharacterLevelLbl", string.Empty);
            _root.SetLocal("CurrentCharacterWageLbl", string.Empty);
            _root.SetLocal("CurrentCharacterTier", _empty);
            _root.SetLocal("IsCurrentCharacterFormationEnabled", false);
            _root.SetLocal("IsCurrentCharacterWageEnabled", false);
        }
        private void Select(string id)
        {
            _initialized = true;
            if (_row == id) return;
            _row = id; HidePreview();
        }
        private bool Command(CoopCampaignAftermathVM source, string name, object[] args)
        {
            if (name == "ExecuteSetSelected") { if (Id(source) is string id) Select(id); return true; }
            if (name == "ExecuteClearSelectedCharacterTuple") { Select(null); return true; }
            if (name == "ExecuteSetFocused" || name == "ExecuteSetUnfocused")
            { if (Id(source) != null) source.SetLocal("IsFocused", name == "ExecuteSetFocused"); return true; }
            return false;
        }
        private bool Setter(CoopCampaignAftermathVM source, string name, object value)
        {
            if (name != "IsSelected" || Id(source) == null) return false;
            if (value is bool selected && selected) Select(Id(source));
            return true;
        }
        public void Tick(CoopCampaignAftermathPresentation state)
        {
            if (!_initialized)
            {
                _initialized = true;
                _row = Id(_root.GetPropertyValue("CurrentCharacter") as CoopCampaignAftermathVM);
            }
            CoopCampaignAftermathVM selectedRow = null;
            foreach (var row in Rows())
            {
                bool selected = _row != null && Id(row) == _row;
                row.SetLocal("IsSelected", selected);
                if (selected) selectedRow = row;
            }
            var target = new CoopCampaignAftermathTroopTarget { WindowId = _window, RowId = _row };
            string id = _row == null ? null : target.Encode();
            if (selectedRow == null || id == null || !CoopCampaignAftermathTroopTarget.IsDisclosed(state, id))
            {
                if (_row != null || _applied != null) { _row = null; HidePreview(); }
                _root.SetLocal("CurrentCharacter", _empty);
                return;
            }
            // Counts/row fields follow host transfers; model selection remains local.
            _root.SetLocal("CurrentCharacter", selectedRow);
            _set(id, false);
            var payload = _current();
            if (payload?.Kind != CoopCampaignMapTooltipKind.AftermathTroop || payload.EntityId != id ||
                !target.MatchesPresentation(payload.AftermathCard)) return;
            if (ReferenceEquals(_applied, payload)) return;
            _preview.Apply(payload.AftermathCard.Root);
            foreach (string field in Fields) _root.SetLocal(field, _preview.GetPropertyValue(field));
            _applied = payload;
        }
        public void Suspend() { HidePreview(); }
        public void Dispose()
        {
            HidePreview(); _root.BindLocalHandlers(null, null);
            foreach (var row in Rows()) { row.ClearLocal("IsSelected"); row.ClearLocal("IsFocused"); }
            foreach (string field in Fields) _root.ClearLocal(field);
            _root.ClearLocal("CurrentCharacter");
            _preview.OnFinalize(); _empty.OnFinalize();
        }
    }
}
