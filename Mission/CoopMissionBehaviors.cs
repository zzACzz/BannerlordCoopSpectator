// Файл: Mission/CoopMissionBehaviors.cs
// Призначення: логіка місії для кооп-спектатора — клієнтський зворотний зв'язок (етап 3.5) і заглушка серверного спавну (етап 3.4).
// Клієнтський behavior додається, коли ми в MP-місії як клієнт; серверний — коли наш модуль завантажено на сервері (наприклад, на дедику в майбутньому).

using System; // Exception
using System.Collections.Generic; // List<string>
using TaleWorlds.Core; // BasicCharacterObject, MBObjectManager (для спавну)
using TaleWorlds.Library; // Vec2, Vec3
using TaleWorlds.MountAndBlade; // Mission, MissionLogic, GameNetwork, Agent, Team, MissionMode
using CoopSpectator.Campaign; // BattleRosterFileHelper (варіант A: roster з кампанії)
using CoopSpectator.Infrastructure; // ModLogger, UiFeedback

namespace CoopSpectator.MissionBehaviors
{
    /// <summary>
    /// Клієнтська логіка в MP-місії: після старту показує статус "у битві", при завершенні місії — лог (етап 3.5).
    /// Не спавнить агентів — тільки зворотний зв'язок і детекція завершення битви.
    /// </summary>
    public sealed class CoopMissionClientLogic : MissionLogic
    {
        public override void AfterStart()
        {
            base.AfterStart();
            if (TaleWorlds.MountAndBlade.Mission.Current == null) return;
            // Коротке повідомлення: клієнт у кооп-битві (ванільний флоу місії вже завантажив нас сюди).
            ModLogger.Info("CoopMissionClientLogic: MP battle started (client).");
            UiFeedback.ShowMessageDeferred("Coop: in battle. (Leave battle on host to return to lobby.)");

            // Опційно: перевірити, чи є у нас керований агент (Agent.Main). Якщо ні — ванільний спавн ще не призначив юніта.
            try
            {
                if (Agent.Main != null)
                    ModLogger.Info("CoopMissionClientLogic: Agent.Main is set — you have a controlled character.");
                else
                    ModLogger.Info("CoopMissionClientLogic: Agent.Main is null — no controlled agent yet (vanilla spawn may assign later).");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionClientLogic: Agent.Main check failed: " + ex.Message);
            }
        }

        // Завершення місії: ванільний флоу повертає клієнта на Awaiting Server. Логуємо при готовності результату (етап 3.5).
        public override void OnMissionResultReady(MissionResult missionResult)
        {
            base.OnMissionResultReady(missionResult);
            ModLogger.Info("CoopMissionClientLogic: mission result ready — returning to lobby.");
        }
    }

    /// <summary>
    /// Серверна логіка: заглушка для майбутнього спавну гравців (етап 3.4). Варіант A: читає battle_roster.json для обмеження вибору юнітів.
    /// Повноцінний спавн peer'ів і підстановка списку класів потребують завантаження модуля на дедик (DedicatedServerType).
    /// </summary>
    public sealed class CoopMissionSpawnLogic : MissionLogic
    {
        private bool _hasLogged;

        /// <summary>Після читання файлу — список troop ID з кампанії для обмеження вибору юнітів клієнтами (варіант A).</summary>
        public static List<string> CampaignRosterTroopIds { get; private set; } = new List<string>();

        public override void AfterStart()
        {
            base.AfterStart();
            if (TaleWorlds.MountAndBlade.Mission.Current == null) return;
            if (_hasLogged) return;
            _hasLogged = true;

            // Варіант A: читаємо файл roster, записаний хостом перед start_mission (Documents\...\CoopSpectator\battle_roster.json).
            List<string> roster = BattleRosterFileHelper.ReadRoster();
            CampaignRosterTroopIds = roster ?? new List<string>();
            ModLogger.Info("CoopMissionSpawnLogic: server mission started. Campaign roster: " + CampaignRosterTroopIds.Count + " troop IDs (use for limiting client unit selection).");
            // TODO: підставити CampaignRosterTroopIds у список доступних класів місії (потрібно API ванільного режиму).
        }
    }
}
