// Точка входу тільки для Dedicated Server. Реєстрація CoopTdm/CoopBattle + Harmony-патчі WebPanel (неблокуючий старт).
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Patches;
using TaleWorlds.MountAndBlade;

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
                    TryApplyFixedTestCulturesForNextMission();
                    return;
                }

                _hasAppliedFixedTestCultures = false;

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

                if (CleanModuleLoadOnly)
                {
                    try { ModLogger.Info("CoopSpectatorDedicated minimal mode active (no Harmony, no game mode registration)."); } catch (Exception) { }
                    System.Console.WriteLine("[CoopSpectator] CoopSpectatorDedicated minimal mode active.");
                }
                else
                {
                    TryApplyGameModeOverridePatch();
                    TryApplyMissionStateOpenNewPatches();
                    TryApplyMultiplayerHeroClassOverridePatch();
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

        private static void TryApplyGameModeOverridePatch()
        {
            if (!ExperimentalFeatures.EnableTdmCloneExperiment)
            {
                ModLogger.Info("[GameModeReg] Stable baseline active: skip TeamDeathmatch override patch.");
                return;
            }

            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                GameModeOverridePatches.Apply(_harmony);
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
            ModLogger.Info("CoopSpectatorDedicated: skipping CampaignCombatProfileAgentStatsPatch on dedicated; using manual combat-profile refresh path.");
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
