using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;

namespace CoopSpectator.UI
{
    // Only detached, host-prepared text/rows. No network interest, native
    // scoreboard owner, delegate provider or simulation command is retained.
    public sealed class CoopCampaignAutoBattleHints : IDisposable
    {
        private readonly Action<string> _show;
        private readonly Action _hide;
        private readonly Action<List<TooltipProperty>> _showRows;
        private CoopCampaignAftermathVM _hovered;
        private string _text;
        private bool _disposed;
        public CoopCampaignAutoBattleHints(Action<string> show, Action hide)
            : this(show, hide, null) { }
        public CoopCampaignAutoBattleHints(Action<string> show, Action hide, Action<List<TooltipProperty>> showRows)
        { _show = show ?? throw new ArgumentNullException(nameof(show)); _hide = hide ?? throw new ArgumentNullException(nameof(hide)); _showRows = showRows; }
        public bool Command(CoopCampaignAftermathVM source, string name, object[] args)
        {
            if (_disposed || source == null) return false;
            if (name == "ExecuteEndHint")
            { if (ReferenceEquals(source, _hovered)) Suspend(); return true; }
            if (name != "ExecuteBeginHint") return false;
            string text = source.GetPropertyValue("ReplicaHintText") as string;
            List<TooltipProperty> rows = null;
            if (string.IsNullOrEmpty(text) && (_showRows == null || !TryRows(source, out rows, out text))) return false;
            if (ReferenceEquals(source, _hovered) && _text == text) return true;
            Suspend(); _hovered = source; _text = text;
            if (rows != null) _showRows(rows); else _show(text);
            return true;
        }
        public void Tick()
        {
            if (_hovered == null) return;
            string current = _hovered.GetPropertyValue("ReplicaHintText") as string;
            List<TooltipProperty> rows = null;
            if (string.IsNullOrEmpty(current) && _showRows != null) TryRows(_hovered, out rows, out current);
            if (string.IsNullOrEmpty(current)) { Suspend(); return; }
            if (_text != current) { _text = current; if (rows != null) _showRows(rows); else _show(current); }
        }
        public void Suspend()
        {
            bool owned = _hovered != null;
            _hovered = null; _text = null;
            if (owned) _hide();
        }
        public void Dispose() { if (_disposed) return; Suspend(); _disposed = true; }

        private static bool TryRows(CoopCampaignAftermathVM source, out List<TooltipProperty> result, out string signature)
        {
            result = null; signature = null;
            if (!(source.GetPropertyValue("ReplicaTooltipRows") is MBBindingList<CoopCampaignAftermathVM> rows) ||
                rows.Count < 1 || rows.Count > 128) return false;
            var copy = new List<TooltipProperty>(); var key = new StringBuilder("rows:");
            foreach (var row in rows)
            {
                if (!(row.GetPropertyValue("DefinitionLabel") is string definition) || definition.Length > 4096 ||
                    !(row.GetPropertyValue("ValueLabel") is string value) || value.Length > 4096 ||
                    !(row.GetPropertyValue("TextHeight") is int height) || height < -1 || height > 4096 ||
                    !(row.GetPropertyValue("PropertyModifier") is int flags) || flags < 0 || flags > 0x3fff ||
                    !(row.GetPropertyValue("TextColor") is Color color)) return false;
                key.Append(definition.Length).Append(':').Append(definition).Append(value.Length).Append(':').Append(value)
                    .Append(';').Append(height).Append(';').Append(flags).Append(';').Append(color.ToUnsignedInteger()).Append(';');
                copy.Add(new TooltipProperty(definition, value, height, color, false, (TooltipProperty.TooltipPropertyFlags)flags));
            }
            result = copy; signature = key.ToString(); return true;
        }
    }
}
