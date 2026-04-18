// Точка входу тільки для Dedicated Server. Реєстрація CoopTdm/CoopBattle + Harmony-патчі WebPanel (неблокуючий старт).
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using CoopSpectator.DedicatedServer.MissionOverrides;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.MissionModels;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ComponentInterfaces;

namespace CoopSpectator
{
    public sealed class SubModule : MBSubModuleBase
    {
        /// <summary>ТЗ A: true = тільки proof-of-load, без Harmony і без реєстрації game mode. false = реєструємо TdmClone + Harmony (для Етапу 3.3 — тест з нашими mission behaviors і логуванням; UseTdmCloneForListedTest у Launcher має бути true).</summary>
        private const bool CleanModuleLoadOnly = false;
        private const bool EnableFixedTestCultures = true;
        private const string FixedAttackerCultureId = "empire";
        private const string FixedDefenderCultureId = "vlandia";

        private static Harmony _harmony;
        private static bool _hasAppliedFixedTestCultures;
        private static DateTime _fixedCultureFirstAttemptUtc = DateTime.MinValue;
        private static DateTime _nextFixedCultureAttemptUtc = DateTime.MinValue;
        private static string _configuredHarmonyDmdGeneratorTypeName = "unconfigured";

        private static string GetProofFilePath(string name)
        {
            string dir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
            return Path.Combine(dir, name);
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);

            if (ExperimentalFeatures.EnableTdmCloneExperiment)
                return;

            try
            {
                Mission currentMission = Mission.Current;
                if (currentMission == null)
                {
                    DedicatedKnockoutOutcomeModelOverride.RestoreIfNeeded();
                    TryApplyFixedTestCulturesForNextMission();
                    return;
                }

                _hasAppliedFixedTestCultures = false;

                DedicatedKnockoutOutcomeModelOverride.UpdateForMission(currentMission);
                CoopMissionSpawnLogic.TryRunDedicatedMissionObserver(currentMission);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: mission observer tick failed: " + ex.Message);
            }
        }

        private static void TryApplyFixedTestCulturesForNextMission()
        {
            if (!EnableFixedTestCultures || _hasAppliedFixedTestCultures)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (_fixedCultureFirstAttemptUtc == DateTime.MinValue)
            {
                _fixedCultureFirstAttemptUtc = nowUtc;
                _nextFixedCultureAttemptUtc = nowUtc.AddSeconds(10);
                return;
            }

            if (nowUtc < _nextFixedCultureAttemptUtc)
                return;

            _nextFixedCultureAttemptUtc = nowUtc.AddSeconds(1);

            try
            {
                MultiplayerOptions.OptionType attackerCultureOption = (MultiplayerOptions.OptionType)14;
                MultiplayerOptions.OptionType defenderCultureOption = (MultiplayerOptions.OptionType)15;
                MultiplayerOptions.MultiplayerOptionsAccessMode accessMode = (MultiplayerOptions.MultiplayerOptionsAccessMode)2;

                MultiplayerOptionsExtensions.SetValue(attackerCultureOption, FixedAttackerCultureId, accessMode);
                MultiplayerOptionsExtensions.SetValue(defenderCultureOption, FixedDefenderCultureId, accessMode);

                string appliedAttackerCulture = MultiplayerOptionsExtensions.GetStrValue(attackerCultureOption, accessMode);
                string appliedDefenderCulture = MultiplayerOptionsExtensions.GetStrValue(defenderCultureOption, accessMode);

                _hasAppliedFixedTestCultures = true;
                ModLogger.Info(
                    "CoopSpectatorDedicated: applied fixed next-mission cultures. " +
                    "Attacker=" + appliedAttackerCulture +
                    " Defender=" + appliedDefenderCulture);
            }
            catch (NullReferenceException)
            {
                // MultiplayerOptions internals are not initialized yet during early dedicated startup.
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: fixed next-mission culture override failed: " + ex.Message);
            }
        }

        protected override void OnSubModuleLoad()
        {
            const string loadedFile = "CoopSpectatorDedicated_loaded.txt";
            const string errorFile = "CoopSpectatorDedicated_error.txt";

            try
            {
                // --- Proof-of-load: три незалежні маркери (консоль, файл, Debug) ---
                System.Console.WriteLine("[CoopSpectator] DEDICATED OnSubModuleLoad called");

                string loadedPath = GetProofFilePath(loadedFile);
                int pid = Process.GetCurrentProcess().Id;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                string processName = Process.GetCurrentProcess().ProcessName ?? "";
                string asmLocation = "";
                try { asmLocation = Assembly.GetExecutingAssembly().Location ?? ""; } catch (Exception) { asmLocation = "(get failed)"; }
                string line = string.Format("[{0:O}] PID={1} ProcessName={2} BaseDirectory={3} Assembly.Location={4}{5}",
                    DateTime.UtcNow, pid, processName, baseDir, asmLocation, Environment.NewLine);
                try { File.AppendAllText(loadedPath, line); } catch (Exception ex) { System.Console.WriteLine("[CoopSpectator] failed to write loaded file: " + ex.Message); }

                try { ModLogger.Info("[DedicatedDiag] CoopSpectatorDedicated OnSubModuleLoad. PID=" + pid + " BaseDir=" + baseDir + " ExecutingAssembly.Location=" + asmLocation); } catch (Exception) { }

                // Runtime diagnostics: assembly paths/versions, BUILD_MARKER, SERVER_BINARY_ID.
                try { AssemblyDiagnostics.LogRuntimeLoadPaths(); AssemblyDiagnostics.WarnIfAssemblyPathUnexpected(); } catch (Exception ex) { ModLogger.Info("[DedicatedDiag] AssemblyDiagnostics failed: " + ex.Message); }

                base.OnSubModuleLoad();
                LogDedicatedStartupInfo();

                try { DedicatedSceneContractProbe.RunStartupProbe(); } catch (Exception ex) { ModLogger.Info("CoopSpectatorDedicated: scene contract startup probe failed: " + ex.Message); }

                if (CleanModuleLoadOnly)
                {
                    try { ModLogger.Info("CoopSpectatorDedicated minimal mode active (no Harmony, no game mode registration)."); } catch (Exception) { }
                    System.Console.WriteLine("[CoopSpectator] CoopSpectatorDedicated minimal mode active.");
                }
                else
                {
                    TryConfigureDedicatedHarmonyRuntimeCompat();
                    TryApplyGameModeOverridePatch();
                    TryApplyMissionStateOpenNewPatches();
                    TryApplyExactCampaignArmyBootstrapPatch();
                    TryApplyExactCampaignPreSpawnLoadoutPatch();
                    TryApplyBattleMapSpawnHandoffPatch();
                    TryApplyBattleShellSuppressionPatch();
                    TryApplyMultiplayerHeroClassOverridePatch();
                    TryApplyMultiplayerCharacterClassFallbackPatch();
                    TryApplyServerChangeCultureCanonicalizationPatch();
                    TryApplyCampaignCombatProfileAgentStatsPatch();
                    RegisterCoopBattleGameMode();
                    TryApplyWebPanelPatches();
                    AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
                    {
                        string name = e.LoadedAssembly.GetName().Name ?? "";
                        if (name.IndexOf("DedicatedCustomServer", StringComparison.OrdinalIgnoreCase) >= 0)
                            TryApplyWebPanelPatches();
                    };
                    ModLogger.Info("CoopSpectatorDedicated loaded (game modes registered).");
                }
            }
            catch (Exception ex)
            {
                string msg = "[CoopSpectator] OnSubModuleLoad EXCEPTION: " + ex;
                System.Console.WriteLine(msg);
                try { File.AppendAllText(GetProofFilePath(errorFile), ex.ToString() + Environment.NewLine); } catch (Exception) { }
                throw;
            }
        }

        private static void LogDedicatedStartupInfo()
        {
            try
            {
                GameStartupInfo startupInfo = TaleWorlds.MountAndBlade.Module.CurrentModule?.StartupInfo;
                if (startupInfo == null)
                {
                    ModLogger.Info("CoopSpectatorDedicated [startup] StartupInfo unavailable.");
                    return;
                }

                ModLogger.Info(
                    "CoopSpectatorDedicated [startup] StartupInfo. " +
                    "StartupType=" + startupInfo.StartupType +
                    " DedicatedServerType=" + startupInfo.DedicatedServerType +
                    " PlayerHostedDedicatedServer=" + startupInfo.PlayerHostedDedicatedServer +
                    " ServerPort=" + startupInfo.ServerPort +
                    " ServerRegion=" + (startupInfo.ServerRegion ?? string.Empty) +
                    " Permission=" + startupInfo.Permission +
                    " CustomServerHostIP=" + (string.IsNullOrWhiteSpace(startupInfo.CustomServerHostIP) ? "(default)" : startupInfo.CustomServerHostIP) +
                    " CustomGameServerConfigFile=" + (startupInfo.CustomGameServerConfigFile ?? string.Empty) +
                    " AllowsOptionalModules=" + startupInfo.CustomGameServerAllowsOptionalModules + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated [startup] StartupInfo logging failed: " + ex.Message);
            }
        }

        private static void TryApplyGameModeOverridePatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                GameModeOverridePatches.Apply(_harmony);
                if (!ExperimentalFeatures.EnableTdmCloneExperiment)
                    ModLogger.Info("[GameModeReg] Stable baseline active: TeamDeathmatch override disabled, Battle override remains available.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("[HarmonyFallback] GameModeOverridePatches.Apply failed. patch=GetMultiplayerGameMode postfix target=Module.GetMultiplayerGameMode(string). skipped intentionally, fallback active. Exception: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void TryApplyMissionStateOpenNewPatches()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                MissionStateOpenNewPatches.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: MissionStateOpenNew patches apply failed: " + ex.Message);
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            TryRegisterCoopCampaignDerivedAgentStatModel(game, gameStarterObject, "dedicated");
            TryRegisterCoopCampaignDerivedStrikeMagnitudeModel(game, gameStarterObject, "dedicated");
            TryRegisterCoopCampaignDerivedAgentApplyDamageModel(game, gameStarterObject, "dedicated");
        }

        private static void TryApplyExactCampaignArmyBootstrapPatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                ExactCampaignArmyBootstrapPatch.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: ExactCampaignArmyBootstrap patch apply failed: " + ex.Message);
            }
        }

        private static void TryApplyExactCampaignPreSpawnLoadoutPatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                ExactCampaignPreSpawnLoadoutPatch.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: ExactCampaignPreSpawnLoadout patch apply failed: " + ex.Message);
            }
        }

        private static void TryApplyBattleShellSuppressionPatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                BattleShellSuppressionPatch.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: BattleShellSuppression patch apply failed: " + ex.Message);
            }
        }

        private static void TryApplyBattleMapSpawnHandoffPatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                BattleMapSpawnHandoffPatch.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: BattleMapSpawnHandoff patch apply failed: " + ex.Message);
            }
        }

        private static void TryApplyMultiplayerCharacterClassFallbackPatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                MultiplayerCharacterClassFallbackPatch.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: MultiplayerCharacterClassFallback patch apply failed: " + ex.Message);
            }
        }

        private static void TryConfigureDedicatedHarmonyRuntimeCompat()
        {
            try
            {
                Harmony.DEBUG = false;
                Harmony.SetSwitch("DMDDebug", false);
                Harmony.ClearSwitch("DMDDumpTo");

                (string SwitchValue, string TypeName)[] preferredGeneratorTypes =
                {
                    // Dedicated runtime on the helper host has been failing patch apply under DMDCecilGenerator
                    // with MissingMethodException on ILGenerator.MarkSequencePoint. Prefer emit-based generators first.
                    ("dynamicmethod", "MonoMod.Utils.DMDEmitDynamicMethodGenerator"),
                    ("methodbuilder", "MonoMod.Utils.DMDEmitMethodBuilderGenerator"),
                    ("cecil", "MonoMod.Utils.DMDCecilGenerator")
                };

                string selectedGeneratorSwitchValue = null;
                var availableGeneratorTypes = new System.Collections.Generic.List<string>();
                foreach ((string switchValue, string generatorTypeName) in preferredGeneratorTypes)
                {
                    Type candidateType = typeof(Harmony).Assembly.GetType(generatorTypeName, throwOnError: false);
                    if (candidateType != null)
                    {
                        availableGeneratorTypes.Add(generatorTypeName + "=>" + switchValue);
                        if (selectedGeneratorSwitchValue == null)
                        {
                            selectedGeneratorSwitchValue = switchValue;
                            _configuredHarmonyDmdGeneratorTypeName = generatorTypeName;
                        }
                    }
                }

                if (selectedGeneratorSwitchValue == null)
                {
                    _configuredHarmonyDmdGeneratorTypeName = "not-found";
                    Harmony.ClearSwitch("DMDType");
                    ModLogger.Info(
                        "CoopSpectatorDedicated: configured Harmony runtime compat. " +
                        "DMDDebug=false AvailableDMDTypes=[] SelectedDMDType=none.");
                    return;
                }

                // Harmony 2.4.2 reads the DMDType switch as a string token/alias, not as a System.Type.
                // Passing the Type object logs nicely but gets ignored by DynamicMethodDefinition.Generate(),
                // which silently falls back to its default generator selection.
                Harmony.SetSwitch("DMDType", selectedGeneratorSwitchValue);
                ModLogger.Info(
                    "CoopSpectatorDedicated: configured Harmony runtime compat. " +
                    "DMDDebug=false AvailableDMDTypes=[" + string.Join(", ", availableGeneratorTypes) + "] " +
                    "SelectedDMDType=" + _configuredHarmonyDmdGeneratorTypeName +
                    " SelectedDMDSwitch=" + selectedGeneratorSwitchValue + ".");
            }
            catch (Exception ex)
            {
                _configuredHarmonyDmdGeneratorTypeName = "config-failed";
                ModLogger.Info("CoopSpectatorDedicated: Harmony runtime compat configuration failed: " + ex.Message);
            }
        }

        private static void TryApplyDedicatedKnockoutOutcomePatches()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                DedicatedKnockoutOutcomePatches.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: dedicated knockout outcome patch apply failed: " + ex.Message);
            }
        }

        private static void TryApplyMultiplayerHeroClassOverridePatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                MultiplayerHeroClassOverridePatch.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: MultiplayerHeroClass override patch apply failed: " + ex.Message);
            }
        }

        private static void TryApplyServerChangeCultureCanonicalizationPatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                ServerChangeCultureCanonicalizationPatch.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: server ChangeCulture canonicalization patch apply failed: " + ex.Message);
            }
        }

        private static void TryApplyCampaignCombatProfileAgentStatsPatch()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");

                if (CampaignCombatProfileAgentStatsPatch.ApplyWeaponDamageOnly(_harmony))
                    ModLogger.Info("CoopSpectatorDedicated: applied CampaignCombatProfileAgentStatsPatch weapon damage postfix on dedicated; UpdateAgentStats remains manual.");
                else
                    ModLogger.Info("CoopSpectatorDedicated: failed to apply CampaignCombatProfileAgentStatsPatch weapon damage postfix on dedicated; using OnScoreHit fallback path.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: weapon damage combat-profile patch apply failed: " + ex.Message);
            }
        }

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

        private static void TryApplyWebPanelPatches()
        {
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                DedicatedWebPanelPatches.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("[HarmonyFallback] DedicatedWebPanelPatches.Apply failed. patch=DedicatedCustomGameServerStateActivated/OnSubModuleUnloaded. skipped intentionally, fallback active. Exception: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void RegisterCoopBattleGameMode()
        {
            try
            {
                ModLogger.Info("[GameModeReg] add CoopBattle id=" + MissionMultiplayerCoopBattleMode.GameModeId);
                TaleWorlds.MountAndBlade.Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerCoopBattleMode(MissionMultiplayerCoopBattleMode.GameModeId));
                var battleOverride = new MissionMultiplayerCoopBattleMode(CoopGameModeIds.OfficialBattle);
                GameModeOverridePatches.SetBattleOverride(battleOverride);
                ModLogger.Info("[GameModeReg] Battle override armed via Harmony. GetMultiplayerGameMode(Battle) will return CoopBattle runtime.");
                ModLogger.Info("[GameModeReg] add CoopTdm id=" + MissionMultiplayerCoopTdmMode.GameModeId);
                TaleWorlds.MountAndBlade.Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerCoopTdmMode(MissionMultiplayerCoopTdmMode.GameModeId));
                if (ExperimentalFeatures.EnableTdmCloneExperiment)
                {
                    ModLogger.Info("[GameModeReg] add TdmClone id=" + MissionMultiplayerTdmCloneMode.GameModeId);
                    TaleWorlds.MountAndBlade.Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerTdmCloneMode(MissionMultiplayerTdmCloneMode.GameModeId));
                    var teamDeathmatchOverride = new MissionMultiplayerTdmCloneMode(CoopGameModeIds.OfficialTeamDeathmatch);
                    GameModeOverridePatches.SetTeamDeathmatchOverride(teamDeathmatchOverride);
                    ModLogger.Info("[GameModeReg] skip AddMultiplayerGameMode(TeamDeathmatch) — use Harmony override only (avoids same key). GetMultiplayerGameMode(TeamDeathmatch) will return TdmClone 3+3.");
                    ModLogger.Info("[GameModeReg] Registered: CoopBattle, CoopTdm, TdmClone. TeamDeathmatch handled by Harmony postfix.");
                }
                else
                {
                    ModLogger.Info("[GameModeReg] Stable baseline active: TdmClone registration/override disabled. Listed flow stays on vanilla TeamDeathmatch.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("[GameModeReg] Failed to register game modes.", ex);
            }
        }
    }
}
