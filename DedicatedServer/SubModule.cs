// Точка входу тільки для Dedicated Server. Реєстрація CoopTdm/CoopBattle + Harmony-патчі WebPanel (неблокуючий старт).
using System;
using HarmonyLib;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.Patches;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator
{
    public sealed class SubModule : MBSubModuleBase
    {
        private static Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
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
                Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerCoopBattleMode(MissionMultiplayerCoopBattleMode.GameModeId));
                Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerCoopTdmMode(MissionMultiplayerCoopTdmMode.GameModeId));
                Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerTdmCloneMode(MissionMultiplayerTdmCloneMode.GameModeId));
                ModLogger.Info("CoopBattle registered.");
                ModLogger.Info("CoopTdm registered.");
                ModLogger.Info("TdmClone registered. [ID check] GameTypeId=" + CoopGameModeIds.TdmClone);
                ModLogger.Info("Registered game modes: CoopBattle, CoopTdm, TdmClone.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Failed to register game modes.", ex);
            }
        }
    }
}
