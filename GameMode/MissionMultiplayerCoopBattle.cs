using CoopSpectator.Infrastructure; // Підключаємо ModLogger
using TaleWorlds.Core; // Підключаємо BattleSideEnum для створення команд
using TaleWorlds.MountAndBlade; // Підключаємо Mission, GameNetwork, Team, MissionLogic
using TaleWorlds.MountAndBlade.Multiplayer; // Підключаємо MissionMultiplayerGameModeBase та типи для GetMissionType

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Серверна логіка режиму CoopBattle: на старті місії створює команди (Attacker/Defender) і запускає мінімальний спавн (манекени або заглушка).
    /// Наслідує MissionMultiplayerGameModeBase за офіційним шаблоном.
    /// </summary>
    public sealed class MissionMultiplayerCoopBattle : MissionMultiplayerGameModeBase
    {
        private bool _hasInitialized;

        /// <summary>Повертає тип місії для API/scoreboard. CoopBattle реалізований як окремий game mode (GameType CoopBattle), але enum не має Custom — повертаємо TDM для сумісності.</summary>
        public override MultiplayerGameType GetMissionType()
        {
            return MultiplayerGameType.TeamDeathmatch; // Workaround: гра не має MultiplayerGameType.Custom; логіка режиму — CoopBattle (дві команди)
        }

        /// <summary>Режим використовує дві протилежні команди (Attacker vs Defender).</summary>
        public override bool IsGameModeUsingOpposingTeams => true;

        /// <summary>Не ховаємо візуали агентів (показуємо їх як у звичайному бою).</summary>
        public override bool IsGameModeHidingAllAgentVisuals => false;

        /// <summary>Викликається після ініціалізації behavior. Тут можна підписатися на події місії.</summary>
        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _hasInitialized = false;
        }

        /// <summary>Викликається кожен кадр. На першому тіку виконуємо одноразову ініціалізацію: команди + мінімальний спавн.</summary>
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (_hasInitialized || Mission?.Teams == null)
                return;

            _hasInitialized = true;

            // Тільки сервер створює команди та спавнить агентів.
            if (!GameNetwork.IsServer)
                return;

            try
            {
                InitializeTeamsAndMinimalSpawn();
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("CoopBattle server: InitializeTeamsAndMinimalSpawn failed.", ex);
            }
        }

        /// <summary>Створює команди Attacker/Defender і додає мінімальний спавн (поки заглушка — без реальних агентів, лише лог).</summary>
        private void InitializeTeamsAndMinimalSpawn()
        {
            Mission mission = Mission;
            if (mission == null) return;

            // Якщо команди вже є (створені сценою або іншим компонентом), не дублюємо.
            if (mission.Teams.Attacker == null)
                mission.Teams.Add(BattleSideEnum.Attacker, 0xFFCC2222u, 0xFF661111u, null, false, false, false);
            if (mission.Teams.Defender == null)
                mission.Teams.Add(BattleSideEnum.Defender, 0xFF2222CCu, 0xFF111166u, null, false, false, false);

            ModLogger.Info("CoopBattle mission started (teams initialized; no agents yet)."); // У dedicated логах — підтвердження що місія йде в нашому режимі
        }
    }
}
