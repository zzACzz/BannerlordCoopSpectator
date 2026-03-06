// Точка входу тільки для Dedicated Server. Реєстрація CoopTdm/CoopBattle + Harmony-патчі WebPanel (неблокуючий старт).
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.Patches;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator
{
    public sealed class SubModule : MBSubModuleBase
    {
        /// <summary>ТЗ A: true = тільки proof-of-load, без Harmony і без реєстрації game mode (чистий module-load для перевірки завантаження і стабільності).</summary>
        private const bool CleanModuleLoadOnly = true;

        private static Harmony _harmony;

        private static string GetProofFilePath(string name)
        {
            string dir = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
            return Path.Combine(dir, name);
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

                try { ModLogger.Info("CoopSpectatorDedicated OnSubModuleLoad (proof-of-load). PID=" + pid + " BaseDir=" + baseDir); } catch (Exception) { }

                base.OnSubModuleLoad();

                if (CleanModuleLoadOnly)
                {
                    try { ModLogger.Info("CoopSpectatorDedicated minimal mode active (no Harmony, no game mode registration)."); } catch (Exception) { }
                    System.Console.WriteLine("[CoopSpectator] CoopSpectatorDedicated minimal mode active.");
                }
                else
                {
                    TryApplyGameModeOverridePatch();
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
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.coopspectator.dedicated");
                GameModeOverridePatches.Apply(_harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSpectatorDedicated: GameModeOverride patch apply failed: " + ex.Message);
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
                ModLogger.Info("CoopSpectatorDedicated: WebPanel patches apply failed: " + ex.Message);
            }
        }

        private static void RegisterCoopBattleGameMode()
        {
            try
            {
                TaleWorlds.MountAndBlade.Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerCoopBattleMode(MissionMultiplayerCoopBattleMode.GameModeId));
                TaleWorlds.MountAndBlade.Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerCoopTdmMode(MissionMultiplayerCoopTdmMode.GameModeId));
                TaleWorlds.MountAndBlade.Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerTdmCloneMode(MissionMultiplayerTdmCloneMode.GameModeId));
                // Під офіційне ім'я TeamDeathmatch: реєструємо нашу місію (3+3 спавн) і зберігаємо її для Harmony-патча GetMultiplayerGameMode (ванільний TDM реєструється раніше).
                var teamDeathmatchOverride = new MissionMultiplayerTdmCloneMode(CoopGameModeIds.OfficialTeamDeathmatch);
                GameModeOverridePatches.SetTeamDeathmatchOverride(teamDeathmatchOverride);
                TaleWorlds.MountAndBlade.Module.CurrentModule.AddMultiplayerGameMode(teamDeathmatchOverride);
                ModLogger.Info("CoopBattle registered.");
                ModLogger.Info("CoopTdm registered.");
                ModLogger.Info("TdmClone registered. [ID check] GameTypeId=" + CoopGameModeIds.TdmClone);
                ModLogger.Info("TeamDeathmatch overridden with TdmClone logic (3+3 spawn). Registered: CoopBattle, CoopTdm, TdmClone, TeamDeathmatch.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Failed to register game modes.", ex);
            }
        }
    }
}
