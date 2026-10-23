using System;
using System.Reflection;
using System.Threading;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core.ViewModelCollection.Information.RundownTooltip;
using TaleWorlds.Library;
using TaleWorlds.Library.Information;
using TaleWorlds.Localization;

namespace CoopSpectator.Campaign
{
    internal sealed partial class CoopCampaignMapTooltipCapture
    {
        private sealed class RundownCaptureContext { }
        private static readonly FieldInfo InfoTrigger = typeof(MapInfoItemVM).GetField("_tooltipTrigger", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TriggerArgs = typeof(TooltipTriggerVM).GetField("_args", BindingFlags.Instance | BindingFlags.NonPublic);

        internal bool TryCaptureBarGold(MapInfoItemVM source, string session, string token, long generation, long sequence,
            bool extended, out CoopCampaignMapTooltipPayload payload, out string reason)
        {
            payload = null; reason = "bar-gold-context-unavailable";
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread || _cleanupFaulted ||
                !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(session, token, generation, sequence) ||
                Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
            CoopCampaignMapTooltipTextTransaction<TextObject> textScope = null;
            CoopCampaignMapTooltipThreadStateScope languageScope = null;
            RundownTooltipVM vm = null;
            bool success = false;
            try
            {
                if (!IsMapContext() || source?.GetType() != typeof(MapInfoItemVM) || source.ItemId != "gold" ||
                    !CoopCampaignMapBarCapture.TryResolveCurrentInfoHint(session, token, out var current) || !ReferenceEquals(source, current)) return false;
                var model = TaleWorlds.CampaignSystem.Campaign.Current.Models.ClanFinanceModel;
                if (model?.GetType() != typeof(DefaultClanFinanceModel) || InformationManager.RegisteredTypes.ContainsKey(typeof(RundownCaptureContext))) return false;
                var trigger = InfoTrigger?.GetValue(source) as TooltipTriggerVM;
                var nativeArgs = trigger == null ? null : TriggerArgs?.GetValue(trigger) as object[];
                if (nativeArgs == null || nativeArgs.Length != 5 ||
                    !(nativeArgs[0] is Func<ExplainedNumber> normal) || !(nativeArgs[1] is Func<ExplainedNumber> detailed) ||
                    normal.Method.DeclaringType?.DeclaringType != typeof(CampaignUIHelper) ||
                    detailed.Method.DeclaringType?.DeclaringType != typeof(CampaignUIHelper) ||
                    !normal.Method.Name.Contains("GetDenarTooltip") || !detailed.Method.Name.Contains("GetDenarTooltip") ||
                    !(nativeArgs[2] is TextObject title) || !(nativeArgs[3] is TextObject total) ||
                    !(nativeArgs[4] is RundownTooltipVM.ValueCategorization category)) return false;
                if (!TryBeginTextScope(out textScope) || !CoopCampaignMapTooltipLanguageState.AreRundownWritableTemplatesIsolated() ||
                    !CoopCampaignMapTooltipLanguageState.TryBegin(_ownerThread, out languageScope)) return false;
                // Do not invoke opaque delegates; explicitly preserve native read-only flags.
                object[] args = {
                    new Func<ExplainedNumber>(() => model.CalculateClanGoldChange(Clan.PlayerClan, true, false, false)),
                    new Func<ExplainedNumber>(() => model.CalculateClanGoldChange(Clan.PlayerClan, true, false, true)),
                    title, total, category
                };
                vm = new RundownTooltipVM(typeof(RundownCaptureContext), args);
                vm.IsExtended = extended;
                TooltipRefresherCollection.RefreshExplainedNumberTooltip(vm, args);
                vm.RefreshValues();
                var data = new CoopCampaignMapRundownPresentation {
                    IsActive = vm.IsActive, IsExtended = vm.IsExtended, ValueCategorizationAsInt = vm.ValueCategorizationAsInt,
                    CurrentExpectedChange = vm.CurrentExpectedChange, TitleText = vm.TitleText,
                    ExpectedChangeText = vm.ExpectedChangeText, ExtendText = vm.ExtendText
                };
                if (vm.Lines == null || vm.Lines.Count > CoopCampaignMapRundownPresentation.MaxLines) return false;
                foreach (var line in vm.Lines)
                {
                    if (line == null) return false;
                    data.Lines.Add(new CoopCampaignMapRundownLine { Name = line.Name, Value = line.Value, ValueAsString = line.ValueAsString });
                }
                var result = new CoopCampaignMapTooltipPayload { SessionId = session, EntityId = token,
                    RequestGeneration = generation, Sequence = sequence, IsExtended = extended,
                    Kind = CoopCampaignMapTooltipKind.BarRundown, Rundown = data };
                if (!IsMapContext() || !CoopCampaignMapBarCapture.TryResolveCurrentInfoHint(session, token, out current) ||
                    !ReferenceEquals(source, current) || !CoopCampaignMapTooltipPayloadCodec.IsValid(result)) return false;
                payload = result; reason = null; success = true;
            }
            catch (Exception) { reason = "bar-gold-capture-failed"; }
            finally
            {
                try
                {
                    try { vm?.OnFinalize(); }
                    finally { try { languageScope?.Dispose(); } finally { textScope?.Dispose(); } }
                }
                catch (Exception) { _cleanupFaulted = true; payload = null; success = false; reason = "bar-gold-cleanup-failed"; }
                finally { Volatile.Write(ref _capturing, 0); }
            }
            return success;
        }
    }
}
