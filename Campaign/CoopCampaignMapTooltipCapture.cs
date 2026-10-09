using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    // Host-main-thread adapter only, called by the launcher-owned local relay.
    // Caller must resolve a current admitted catalog target before calling.
    internal sealed class CoopCampaignMapTooltipCapture
    {
        private sealed class CaptureContext { }
        private readonly int _ownerThread = Thread.CurrentThread.ManagedThreadId;
        private static int _capturing;
        private static bool _cleanupFaulted;
        private static readonly FieldInfo ContextField = typeof(MBTextManager).GetField(
            "TextContext", BindingFlags.Static | BindingFlags.NonPublic);

        internal bool TryCapture(PartyBase target, string session, string entityId,
            long generation, long sequence, bool extended,
            out CoopCampaignMapTooltipPayload payload, out string reason)
        {
            payload = null;
            reason = "tooltip-context-not-ready";
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread || _cleanupFaulted ||
                !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(session, entityId, generation, sequence) ||
                Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
            PropertyBasedTooltipVM vm = null;
            CoopCampaignMapTooltipTextTransaction<TextObject> textScope = null;
            CoopCampaignMapTooltipThreadStateScope languageScope = null;
            bool success = false;
            try
            {
                if (!IsMapContext() || !IsDisclosedTarget(target, entityId) ||
                    !TryRoute(target, out Type type, out object[] args,
                        out CoopCampaignMapTooltipKind kind, out Action<PropertyBasedTooltipVM, object[]> provider)) return false;
                if (!InformationManager.RegisteredTypes.TryGetValue(type, out var registration) ||
                    registration.TooltipType != typeof(PropertyBasedTooltipVM) ||
                    registration.MovieName != "PropertyBasedTooltip" ||
                    !(registration.OnRefreshData is Action<PropertyBasedTooltipVM, object[]> registered) ||
                    registered.Method != provider.Method || registered.Target != provider.Target ||
                    InformationManager.RegisteredTypes.ContainsKey(typeof(CaptureContext)))
                {
                    reason = "tooltip-provider-incompatible";
                    return false;
                }
                if (!TryBeginTextScope(out textScope))
                {
                    reason = "tooltip-text-context-incompatible-or-busy";
                    return false;
                }
                if (!CoopCampaignMapTooltipLanguageState.AreWritableTemplatesIsolated() ||
                    !CoopCampaignMapTooltipLanguageState.TryBegin(_ownerThread, out languageScope))
                {
                    reason = "tooltip-language-state-incompatible";
                    return false;
                }
                // An unregistered type creates an empty, inactive local VM.
                // Set extension before calling the verified native provider once;
                // no global ShowTooltip, registration or host hover is performed.
                vm = new PropertyBasedTooltipVM(typeof(CaptureContext), Array.Empty<object>());
                vm.IsExtended = extended;
                if (string.IsNullOrWhiteSpace(vm.GetKeyText("ExtendModifier")) ||
                    (kind == CoopCampaignMapTooltipKind.Settlement &&
                        (string.IsNullOrWhiteSpace(vm.GetKeyText("MapClick")) ||
                         string.IsNullOrWhiteSpace(vm.GetKeyText("FollowModifier")))))
                {
                    reason = "tooltip-key-text-not-ready";
                    return false;
                }
                provider(vm, args);
                if (vm.TooltipPropertyList.Count < 1 ||
                    vm.TooltipPropertyList.Count > CoopCampaignMapTooltipPayloadCodec.MaxRows)
                {
                    reason = "tooltip-empty-or-too-many-rows";
                    return false;
                }
                var result = new CoopCampaignMapTooltipPayload {
                    SessionId = session, EntityId = entityId, RequestGeneration = generation,
                    Sequence = sequence, IsExtended = extended, Kind = kind, Mode = vm.Mode
                };
                foreach (TooltipProperty row in vm.TooltipPropertyList)
                {
                    if (row == null) { reason = "tooltip-null-row"; return false; }
                    Color color = row.TextColor;
                    result.Rows.Add(new CoopCampaignMapTooltipRow {
                        Definition = row.DefinitionLabel, Value = row.ValueLabel,
                        TextHeight = row.TextHeight, PropertyModifier = row.PropertyModifier,
                        Red = color.Red, Green = color.Green, Blue = color.Blue, Alpha = color.Alpha
                    });
                }
                if (!IsMapContext() || !IsDisclosedTarget(target, entityId) ||
                    !CoopCampaignMapTooltipPayloadCodec.IsValid(result))
                {
                    reason = "tooltip-capture-invalidated";
                    return false;
                }
                payload = result;
                reason = null;
                success = true;
            }
            catch (Exception)
            {
                reason = "tooltip-native-capture-failed";
                return false;
            }
            finally
            {
                try
                {
                    try { vm?.OnFinalize(); }
                    finally
                    {
                        try { languageScope?.Dispose(); }
                        finally { textScope?.Dispose(); }
                    }
                }
                catch (Exception)
                {
                    // Do not publish after uncertain cleanup, and do not repeat
                    // a native capture in this process after such a fault.
                    _cleanupFaulted = true;
                    payload = null;
                    reason = "tooltip-cleanup-failed";
                    success = false;
                }
                finally { Volatile.Write(ref _capturing, 0); }
            }
            return success;
        }

        internal static bool IsMapContext()
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            return campaign?.MainParty != null && MapScreen.Instance != null && Mission.Current == null &&
                Game.Current?.GameStateManager?.ActiveState is MapState &&
                campaign.ConversationManager != null && !campaign.ConversationManager.IsConversationInProgress;
        }

        private static bool IsDisclosedTarget(PartyBase target, string entityId)
        {
            if (target == null || !target.IsVisible) return false;
            if (target.IsSettlement && target.Settlement != null)
            {
                Settlement settlement = target.Settlement;
                ISpottable spottable = settlement.SettlementComponent as ISpottable;
                return entityId == "settlement:" + settlement.StringId &&
                    CoopCampaignMapPrototypeVisibilityPolicy.ShouldReplicateSettlement(
                        settlement.IsVisible, spottable != null, spottable?.IsSpotted == true);
            }
            MobileParty party = target.MobileParty;
            return target.IsMobile && party != null && party.IsActive && party.CurrentSettlement == null &&
                (party.IsMainParty || !party.IsInRaftState) &&
                entityId == "party:" + party.StringId &&
                CoopCampaignMapPrototypeVisibilityPolicy.ShouldReplicateMobileParty(
                    party == MobileParty.MainParty, party.IsActive, party.IsGarrison, party.IsMilitia,
                    party.CurrentSettlement != null, party.IsSpotted());
        }

        private static bool TryRoute(PartyBase target, out Type type, out object[] args,
            out CoopCampaignMapTooltipKind kind, out Action<PropertyBasedTooltipVM, object[]> provider)
        {
            type = null; args = null; kind = default; provider = null;
            // Same precedence as native MobilePartyVisual/SettlementVisual.OnHover,
            // but only after the stronger current host disclosure admission.
            if (target.MapEvent != null)
            {
                var mapEvent = target.MapEvent;
                if (mapEvent.AttackerSide?.LeaderParty == null || mapEvent.DefenderSide?.LeaderParty == null) return false;
                type = typeof(MapEvent); args = new object[] { mapEvent };
                kind = CoopCampaignMapTooltipKind.MapEvent; provider = TooltipRefresherCollection.RefreshMapEventTooltip;
                return true;
            }
            Army army = target.MobileParty?.Army;
            bool attachedArmy = army != null && army.LeaderParty != null &&
                army.DoesLeaderPartyAndAttachedPartiesContain(target.MobileParty);
            SiegeEvent siege = target.IsSettlement ? target.Settlement?.SiegeEvent :
                attachedArmy ? army.LeaderParty.SiegeEvent : target.MobileParty?.SiegeEvent;
            if (siege != null)
            {
                if (siege.ReadyToBeRemoved || siege.BesiegerCamp == null || siege.BesiegedSettlement == null) return false;
                type = typeof(SiegeEvent); args = new object[] { siege };
                kind = CoopCampaignMapTooltipKind.SiegeEvent; provider = TooltipRefresherCollection.RefreshSiegeEventTooltip;
            }
            else if (target.IsSettlement)
            {
                type = typeof(Settlement); args = new object[] { target.Settlement };
                kind = CoopCampaignMapTooltipKind.Settlement; provider = TooltipRefresherCollection.RefreshSettlementTooltip;
            }
            else if (attachedArmy)
            {
                type = typeof(Army); args = new object[] { army, false, true };
                kind = CoopCampaignMapTooltipKind.Army; provider = TooltipRefresherCollection.RefreshArmyTooltip;
            }
            else if (target.MobileParty != null)
            {
                type = typeof(MobileParty); args = new object[] { target.MobileParty, false, true };
                kind = CoopCampaignMapTooltipKind.MobileParty; provider = TooltipRefresherCollection.RefreshMobilePartyTooltip;
            }
            return provider != null;
        }

        private bool TryBeginTextScope(out CoopCampaignMapTooltipTextTransaction<TextObject> scope)
        {
            scope = null;
            object context = ContextField?.GetValue(null);
            Type contextType = context?.GetType();
            if (contextType?.FullName != "TaleWorlds.Localization.TextProcessor.TextProcessingContext") return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var variables = contextType.GetField("_variables", flags)?.GetValue(context) as Dictionary<string, TextObject>;
            var evaluated = contextType.GetField("_curParams", flags)?.GetValue(context) as Stack<TextObject[]>;
            var raw = contextType.GetField("_curParamsWithoutEvaluate", flags)?.GetValue(context) as Stack<TextObject[]>;
            return CoopCampaignMapTooltipTextTransaction<TextObject>.TryBegin(variables, evaluated, raw, _ownerThread, out scope);
        }
    }
}
