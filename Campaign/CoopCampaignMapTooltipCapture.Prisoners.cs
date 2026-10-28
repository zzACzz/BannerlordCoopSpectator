using System;
using System.Threading;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace CoopSpectator.Campaign
{
    internal sealed partial class CoopCampaignMapTooltipCapture
    {
        // Owned inspection projection. Do not call the host CurrentCharacter
        // setter: it refreshes live rows and removes zero counts from the roster.
        internal bool TryPreparePrisonerPreview(string window, string rowId, out CoopCampaignPresentationValue preview)
        {
            preview = null;
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread || _cleanupFaulted ||
                Interlocked.CompareExchange(ref _capturing, 1, 0) != 0) return false;
            CoopCampaignMapTooltipTextTransaction<TextObject> textScope = null;
            CoopCampaignMapTooltipThreadStateScope languageScope = null;
            HeroViewModel detached = null;
            bool restored = false;
            try
            {
                if (!CoopCampaignAftermathCapture.TryResolvePrisonerRow(window, rowId, out var owner, out var raw) ||
                    !TryBeginTextScope(out textScope) ||
                    !CoopCampaignMapTooltipLanguageState.TryBegin(_ownerThread, out languageScope)) return false;
                var row = (PartyCharacterVM)raw;
                var party = (PartyVM)owner;
                var character = row.Character;
                detached = new HeroViewModel();
                if (character.IsHero) detached.FillFrom(character.HeroObject);
                else
                {
                    string banner = string.Empty;
                    if (!row.IsPrisoner)
                    {
                        var ownerParty = row.Side == PartyScreenLogic.PartyRosterSide.Left ?
                            party.PartyScreenLogic.LeftOwnerParty : party.PartyScreenLogic.RightOwnerParty;
                        banner = ownerParty?.Banner?.BannerCode ?? string.Empty;
                    }
                    detached.FillFrom(character, character.StringId.GetDeterministicHashCode(), banner);
                }
                detached.SetEquipment(character.Equipment);
                if (!row.IsPrisoner)
                {
                    var faction = (row.Side == PartyScreenLogic.PartyRosterSide.Left ?
                        party.PartyScreenLogic.LeftOwnerParty : party.PartyScreenLogic.RightOwnerParty)?.MapFaction;
                    if (faction != null) { detached.ArmorColor1 = faction.Color; detached.ArmorColor2 = faction.Color2; }
                }
                var result = CoopCampaignPresentationValue.Node();
                result.Properties.Add("SelectedCharacter", CoopCampaignAftermathCapture.CapturePrepared(detached));
                var aliases = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 0; i < (int)TaleWorlds.Core.EquipmentIndex.NumEquipmentSetSlots; i++)
                {
                    var item = character.Equipment.GetEquipmentFromSlot((TaleWorlds.Core.EquipmentIndex)i).Item;
                    if (BattleDetector.TryGetCraftedVisualMirrorId(item, out string mirror)) aliases[item.StringId] = mirror;
                }
                result.Properties.Add("CurrentCharacter", CoopCampaignAftermathCapture.CapturePrepared(row));
                result.Properties.Add("CurrentCharacterLevelLbl", PreviewText(character.Level.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                result.Properties.Add("CurrentCharacterWageLbl", PreviewText(row.Type == PartyScreenLogic.TroopType.Member &&
                    character != CharacterObject.PlayerCharacter ? character.TroopWage.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty));
                result.Properties.Add("CurrentCharacterTier", CoopCampaignAftermathCapture.CapturePrepared(CampaignUIHelper.GetCharacterTierData(character, isBig: true)));
                result.Properties.Add("IsCurrentCharacterFormationEnabled", PreviewFlag(!row.IsMainHero && !row.IsPrisoner && row.Side != PartyScreenLogic.PartyRosterSide.Left));
                result.Properties.Add("IsCurrentCharacterWageEnabled", PreviewFlag(!row.IsMainHero && !row.IsPrisoner));
                CoopCampaignAftermathEquipmentProjection.ApplyCraftedAliases(result, aliases);
                if (!CoopCampaignAftermathCapture.TryResolvePrisonerRow(window, rowId, out var currentOwner, out var currentRow) ||
                    !ReferenceEquals(owner, currentOwner) || !ReferenceEquals(raw, currentRow) || !ReferenceEquals(character, row.Character)) return false;
                if (!new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Prisoners, WindowId = window, Root = result }.IsValid()) return false;
                preview = result;
            }
            catch (Exception) { preview = null; }
            finally
            {
                try
                {
                    try { detached?.OnFinalize(); }
                    finally { try { languageScope?.Dispose(); } finally { textScope?.Dispose(); } }
                    restored = true;
                }
                catch (Exception) { _cleanupFaulted = true; preview = null; }
                finally { Interlocked.Exchange(ref _capturing, 0); }
            }
            return restored && preview != null;
        }
        private static CoopCampaignPresentationValue PreviewText(string value) => new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.Text, Text = value ?? string.Empty };
        private static CoopCampaignPresentationValue PreviewFlag(bool value) => new CoopCampaignPresentationValue {
            Kind = CoopCampaignPresentationValueKind.Boolean, Number = value ? 1 : 0 };
    }
}
