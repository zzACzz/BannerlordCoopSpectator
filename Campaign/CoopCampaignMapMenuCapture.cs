using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Patches;
using SandBox.View.Map;
using SandBox.View.Menu;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace CoopSpectator.Campaign
{
    internal static partial class CoopCampaignMapMenuCapture
    {
        private sealed class EquipmentIdentityComparer : IEqualityComparer<EquipmentElement>
        {
            public bool Equals(EquipmentElement a, EquipmentElement b) =>
                ReferenceEquals(a.Item, b.Item) && ReferenceEquals(a.ItemModifier, b.ItemModifier) &&
                ReferenceEquals(a.CosmeticItem, b.CosmeticItem) && a.IsQuestItem == b.IsQuestItem;
            public int GetHashCode(EquipmentElement value) => 0; // Lease performs bounded linear comparisons only.
        }
        private static readonly CoopCampaignMapPlunderIdentity<EquipmentElement> PlunderIdentity =
            new CoopCampaignMapPlunderIdentity<EquipmentElement>(new EquipmentIdentityComparer());
        private static string _plunderSession;
        private static object _plunderOwner;
        private static object _plunderContext;
        private static string _plunderMenuId;
        private static readonly CoopCampaignMapEncounterIdentity EncounterIdentity = new CoopCampaignMapEncounterIdentity();
        private static object _encounterOverlay;

        internal static void ResetPlunderIdentity()
        {
            PlunderIdentity.Clear();
            EncounterIdentity.Clear(); _encounterOverlay = null;
            SettlementHintIdentity.Clear(); _settlementOverlay = null;
            _settlementHintSources.Clear();
            _plunderSession = null; _plunderOwner = null; _plunderContext = null; _plunderMenuId = null;
        }

        private static readonly FieldInfo ContextField = typeof(MapScreen).GetField(
            "_menuViewContext", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ActiveField = typeof(MenuViewContext).GetField(
            "_isActive", BindingFlags.Instance | BindingFlags.NonPublic);

        // Call on the campaign/UI thread after native presentation has updated.
        // Null is an explicit hidden state, never permission to reuse an old menu.
        public static bool TryCapture(string session, out CoopCampaignMapMenuPresentation snapshot, out string reason)
        {
            snapshot = null;
            reason = null;
            try
            {
                if (string.IsNullOrEmpty(session)) return false;
                if (!string.Equals(session, _plunderSession, StringComparison.Ordinal)) ResetPlunderIdentity();
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                var screen = MapScreen.Instance;
                if (campaign == null || screen == null ||
                    Mission.Current != null || !(Game.Current?.GameStateManager?.ActiveState is MapState) ||
                    campaign.ConversationManager?.IsConversationInProgress == true)
                { CoopCampaignAutoBattleCapture.Reset(); return true; }
                if (!CoopCampaignAutoBattleCapture.TryCapture(session, out var simulation, out reason)) return false;
                if (simulation != null)
                {
                    var autoMenu = new CoopCampaignMapMenuPresentation { MenuId = "autobattle:" + simulation.WindowId, AutoBattle = simulation };
                    return autoMenu.TrySnapshot(out snapshot);
                }
                if (!screen.IsInMenu) return true;
                if (ContextField == null || ActiveField == null)
                { reason = "native-menu-layout-unavailable"; return false; }
                var context = ContextField.GetValue(screen) as MenuViewContext;
                if (context == null || !(ActiveField.GetValue(context) is bool active) || !active)
                    return true;
                GameMenuVM source = null;
                foreach (var view in context.MenuViews)
                {
                    // Avoid a compile-time dependency on Gauntlet's concrete view.
                    var property = view?.GetType().GetProperty("GameMenuDataSource",
                        BindingFlags.Instance | BindingFlags.Public);
                    if (property?.GetValue(view) is GameMenuVM menu)
                    { source = menu; break; }
                }
                if (source == null || source.MenuContext != context.MenuContext)
                { reason = "native-menu-source-unavailable"; return false; }
                if (source.ItemList == null || source.ProgressItemList == null || source.PlunderItems == null ||
                    source.PlunderItems.Count > CoopCampaignMapMenuPresentation.MaxPlunderItems ||
                    source.ItemList.Count > CoopCampaignMapMenuPresentation.MaxOptions ||
                    source.ProgressItemList.Count > CoopCampaignMapMenuPresentation.MaxProgressItems)
                { reason = "native-menu-list-invalid"; return false; }
                var captured = new CoopCampaignMapMenuPresentation
                {
                    MenuId = source.MenuId ?? string.Empty,
                    Title = source.TitleText ?? string.Empty,
                    Context = source.ContextText ?? string.Empty,
                    Background = source.Background ?? string.Empty,
                    IsNight = source.IsNight,
                    IsEncounterMenu = source.IsEncounterMenu,
                    IsInSiegeMode = source.IsInSiegeMode
                };
                foreach (var option in source.ItemList)
                {
                    if (option == null) { reason = "native-menu-null-option"; return false; }
                    captured.Options.Add(new CoopCampaignMapMenuOption
                    {
                        Text = option.Item ?? string.Empty,
                        Hint = option.ItemHint?.HintText?.ToString() ?? string.Empty,
                        LeaveType = option.OptionLeaveType ?? string.Empty,
                        ItemType = option.ItemType, BattleSize = option.BattleSize,
                        IsEnabled = option.IsEnabled, IsWaitActive = option.IsWaitActive,
                        IsHighlightEnabled = option.IsHighlightEnabled, IsNavalBattle = option.IsNavalBattle
                    });
                }
                foreach (var progress in source.ProgressItemList)
                {
                    if (progress == null) { reason = "native-menu-null-progress"; return false; }
                    captured.ProgressItems.Add(new CoopCampaignMapMenuProgress
                    {
                        Text = progress.Text ?? string.Empty,
                        ProgressText = progress.ProgressText ?? string.Empty,
                        Progress = progress.Progress
                    });
                }
                foreach (var loot in source.PlunderItems)
                {
                    if (loot?.Item.Item == null) { reason = "native-menu-null-plunder"; return false; }
                    captured.PlunderItems.Add(new CoopCampaignMapMenuPlunder
                    { ItemId = loot.Item.Item.StringId, Amount = loot.Amount });
                }
                var equipment = new EquipmentElement[source.PlunderItems.Count];
                for (int i = 0; i < equipment.Length; i++) equipment[i] = source.PlunderItems[i].Item;
                if (!ReferenceEquals(_plunderContext, context.MenuContext)) PlunderIdentity.Clear();
                if (!PlunderIdentity.TryUpdate(source, captured.MenuId, equipment, out var tooltipIds))
                { reason = "native-plunder-identity-invalid"; return false; }
                _plunderSession = session; _plunderOwner = source;
                _plunderContext = context.MenuContext; _plunderMenuId = captured.MenuId;
                for (int i = 0; i < tooltipIds.Length; i++) captured.PlunderItems[i].TooltipId = tooltipIds[i];
                if (captured.IsEncounterMenu)
                {
                    SettlementHintIdentity.Clear(); _settlementOverlay = null;
                    _settlementHintSources.Clear();
                    captured.Encounter = CaptureEncounter(context, captured.MenuId);
                    if (captured.Encounter == null)
                    { reason = "native-encounter-source-unavailable"; return false; }
                }
                else
                {
                    EncounterIdentity.Clear(); _encounterOverlay = null;
                    captured.SettlementOverlay = CaptureSettlementOverlay(context, session, captured.MenuId);
                }
                if (!captured.TrySnapshot(out snapshot))
                { reason = "native-menu-presentation-invalid"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                snapshot = null;
                reason = "native-menu-capture-" + ex.GetType().Name;
                return false;
            }
            finally
            {
                // Hidden, failed or incomplete capture must revoke old capabilities.
                if (snapshot == null) ResetPlunderIdentity();
            }
        }

        internal static bool TryResolveCurrentPlunder(string session, string tooltipId, out EquipmentElement equipment)
        {
            equipment = default(EquipmentElement);
            // Re-read current native visibility/owner/equipment, never trust a
            // previously published token alone. Caller must additionally admit
            // the token against its successful publication and peer lease.
            return !string.IsNullOrEmpty(tooltipId) &&
                TryCapture(session, out var current, out _) && current != null &&
                PlunderIdentity.TryResolve(_plunderOwner, _plunderMenuId, tooltipId, out equipment);
        }
        internal static bool TryResolveCurrentEncounter(string session, string tooltipId, out object target)
        {
            target = null;
            return CoopCampaignMapEncounterParty.IsTooltipId(tooltipId) &&
                TryCapture(session, out var current, out _) && current?.Encounter != null &&
                EncounterIdentity.TryResolve(_plunderContext, _encounterOverlay, current.MenuId, tooltipId, out target);
        }

        private static CoopCampaignMapEncounterPresentation CaptureEncounter(MenuViewContext context, string menuId)
        {
            // Read the existing UI owner only. Never invoke Refresh, constructors,
            // tooltip delegates or campaign strength calculations for replication.
            EncounterMenuOverlayVM source = null;
            foreach (var view in context.MenuViews)
            {
                var type = view?.GetType();
                if (type?.FullName != "SandBox.GauntletUI.Menu.GauntletMenuOverlayBaseView") continue;
                source = type.GetField("_overlayDataSource", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(view) as EncounterMenuOverlayVM;
                break;
            }
            if (source?.PowerComparer == null || source.AttackerPartyList == null || source.DefenderPartyList == null ||
                source.AttackerPartyList.Count > CoopCampaignMapEncounterPresentation.MaxPartiesPerSide ||
                source.DefenderPartyList.Count > CoopCampaignMapEncounterPresentation.MaxPartiesPerSide) return null;
            var power = source.PowerComparer;
            var result = new CoopCampaignMapEncounterPresentation
            {
                TitleText = source.TitleText ?? string.Empty, IsSiege = source.IsSiege, IsNaval = source.IsNaval,
                AttackerBanner = source.AttackerPartyBanner?.Id ?? string.Empty,
                DefenderBanner = source.DefenderPartyBanner?.Id ?? string.Empty,
                AttackerCountLabel = source.AttackerPartyCountLbl ?? string.Empty,
                DefenderCountLabel = source.DefenderPartyCountLbl ?? string.Empty,
                AttackerMorale = source.AttackerPartyMorale ?? string.Empty,
                DefenderMorale = source.DefenderPartyMorale ?? string.Empty,
                AttackerFood = source.AttackerPartyFood ?? string.Empty,
                DefenderFood = source.DefenderPartyFood ?? string.Empty,
                DefenderWallHitPoints = source.DefenderWallHitPoints ?? string.Empty,
                AttackerShipCount = source.AttackerShipCount, DefenderShipCount = source.DefenderShipCount,
                AttackerPower = power.AttackerBattlePowerValue, DefenderPower = power.DefenderBattlePowerValue,
                InitialAttackerPower = power.InitialAttackerBattlePowerValue,
                InitialDefenderPower = power.InitialDefenderBattlePowerValue,
                AttackerColor = power.AttackerColor, DefenderColor = power.DefenderColor
            };
            foreach (var party in source.AttackerPartyList) result.Attackers.Add(CaptureEncounterParty(party));
            foreach (var party in source.DefenderPartyList) result.Defenders.Add(CaptureEncounterParty(party));
            var attackers = new object[source.AttackerPartyList.Count];
            var defenders = new object[source.DefenderPartyList.Count];
            for (int i = 0; i < attackers.Length; i++) attackers[i] = EncounterTarget(source.AttackerPartyList[i]);
            for (int i = 0; i < defenders.Length; i++) defenders[i] = EncounterTarget(source.DefenderPartyList[i]);
            if (!EncounterIdentity.TryUpdate(context.MenuContext, source, menuId, attackers, defenders,
                out var attackerTokens, out var defenderTokens)) return null;
            _encounterOverlay = source;
            for (int i = 0; i < attackerTokens.Length; i++) result.Attackers[i].TooltipId = attackerTokens[i];
            for (int i = 0; i < defenderTokens.Length; i++) result.Defenders[i].TooltipId = defenderTokens[i];
            if (!CoopCampaignCraftedVisualCapture.TryProjectEncounter(result)) return null;
            return result.TrySnapshot(out var detached) ? detached : null;
        }

        private static CoopCampaignSettlementMenuPresentation CaptureSettlementOverlay(MenuViewContext context, string session, string menuId)
        {
            SettlementMenuOverlayVM source = null;
            foreach (var view in context.MenuViews)
            {
                var type = view?.GetType();
                if (type?.FullName != "SandBox.GauntletUI.Menu.GauntletMenuOverlayBaseView") continue;
                source = type.GetField("_overlayDataSource", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) as SettlementMenuOverlayVM;
                break;
            }
            if (source == null) { SettlementHintIdentity.Clear(); _settlementOverlay = null; _settlementHintSources.Clear(); return null; }
            if (source.CharacterList == null || source.PartyList == null ||
                source.CharacterList.Count > CoopCampaignMapEncounterPresentation.MaxPartiesPerSide ||
                source.PartyList.Count > CoopCampaignMapEncounterPresentation.MaxPartiesPerSide)
                throw new InvalidOperationException("Invalid settlement overlay lists.");
            var result = new CoopCampaignSettlementMenuPresentation { BannerCode = source.SettlementOwnerBanner?.Id ?? string.Empty };
            // Whitelisted audited scalar getters only. Never Refresh, tick,
            // produce tooltips or query simulation to rebuild undisclosed data.
            foreach (string name in CoopCampaignSettlementMenuPresentation.LabelNames)
                result.Labels[name] = (string)typeof(SettlementMenuOverlayVM).GetProperty(name).GetValue(source) ?? string.Empty;
            foreach (string name in CoopCampaignSettlementMenuPresentation.NumberNames)
                result.Numbers[name] = (int)typeof(SettlementMenuOverlayVM).GetProperty(name).GetValue(source);
            foreach (string name in CoopCampaignSettlementMenuPresentation.FlagNames)
                result.Flags[name] = (bool)typeof(SettlementMenuOverlayVM).GetProperty(name).GetValue(source);
            foreach (var character in source.CharacterList) result.Characters.Add(CaptureEncounterParty(character));
            foreach (var party in source.PartyList) result.Parties.Add(CaptureEncounterParty(party));
            CaptureSettlementHintTargets(source, context, session, menuId, result);
            if (!CoopCampaignCraftedVisualCapture.TryProjectParties(result.Characters.Concat(result.Parties), out var weapons))
                throw new InvalidOperationException("Settlement portrait resources unavailable.");
            result.VisualWeapons = weapons;
            if (!result.IsValid()) throw new InvalidOperationException("Invalid settlement overlay presentation.");
            return result;
        }

        private static object EncounterTarget(GameMenuPartyItemVM entry)
        {
            // Exact native ExecuteOpenTooltip precedence for encounter entries.
            // Character-only menus require a separate Hero-provider audit.
            if (entry?.Party?.MobileParty != null) return entry.Party.MobileParty;
            return entry?.Settlement;
        }

        private static CoopCampaignMapEncounterParty CaptureEncounterParty(GameMenuPartyItemVM party)
        {
            if (party == null || party.Quests == null || party.Quests.Count > 32) return null;
            if (party.Visual != null && (party.Visual.TextureProviderName != "CharacterImageTextureProvider" ||
                !string.IsNullOrEmpty(party.Visual.AdditionalArgs))) return null;
            var result = new CoopCampaignMapEncounterParty
            {
                PortraitCode = party.Visual?.Id ?? string.Empty, BannerCode = party.Banner_9?.Id ?? string.Empty,
                PortraitPose = CapturePortraitPose(party.Visual?.Id),
                NameText = party.NameText ?? string.Empty, PartySizeLabel = party.PartySizeLbl ?? string.Empty,
                DescriptionText = party.DescriptionText ?? string.Empty, LocationText = party.LocationText ?? string.Empty,
                PowerText = party.PowerText ?? string.Empty, ProfessionText = party.ProfessionText ?? string.Empty,
                Relation = party.Relation, ShipCount = party.ShipCount,
                IsPlayer = party.IsPlayer, IsEnemy = party.IsEnemy, IsAlly = party.IsAlly, IsNeutral = party.IsNeutral,
                IsCharacterInPrison = party.IsCharacterInPrison, IsHighlightEnabled = party.IsHighlightEnabled
            };
            foreach (var quest in party.Quests)
            {
                if (quest == null) return null;
                result.QuestMarkers.Add(quest.QuestMarkerType);
            }
            return result.IsValid() ? result : null;
        }

        private static int CapturePortraitPose(string visibleCode)
        {
            if (string.IsNullOrEmpty(visibleCode) ||
                Environment.GetEnvironmentVariable("COOPSPECTATOR_PORTRAIT_POSE_OBSERVER") != "1") return -1;
            // Native GameMenuPartyItem uses small mode, no AdditionalArgs. Create
            // detached managed request data solely to reproduce native key normalization;
            // never enqueue a render, mutate the VM's code or read native scene state.
            var character = CharacterCode.CreateFrom(visibleCode);
            if (!character.IsHero) return -1;
            var request = new CharacterThumbnailCreationData(character, null, null, false);
            return CoopPortraitPoseObservationPatch.TryGetCurrent(request.RenderId, out int pose) ? pose : -1;
        }
    }
}
