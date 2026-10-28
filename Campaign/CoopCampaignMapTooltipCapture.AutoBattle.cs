using System;
using System.Collections.Generic;
using System.Threading;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace CoopSpectator.Campaign
{
    internal sealed partial class CoopCampaignMapTooltipCapture
    {
        internal bool TryCaptureAutoBattleExplanation(ExplainedNumber explanation, out CoopCampaignPresentationValue rows)
        {
            rows = null;
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread || _cleanupFaulted ||
                Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
            CoopCampaignMapTooltipTextTransaction<TextObject> textScope = null;
            CoopCampaignMapTooltipThreadStateScope languageScope = null;
            bool success = false;
            try
            {
                if (!IsMapContext() || !TryBeginTextScope(out textScope) ||
                    !CoopCampaignMapTooltipLanguageState.AreAutoBattleWritableTemplatesIsolated() ||
                    !CoopCampaignMapTooltipLanguageState.TryBegin(_ownerThread, out languageScope)) return false;
                // Native formatter writes NUMBER. Copy input and restore text / language
                // state even on exception. Never invoke a reward delegate or recalculate.
                var native = SandBox.ViewModelCollection.SandBoxUIHelper.GetExplainedNumberTooltip(ref explanation);
                if (native == null || native.Count < 1 || native.Count > 128) return false;
                var captured = new CoopCampaignPresentationValue { Kind = CoopCampaignPresentationValueKind.List,
                    Items = new List<CoopCampaignPresentationValue>() };
                foreach (TooltipProperty row in native)
                    captured.Items.Add(CoopCampaignAftermathCapture.CapturePrepared(row, false));
                rows = captured; success = true;
            }
            catch (Exception) { rows = null; success = false; }
            finally
            {
                try { try { languageScope?.Dispose(); } finally { textScope?.Dispose(); } }
                catch (Exception) { _cleanupFaulted = true; rows = null; success = false; }
                finally { Volatile.Write(ref _capturing, 0); }
            }
            return success;
        }
    }
}
