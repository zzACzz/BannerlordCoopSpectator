using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Helpers;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace CoopSpectator.Campaign
{
    internal sealed partial class CoopCampaignMapTooltipCapture
    {
        // Independent visual projection. Never select the hero on the
        // live SPInventoryVM: selection can alter host transaction/UI state.
        // Native rendering acceptance is separate from transport validation.
        internal bool TryPrepareLootHeroVisual(string window, string heroRowId, int equipmentMode,
            out CoopCampaignPresentationValue visual)
        {
            visual = null;
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread || _cleanupFaulted ||
                Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
            CoopCampaignMapTooltipTextTransaction<TextObject> textScope = null;
            CoopCampaignMapTooltipThreadStateScope languageScope = null;
            TaleWorlds.CampaignSystem.ViewModelCollection.HeroViewModel detached = null;
            var rows = new List<SPItemVM>();
            bool restored = false;
            try
            {
                if (equipmentMode < 0 || equipmentMode > 2 ||
                    !CoopCampaignAftermathCapture.TryResolveLootHero(window, heroRowId, out var owner, out var raw) ||
                    !TryBeginTextScope(out textScope) ||
                    !CoopCampaignMapTooltipLanguageState.TryBegin(_ownerThread, out languageScope)) return false;
                var hero = ((InventoryCharacterSelectorItemVM)raw).Hero;
                string loadoutVersion = CoopCampaignAftermathCapture.LootLoadoutVersion(hero);
                var character = hero.CharacterObject;
                var equipment = equipmentMode == 0 ? character.FirstCivilianEquipment :
                    equipmentMode == 1 ? character.FirstBattleEquipment : character.FirstStealthEquipment;
                if (equipment == null) return false;
                var elements = new EquipmentElement[(int)EquipmentIndex.NumEquipmentSetSlots];
                for (int i = 0; i < elements.Length; i++) elements[i] = equipment.GetEquipmentFromSlot((EquipmentIndex)i);
                var craftedAliases = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var element in elements)
                    if (!element.IsEmpty && BattleDetector.TryGetCraftedVisualMirrorId(element.Item, out string mirrorId))
                        craftedAliases[element.Item.StringId] = mirrorId;
                detached = new TaleWorlds.CampaignSystem.ViewModelCollection.HeroViewModel();
                detached.FillFrom(hero);
                detached.SetEquipment(equipment);
                // Match SPInventoryVM.UpdateCharacterArmorColor on this owned VM.
                detached.ArmorColor1 = equipment.IsStealth ? 4279111698u : hero.MapFaction?.Color ?? 0;
                detached.ArmorColor2 = equipment.IsStealth ? 4279111698u : hero.MapFaction?.Color2 ?? 0;
                var result = CoopCampaignPresentationValue.Node();
                result.Properties.Add("MainCharacter", CoopCampaignAftermathCapture.CapturePrepared(detached));
                // Same read-only calculations as installed SPInventoryVM's
                // UpdateCharacterArmorValues/RefreshCharacterTotalWeight. Do not
                // invoke either live VM method or change its selected character.
                var equipmentType = equipmentMode == 0 ? Equipment.EquipmentType.Civilian :
                    equipmentMode == 1 ? Equipment.EquipmentType.Battle : Equipment.EquipmentType.Stealth;
                result.Properties.Add("CurrentCharacterArmArmor", PreviewFloat(character.GetArmArmorSum(equipmentType)));
                result.Properties.Add("CurrentCharacterBodyArmor", PreviewFloat(character.GetBodyArmorSum(equipmentType)));
                result.Properties.Add("CurrentCharacterHeadArmor", PreviewFloat(character.GetHeadArmorSum(equipmentType)));
                result.Properties.Add("CurrentCharacterLegArmor", PreviewFloat(character.GetLegArmorSum(equipmentType)));
                result.Properties.Add("CurrentCharacterHorseArmor", PreviewFloat(character.GetHorseArmorSum(equipmentType)));
                float armorWeightFactor = character.GetPerkValue(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultPerks.Athletics.FormFittingArmor) ?
                    1f + TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultPerks.Athletics.FormFittingArmor.PrimaryBonus : 1f;
                result.Properties.Add("CurrentCharacterTotalEncumbrance", PreviewText(TaleWorlds.Library.MathF.Round(
                    equipment.GetTotalWeightOfWeapons() + equipment.GetTotalWeightOfArmor(forHuman: true) * armorWeightFactor, 1).ToString("0.0")));
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                var logic = typeof(SPInventoryVM).GetField("_inventoryLogic", fields)?.GetValue(owner) as InventoryLogic;
                if (logic == null) return false;
                var side = equipmentMode == 0 ? InventoryLogic.InventorySide.CivilianEquipment :
                    equipmentMode == 1 ? InventoryLogic.InventorySide.BattleEquipment : InventoryLogic.InventorySide.StealthEquipment;
                string[] names = { "CharacterWeapon1Slot", "CharacterWeapon2Slot", "CharacterWeapon3Slot", "CharacterWeapon4Slot",
                    "CharacterBannerSlot", "CharacterHelmSlot", "CharacterTorsoSlot", "CharacterBootSlot", "CharacterGloveSlot",
                    "CharacterCloakSlot", "CharacterMountSlot", "CharacterMountArmorSlot" };
                if (names.Length != elements.Length) return false;
                for (int i = 0; i < names.Length; i++)
                {
                    var element = elements[i];
                    var roster = new ItemRosterElement(element, 1);
                    var slot = element.IsEmpty ? new SPItemVM() : new SPItemVM(logic, character.IsFemale,
                        CharacterHelper.CanUseItem(character, element), InventoryScreenHelper.InventoryMode.Loot,
                        roster, side, logic.GetCostOfItemRosterElement(roster, side), (EquipmentIndex)i);
                    rows.Add(slot);
                    // These are detached equipment slots, not live inventory rows.
                    // Never register them as transferable/inspectable host row IDs.
                    var preparedSlot = CoopCampaignAftermathCapture.CapturePrepared(slot, registerRows: false);
                    preparedSlot.Properties.Add("ReplicaSlotIndex", new CoopCampaignPresentationValue {
                        Kind = CoopCampaignPresentationValueKind.Integer, Number = i });
                    result.Properties.Add(names[i], preparedSlot);
                }
                result.Properties.Add("HeroRowId", PreviewText(heroRowId));
                result.Properties.Add("ReplicaLoadoutVersion", PreviewText(loadoutVersion));
                result.Properties.Add("EquipmentMode", new CoopCampaignPresentationValue {
                    Kind = CoopCampaignPresentationValueKind.Integer, Number = equipmentMode });
                CoopCampaignAftermathEquipmentProjection.ApplyCraftedAliases(result, craftedAliases);
                if (!CoopCampaignAftermathCapture.TryResolveLootHero(window, heroRowId, out var currentOwner, out var currentRow) ||
                    !ReferenceEquals(owner, currentOwner) || !ReferenceEquals(raw, currentRow)) return false;
                var currentEquipment = equipmentMode == 0 ? character.FirstCivilianEquipment :
                    equipmentMode == 1 ? character.FirstBattleEquipment : character.FirstStealthEquipment;
                if (!ReferenceEquals(equipment, currentEquipment)) return false;
                for (int i = 0; i < elements.Length; i++)
                    if (!elements[i].IsEqualTo(equipment.GetEquipmentFromSlot((EquipmentIndex)i))) return false;
                if (loadoutVersion != CoopCampaignAftermathCapture.LootLoadoutVersion(hero)) return false;
                if (!new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Loot,
                    WindowId = window, Root = result }.IsValid()) return false;
                visual = result;
            }
            catch (Exception) { visual = null; }
            finally
            {
                try
                {
                    try { detached?.OnFinalize(); foreach (var row in rows) row.OnFinalize(); }
                    finally { try { languageScope?.Dispose(); } finally { textScope?.Dispose(); } }
                    restored = true;
                }
                catch (Exception) { _cleanupFaulted = true; visual = null; }
                finally { Interlocked.Exchange(ref _capturing, 0); }
            }
            return restored && visual != null;
        }
        private static CoopCampaignPresentationValue PreviewFloat(float value) => new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.Float, Number = value };

        // Called only through the publisher's admitted publication/session/lease
        // path. Independent equipment-slot admission still needs its own target.
        // Never call SPInventoryVM.ProcessItemTooltip or ItemMenuVM.SetItem:
        // both have side effects beyond detached property generation.
        internal bool TryPrepareLootCard(string window, string rowId, string heroRowId, int equipmentMode,
            int alternativeUsage, int comparisonOffset, out CoopCampaignPresentationValue card, int equipmentSlot = -1)
        {
            card = null;
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread || _cleanupFaulted ||
                Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
            CoopCampaignMapTooltipTextTransaction<TextObject> textScope = null;
            CoopCampaignMapTooltipThreadStateScope languageScope = null;
            ItemMenuVM detached = null;
            var equipmentRows = new List<SPItemVM>();
            bool restored = false;
            try
            {
                if (equipmentMode < 0 || equipmentMode > 2 || equipmentSlot < -1 || equipmentSlot >= 12 ||
                    alternativeUsage < 0 || comparisonOffset < 0 || comparisonOffset >= 4 ||
                    !CoopCampaignAftermathCapture.TryResolveLootHero(window, heroRowId, out var heroOwner, out var heroRow) ||
                    !TryBeginTextScope(out textScope) ||
                    !CoopCampaignMapTooltipLanguageState.AreItemWritableTemplatesIsolated() ||
                    !CoopCampaignMapTooltipLanguageState.TryBegin(_ownerThread, out languageScope)) return false;
                object owner = heroOwner, raw = null;
                if (equipmentSlot < 0 && (!CoopCampaignAftermathCapture.TryResolveLootRow(window, rowId, out owner, out raw) ||
                    !ReferenceEquals(owner, heroOwner))) return false;
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                var logic = typeof(SPInventoryVM).GetField("_inventoryLogic", fields)?.GetValue(owner) as InventoryLogic;
                var usageFlags = typeof(SPInventoryVM).GetField("_getItemUsageSetFlags", fields)?.GetValue(owner)
                    as Func<WeaponComponentData, ItemObject.ItemUsageSetFlags>;
                var target = raw as SPItemVM;
                var refresh = typeof(ItemMenuVM).GetMethod("RefreshItemTooltips", fields);
                var targetField = typeof(ItemMenuVM).GetField("_targetItem", fields);
                var characterField = typeof(ItemMenuVM).GetField("_character", fields);
                if (logic == null || usageFlags == null ||
                    refresh == null || targetField == null || characterField == null) return false;
                var character = ((InventoryCharacterSelectorItemVM)heroRow).Hero.CharacterObject;
                var equipment = equipmentMode == 0 ? character.FirstCivilianEquipment :
                    equipmentMode == 1 ? character.FirstBattleEquipment : character.FirstStealthEquipment;
                var side = equipmentMode == 0 ? InventoryLogic.InventorySide.CivilianEquipment :
                    equipmentMode == 1 ? InventoryLogic.InventorySide.BattleEquipment : InventoryLogic.InventorySide.StealthEquipment;
                if (equipment == null) return false;
                var elements = new EquipmentElement[(int)EquipmentIndex.NumEquipmentSetSlots];
                var slots = new Dictionary<EquipmentIndex, SPItemVM>();
                for (int i = 0; i < elements.Length; i++)
                {
                    var index = (EquipmentIndex)i;
                    var element = equipment.GetEquipmentFromSlot(index); elements[i] = element;
                    if (element.IsEmpty) continue;
                    var roster = new ItemRosterElement(element, 1);
                    var row = new SPItemVM(logic, character.IsFemale, CharacterHelper.CanUseItem(character, element),
                        InventoryScreenHelper.InventoryMode.Loot, roster, side,
                        logic.GetCostOfItemRosterElement(roster, side), index);
                    slots.Add(index, row); equipmentRows.Add(row);
                }
                if (equipmentSlot >= 0) slots.TryGetValue((EquipmentIndex)equipmentSlot, out target);
                if (target == null || target.ItemRosterElement.EquipmentElement.IsEmpty) return false;
                var itemObject = target.ItemRosterElement.EquipmentElement.Item;
                int usages = itemObject.Weapons?.Count ?? 0;
                if (!CoopCampaignAftermathComparisonPolicy.IsValidSelection(
                    equipmentMode, alternativeUsage, usages, comparisonOffset)) return false;
                SPItemVM compared = null;
                if (!InventoryLogic.IsEquipmentSide(target.InventorySide))
                {
                    if (target.ItemType >= EquipmentIndex.WeaponItemBeginSlot && target.ItemType < EquipmentIndex.ExtraWeaponSlot)
                    {
                        var candidates = equipmentRows.Where(row => row.ItemType >= EquipmentIndex.WeaponItemBeginSlot &&
                            row.ItemType < EquipmentIndex.ExtraWeaponSlot &&
                            ItemHelper.CheckComparability(itemObject, row.ItemRosterElement.EquipmentElement.Item, alternativeUsage))
                            .OrderBy(row => ComparisonPriority(target, row)).ToArray();
                        if (candidates.Length > 0) compared = candidates[comparisonOffset % candidates.Length];
                    }
                    else slots.TryGetValue(target.ItemType, out compared);
                }
                detached = new ItemMenuVM((item, usage) => { }, logic, usageFlags,
                    slot => slots.TryGetValue(slot, out var row) ? row : null);
                detached.IsInitializationOver = false;
                targetField.SetValue(detached, target);
                characterField.SetValue(detached, character);
                var comparedField = typeof(ItemMenuVM).GetField("_comparedItem", fields);
                if (comparedField == null) return false;
                comparedField.SetValue(detached, compared);
                detached.IsPlayerItem = target.InventorySide == InventoryLogic.InventorySide.PlayerInventory;
                detached.ImageIdentifier = target.ImageIdentifier;
                detached.ItemName = target.ItemDescription;
                detached.IsComparing = compared != null;
                detached.IsStealthModeActive = equipmentMode == 2;
                detached.ComparedImageIdentifier = compared?.ImageIdentifier;
                detached.ComparedItemName = compared?.ItemDescription;
                refresh.Invoke(detached, new object[] { target, compared, alternativeUsage });
                var prepared = CoopCampaignAftermathCapture.CapturePrepared(detached);
                if (equipmentSlot < 0 && (!CoopCampaignAftermathCapture.TryResolveLootRow(window, rowId, out var currentOwner, out var currentRow) ||
                    !ReferenceEquals(owner, currentOwner) || !ReferenceEquals(raw, currentRow))) return false;
                if (!CoopCampaignAftermathCapture.TryResolveLootHero(window, heroRowId, out var currentHeroOwner, out var currentHero) ||
                    !ReferenceEquals(owner, currentHeroOwner) || !ReferenceEquals(heroRow, currentHero)) return false;
                var currentEquipment = equipmentMode == 0 ? character.FirstCivilianEquipment :
                    equipmentMode == 1 ? character.FirstBattleEquipment : character.FirstStealthEquipment;
                if (!ReferenceEquals(equipment, currentEquipment)) return false;
                for (int i = 0; i < elements.Length; i++)
                    if (!elements[i].IsEqualTo(equipment.GetEquipmentFromSlot((EquipmentIndex)i))) return false;
                card = prepared;
            }
            catch (Exception) { card = null; }
            finally
            {
                try
                {
                    try { detached?.OnFinalize(); foreach (var row in equipmentRows) row.OnFinalize(); }
                    finally { try { languageScope?.Dispose(); } finally { textScope?.Dispose(); } }
                    restored = true;
                }
                catch (Exception) { _cleanupFaulted = true; card = null; }
                finally { Interlocked.Exchange(ref _capturing, 0); }
            }
            return restored && card != null;
        }

        private static int ComparisonPriority(SPItemVM target, SPItemVM candidate)
        {
            var a = target.ItemRosterElement.EquipmentElement.Item;
            var b = candidate.ItemRosterElement.EquipmentElement.Item;
            var aw = a.Weapons; var bw = b.Weapons;
            bool alternateClass = aw != null && bw != null && aw.Count > 0 && bw.Count > 0 &&
                ((aw.Count > 1 && aw[1].WeaponClass == bw[0].WeaponClass) ||
                 (bw.Count > 1 && bw[1].WeaponClass == aw[0].WeaponClass) ||
                 (aw.Count > 1 && bw.Count > 1 && aw[1].WeaponClass == bw[1].WeaponClass));
            return CoopCampaignAftermathComparisonPolicy.Priority(target.StringId == candidate.StringId,
                a.Type == b.Type, alternateClass);
        }
    }
}
