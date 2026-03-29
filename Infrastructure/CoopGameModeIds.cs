namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Єдині ідентифікатори мультиплеєрних режимів для узгодження між:
    /// (a) реєстрацією на дедик-сервері (MP module),
    /// (b) payload start_mission на хостові,
    /// (c) startup config/rotation дедика.
    /// При додаванні клону (наприклад TdmClone) змінюємо ID одночасно в усіх трьох місцях.
    /// </summary>
    public static class CoopGameModeIds
    {
        /// <summary>Чистий coop battle runtime поверх MP bootstrap.</summary>
        public const string CoopBattle = "CoopBattle";

        /// <summary>Поточний робочий режим TDM (клон ванільного TDM).</summary>
        public const string CoopTdm = "CoopTdm";

        /// <summary>Точна копія 1:1 TDM з іншою назвою — для тесту без зміни логіки (виключення помилок через розсинхрон ID).</summary>
        public const string TdmClone = "TdmClone";

        /// <summary>Офіційне ім'я TDM у конфігу дедика (Captain, TeamDeathmatch, Skirmish, …). Під це ім'я реєструємо нашу логіку (3+3 спавн), щоб GameType TeamDeathmatch у конфігу запускав нашу місію.</summary>
        public const string OfficialTeamDeathmatch = "TeamDeathmatch";

        /// <summary>Офіційне ім'я Battle у конфігу дедика. Для battle-map runtime треба підміняти саме його, а не жити на окремому CoopBattle ID.</summary>
        public const string OfficialBattle = "Battle";
    }

    /// <summary>
    /// Тимчасовий мінімальний режим dedicated: повертати мінімальний список behaviors без spawn/team setup,
    /// щоб перевірити, чи dedicated взагалі переживає post-create_mission стадію. Вимкнути після діагностики.
    /// </summary>
    public static class MinimalDedicatedMissionMode
    {
        /// <summary>true = на dedicated повертати мінімальний список behaviors і не виконувати InitializeTeamsAndMinimalSpawn.</summary>
        public static bool UseMinimalDedicatedMode => true;
    }
}
