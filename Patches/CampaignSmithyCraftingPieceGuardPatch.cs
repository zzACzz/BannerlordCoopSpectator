using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.WeaponDesign;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    [HarmonyPatch(typeof(WeaponDesignVM))]
    [HarmonyPatch("OnSetItemPiece")]
    [HarmonyPatch(new Type[] { typeof(CraftingPieceVM), typeof(int), typeof(bool), typeof(bool) })]
    internal static class CampaignSmithyCraftingPieceGuardPatch
    {
        private static readonly FieldInfo CraftingField =
            AccessTools.Field(typeof(WeaponDesignVM), "_crafting");

        [HarmonyPrefix]
        private static bool Prefix(WeaponDesignVM __instance, CraftingPieceVM piece)
        {
            bool isCampaignSmithy =
                TaleWorlds.CampaignSystem.Campaign.Current != null &&
                TaleWorlds.CampaignSystem.Campaign.Current.GameMode == CampaignGameMode.Campaign;

            WeaponDesignElement designElement = piece?.CraftingPiece;
            CraftingPiece craftingPiece = designElement?.CraftingPiece;
            Crafting crafting = CraftingField?.GetValue(__instance) as Crafting;
            CraftingTemplate craftingTemplate = crafting?.CurrentCraftingTemplate;

            bool belongsToTemplate =
                craftingPiece != null &&
                craftingTemplate?.Pieces != null &&
                craftingTemplate.Pieces.Contains(craftingPiece);
            bool resolvesToSameObject = ResolvesToSameObject(craftingPiece);

            return ExactCampaignCraftingRuntimeSafetyContract.ShouldAllowCampaignSmithyPieceSelection(
                isCampaignSmithy,
                belongsToTemplate,
                resolvesToSameObject,
                craftingPiece?.IsReady == true,
                craftingPiece?.IsValid == true);
        }

        private static bool ResolvesToSameObject(CraftingPiece craftingPiece)
        {
            if (craftingPiece == null || string.IsNullOrWhiteSpace(craftingPiece.StringId))
                return false;

            try
            {
                MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                CraftingPiece resolvedPiece = objectManager?.GetObject<CraftingPiece>(craftingPiece.StringId);
                return ReferenceEquals(resolvedPiece, craftingPiece);
            }
            catch
            {
                return false;
            }
        }
    }
}
