namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Canonical multiplayer mode ids still used by the active runtime.
    /// TeamDeathmatch remains the official listed-shell mode.
    /// Battle is the official id overridden by CoopBattle.
    /// </summary>
    public static class CoopGameModeIds
    {
        public const string CoopBattle = "CoopBattle";
        public const string OfficialTeamDeathmatch = "TeamDeathmatch";
        public const string OfficialBattle = "Battle";
    }
}
