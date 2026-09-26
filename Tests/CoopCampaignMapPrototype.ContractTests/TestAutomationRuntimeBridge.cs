namespace CoopSpectator.Infrastructure.Automation
{
    internal static class CoopAutomationRuntimeBridge
    {
        public static string TestCoopFolderPath { get; set; }

        public static string ResolveCoopFolderPath()
        {
            return TestCoopFolderPath;
        }
    }
}
