using System; // ÐŸÑ–Ð´ÐºÐ»ÑŽÑ‡Ð°Ñ”Ð¼Ð¾ Ð±Ð°Ð·Ð¾Ð²Ñ– Ñ‚Ð¸Ð¿Ð¸ .NET (Exception)
using CoopSpectator.Campaign; // ÐŸÑ–Ð´ÐºÐ»ÑŽÑ‡Ð°Ñ”Ð¼Ð¾ campaign behaviors (HostStateBroadcaster, SpectatorStateReceiver)
#if HAS_GAMEMODE
using CoopSpectator.GameMode; // CoopBattle client bootstrap for custom-game mission open.
#endif
using CoopSpectator.Infrastructure; // ÐŸÑ–Ð´ÐºÐ»ÑŽÑ‡Ð°Ñ”Ð¼Ð¾ Ñ–Ð½Ñ„Ñ€Ð°ÑÑ‚Ñ€ÑƒÐºÑ‚ÑƒÑ€Ñƒ (Ð»Ð¾Ð³ÐµÑ€, dispatcher)
using CoopSpectator.MissionModels; // Low-level mission model wrappers for CoopBattle
using CoopSpectator.Patches; // LobbyCustomGameLocalJoinPatch
using HarmonyLib; // ÐŸÑ–Ð´ÐºÐ»ÑŽÑ‡Ð°Ñ”Ð¼Ð¾ Harmony Ð´Ð»Ñ Ð¿Ð°Ñ‚Ñ‡Ð¸Ð½Ð³Ñƒ Ð¼ÐµÑ‚Ð¾Ð´Ñ–Ð² Ð³Ñ€Ð¸
using TaleWorlds.CampaignSystem; // ÐŸÑ–Ð´ÐºÐ»ÑŽÑ‡Ð°Ñ”Ð¼Ð¾ CampaignGameStarter Ñ‚Ð° Campaign (Ñ‰Ð¾Ð± Ð´Ð¾Ð´Ð°Ñ‚Ð¸ behaviors Ñƒ ÐºÐ°Ð¼Ð¿Ð°Ð½Ñ–ÑŽ)
using TaleWorlds.Core; // ÐŸÑ–Ð´ÐºÐ»ÑŽÑ‡Ð°Ñ”Ð¼Ð¾ Ð±Ð°Ð·Ð¾Ð²Ñ– Ñ‚Ð¸Ð¿Ð¸ Bannerlord (InformationMessage)
using TaleWorlds.Library; // ÐŸÑ–Ð´ÐºÐ»ÑŽÑ‡Ð°Ñ”Ð¼Ð¾ ÑƒÑ‚Ð¸Ð»Ñ–Ñ‚Ð¸ Bannerlord (InformationManager)
using TaleWorlds.MountAndBlade; // ÐŸÑ–Ð´ÐºÐ»ÑŽÑ‡Ð°Ñ”Ð¼Ð¾ Ð±Ð°Ð·Ð¾Ð²Ð¸Ð¹ API Ð¼Ð¾Ð´Ñ–Ð² (MBSubModuleBase)
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace CoopSpectator // Ð’Ð¸ÐºÐ¾Ñ€Ð¸ÑÑ‚Ð¾Ð²ÑƒÑ”Ð¼Ð¾ ÐºÐ¾Ñ€ÐµÐ½ÐµÐ²Ð¸Ð¹ namespace Ð¼Ð¾Ð´Ñƒ (Ð¼Ð°Ñ” ÑÐ¿Ñ–Ð²Ð¿Ð°ÑÑ‚Ð¸ Ð· SubModule.xml)
{ // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¿Ñ€Ð¾ÑÑ‚Ð¾Ñ€Ñƒ Ñ–Ð¼ÐµÐ½
    /// <summary> // Ð”Ð¾ÐºÑƒÐ¼ÐµÐ½Ñ‚ÑƒÑ”Ð¼Ð¾ ÐºÐ»Ð°Ñ
    /// Ð¢Ð¾Ñ‡ÐºÐ° Ð²Ñ…Ð¾Ð´Ñƒ Ð¼Ð¾Ð´Ñƒ Ð´Ð»Ñ Bannerlord (Ð·Ð°Ð²Ð°Ð½Ñ‚Ð°Ð¶ÑƒÑ”Ñ‚ÑŒÑÑ Ñ‡ÐµÑ€ÐµÐ· SubModule.xml). // ÐŸÐ¾ÑÑÐ½ÑŽÑ”Ð¼Ð¾ Ð¿Ñ€Ð¸Ð·Ð½Ð°Ñ‡ÐµÐ½Ð½Ñ
    /// </summary> // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ XML-ÐºÐ¾Ð¼ÐµÐ½Ñ‚Ð°Ñ€
    public sealed class SubModule : MBSubModuleBase // ÐÐ°ÑÐ»Ñ–Ð´ÑƒÑ”Ð¼Ð¾ MBSubModuleBase, Ñ‰Ð¾Ð± Ð¾Ñ‚Ñ€Ð¸Ð¼Ð°Ñ‚Ð¸ callbacks Ð²Ñ–Ð´ Ð³Ñ€Ð¸
    { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº ÐºÐ»Ð°ÑÑƒ
        // Startup isolation switches for coarse bisect of global mod side effects.
        private const bool EnableCoopRuntimeStartup = true;
        private const bool EnableHarmonyPatching = true;
        private const bool EnableHarmonyPatchAll = true;
        private const bool EnableManualHarmonyApply = true;
        private const bool EnableHarmonyAssemblyLoadReapply = true;
        private const bool EnableManualPatchLobbyClient = true;
        private const bool EnableManualPatchMissionFlowUi = true;
        private const bool EnableManualPatchMissionFlowOpenNew = true;
        // Disabled after 2026-05-15 reconnect/load-crash triage:
        // BattleShellSuppressionPatch aggressively hijacks native mission-loading lifecycle
        // and correlates with host/client/dedicated access-violation crashes during mission load.
        private const bool EnableManualPatchMissionFlowBattleShell = false;
        private const bool EnableManualPatchMissionFlowBattleRuntime = true;
        private const bool EnableManualPatchMissionFlowBattleRuntimeEntryUi = true;
        private const bool EnableManualPatchMissionFlowBattleRuntimeSpawnHandoff = true;
        private const bool EnableManualPatchMissionFlowBattleRuntimeBootstrapGates = true;
        // Server-side deferral of MissionNetworkComponent late existing-object sync deadlocks
        // reconnect/bootstrap during active battle. Keep the patch code available for future
        // narrower experiments, but disable registration in the normal runtime profile.
        private const bool EnableManualPatchMissionFlowBattleRuntimeLateJoinBootstrapGate = false;
        private const bool EnableManualPatchMissionFlowBattleRuntimeLateJoinHandleLateClientAfterLoadingFinished = false;
        private const bool EnableManualPatchMissionFlowBattleRuntimeLateJoinSendAgentsToPeer = false;
        private const bool EnableManualPatchMissionFlowBattleRuntimeLateJoinSendMissilesToPeer = false;
        private const bool EnableManualPatchMissionFlowBattleRuntimeLateJoinSendAgentsNoOpPrefixProbe = false;
        private const bool EnableManualPatchMissionFlowBattleRuntimeFinishedLoadingGate = true;
        private const bool EnableManualPatchMissionFlowBattleRuntimeCameraPreview = true;
        private const bool EnableManualPatchExactCampaignRuntime = true;
        private const bool EnableManualPatchPreviewDiagnostics = false;
        private const bool EnableClientGameModeRegistration = true;
        private const bool EnableCampaignBehaviors = true;
        private const bool EnableNonCampaignModelRegistration = true;

        private bool _hasShownLoadedMessage; // Ð—Ð±ÐµÑ€Ñ–Ð³Ð°Ñ”Ð¼Ð¾ Ð¿Ñ€Ð°Ð¿Ð¾Ñ€ÐµÑ†ÑŒ, Ñ‰Ð¾Ð± Ð¿Ð¾ÐºÐ°Ð·Ð°Ñ‚Ð¸ "mod loaded" Ð»Ð¸ÑˆÐµ Ð¾Ð´Ð¸Ð½ Ñ€Ð°Ð· (Ñ– Ð½Ðµ ÑÐ¿Ð°Ð¼Ð¸Ñ‚Ð¸ Ð¿Ñ€Ð¸ Ð¿ÐµÑ€ÐµÐ·Ð°Ð¿ÑƒÑÐºÐ°Ñ… Ð³Ñ€Ð¸/ÑÐµÑÑ–Ð¹)
        private BattleDetector _battleDetector; // Ð”ÐµÑ‚ÐµÐºÑ‚Ð¾Ñ€ ÑÑ‚Ð°Ñ€Ñ‚Ñƒ Ð±Ð¸Ñ‚Ð²Ð¸ (Ð²Ñ–Ð´ÑÑ‚ÐµÐ¶ÑƒÑ” Ð¿ÐµÑ€ÐµÑ…Ñ–Ð´ Mission.Current Ð· null â†’ Ð½Ðµ-null)

        protected override void OnSubModuleLoad() // Ð’Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ñ‚ÑŒÑÑ ÐºÐ¾Ð»Ð¸ Ð¼Ð¾Ð´ Ð·Ð°Ð²Ð°Ð½Ñ‚Ð°Ð¶ÑƒÑ”Ñ‚ÑŒÑÑ (Ð´Ð¾ ÑÑ‚Ð°Ñ€Ñ‚Ñƒ Ð³Ñ€Ð¸/ÐºÐ°Ð¼Ð¿Ð°Ð½Ñ–Ñ—)
        { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ
            base.OnSubModuleLoad(); // Ð’Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ð¼Ð¾ Ð±Ð°Ð·Ð¾Ð²Ñƒ Ñ€ÐµÐ°Ð»Ñ–Ð·Ð°Ñ†Ñ–ÑŽ, Ñ‰Ð¾Ð± Ð½Ðµ Ð»Ð°Ð¼Ð°Ñ‚Ð¸ Ð²Ð½ÑƒÑ‚Ñ€Ñ–ÑˆÐ½ÑŽ Ð»Ð¾Ð³Ñ–ÐºÑƒ Ð³Ñ€Ð¸

            // Runtime diagnostics: assembly paths/versions (to detect build mismatch vs dedicated).
            try { AssemblyDiagnostics.LogRuntimeLoadPaths(); AssemblyDiagnostics.WarnIfAssemblyPathUnexpected(); } catch (Exception ex) { ModLogger.Info("AssemblyDiagnostics failed: " + ex.Message); }
            try { ExactBattleRuntimeBundleBridgeFile.ResetBundle("SubModule.OnSubModuleLoad"); } catch (Exception ex) { ModLogger.Info("ExactBattleRuntimeBundleBridgeFile reset failed: " + ex.Message); }
            CoopBattlePeerReconnectState.EnsureHooksInstalled();

            if (EnableCoopRuntimeStartup)
            {
                CoopRuntime.Initialize(); // Ð†Ð½Ñ–Ñ†Ñ–Ð°Ð»Ñ–Ð·ÑƒÑ”Ð¼Ð¾ Ð³Ð»Ð¾Ð±Ð°Ð»ÑŒÐ½Ð¸Ð¹ runtime (NetworkManager, Ñ‚Ð¾Ñ‰Ð¾)
                _battleDetector = new BattleDetector(); // Ð¡Ñ‚Ð²Ð¾Ñ€ÑŽÑ”Ð¼Ð¾ Ð´ÐµÑ‚ÐµÐºÑ‚Ð¾Ñ€ Ð±Ð¸Ñ‚Ð²Ð¸ Ð¾Ð´Ð¸Ð½ Ñ€Ð°Ð· (Ð²Ð¸ÐºÐ¾Ñ€Ð¸ÑÑ‚Ð¾Ð²ÑƒÐ²Ð°Ñ‚Ð¸Ð¼ÐµÑ‚ÑŒÑÑ Ð· OnApplicationTick)

                if (CoopRuntime.Network != null) // ÐŸÐµÑ€ÐµÐ²Ñ–Ñ€ÑÑ”Ð¼Ð¾ Ñ‰Ð¾ NetworkManager ÑÑ‚Ð²Ð¾Ñ€Ð¸Ð²ÑÑ ÑƒÑÐ¿Ñ–ÑˆÐ½Ð¾
                { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
                    CoopRuntime.Network.MessageReceived += OnNetworkMessageReceived; // ÐŸÑ–Ð´Ð¿Ð¸ÑÑƒÑ”Ð¼Ð¾ÑÑŒ Ð½Ð° Ð¿Ð¾Ð´Ñ–Ñ— Ð¼ÐµÑ€ÐµÐ¶Ñ–, Ñ‰Ð¾Ð± Ð±Ð°Ñ‡Ð¸Ñ‚Ð¸ Ñ‚ÐµÑÑ‚Ð¾Ð²Ñ– Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ
                } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
            }
            else
            {
                ModLogger.Info("Startup isolation: skipped CoopRuntime initialization, BattleDetector, and network subscriptions.");
            }

            _hasShownLoadedMessage = false; // Ð¡ÐºÐ¸Ð´Ð°Ñ”Ð¼Ð¾ Ð¿Ñ€Ð°Ð¿Ð¾Ñ€ÐµÑ†ÑŒ, Ð±Ð¾ OnSubModuleLoad Ð²Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ñ‚ÑŒÑÑ Ð´Ð¾ ÑÑ‚Ð°Ñ€Ñ‚Ñƒ Ð³Ñ€Ð¸, Ð° UI Ð¼Ð¾Ð¶Ðµ Ð±ÑƒÑ‚Ð¸ Ñ‰Ðµ Ð½Ðµ Ð³Ð¾Ñ‚Ð¾Ð²Ð¸Ð¹

            if (EnableHarmonyPatching)
            {
                LogStartupIsolationConfiguration();
                TryApplyHarmonyPatches(); // Пробуємо застосувати Harmony патчі (навіть якщо Bannerlord.Harmony мод не встановлений/не увімкнений)
            }
            else
            {
                ModLogger.Info("Startup isolation: skipped all Harmony patch registration.");
            }
#if HAS_GAMEMODE
            ModLogger.Info("[CoopSpectator] HAS_GAMEMODE=true (multiplayer game-mode registration available).");
            if (EnableClientGameModeRegistration)
            {
                TryRegisterCoopBattleForClient();
            }
            else
            {
                ModLogger.Info("Startup isolation: skipped CoopBattle client game-mode registration.");
            }
#else
            ModLogger.Info("[CoopSpectator] HAS_GAMEMODE=false (campaign + listed dedicated only).");
#endif
            ModLogger.Info("SubModule Ð·Ð°Ð²Ð°Ð½Ñ‚Ð°Ð¶ÐµÐ½Ð¾."); // Ð›Ð¾Ð³ÑƒÑ”Ð¼Ð¾ Ð·Ð°Ð²Ð°Ð½Ñ‚Ð°Ð¶ÐµÐ½Ð½Ñ Ð´Ð»Ñ Ð´ÐµÐ±Ð°Ð³Ñƒ
        } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ

        private static void LogStartupIsolationConfiguration()
        {
            ModLogger.Info(
                "Startup isolation config: " +
                $"PatchAll={EnableHarmonyPatchAll}, " +
                $"AssemblyLoadReapply={EnableHarmonyAssemblyLoadReapply}, " +
                $"LobbyClient={EnableManualPatchLobbyClient}, " +
                $"MissionFlowUi={EnableManualPatchMissionFlowUi}, " +
                $"OpenNew={EnableManualPatchMissionFlowOpenNew}, " +
                $"BattleShell={EnableManualPatchMissionFlowBattleShell}, " +
                $"BattleRuntime={EnableManualPatchMissionFlowBattleRuntime}, " +
                $"EntryUi={EnableManualPatchMissionFlowBattleRuntimeEntryUi}, " +
                $"SpawnHandoff={EnableManualPatchMissionFlowBattleRuntimeSpawnHandoff}, " +
                $"BootstrapGates={EnableManualPatchMissionFlowBattleRuntimeBootstrapGates}, " +
                $"LateJoinGate={EnableManualPatchMissionFlowBattleRuntimeLateJoinBootstrapGate}, " +
                $"HandleLateClientAfterLoadingFinished={EnableManualPatchMissionFlowBattleRuntimeLateJoinHandleLateClientAfterLoadingFinished}, " +
                $"SendAgentsToPeer={EnableManualPatchMissionFlowBattleRuntimeLateJoinSendAgentsToPeer}, " +
                $"SendMissilesToPeer={EnableManualPatchMissionFlowBattleRuntimeLateJoinSendMissilesToPeer}, " +
                $"SendAgentsNoOpPrefixProbe={EnableManualPatchMissionFlowBattleRuntimeLateJoinSendAgentsNoOpPrefixProbe}, " +
                $"FinishedLoadingGate={EnableManualPatchMissionFlowBattleRuntimeFinishedLoadingGate}, " +
                $"CameraPreview={EnableManualPatchMissionFlowBattleRuntimeCameraPreview}, " +
                $"ExactCampaignRuntime={EnableManualPatchExactCampaignRuntime}, " +
                $"ClientGameModes={EnableClientGameModeRegistration}, " +
                $"NonCampaignModels={EnableNonCampaignModelRegistration}.");
        }

#if HAS_GAMEMODE
        /// <summary>Ð ÐµÑ”ÑÑ‚Ñ€ÑƒÑ”Ð¼Ð¾ CoopBattle Ð½Ð° Ð·Ð²Ð¸Ñ‡Ð°Ð¹Ð½Ð¾Ð¼Ñƒ ÐºÐ»Ñ–Ñ”Ð½Ñ‚Ñ–, Ñ‰Ð¾Ð± InitializeCustomGameMessage Ð· mission=CoopBattle Ð¼Ð°Ð² Ð»Ð¾ÐºÐ°Ð»ÑŒÐ½Ð¸Ð¹ game-mode bootstrap.</summary>
        private static void TryRegisterCoopBattleForClient()
        {
            ModLogger.Info("[CoopSpectator] CoopBattle client registration start.");
            try
            {
                if (TaleWorlds.MountAndBlade.Module.CurrentModule != null)
                {
                    TaleWorlds.MountAndBlade.Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerCoopBattleMode(MissionMultiplayerCoopBattleMode.GameModeId));
                    GameModeOverridePatches.SetBattleOverride(new MissionMultiplayerCoopBattleMode(CoopGameModeIds.OfficialBattle));
                    ModLogger.Info("[CoopSpectator] CoopBattle client registration success (ready for joining CoopBattle custom games).");
                    ModLogger.Info("[CoopSpectator] Battle override armed on client. GetMultiplayerGameMode(Battle) will return CoopBattle runtime.");
                }
                else
                {
                    ModLogger.Info("[CoopSpectator] CoopBattle client registration fail: Module.CurrentModule is null.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("[CoopSpectator] CoopBattle client registration fail: " + ex.Message);
            }
        }

#endif

        private static void TryApplyHarmonyPatches() // Startup-isolated Harmony loader for bisect
        {
            try
            {
                var harmony = new Harmony("com.coopspectator.mod");

                if (EnableHarmonyPatchAll)
                {
                    harmony.PatchAll();
                }
                else
                {
                    ModLogger.Info("Startup isolation: skipped harmony.PatchAll() attribute patch registration.");
                }

                if (EnableManualHarmonyApply)
                {
                    if (EnableManualPatchLobbyClient)
                    {
                        GameModeOverridePatches.Apply(harmony);
                        LobbyCustomGameLocalJoinPatch.Apply(harmony);
                        LobbyJoinResultSelfJoinArmPatch.Apply(harmony);
                        LobbyRequestJoinDiagnosticsPatch.Apply(harmony);
                        IntermissionVmCrashGuardPatch.Apply(harmony);
                        StartupSafeMpHeroClassBootstrapPatch.Apply(harmony);
                    }
                    else
                    {
                        ModLogger.Info("Startup isolation: skipped manual lobby/client Apply(...) patch group.");
                    }

                    if (EnableManualPatchMissionFlowUi)
                    {
                        if (EnableManualPatchMissionFlowOpenNew)
                        {
                            MissionStateOpenNewPatches.Apply(harmony);
                        }
                        else
                        {
                            ModLogger.Info("Startup isolation: skipped mission-flow OpenNew Apply(...) patch subgroup.");
                        }

                        if (EnableManualPatchMissionFlowBattleRuntime)
                        {
                            if (EnableManualPatchMissionFlowBattleRuntimeEntryUi)
                            {
                                VanillaEntryUiSuppressionPatch.Apply(harmony);
                                BattleMapHudSuppressionPatch.Apply(harmony);
                            }
                            else
                            {
                                ModLogger.Info("Startup isolation: skipped mission-flow battle-runtime entry-ui Apply(...) patch subgroup.");
                            }

                            if (EnableManualPatchMissionFlowBattleRuntimeSpawnHandoff)
                            {
                                BattleMapSpawnHandoffPatch.Apply(harmony);
                            }
                            else
                            {
                                ModLogger.Info("Startup isolation: skipped mission-flow battle-runtime spawn-handoff Apply(...) patch subgroup.");
                            }

                            if (EnableManualPatchMissionFlowBattleRuntimeBootstrapGates)
                            {
                                if (EnableManualPatchMissionFlowBattleRuntimeLateJoinBootstrapGate)
                                {
                                    LateJoinPeerBootstrapGatePatch.Apply(
                                        harmony,
                                        patchHandleLateNewClientAfterLoadingFinished: EnableManualPatchMissionFlowBattleRuntimeLateJoinHandleLateClientAfterLoadingFinished,
                                        patchSendAgentsToPeer: EnableManualPatchMissionFlowBattleRuntimeLateJoinSendAgentsToPeer,
                                        patchSendMissilesToPeer: EnableManualPatchMissionFlowBattleRuntimeLateJoinSendMissilesToPeer,
                                        useNoOpSendAgentsPrefix: EnableManualPatchMissionFlowBattleRuntimeLateJoinSendAgentsNoOpPrefixProbe);
                                }
                                else
                                {
                                    ModLogger.Info("Startup isolation: skipped mission-flow battle-runtime late-join bootstrap-gate Apply(...) patch subgroup.");
                                }

                                if (EnableManualPatchMissionFlowBattleRuntimeFinishedLoadingGate)
                                {
                                    FinishedLoadingMissionReadyGatePatch.Apply(harmony);
                                }
                                else
                                {
                                    ModLogger.Info("Startup isolation: skipped mission-flow battle-runtime finished-loading gate Apply(...) patch subgroup.");
                                }
                            }
                            else
                            {
                                ModLogger.Info("Startup isolation: skipped mission-flow battle-runtime bootstrap-gates Apply(...) patch subgroup.");
                            }

                            if (EnableManualPatchMissionFlowBattleRuntimeCameraPreview)
                            {
                                MissionScreenCameraPreviewPatch.Apply(harmony);
                            }
                            else
                            {
                                ModLogger.Info("Startup isolation: skipped mission-flow battle-runtime camera-preview Apply(...) patch subgroup.");
                            }
                        }
                        else
                        {
                            ModLogger.Info("Startup isolation: skipped mission-flow battle-runtime/ui Apply(...) patch subgroup.");
                        }

                        if (EnableManualPatchMissionFlowBattleShell)
                        {
                            BattleShellSuppressionPatch.Apply(harmony);
                        }
                        else
                        {
                            ModLogger.Info("Startup isolation: skipped mission-flow battle-shell Apply(...) patch subgroup.");
                        }
                    }
                    else
                    {
                        ModLogger.Info("Startup isolation: skipped manual mission-flow/ui Apply(...) patch group.");
                    }

                    if (EnableManualPatchExactCampaignRuntime)
                    {
                        ExactCampaignArmyBootstrapPatch.Apply(harmony);
                        ExactCampaignPreSpawnLoadoutPatch.Apply(harmony);
                        ExactCampaignNetworkObjectBootstrapPatch.Apply(harmony);
                        AgentDisplayNamePatch.Apply(harmony);
                        ClientChangeCultureCanonicalizationPatch.Apply(harmony);
                        MultiplayerCharacterClassFallbackPatch.Apply(harmony);
                        CampaignCombatProfileAgentStatsPatch.Apply(harmony);
                    }
                    else
                    {
                        ModLogger.Info("Startup isolation: skipped manual exact/runtime Apply(...) patch group.");
                    }

                    if (EnableManualPatchPreviewDiagnostics)
                    {
                        CharacterTableauChainDiagnosticsPatch.Apply(harmony);
                        CampaignVisualResetPatch.Apply(harmony);
                    }
                    else
                    {
                        ModLogger.Info("Startup isolation: skipped manual preview/diagnostics Apply(...) patch group.");
                    }
                }
                else
                {
                    ModLogger.Info("Startup isolation: skipped manual Harmony Apply(...) patch registration.");
                }

                if (EnableManualHarmonyApply && EnableHarmonyAssemblyLoadReapply)
                {
                    AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
                    {
                        string loadedAssemblyName = e.LoadedAssembly.GetName().Name;
                        if (loadedAssemblyName == "TaleWorlds.MountAndBlade.Multiplayer" ||
                            loadedAssemblyName == "TaleWorlds.MountAndBlade.Lobby")
                        {
                            LobbyCustomGameLocalJoinPatch.Apply(harmony);
                        }

                        if (loadedAssemblyName == "TaleWorlds.MountAndBlade.Diamond")
                        {
                            LobbyJoinResultSelfJoinArmPatch.Apply(harmony);
                            LobbyRequestJoinDiagnosticsPatch.Apply(harmony);
                        }

                        if (loadedAssemblyName == "TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection")
                        {
                            IntermissionVmCrashGuardPatch.Apply(harmony);
                        }
                    };
                }
                else if (EnableManualHarmonyApply)
                {
                    ModLogger.Info("Startup isolation: skipped Harmony AssemblyLoad reapply hooks.");
                }

                ModLogger.Info("Harmony патчі застосовано успішно.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Не вдалося застосувати Harmony патчі.", ex);
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject) // Викликається коли стартує гра/кампанія (UI вже зазвичай ініціалізований)
        { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ
            base.OnGameStart(game, gameStarterObject); // Ð’Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ð¼Ð¾ Ð±Ð°Ð·Ð¾Ð²Ñƒ Ñ€ÐµÐ°Ð»Ñ–Ð·Ð°Ñ†Ñ–ÑŽ, Ñ‰Ð¾Ð± Ð½Ðµ Ð»Ð°Ð¼Ð°Ñ‚Ð¸ Ñ–Ð½Ñ–Ñ†Ñ–Ð°Ð»Ñ–Ð·Ð°Ñ†Ñ–ÑŽ Ð³Ñ€Ð¸

            bool isCampaignGame = game != null && game.GameType is TaleWorlds.CampaignSystem.Campaign;

            if (isCampaignGame) // ÐŸÐµÑ€ÐµÐ²Ñ–Ñ€ÑÑ”Ð¼Ð¾ Ñ‰Ð¾ Ð³Ñ€Ð° ÑÑ‚Ð°Ñ€Ñ‚ÑƒÐ²Ð°Ð»Ð° ÑÐ°Ð¼Ðµ ÑÐº ÐºÐ°Ð¼Ð¿Ð°Ð½Ñ–Ñ (Ð½Ð°Ñˆ Ð¼Ð¾Ð´ Ð¿Ñ€Ð°Ñ†ÑŽÑ” Ð² ÐºÐ°Ð¼Ð¿Ð°Ð½Ñ–Ñ—)
            { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
                CampaignGameStarter starter = gameStarterObject as CampaignGameStarter; // ÐŸÑ€Ð¾Ð±ÑƒÑ”Ð¼Ð¾ Ð¿Ñ€Ð¸Ð²ÐµÑÑ‚Ð¸ IGameStarter Ð´Ð¾ CampaignGameStarter, Ñ‰Ð¾Ð± Ð´Ð¾Ð´Ð°Ñ‚Ð¸ behaviors

                if (starter != null && EnableCampaignBehaviors) // ÐŸÐµÑ€ÐµÐ²Ñ–Ñ€ÑÑ”Ð¼Ð¾ Ñ‰Ð¾ Ð¿Ñ€Ð¸Ð²ÐµÐ´ÐµÐ½Ð½Ñ ÑƒÑÐ¿Ñ–ÑˆÐ½Ðµ Ñ– starter Ð½Ðµ null
                { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
                    starter.AddBehavior(new HostStateBroadcaster()); // Ð”Ð¾Ð´Ð°Ñ”Ð¼Ð¾ behavior Ñ…Ð¾ÑÑ‚Ð° (Ð²Ñ–Ð½ Ð±ÑƒÐ´Ðµ Ð²Ñ–Ð´Ð¿Ñ€Ð°Ð²Ð»ÑÑ‚Ð¸ STATE Ñ‚Ñ–Ð»ÑŒÐºÐ¸ ÐºÐ¾Ð»Ð¸ Ð¼Ð¸ Server)
                    starter.AddBehavior(new SpectatorStateReceiver()); // Ð”Ð¾Ð´Ð°Ñ”Ð¼Ð¾ behavior ÐºÐ»Ñ–Ñ”Ð½Ñ‚Ð° (Ð²Ñ–Ð½ Ð±ÑƒÐ´Ðµ Ð¿Ñ€Ð¸Ð¹Ð¼Ð°Ñ‚Ð¸ STATE Ñ‚Ñ–Ð»ÑŒÐºÐ¸ ÐºÐ¾Ð»Ð¸ Ð¼Ð¸ Client)
                    starter.AddBehavior(new ClientBattleNotification()); // ÐšÐ»Ñ–Ñ”Ð½Ñ‚: ÑÐ»ÑƒÑ…Ð°Ñ” BATTLE_START Ñ– Ð¿Ð¾ÐºÐ°Ð·ÑƒÑ” countdown/notification
                    starter.AddBehavior(new MainThreadDispatcherPumpBehavior()); // Pump dispatcher from campaign tick for reliable UI feedback
                    ModLogger.Info("Campaign behaviors Ð´Ð¾Ð´Ð°Ð½Ð¾ (HostStateBroadcaster + SpectatorStateReceiver)."); // Ð›Ð¾Ð³ÑƒÑ”Ð¼Ð¾ Ñ„Ð°ÐºÑ‚ Ð´Ð¾Ð´Ð°Ð²Ð°Ð½Ð½Ñ behaviors Ð´Ð»Ñ Ð´ÐµÐ±Ð°Ð³Ñƒ
                } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
                else if (!EnableCampaignBehaviors)
                {
                    ModLogger.Info("Startup isolation: skipped campaign coop behaviors.");
                }
            } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
            else if (EnableNonCampaignModelRegistration)
            {
                TryRegisterCoopCampaignDerivedAgentStatModel(game, gameStarterObject, "client");
                TryRegisterCoopCampaignDerivedStrikeMagnitudeModel(game, gameStarterObject, "client");
                TryRegisterCoopCampaignDerivedAgentApplyDamageModel(game, gameStarterObject, "client");
                TryRegisterCoopCampaignDerivedMissionDifficultyModel(game, gameStarterObject, "client");
            }
            else
            {
                ModLogger.Info("Startup isolation: skipped non-campaign model registration.");
            }

            if (_hasShownLoadedMessage) // ÐŸÐµÑ€ÐµÐ²Ñ–Ñ€ÑÑ”Ð¼Ð¾ Ð¿Ñ€Ð°Ð¿Ð¾Ñ€ÐµÑ†ÑŒ, Ñ‰Ð¾Ð± Ð½Ðµ Ð¿Ð¾ÐºÐ°Ð·ÑƒÐ²Ð°Ñ‚Ð¸ Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ Ð¿Ð¾Ð²Ñ‚Ð¾Ñ€Ð½Ð¾
            { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
                return; // Ð’Ð¸Ñ…Ð¾Ð´Ð¸Ð¼Ð¾, Ð±Ð¾ Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ Ð²Ð¶Ðµ Ð±ÑƒÐ»Ð¾ Ð¿Ð¾ÐºÐ°Ð·Ð°Ð½Ð¾
            } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if

            _hasShownLoadedMessage = true; // Ð’ÑÑ‚Ð°Ð½Ð¾Ð²Ð»ÑŽÑ”Ð¼Ð¾ Ð¿Ñ€Ð°Ð¿Ð¾Ñ€ÐµÑ†ÑŒ, Ñ‰Ð¾Ð± Ð¿Ð¾Ð²Ñ‚Ð¾Ñ€Ð½Ð¸Ð¹ OnGameStart Ð½Ðµ ÑÐ¿Ð°Ð¼Ð¸Ð² Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½ÑÐ¼Ð¸
            ShowMessage("Bannerlord Coop Campaign mod loaded! (v0.1.1-ui)"); // Version marker to confirm the client runs the latest build
        } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ

        private static void TryRegisterCoopCampaignDerivedAgentStatModel(Game game, IGameStarter gameStarterObject, string source)
        {
            try
            {
                BasicGameStarter basicStarter = gameStarterObject as BasicGameStarter;
                if (basicStarter == null)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedAgentStatCalculateModel: skip registration on " + source +
                        " because starter is " + (gameStarterObject?.GetType().FullName ?? "null") + ".");
                    return;
                }

                AgentStatCalculateModel baseModel = basicStarter.GetModel<AgentStatCalculateModel>();
                if (baseModel == null)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedAgentStatCalculateModel: skip registration on " + source +
                        " because base AgentStatCalculateModel is missing. GameType=" +
                        (game?.GameType?.GetType().FullName ?? "null") + ".");
                    return;
                }

                if (baseModel is CoopCampaignDerivedAgentStatCalculateModel)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedAgentStatCalculateModel: already registered on " + source +
                        ". GameType=" + (game?.GameType?.GetType().FullName ?? "null") + ".");
                    return;
                }

                basicStarter.AddModel(new CoopCampaignDerivedAgentStatCalculateModel(baseModel));
                ModLogger.Info(
                    "CoopCampaignDerivedAgentStatCalculateModel: registered on " + source +
                    ". GameType=" + (game?.GameType?.GetType().FullName ?? "null") +
                    " BaseModel=" + baseModel.GetType().FullName + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopCampaignDerivedAgentStatCalculateModel: registration failed on " + source + ": " + ex.Message);
            }
        }

        private static void TryRegisterCoopCampaignDerivedStrikeMagnitudeModel(Game game, IGameStarter gameStarterObject, string source)
        {
            try
            {
                BasicGameStarter basicStarter = gameStarterObject as BasicGameStarter;
                if (basicStarter == null)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedStrikeMagnitudeCalculationModel: skip registration on " + source +
                        " because starter is " + (gameStarterObject?.GetType().FullName ?? "null") + ".");
                    return;
                }

                StrikeMagnitudeCalculationModel baseModel = basicStarter.GetModel<StrikeMagnitudeCalculationModel>();
                if (baseModel == null)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedStrikeMagnitudeCalculationModel: skip registration on " + source +
                        " because base StrikeMagnitudeCalculationModel is missing. GameType=" +
                        (game?.GameType?.GetType().FullName ?? "null") + ".");
                    return;
                }

                if (baseModel is CoopCampaignDerivedStrikeMagnitudeCalculationModel)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedStrikeMagnitudeCalculationModel: already registered on " + source +
                        ". GameType=" + (game?.GameType?.GetType().FullName ?? "null") + ".");
                    return;
                }

                basicStarter.AddModel(new CoopCampaignDerivedStrikeMagnitudeCalculationModel(baseModel));
                ModLogger.Info(
                    "CoopCampaignDerivedStrikeMagnitudeCalculationModel: registered on " + source +
                    ". GameType=" + (game?.GameType?.GetType().FullName ?? "null") +
                    " BaseModel=" + baseModel.GetType().FullName + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopCampaignDerivedStrikeMagnitudeCalculationModel: registration failed on " + source + ": " + ex.Message);
            }
        }

        private static void TryRegisterCoopCampaignDerivedAgentApplyDamageModel(Game game, IGameStarter gameStarterObject, string source)
        {
            try
            {
                BasicGameStarter basicStarter = gameStarterObject as BasicGameStarter;
                if (basicStarter == null)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedAgentApplyDamageModel: skip registration on " + source +
                        " because starter is " + (gameStarterObject?.GetType().FullName ?? "null") + ".");
                    return;
                }

                AgentApplyDamageModel baseModel = basicStarter.GetModel<AgentApplyDamageModel>();
                if (baseModel == null)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedAgentApplyDamageModel: skip registration on " + source +
                        " because base AgentApplyDamageModel is missing. GameType=" +
                        (game?.GameType?.GetType().FullName ?? "null") + ".");
                    return;
                }

                if (baseModel is CoopCampaignDerivedAgentApplyDamageModel)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedAgentApplyDamageModel: already registered on " + source +
                        ". GameType=" + (game?.GameType?.GetType().FullName ?? "null") + ".");
                    return;
                }

                basicStarter.AddModel(new CoopCampaignDerivedAgentApplyDamageModel(baseModel));
                ModLogger.Info(
                    "CoopCampaignDerivedAgentApplyDamageModel: registered on " + source +
                    ". GameType=" + (game?.GameType?.GetType().FullName ?? "null") +
                    " BaseModel=" + baseModel.GetType().FullName + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopCampaignDerivedAgentApplyDamageModel: registration failed on " + source + ": " + ex.Message);
            }
        }

        private static void TryRegisterCoopCampaignDerivedMissionDifficultyModel(Game game, IGameStarter gameStarterObject, string source)
        {
            try
            {
                BasicGameStarter basicStarter = gameStarterObject as BasicGameStarter;
                if (basicStarter == null)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedMissionDifficultyModel: skip registration on " + source +
                        " because starter is " + (gameStarterObject?.GetType().FullName ?? "null") + ".");
                    return;
                }

                MissionDifficultyModel baseModel = basicStarter.GetModel<MissionDifficultyModel>();
                if (baseModel is CoopCampaignDerivedMissionDifficultyModel)
                {
                    ModLogger.Info(
                        "CoopCampaignDerivedMissionDifficultyModel: already registered on " + source +
                        ". GameType=" + (game?.GameType?.GetType().FullName ?? "null") + ".");
                    return;
                }

                MissionDifficultyModel effectiveBaseModel = baseModel ?? new DefaultMissionDifficultyModel();
                basicStarter.AddModel(new CoopCampaignDerivedMissionDifficultyModel(effectiveBaseModel));
                ModLogger.Info(
                    "CoopCampaignDerivedMissionDifficultyModel: registered on " + source +
                    ". GameType=" + (game?.GameType?.GetType().FullName ?? "null") +
                    " BaseModel=" + effectiveBaseModel.GetType().FullName +
                    " BaseWasMissing=" + (baseModel == null) + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopCampaignDerivedMissionDifficultyModel: registration failed on " + source + ": " + ex.Message);
            }
        }

        protected override void OnSubModuleUnloaded() // Ð’Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ñ‚ÑŒÑÑ ÐºÐ¾Ð»Ð¸ Ð¼Ð¾Ð´ Ð²Ð¸Ð²Ð°Ð½Ñ‚Ð°Ð¶ÑƒÑ”Ñ‚ÑŒÑÑ (Ð·Ð°ÐºÑ€Ð¸Ñ‚Ñ‚Ñ Ð³Ñ€Ð¸ / Ð¿ÐµÑ€ÐµÐ·Ð°Ð²Ð°Ð½Ñ‚Ð°Ð¶ÐµÐ½Ð½Ñ Ð¼Ð¾Ð´Ñ–Ð²)
        { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ
            try // ÐžÐ±Ð³Ð¾Ñ€Ñ‚Ð°Ñ”Ð¼Ð¾ cleanup Ð² try-catch, Ñ‰Ð¾Ð± Ð½Ðµ ÐºÑ€Ð°ÑˆÐ½ÑƒÑ‚Ð¸ Ð³Ñ€Ñƒ Ð¿Ñ€Ð¸ Ð²Ð¸Ð²Ð°Ð½Ñ‚Ð°Ð¶ÐµÐ½Ð½Ñ–
            { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº try
                if (EnableCoopRuntimeStartup && CoopRuntime.Network != null) // ÐŸÐµÑ€ÐµÐ²Ñ–Ñ€ÑÑ”Ð¼Ð¾ Ñ‰Ð¾ NetworkManager Ñ–ÑÐ½ÑƒÑ”
                { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
                    CoopRuntime.Network.MessageReceived -= OnNetworkMessageReceived; // Ð’Ñ–Ð´Ð¿Ð¸ÑÑƒÑ”Ð¼Ð¾ÑÑŒ Ð²Ñ–Ð´ Ð¿Ð¾Ð´Ñ–Ð¹, Ñ‰Ð¾Ð± ÑƒÐ½Ð¸ÐºÐ½ÑƒÑ‚Ð¸ memory leak
                } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if

                if (EnableCoopRuntimeStartup)
                {
                    CoopRuntime.Shutdown(); // ÐšÐ¾Ñ€ÐµÐºÑ‚Ð½Ð¾ Ð·ÑƒÐ¿Ð¸Ð½ÑÑ”Ð¼Ð¾ Ð¼ÐµÑ€ÐµÐ¶Ñƒ Ñ– Ð·Ð²Ñ–Ð»ÑŒÐ½ÑÑ”Ð¼Ð¾ Ñ€ÐµÑÑƒÑ€ÑÐ¸
                }
            } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº try
            catch (Exception ex) // Ð›Ð¾Ð²Ð¸Ð¼Ð¾ Ð±ÑƒÐ´ÑŒ-ÑÐºÑ– Ð²Ð¸Ð½ÑÑ‚ÐºÐ¸, Ñ‰Ð¾Ð± unload Ð½Ðµ Ð¿Ð°Ð´Ð°Ð²
            { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº catch
                ModLogger.Error("ÐŸÐ¾Ð¼Ð¸Ð»ÐºÐ° Ð¿Ñ–Ð´ Ñ‡Ð°Ñ OnSubModuleUnloaded.", ex); // Ð›Ð¾Ð³ÑƒÑ”Ð¼Ð¾ Ð¿Ð¾Ð¼Ð¸Ð»ÐºÑƒ Ð´Ð»Ñ Ð´ÐµÐ±Ð°Ð³Ñƒ
            } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº catch
            finally // Ð“Ð°Ñ€Ð°Ð½Ñ‚ÑƒÑ”Ð¼Ð¾ Ð²Ð¸ÐºÐ»Ð¸Ðº Ð±Ð°Ð·Ð¾Ð²Ð¾Ð³Ð¾ Ð¼ÐµÑ‚Ð¾Ð´Ñƒ
            { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº finally
                base.OnSubModuleUnloaded(); // Ð’Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ð¼Ð¾ Ð±Ð°Ð·Ð¾Ð²Ñƒ Ñ€ÐµÐ°Ð»Ñ–Ð·Ð°Ñ†Ñ–ÑŽ
            } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº finally
        } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ

        protected override void OnApplicationTick(float dt) // Ð’Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ñ‚ÑŒÑÑ ÐºÐ¾Ð¶ÐµÐ½ ÐºÐ°Ð´Ñ€ Ð½Ð° Ñ€Ñ–Ð²Ð½Ñ– Ð´Ð¾Ð´Ð°Ñ‚ÐºÑƒ (ÐºÐ¾Ð»Ð¸ Ð³Ñ€Ð° Ð·Ð°Ð¿ÑƒÑ‰ÐµÐ½Ð°)
        { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ
            base.OnApplicationTick(dt); // Ð’Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ð¼Ð¾ Ð±Ð°Ð·Ð¾Ð²Ñƒ Ñ€ÐµÐ°Ð»Ñ–Ð·Ð°Ñ†Ñ–ÑŽ

            if (EnableCoopRuntimeStartup)
            {
                MainThreadDispatcher.ExecutePending(); // Ð’Ð¸ÐºÐ¾Ð½ÑƒÑ”Ð¼Ð¾ Ð´Ñ–Ñ—, ÑÐºÑ– Ð±ÑƒÐ»Ð¸ Ð¿Ð¾ÑÑ‚Ð°Ð²Ð»ÐµÐ½Ñ– Ð² Ñ‡ÐµÑ€Ð³Ñƒ Ð· Ð¼ÐµÑ€ÐµÐ¶ÐµÐ²Ð¾Ð³Ð¾ Ð¿Ð¾Ñ‚Ð¾ÐºÑƒ
                _battleDetector?.Tick(); // ÐŸÐµÑ€ÐµÐ²Ñ–Ñ€ÑÑ”Ð¼Ð¾ Ñ‡Ð¸ ÑÑ‚Ð°Ñ€Ñ‚ÑƒÐ²Ð°Ð»Ð° Ð¼Ñ–ÑÑ–Ñ/Ð±Ð¸Ñ‚Ð²Ð° Ñ–, ÑÐºÑ‰Ð¾ Ð¼Ð¸ Ñ…Ð¾ÑÑ‚, ÑˆÐ»ÐµÐ¼Ð¾ BATTLE_START ÐºÐ»Ñ–Ñ”Ð½Ñ‚Ð°Ð¼
            }
        } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ

        private void OnNetworkMessageReceived(string message) // ÐžÐ±Ñ€Ð¾Ð±Ð»ÑÑ”Ð¼Ð¾ Ð¼ÐµÑ€ÐµÐ¶ÐµÐ²Ñ– Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ (Ð²Ð¸ÐºÐ»Ð¸ÐºÐ°Ñ”Ñ‚ÑŒÑÑ Ð² Ð³Ð¾Ð»Ð¾Ð²Ð½Ð¾Ð¼Ñƒ Ð¿Ð¾Ñ‚Ð¾Ñ†Ñ–)
        { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ
            if (string.IsNullOrEmpty(message)) // ÐŸÐµÑ€ÐµÐ²Ñ–Ñ€ÑÑ”Ð¼Ð¾ Ñ‰Ð¾ Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ Ð½Ðµ Ð¿Ð¾Ñ€Ð¾Ð¶Ð½Ñ”
            { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if
                return; // Ð’Ð¸Ñ…Ð¾Ð´Ð¸Ð¼Ð¾, Ð±Ð¾ Ð½Ñ–Ñ‡Ð¾Ð³Ð¾ Ð¿Ð¾ÐºÐ°Ð·ÑƒÐ²Ð°Ñ‚Ð¸
            } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº if

            string trimmed = message.TrimStart(); // Normalize possible leading whitespace from network framing

            // Filter out raw host state sync spam from UI (STATE:{json} is handled elsewhere). // Explain why we skip
            if (trimmed.StartsWith("STATE:", StringComparison.Ordinal)) // Check protocol prefix safely and fast
            { // Begin if
                return; // Don't show NET: STATE:{json} in UI
            } // End if

            // Filter out raw battle start payload spam (handled by ClientBattleNotification). // Explain why we skip
            if (trimmed.StartsWith("BATTLE_START:", StringComparison.Ordinal)) // Check protocol prefix safely and fast
            { // Begin if
                return; // Don't show NET: BATTLE_START:{json} in UI
            } // End if

            ShowMessage("NET: " + message); // ÐŸÐ¾ÐºÐ°Ð·ÑƒÑ”Ð¼Ð¾ Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ Ð² UI Ð´Ð»Ñ ÑˆÐ²Ð¸Ð´ÐºÐ¾Ð³Ð¾ Ñ‚ÐµÑÑ‚Ñƒ networking
            ModLogger.Info("ÐžÑ‚Ñ€Ð¸Ð¼Ð°Ð½Ð¾ Ð¼ÐµÑ€ÐµÐ¶ÐµÐ²Ðµ Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ: " + message); // Ð›Ð¾Ð³ÑƒÑ”Ð¼Ð¾ Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ Ð² Ð»Ð¾Ð³ Ð³Ñ€Ð¸
        } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ

        private static void ShowMessage(string text) // ÐžÐ³Ð¾Ð»Ð¾ÑˆÑƒÑ”Ð¼Ð¾ helper Ð´Ð»Ñ Ð¿Ð¾ÐºÐ°Ð·Ñƒ Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½ÑŒ Ð² UI Bannerlord
        { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ
            try // Ð—Ð°Ñ…Ð¸Ñ‰Ð°Ñ”Ð¼Ð¾ UI Ð²Ð¸ÐºÐ»Ð¸Ðº, Ñ‰Ð¾Ð± Ð½Ðµ ÐºÑ€Ð°ÑˆÐ½ÑƒÑ‚Ð¸ ÑÐºÑ‰Ð¾ InformationManager Ð½ÐµÐ´Ð¾ÑÑ‚ÑƒÐ¿Ð½Ð¸Ð¹ Ñƒ Ð¿ÐµÐ²Ð½Ð¸Ð¹ Ð¼Ð¾Ð¼ÐµÐ½Ñ‚
            { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº try
                InformationManager.DisplayMessage(new InformationMessage(text)); // ÐŸÐ¾ÐºÐ°Ð·ÑƒÑ”Ð¼Ð¾ Ð¿Ð¾Ð²Ñ–Ð´Ð¾Ð¼Ð»ÐµÐ½Ð½Ñ Ð³Ñ€Ð°Ð²Ñ†ÑŽ Ð² Ð»Ñ–Ð²Ð¾Ð¼Ñƒ Ð½Ð¸Ð¶Ð½ÑŒÐ¾Ð¼Ñƒ ÐºÑƒÑ‚Ñ–
            } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº try
            catch (Exception ex) // Ð›Ð¾Ð²Ð¸Ð¼Ð¾ Ð²Ð¸Ð½ÑÑ‚Ð¾Ðº ÑÐº fallback
            { // ÐŸÐ¾Ñ‡Ð¸Ð½Ð°Ñ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº catch
                ModLogger.Error("ÐÐµ Ð²Ð´Ð°Ð»Ð¾ÑÑ Ð¿Ð¾ÐºÐ°Ð·Ð°Ñ‚Ð¸ InformationMessage.", ex); // Ð›Ð¾Ð³ÑƒÑ”Ð¼Ð¾ Ð¿Ð¾Ð¼Ð¸Ð»ÐºÑƒ, Ñ‰Ð¾Ð± Ð·Ð½Ð°Ñ‚Ð¸ Ñ‰Ð¾ ÑÐ°Ð¼Ðµ ÑÑ‚Ð°Ð»Ð¾ÑÑ
            } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº catch
        } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¼ÐµÑ‚Ð¾Ð´Ñƒ
    } // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº ÐºÐ»Ð°ÑÑƒ
} // Ð—Ð°Ð²ÐµÑ€ÑˆÑƒÑ”Ð¼Ð¾ Ð±Ð»Ð¾Ðº Ð¿Ñ€Ð¾ÑÑ‚Ð¾Ñ€Ñƒ Ñ–Ð¼ÐµÐ½


