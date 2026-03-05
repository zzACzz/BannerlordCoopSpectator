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
        /// <summary>Поточний робочий режим TDM (клон ванільного TDM).</summary>
        public const string CoopTdm = "CoopTdm";

        /// <summary>Точна копія 1:1 TDM з іншою назвою — для тесту без зміни логіки (виключення помилок через розсинхрон ID).</summary>
        public const string TdmClone = "TdmClone";
    }
}
