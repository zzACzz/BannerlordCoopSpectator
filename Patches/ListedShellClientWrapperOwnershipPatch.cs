using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Intercepts the native listed wrapper-entry chain and routes ownership decisions into
    /// explicit coop runtime helpers instead of keeping bootstrap logic inside the patch body.
    /// </summary>
    internal static class ListedShellClientWrapperOwnershipPatch
    {
        private static bool _isApplied;

        public static void Apply(Harmony harmony)
        {
            if (_isApplied)
                return;

            try
            {
                PatchDiamondJoinResult(harmony);
                PatchListedWrapperStarts(harmony);
                _isApplied = true;
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: patched listed client wrapper-entry chain.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ListedShellClientWrapperOwnershipPatch.Apply failed.", ex);
            }
        }

        private static void PatchDiamondJoinResult(Harmony harmony)
        {
            Assembly diamondAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "TaleWorlds.MountAndBlade.Diamond");
            if (diamondAssembly == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: TaleWorlds.MountAndBlade.Diamond not loaded, skip join-result patch.");
                return;
            }

            Type lobbyClientType = diamondAssembly.GetType("TaleWorlds.MountAndBlade.Diamond.LobbyClient");
            if (lobbyClientType == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: LobbyClient type not found.");
                return;
            }

            MethodInfo method = lobbyClientType
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "OnJoinCustomGameResultMessage" && m.GetParameters().Length == 1);
            MethodInfo prefix = typeof(ListedShellClientWrapperOwnershipPatch).GetMethod(
                nameof(OnJoinCustomGameResultMessage_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null || prefix == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: OnJoinCustomGameResultMessage target/prefix not found.");
                return;
            }

            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
        }

        private static void PatchListedWrapperStarts(Harmony harmony)
        {
            Type customClientType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStateCustomGameClient");
            Type communityClientType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStateCommunityClient");
            Type playerBasedCustomServerType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.LobbyGameStatePlayerBasedCustomServer");
            if (customClientType == null || communityClientType == null || playerBasedCustomServerType == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: one or more native lobby game-state types not found. Skip wrapper patch.");
                return;
            }

            ListedShellWrapperInteropRuntime.InitializeWrapperContracts();

            PatchClientStateStart(harmony, customClientType, nameof(CustomGameClientStartMultiplayer_Prefix));
            PatchClientStateStart(harmony, communityClientType, nameof(CommunityClientStartMultiplayer_Prefix));
            PatchHostedServerStart(harmony, playerBasedCustomServerType);
        }

        private static void PatchHostedServerStart(Harmony harmony, Type playerBasedCustomServerType)
        {
            MethodInfo hostedSetStartingParametersMethod = playerBasedCustomServerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "SetStartingParameters" && m.GetParameters().Length == 1);
            MethodInfo hostedSetStartingParametersPostfixMethod = typeof(ListedShellClientWrapperOwnershipPatch).GetMethod(
                nameof(SetStartingParameters_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (hostedSetStartingParametersMethod == null || hostedSetStartingParametersPostfixMethod == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: hosted custom-server SetStartingParameters target/postfix not found. Skip.");
            }
            else
            {
                harmony.Patch(hostedSetStartingParametersMethod, postfix: new HarmonyMethod(hostedSetStartingParametersPostfixMethod));
            }

            MethodInfo hostedStartMethod = playerBasedCustomServerType.GetMethod("HandleServerStartMultiplayer", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo hostedStartPrefixMethod = typeof(ListedShellClientWrapperOwnershipPatch).GetMethod(
                nameof(HandleServerStartMultiplayer_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (hostedStartMethod == null || hostedStartPrefixMethod == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: hosted custom-server target/prefix not found. Skip.");
                return;
            }

            harmony.Patch(hostedStartMethod, prefix: new HarmonyMethod(hostedStartPrefixMethod));
        }

        private static void PatchClientStateStart(Harmony harmony, Type stateType, string prefixMethodName)
        {
            MethodInfo targetMethod = stateType.GetMethod("StartMultiplayer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo prefixMethod = typeof(ListedShellClientWrapperOwnershipPatch).GetMethod(prefixMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (targetMethod == null || prefixMethod == null)
            {
                ModLogger.Info("ListedShellClientWrapperOwnershipPatch: client StartMultiplayer target/prefix not found for " + stateType.FullName + ".");
                return;
            }

            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }

        private static void OnJoinCustomGameResultMessage_Prefix(object __0)
        {
            ListedShellWrapperInteropRuntime.HandleNativeJoinResult(
                __0,
                "ListedShellClientWrapperOwnershipPatch");
        }

        private static void SetStartingParameters_Postfix(object __0)
        {
            ListedShellWrapperInteropRuntime.HandleHostedServerStartingParameters(
                __0,
                "ListedShellClientWrapperOwnershipPatch.SetStartingParameters");
        }

        private static bool CustomGameClientStartMultiplayer_Prefix(object __instance)
        {
            if (!ListedShellWrapperInteropRuntime.ShouldOwnListedShellClientStart())
                return true;

            return ListedShellWrapperInteropRuntime.TryOwnCustomGameClientStart(
                "ListedShellClientWrapperOwnershipPatch.CustomGameClientStart");
        }

        private static bool CommunityClientStartMultiplayer_Prefix(object __instance)
        {
            if (!ListedShellWrapperInteropRuntime.ShouldOwnListedShellClientStart())
                return true;

            return ListedShellWrapperInteropRuntime.TryOwnCommunityClientStart(
                "ListedShellClientWrapperOwnershipPatch.CommunityClientStart");
        }

        private static bool HandleServerStartMultiplayer_Prefix()
        {
            return ListedShellWrapperInteropRuntime.TryOwnHostedListedServerStart(
                "ListedShellClientWrapperOwnershipPatch.HandleServerStartMultiplayer");
        }
    }
}
