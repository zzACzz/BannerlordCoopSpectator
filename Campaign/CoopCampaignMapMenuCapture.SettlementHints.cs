using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using SandBox.View.Menu;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.Core.ViewModelCollection.Information;

namespace CoopSpectator.Campaign
{
    internal static partial class CoopCampaignMapMenuCapture
    {
        private static readonly CoopCampaignSettlementHintIdentity SettlementHintIdentity = new CoopCampaignSettlementHintIdentity();
        private static SettlementMenuOverlayVM _settlementOverlay;
        private static Dictionary<string, SettlementHintSource> _settlementHintSources = new Dictionary<string, SettlementHintSource>(StringComparer.Ordinal);
        private static readonly FieldInfo OverlaySettlement = typeof(SettlementMenuOverlayVM).GetField("_settlement", BindingFlags.Instance | BindingFlags.NonPublic);
        internal sealed class SettlementHintSource
        {
            internal string Key;
            internal object NativeSource;
            internal Settlement Settlement;
            internal object Target;
            internal object Overlay;
        }
        private static void CaptureSettlementHintTargets(SettlementMenuOverlayVM source, MenuViewContext context,
            string session, string menu, CoopCampaignSettlementMenuPresentation state)
        {
            var settlement = OverlaySettlement?.GetValue(source) as Settlement;
            if (settlement == null || !ReferenceEquals(settlement, Settlement.CurrentSettlement))
                throw new InvalidOperationException("Settlement overlay ownership changed.");
            var targets = new Dictionary<string, object>(StringComparer.Ordinal);
            var nextSources = new Dictionary<string, SettlementHintSource>(StringComparer.Ordinal);
            Action<string, object> add = (key, native) => {
                object target = native is GameMenuPartyItemVM row ? SettlementPortraitTarget(row) : null;
                if (!_settlementHintSources.TryGetValue(key, out var lease) || !ReferenceEquals(lease.NativeSource, native) ||
                    !ReferenceEquals(lease.Target, target) || !ReferenceEquals(lease.Settlement, settlement) || !ReferenceEquals(lease.Overlay, source))
                    lease = new SettlementHintSource { Key = key, NativeSource = native, Target = target, Settlement = settlement, Overlay = source };
                targets.Add(key, lease); nextSources.Add(key, lease);
            };
            if (state.Flags["IsInitializationOver"])
            {
                foreach (string key in CoopCampaignSettlementMenuPresentation.HintNames)
                {
                    if (!state.IsHintAvailable(key)) continue;
                    var native = typeof(SettlementMenuOverlayVM).GetProperty(key)?.GetValue(source);
                    if (native != null && (native.GetType() == typeof(BasicTooltipViewModel) || native.GetType() == typeof(HintViewModel)))
                        add(key, native);
                }
                for (int i = 0; i < source.CharacterList.Count; i++)
                    if (SettlementPortraitTarget(source.CharacterList[i]) != null) add("Character_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), source.CharacterList[i]);
                for (int i = 0; i < source.PartyList.Count; i++)
                    if (SettlementPortraitTarget(source.PartyList[i]) != null) add("Party_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), source.PartyList[i]);
            }
            if (!SettlementHintIdentity.TryUpdate(context.MenuContext, source, session, menu, targets, out var tokens))
                throw new InvalidOperationException("Settlement tooltip identity unavailable.");
            state.HintTokens = tokens; _settlementOverlay = source; _settlementHintSources = nextSources;
        }
        // Exact installed GameMenuPartyItemVM.ExecuteOpenTooltip precedence.
        internal static object SettlementPortraitTarget(GameMenuPartyItemVM row) =>
            (object)row?.Party?.MobileParty ?? row?.Settlement ?? (object)row?.Character?.HeroObject;
        internal static bool TryResolveCurrentSettlementHint(string session, string token, out SettlementHintSource source)
        {
            source = null;
            if (!CoopCampaignSettlementMenuPresentation.IsHintToken(token) || !TryCapture(session, out var current, out _) ||
                current?.SettlementOverlay == null || !CoopCampaignMapTooltipTargetAdmission.ContainsSettlementHint(current, token) ||
                !SettlementHintIdentity.TryResolve(_plunderContext, _settlementOverlay, session, current.MenuId, token, out var key, out var native)) return false;
            var settlement = OverlaySettlement?.GetValue(_settlementOverlay) as Settlement;
            if (settlement == null || !ReferenceEquals(settlement, Settlement.CurrentSettlement)) return false;
            source = native as SettlementHintSource;
            return source != null && source.Key == key && ReferenceEquals(source.Settlement, settlement);
        }
    }
}
