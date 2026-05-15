using System;
using System.Collections.Concurrent;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade.View.Tableaus;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Low-level diagnostics for the shared CharacterTableau / ThumbnailCache mannequin pipeline.
    /// This intentionally does not change runtime logic; it only logs the inputs and scene reuse
    /// at the first place where MP winners and campaign Party/Loot converge.
    /// </summary>
    public static class CharacterTableauChainDiagnosticsPatch
    {
        private sealed class TableauTraceState
        {
            public string LastRefreshSignature;
            public int LoggedRefreshCount;
            public int LastWidth;
            public int LastHeight;
            public bool FirstInitLogged;
        }

        private sealed class ProviderTraceState
        {
            public int LastWidth;
            public int LastHeight;
            public int LastTableauInstance;
            public int LastEngineTextureInstance;
            public int LastProvidedTextureInstance;
            public bool HasLoggedFirstTick;
            public bool HiddenInitialized;
            public bool LastHidden;
            public int ClearCount;
        }

        private static readonly ConcurrentDictionary<int, TableauTraceState> TableauStates =
            new ConcurrentDictionary<int, TableauTraceState>();

        private static readonly ConcurrentDictionary<int, ProviderTraceState> ProviderStates =
            new ConcurrentDictionary<int, ProviderTraceState>();

        private static readonly ConcurrentDictionary<string, byte> OnceKeys =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        private static readonly BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo CtEquipmentField =
            typeof(CharacterTableau).GetField("_equipment", AnyInstance);
        private static readonly FieldInfo CtMountCreationKeyField =
            typeof(CharacterTableau).GetField("_mountCreationKey", AnyInstance);
        private static readonly FieldInfo CtEquipmentCodeField =
            typeof(CharacterTableau).GetField("_equipmentCode", AnyInstance);
        private static readonly FieldInfo CtCharStringIdField =
            typeof(CharacterTableau).GetField("_charStringId", AnyInstance);
        private static readonly FieldInfo CtBodyPropertiesCodeField =
            typeof(CharacterTableau).GetField("_bodyPropertiesCode", AnyInstance);
        private static readonly FieldInfo CtStanceIndexField =
            typeof(CharacterTableau).GetField("_stanceIndex", AnyInstance);
        private static readonly FieldInfo CtIsCharacterMountPlacesSwappedField =
            typeof(CharacterTableau).GetField("_isCharacterMountPlacesSwapped", AnyInstance);
        private static readonly FieldInfo CtAgentRendererSceneControllerField =
            typeof(CharacterTableau).GetField("_agentRendererSceneController", AnyInstance);
        private static readonly FieldInfo CtMountVisualsField =
            typeof(CharacterTableau).GetField("_mountVisuals", AnyInstance);
        private static readonly FieldInfo CtAgentVisualsField =
            typeof(CharacterTableau).GetField("_agentVisuals", AnyInstance);
        private static readonly FieldInfo CtTableauSizeXField =
            typeof(CharacterTableau).GetField("_tableauSizeX", AnyInstance);
        private static readonly FieldInfo CtTableauSizeYField =
            typeof(CharacterTableau).GetField("_tableauSizeY", AnyInstance);

        private static readonly FieldInfo ThumbnailInventorySceneBeingUsedField =
            typeof(ThumbnailCacheManager).GetField("_inventorySceneBeingUsed", AnyInstance);

        private static Type _characterTableauTextureProviderType;
        private static FieldInfo _cttpCharacterTableauField;
        private static FieldInfo _cttpTextureField;
        private static FieldInfo _cttpProvidedTextureField;
        private static FieldInfo _cttpIsHiddenField;

        public static void Apply(Harmony harmony)
        {
            try
            {
                PatchCharacterTableau(harmony);
                PatchThumbnailCacheManager(harmony);
                PatchCharacterTableauTextureProvider(harmony);
                PatchMultiplayerPreviewVm(harmony);
                PatchPartyVm(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error("CharacterTableauChainDiagnosticsPatch.Apply failed.", ex);
            }
        }

        private static void PatchCharacterTableau(Harmony harmony)
        {
            PatchWithPostfix(harmony, typeof(CharacterTableau), "SetTargetSize", nameof(CharacterTableau_SetTargetSize_Postfix));
            PatchWithPostfix(harmony, typeof(CharacterTableau), "FirstTimeInit", nameof(CharacterTableau_FirstTimeInit_Postfix));
            PatchWithPostfix(harmony, typeof(CharacterTableau), "SetEquipmentCode", nameof(CharacterTableau_SetEquipmentCode_Postfix));
            PatchWithPostfix(harmony, typeof(CharacterTableau), "SetMountCreationKey", nameof(CharacterTableau_SetMountCreationKey_Postfix));
            PatchWithPostfix(harmony, typeof(CharacterTableau), "SetCharStringID", nameof(CharacterTableau_SetCharStringID_Postfix));
            PatchWithPostfix(harmony, typeof(CharacterTableau), "SetBodyProperties", nameof(CharacterTableau_SetBodyProperties_Postfix));
            PatchWithPostfix(harmony, typeof(CharacterTableau), "SetStanceIndex", nameof(CharacterTableau_SetStanceIndex_Postfix));
            PatchWithPostfix(harmony, typeof(CharacterTableau), "UpdateMount", nameof(CharacterTableau_UpdateMount_Postfix));
            PatchWithPrefix(harmony, typeof(CharacterTableau), "RefreshCharacterTableau", nameof(CharacterTableau_RefreshCharacterTableau_Prefix));
            PatchWithPrefix(harmony, typeof(CharacterTableau), "OnFinalize", nameof(CharacterTableau_OnFinalize_Prefix));
        }

        private static void PatchThumbnailCacheManager(Harmony harmony)
        {
            PatchWithPrefix(harmony, typeof(ThumbnailCacheManager), "GetCachedInventoryTableauScene", nameof(ThumbnailCacheManager_GetCachedInventoryTableauScene_Prefix));
            PatchWithPostfix(harmony, typeof(ThumbnailCacheManager), "ReturnCachedInventoryTableauScene", nameof(ThumbnailCacheManager_ReturnCachedInventoryTableauScene_Postfix));
            PatchWithPrefix(harmony, typeof(ThumbnailCacheManager), "ClearManager", nameof(ThumbnailCacheManager_ClearManager_Prefix));
        }

        private static void PatchCharacterTableauTextureProvider(Harmony harmony)
        {
            _characterTableauTextureProviderType =
                AccessTools.TypeByName("TaleWorlds.MountAndBlade.GauntletUI.TextureProviders.CharacterTableauTextureProvider");
            if (_characterTableauTextureProviderType == null)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider type not found. Skip.");
                return;
            }

            _cttpCharacterTableauField = _characterTableauTextureProviderType.GetField("_characterTableau", AnyInstance);
            _cttpTextureField = _characterTableauTextureProviderType.GetField("_texture", AnyInstance);
            _cttpProvidedTextureField = _characterTableauTextureProviderType.GetField("_providedTexture", AnyInstance);
            _cttpIsHiddenField = _characterTableauTextureProviderType.GetField("_isHidden", AnyInstance);

            PatchConstructorWithPostfix(harmony, _characterTableauTextureProviderType, nameof(CharacterTableauTextureProvider_Ctor_Postfix));
            PatchWithPostfix(harmony, _characterTableauTextureProviderType, "SetTargetSize", nameof(CharacterTableauTextureProvider_SetTargetSize_Postfix));
            PatchWithPostfix(harmony, _characterTableauTextureProviderType, "CheckTexture", nameof(CharacterTableauTextureProvider_CheckTexture_Postfix));
            PatchWithPostfix(harmony, _characterTableauTextureProviderType, "Tick", nameof(CharacterTableauTextureProvider_Tick_Postfix));
            PatchWithPrefix(harmony, _characterTableauTextureProviderType, "Clear", nameof(CharacterTableauTextureProvider_Clear_Prefix));
        }

        private static void PatchMultiplayerPreviewVm(Harmony harmony)
        {
            Type previewType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.Lobby.Armory.MPArmoryHeroPreviewVM");
            if (previewType == null)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: MPArmoryHeroPreviewVM type not found. Skip.");
                return;
            }

            PatchWithPostfix(harmony, previewType, "SetCharacter", nameof(MPArmoryHeroPreviewVM_SetCharacter_Postfix));
        }

        private static void PatchPartyVm(Harmony harmony)
        {
            Type partyVmType = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.Party.PartyVM");
            if (partyVmType == null)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: PartyVM type not found. Skip.");
                return;
            }

            PatchWithPostfix(harmony, partyVmType, "RefreshCurrentCharacterInformation", nameof(PartyVM_RefreshCurrentCharacterInformation_Postfix));
        }

        private static void PatchWithPrefix(Harmony harmony, Type targetType, string targetMethodName, string prefixMethodName)
        {
            MethodInfo target = AccessTools.Method(targetType, targetMethodName);
            MethodInfo prefix = typeof(CharacterTableauChainDiagnosticsPatch).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: prefix target not found. Type=" + targetType.FullName + " Method=" + targetMethodName);
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("CharacterTableauChainDiagnosticsPatch: prefix applied to " + targetType.FullName + "." + targetMethodName);
        }

        private static void PatchWithPostfix(Harmony harmony, Type targetType, string targetMethodName, string postfixMethodName)
        {
            MethodInfo target = AccessTools.Method(targetType, targetMethodName);
            MethodInfo postfix = typeof(CharacterTableauChainDiagnosticsPatch).GetMethod(postfixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: postfix target not found. Type=" + targetType.FullName + " Method=" + targetMethodName);
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("CharacterTableauChainDiagnosticsPatch: postfix applied to " + targetType.FullName + "." + targetMethodName);
        }

        private static void PatchConstructorWithPostfix(Harmony harmony, Type targetType, string postfixMethodName)
        {
            MethodBase target = AccessTools.Constructor(targetType, Type.EmptyTypes);
            MethodInfo postfix = typeof(CharacterTableauChainDiagnosticsPatch).GetMethod(postfixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: constructor postfix target not found. Type=" + targetType.FullName);
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("CharacterTableauChainDiagnosticsPatch: constructor postfix applied to " + targetType.FullName + ".ctor");
        }

        private static void CharacterTableau_SetTargetSize_Postfix(CharacterTableau __instance, int width, int height)
        {
            try
            {
                TableauTraceState state = GetState(__instance);
                state.LastWidth = width;
                state.LastHeight = height;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: SetTargetSize postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_FirstTimeInit_Postfix(CharacterTableau __instance)
        {
            try
            {
                TableauTraceState state = GetState(__instance);
                if (state.FirstInitLogged)
                    return;

                state.FirstInitLogged = true;

                bool usesPrivateScene = CtAgentRendererSceneControllerField?.GetValue(__instance) != null;
                string sceneMode = usesPrivateScene ? "private-scene" : "cached-inventory-scene";
                LogOnce(
                    "CharacterTableau.FirstTimeInit|" + RuntimeHelpersHash(__instance) + "|" + sceneMode + "|" + state.LastWidth + "x" + state.LastHeight,
                    "CharacterTableauChainDiagnosticsPatch: initialized CharacterTableau. " +
                    "Instance=" + RuntimeHelpersHash(__instance) +
                    " SceneMode=" + sceneMode +
                    " LastWidgetSize=" + state.LastWidth + "x" + state.LastHeight +
                    " HasAgentRenderer=" + usesPrivateScene);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: FirstTimeInit postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_SetEquipmentCode_Postfix(CharacterTableau __instance, string equipmentCode)
        {
            try
            {
                LogPotentialSelectionSource(__instance, "SetEquipmentCode", equipmentCode);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: SetEquipmentCode postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_SetMountCreationKey_Postfix(CharacterTableau __instance, string value)
        {
            try
            {
                LogPotentialSelectionSource(__instance, "SetMountCreationKey", value);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: SetMountCreationKey postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_SetCharStringID_Postfix(CharacterTableau __instance, string charStringId)
        {
            try
            {
                LogPotentialSelectionSource(__instance, "SetCharStringID", charStringId);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: SetCharStringID postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_SetBodyProperties_Postfix(CharacterTableau __instance, string bodyPropertiesCode)
        {
            try
            {
                LogPotentialSelectionSource(__instance, "SetBodyProperties", Shorten(bodyPropertiesCode, 42));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: SetBodyProperties postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_SetStanceIndex_Postfix(CharacterTableau __instance, int index)
        {
            try
            {
                LogPotentialSelectionSource(__instance, "SetStanceIndex", index.ToString());
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: SetStanceIndex postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_UpdateMount_Postfix(CharacterTableau __instance, bool isRiderAgentMounted)
        {
            try
            {
                Equipment equipment = CtEquipmentField?.GetValue(__instance) as Equipment;
                bool hasMountSlot = HasMountSlot(equipment);
                bool hasMountVisuals = CtMountVisualsField?.GetValue(__instance) != null;
                if (hasMountSlot == hasMountVisuals)
                    return;

                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: mount visual mismatch in CharacterTableau.UpdateMount. " +
                    "Instance=" + RuntimeHelpersHash(__instance) +
                    " HasMountSlot=" + hasMountSlot +
                    " HasMountVisuals=" + hasMountVisuals +
                    " MountedFlag=" + isRiderAgentMounted +
                    " Horse=" + ResolveEquipmentItemId(equipment, EquipmentIndex.ArmorItemEndSlot) +
                    " HorseHarness=" + ResolveEquipmentItemId(equipment, EquipmentIndex.HorseHarness) +
                    " Stance=" + SafeReadField(CtStanceIndexField, __instance) +
                    " MountCreationKey=" + Shorten(CtMountCreationKeyField?.GetValue(__instance) as string, 48));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: UpdateMount postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_RefreshCharacterTableau_Prefix(CharacterTableau __instance)
        {
            try
            {
                TableauTraceState state = GetState(__instance);
                Equipment equipment = CtEquipmentField?.GetValue(__instance) as Equipment;
                bool usesPrivateScene = CtAgentRendererSceneControllerField?.GetValue(__instance) != null;
                bool hasMountVisuals = CtMountVisualsField?.GetValue(__instance) != null;
                bool hasAgentVisuals = CtAgentVisualsField?.GetValue(__instance) != null;
                bool isSwapped = (bool?)CtIsCharacterMountPlacesSwappedField?.GetValue(__instance) ?? false;
                string charId = CtCharStringIdField?.GetValue(__instance) as string ?? string.Empty;
                string equipmentCode = CtEquipmentCodeField?.GetValue(__instance) as string ?? string.Empty;
                string mountCreationKey = CtMountCreationKeyField?.GetValue(__instance) as string ?? string.Empty;
                string bodyPropertiesCode = CtBodyPropertiesCodeField?.GetValue(__instance) as string ?? string.Empty;
                string horse = ResolveEquipmentItemId(equipment, EquipmentIndex.ArmorItemEndSlot);
                string horseHarness = ResolveEquipmentItemId(equipment, EquipmentIndex.HorseHarness);
                int width = state.LastWidth != 0 ? state.LastWidth : ((int?)CtTableauSizeXField?.GetValue(__instance) ?? 0);
                int height = state.LastHeight != 0 ? state.LastHeight : ((int?)CtTableauSizeYField?.GetValue(__instance) ?? 0);
                string signature =
                    (charId ?? string.Empty) + "|" +
                    (equipmentCode?.Length.ToString() ?? "0") + "|" +
                    (mountCreationKey ?? string.Empty) + "|" +
                    horse + "|" +
                    horseHarness + "|" +
                    SafeReadField(CtStanceIndexField, __instance) + "|" +
                    width + "x" + height + "|" +
                    usesPrivateScene + "|" +
                    hasMountVisuals + "|" +
                    isSwapped + "|" +
                    (bodyPropertiesCode?.Length.ToString() ?? "0");

                bool shouldLog = !string.Equals(state.LastRefreshSignature, signature, StringComparison.Ordinal) || state.LoggedRefreshCount < 2;
                if (!shouldLog)
                    return;

                state.LastRefreshSignature = signature;
                state.LoggedRefreshCount++;

                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: CharacterTableau refresh. " +
                    "Instance=" + RuntimeHelpersHash(__instance) +
                    " SceneMode=" + (usesPrivateScene ? "private-scene" : "cached-inventory-scene") +
                    " WidgetSize=" + width + "x" + height +
                    " CharStringId=" + (string.IsNullOrWhiteSpace(charId) ? "empty" : charId) +
                    " EquipmentCodeLen=" + equipmentCode.Length +
                    " MountCreationKey=" + Shorten(mountCreationKey, 48) +
                    " Stance=" + SafeReadField(CtStanceIndexField, __instance) +
                    " HasMountSlot=" + HasMountSlot(equipment) +
                    " Horse=" + horse +
                    " HorseHarness=" + horseHarness +
                    " HasAgentVisuals=" + hasAgentVisuals +
                    " HasMountVisuals=" + hasMountVisuals +
                    " SwapPlaces=" + isSwapped +
                    " BodyPropertiesLen=" + bodyPropertiesCode.Length);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: RefreshCharacterTableau prefix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableau_OnFinalize_Prefix(CharacterTableau __instance)
        {
            try
            {
                Equipment equipment = CtEquipmentField?.GetValue(__instance) as Equipment;
                bool usesPrivateScene = CtAgentRendererSceneControllerField?.GetValue(__instance) != null;
                bool hasMountVisuals = CtMountVisualsField?.GetValue(__instance) != null;
                bool hasAgentVisuals = CtAgentVisualsField?.GetValue(__instance) != null;
                string charId = CtCharStringIdField?.GetValue(__instance) as string ?? string.Empty;
                string mountCreationKey = CtMountCreationKeyField?.GetValue(__instance) as string ?? string.Empty;
                string horse = ResolveEquipmentItemId(equipment, EquipmentIndex.ArmorItemEndSlot);
                string horseHarness = ResolveEquipmentItemId(equipment, EquipmentIndex.HorseHarness);
                TableauTraceState state = GetState(__instance);

                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: CharacterTableau finalize. " +
                    "Instance=" + RuntimeHelpersHash(__instance) +
                    " SceneMode=" + (usesPrivateScene ? "private-scene" : "cached-inventory-scene") +
                    " LastWidgetSize=" + state.LastWidth + "x" + state.LastHeight +
                    " CharStringId=" + (string.IsNullOrWhiteSpace(charId) ? "empty" : charId) +
                    " MountCreationKey=" + Shorten(mountCreationKey, 48) +
                    " Stance=" + SafeReadField(CtStanceIndexField, __instance) +
                    " HasMountSlot=" + HasMountSlot(equipment) +
                    " Horse=" + horse +
                    " HorseHarness=" + horseHarness +
                    " HasAgentVisuals=" + hasAgentVisuals +
                    " HasMountVisuals=" + hasMountVisuals);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: OnFinalize prefix failed open: " + ex.Message);
            }
        }

        private static void ThumbnailCacheManager_GetCachedInventoryTableauScene_Prefix(ThumbnailCacheManager __instance)
        {
            try
            {
                bool wasAlreadyUsed = (bool?)ThumbnailInventorySceneBeingUsedField?.GetValue(__instance) ?? false;
                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: ThumbnailCacheManager.GetCachedInventoryTableauScene. " +
                    "AlreadyUsed=" + wasAlreadyUsed);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: GetCachedInventoryTableauScene prefix failed open: " + ex.Message);
            }
        }

        private static void ThumbnailCacheManager_ReturnCachedInventoryTableauScene_Postfix()
        {
            try
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: ThumbnailCacheManager.ReturnCachedInventoryTableauScene.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: ReturnCachedInventoryTableauScene postfix failed open: " + ex.Message);
            }
        }

        private static void ThumbnailCacheManager_ClearManager_Prefix()
        {
            try
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: ThumbnailCacheManager.ClearManager.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: ClearManager prefix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableauTextureProvider_Ctor_Postfix(object __instance)
        {
            try
            {
                CharacterTableau tableau = _cttpCharacterTableauField?.GetValue(__instance) as CharacterTableau;
                ProviderTraceState state = GetProviderState(__instance);
                state.LastTableauInstance = RuntimeHelpersHash(tableau);
                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider created. " +
                    "Provider=" + RuntimeHelpersHash(__instance) +
                    " Tableau=" + RuntimeHelpersHash(tableau));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider ctor postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableauTextureProvider_SetTargetSize_Postfix(object __instance, int width, int height)
        {
            try
            {
                ProviderTraceState state = GetProviderState(__instance);
                CharacterTableau tableau = _cttpCharacterTableauField?.GetValue(__instance) as CharacterTableau;
                state.LastWidth = width;
                state.LastHeight = height;
                state.LastTableauInstance = RuntimeHelpersHash(tableau);
                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider.SetTargetSize. " +
                    "Provider=" + RuntimeHelpersHash(__instance) +
                    " Tableau=" + RuntimeHelpersHash(tableau) +
                    " Width=" + width +
                    " Height=" + height);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider.SetTargetSize postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableauTextureProvider_CheckTexture_Postfix(object __instance)
        {
            try
            {
                ProviderTraceState state = GetProviderState(__instance);
                CharacterTableau tableau = _cttpCharacterTableauField?.GetValue(__instance) as CharacterTableau;
                object engineTexture = _cttpTextureField?.GetValue(__instance);
                object providedTexture = _cttpProvidedTextureField?.GetValue(__instance);
                int tableauInstance = RuntimeHelpersHash(tableau);
                int engineTextureInstance = RuntimeHelpersHash(engineTexture);
                int providedTextureInstance = RuntimeHelpersHash(providedTexture);
                if (state.LastTableauInstance == tableauInstance &&
                    state.LastEngineTextureInstance == engineTextureInstance &&
                    state.LastProvidedTextureInstance == providedTextureInstance)
                    return;

                state.LastTableauInstance = tableauInstance;
                state.LastEngineTextureInstance = engineTextureInstance;
                state.LastProvidedTextureInstance = providedTextureInstance;

                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider.CheckTexture. " +
                    "Provider=" + RuntimeHelpersHash(__instance) +
                    " Tableau=" + tableauInstance +
                    " EngineTexture=" + engineTextureInstance +
                    " ProvidedTexture=" + providedTextureInstance +
                    " Hidden=" + SafeReadProviderHidden(__instance) +
                    " Size=" + state.LastWidth + "x" + state.LastHeight);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider.CheckTexture postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableauTextureProvider_Tick_Postfix(object __instance, float dt)
        {
            try
            {
                ProviderTraceState state = GetProviderState(__instance);
                CharacterTableau tableau = _cttpCharacterTableauField?.GetValue(__instance) as CharacterTableau;
                bool hidden = SafeReadProviderHidden(__instance);
                if (!state.HasLoggedFirstTick)
                {
                    state.HasLoggedFirstTick = true;
                    state.HiddenInitialized = true;
                    state.LastHidden = hidden;
                    ModLogger.Info(
                        "CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider first tick. " +
                        "Provider=" + RuntimeHelpersHash(__instance) +
                        " Tableau=" + RuntimeHelpersHash(tableau) +
                        " Hidden=" + hidden +
                        " Size=" + state.LastWidth + "x" + state.LastHeight +
                        " DeltaTime=" + dt.ToString("0.000"));
                    return;
                }

                if (state.HiddenInitialized && state.LastHidden == hidden)
                    return;

                state.HiddenInitialized = true;
                state.LastHidden = hidden;
                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider hidden-state changed. " +
                    "Provider=" + RuntimeHelpersHash(__instance) +
                    " Tableau=" + RuntimeHelpersHash(tableau) +
                    " Hidden=" + hidden +
                    " Size=" + state.LastWidth + "x" + state.LastHeight);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider.Tick postfix failed open: " + ex.Message);
            }
        }

        private static void CharacterTableauTextureProvider_Clear_Prefix(object __instance, bool clearNextFrame)
        {
            try
            {
                ProviderTraceState state = GetProviderState(__instance);
                CharacterTableau tableau = _cttpCharacterTableauField?.GetValue(__instance) as CharacterTableau;
                state.ClearCount++;
                ModLogger.Info(
                    "CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider.Clear. " +
                    "Provider=" + RuntimeHelpersHash(__instance) +
                    " Tableau=" + RuntimeHelpersHash(tableau) +
                    " ClearNextFrame=" + clearNextFrame +
                    " ClearCount=" + state.ClearCount +
                    " Hidden=" + SafeReadProviderHidden(__instance) +
                    " Size=" + state.LastWidth + "x" + state.LastHeight);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: CharacterTableauTextureProvider.Clear prefix failed open: " + ex.Message);
            }
        }

        private static void MPArmoryHeroPreviewVM_SetCharacter_Postfix(object __instance, BasicCharacterObject character, int race, bool isFemale)
        {
            try
            {
                object heroVisual = AccessTools.Property(__instance.GetType(), "HeroVisual")?.GetValue(__instance, null);
                if (heroVisual == null)
                    return;

                LogOnce(
                    "MPArmoryHeroPreviewVM.SetCharacter|" + (character?.StringId ?? "null") + "|" + isFemale + "|" + race,
                    "CharacterTableauChainDiagnosticsPatch: MP winners preview VM input. " +
                    "Character=" + (character?.StringId ?? "null") +
                    " Name=" + (character?.Name?.ToString() ?? "null") +
                    " HeroVisual.CharStringId=" + ReadProperty(heroVisual, "CharStringId") +
                    " HeroVisual.EquipmentCodeLen=" + SafeLength(ReadProperty(heroVisual, "EquipmentCode")) +
                    " HeroVisual.HasMount=" + ReadProperty(heroVisual, "HasMount") +
                    " HeroVisual.MountCreationKey=" + Shorten(ReadProperty(heroVisual, "MountCreationKey"), 48) +
                    " HeroVisual.StanceIndex=" + ReadProperty(heroVisual, "StanceIndex") +
                    " HeroVisual.Race=" + ReadProperty(heroVisual, "Race") +
                    " HeroVisual.IsFemale=" + ReadProperty(heroVisual, "IsFemale"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: MPArmoryHeroPreviewVM.SetCharacter postfix failed open: " + ex.Message);
            }
        }

        private static void PartyVM_RefreshCurrentCharacterInformation_Postfix(object __instance)
        {
            try
            {
                object selectedCharacter = AccessTools.Property(__instance.GetType(), "SelectedCharacter")?.GetValue(__instance, null);
                object currentCharacter = AccessTools.Property(__instance.GetType(), "CurrentCharacter")?.GetValue(__instance, null);
                if (selectedCharacter == null)
                    return;

                string currentName = ReadProperty(currentCharacter, "Name");
                string currentId = ReadNestedProperty(currentCharacter, "Character", "StringId");
                string key = "PartyVM.RefreshCurrentCharacterInformation|" + (currentId ?? "null") + "|" + SafeLength(ReadProperty(selectedCharacter, "EquipmentCode"));
                LogOnce(
                    key,
                    "CharacterTableauChainDiagnosticsPatch: Party preview VM input. " +
                    "CurrentCharacterId=" + (currentId ?? "null") +
                    " CurrentCharacterName=" + (currentName ?? "null") +
                    " Selected.CharStringId=" + ReadProperty(selectedCharacter, "CharStringId") +
                    " Selected.EquipmentCodeLen=" + SafeLength(ReadProperty(selectedCharacter, "EquipmentCode")) +
                    " Selected.HasMount=" + ReadProperty(selectedCharacter, "HasMount") +
                    " Selected.MountCreationKey=" + Shorten(ReadProperty(selectedCharacter, "MountCreationKey"), 48) +
                    " Selected.StanceIndex=" + ReadProperty(selectedCharacter, "StanceIndex") +
                    " Selected.Race=" + ReadProperty(selectedCharacter, "Race") +
                    " Selected.IsFemale=" + ReadProperty(selectedCharacter, "IsFemale"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CharacterTableauChainDiagnosticsPatch: PartyVM.RefreshCurrentCharacterInformation postfix failed open: " + ex.Message);
            }
        }

        private static void LogPotentialSelectionSource(CharacterTableau tableau, string source, string value)
        {
            TableauTraceState state = GetState(tableau);
            if (state.LastWidth <= 0 || state.LastHeight <= 0)
                return;

            // Selection preview is one of the few active paths in the mod that can still use a mounted CharacterTableau
            // before winners / Party screens. Keep this log once per instance to prove whether it is active at runtime.
            bool looksLikeSelection =
                state.LastWidth >= 500 &&
                state.LastWidth <= 650 &&
                state.LastHeight >= 700 &&
                state.LastHeight <= 900;
            if (!looksLikeSelection)
                return;

            LogOnce(
                "CharacterTableau.SelectionLike|" + RuntimeHelpersHash(tableau),
                "CharacterTableauChainDiagnosticsPatch: detected selection-like CharacterTableau instance. " +
                "Instance=" + RuntimeHelpersHash(tableau) +
                " LastWidgetSize=" + state.LastWidth + "x" + state.LastHeight +
                " FirstSignal=" + source +
                " Value=" + Shorten(value, 48));
        }

        private static TableauTraceState GetState(CharacterTableau tableau)
        {
            int key = RuntimeHelpersHash(tableau);
            return TableauStates.GetOrAdd(key, _ => new TableauTraceState());
        }

        private static ProviderTraceState GetProviderState(object provider)
        {
            int key = RuntimeHelpersHash(provider);
            return ProviderStates.GetOrAdd(key, _ => new ProviderTraceState());
        }

        private static int RuntimeHelpersHash(object instance)
        {
            return instance == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
        }

        private static string ResolveEquipmentItemId(Equipment equipment, EquipmentIndex index)
        {
            try
            {
                if (equipment == null)
                    return "empty";

                EquipmentElement element = equipment[index];
                return element.Item?.StringId ?? "empty";
            }
            catch
            {
                return "error";
            }
        }

        private static bool HasMountSlot(Equipment equipment)
        {
            if (equipment == null)
                return false;

            try
            {
                EquipmentElement horse = equipment[EquipmentIndex.ArmorItemEndSlot];
                return horse.Item?.HorseComponent != null;
            }
            catch
            {
                return false;
            }
        }

        private static object SafeReadField(FieldInfo field, object instance)
        {
            try
            {
                return field?.GetValue(instance) ?? "null";
            }
            catch
            {
                return "error";
            }
        }

        private static bool SafeReadProviderHidden(object instance)
        {
            try
            {
                return (bool?)_cttpIsHiddenField?.GetValue(instance) ?? false;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadProperty(object instance, string propertyName)
        {
            try
            {
                object value = AccessTools.Property(instance.GetType(), propertyName)?.GetValue(instance, null);
                return value?.ToString() ?? "null";
            }
            catch
            {
                return "error";
            }
        }

        private static string ReadNestedProperty(object instance, string firstProperty, string secondProperty)
        {
            try
            {
                object first = AccessTools.Property(instance.GetType(), firstProperty)?.GetValue(instance, null);
                if (first == null)
                    return "null";

                object second = AccessTools.Property(first.GetType(), secondProperty)?.GetValue(first, null);
                return second?.ToString() ?? "null";
            }
            catch
            {
                return "error";
            }
        }

        private static int SafeLength(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : value.Length;
        }

        private static string Shorten(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value ?? string.Empty;

            return value.Substring(0, maxChars) + "...";
        }

        private static void LogOnce(string key, string message)
        {
            if (OnceKeys.TryAdd(key, 0))
                ModLogger.Info(message);
        }
    }
}
