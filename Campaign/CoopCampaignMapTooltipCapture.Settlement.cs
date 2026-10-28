using System;
using System.Collections.Generic;
using System.Threading;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace CoopSpectator.Campaign
{
    internal sealed partial class CoopCampaignMapTooltipCapture
    {
        internal bool TryCaptureSettlementHint(CoopCampaignMapMenuCapture.SettlementHintSource source,
            string session, string token, long generation, long sequence, bool extended,
            out CoopCampaignMapTooltipPayload payload, out string reason)
        {
            if (source?.NativeSource?.GetType() == typeof(HintViewModel) || source?.Key == "WallsHint")
                return TryCaptureSettlementControl(source, session, token, generation, sequence, extended, out payload, out reason);
            return TryCaptureCore(null, null, session, token, generation, sequence, extended,
                out payload, out reason, settlementHint: source);
        }
        private static bool IsCurrentSettlementHint(CoopCampaignMapMenuCapture.SettlementHintSource expected, string session, string token) =>
            expected != null && IsMapContext() && CoopCampaignMapMenuCapture.TryResolveCurrentSettlementHint(session, token, out var current) &&
            ReferenceEquals(expected, current);
        private static List<TooltipProperty> CaptureSettlementRows(CoopCampaignMapMenuCapture.SettlementHintSource source)
        {
            var settlement = source.Settlement;
            switch (source.Key)
            {
                case "MilitasHint": return settlement.IsFortification ? CampaignUIHelper.GetTownMilitiaTooltip(settlement.Town) : CampaignUIHelper.GetVillageMilitiaTooltip(settlement.Village);
                case "ProsperityHint": return settlement.IsFortification ? CampaignUIHelper.GetTownProsperityTooltip(settlement.Town) : CampaignUIHelper.GetVillageProsperityTooltip(settlement.Village);
                case "RemainingFoodHint": return settlement.Town == null ? null : CampaignUIHelper.GetTownFoodTooltip(settlement.Town);
                case "LoyaltyHint": return settlement.Town == null ? null : CampaignUIHelper.GetTownLoyaltyTooltip(settlement.Town);
                case "SecurityHint": return settlement.Town == null ? null : CampaignUIHelper.GetTownSecurityTooltip(settlement.Town);
                case "GarrisonHint": return settlement.Town == null ? null : CampaignUIHelper.GetTownGarrisonTooltip(settlement.Town);
                // The native overlay wrapper raises CrimeValueInspected...;
                // its audited pure row helper preserves text without that event.
                case "CrimeHint": return CampaignUIHelper.GetCrimeTooltip(settlement);
                default: return null;
            }
        }
        private bool TryCaptureSettlementControl(CoopCampaignMapMenuCapture.SettlementHintSource source,
            string session, string token, long generation, long sequence, bool extended,
            out CoopCampaignMapTooltipPayload payload, out string reason)
        {
            payload = null; reason = "settlement-control-not-ready";
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread || _cleanupFaulted ||
                !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(session, token, generation, sequence) ||
                Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
            CoopCampaignMapTooltipTextTransaction<TextObject> textScope = null;
            CoopCampaignMapTooltipThreadStateScope languageScope = null;
            bool success = false;
            try
            {
                if (!IsCurrentSettlementHint(source, session, token) || !TryBeginTextScope(out textScope) ||
                    !CoopCampaignMapTooltipLanguageState.AreSettlementWritableTemplatesIsolated(false) ||
                    !CoopCampaignMapTooltipLanguageState.TryBegin(_ownerThread, out languageScope)) return false;
                string text;
                if (source.NativeSource?.GetType() == typeof(HintViewModel))
                    text = ((HintViewModel)source.NativeSource).HintText?.ToString();
                else if (source.Key == "WallsHint" && source.Settlement.Town != null)
                    text = CampaignUIHelper.GetTownWallsTooltip(source.Settlement.Town);
                else return false;
                if (string.IsNullOrWhiteSpace(text)) return false;
                var result = new CoopCampaignMapTooltipPayload { SessionId = session, EntityId = token,
                    RequestGeneration = generation, Sequence = sequence, IsExtended = extended,
                    Kind = CoopCampaignMapTooltipKind.SettlementControl, ControlHint = new CoopCampaignMapControlHint { Text = text } };
                if (!IsCurrentSettlementHint(source, session, token) || !CoopCampaignMapTooltipPayloadCodec.IsValid(result)) return false;
                payload = result; reason = null; success = true;
            }
            catch (Exception) { reason = "settlement-control-capture-failed"; }
            finally
            {
                try { try { languageScope?.Dispose(); } finally { textScope?.Dispose(); } }
                catch (Exception) { _cleanupFaulted = true; payload = null; success = false; reason = "settlement-control-cleanup-failed"; }
                finally { Volatile.Write(ref _capturing, 0); }
            }
            return success;
        }
    }
}
