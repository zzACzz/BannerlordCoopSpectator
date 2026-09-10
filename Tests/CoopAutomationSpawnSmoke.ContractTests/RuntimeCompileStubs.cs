// Native-free stand-ins; these tests never execute a Bannerlord mission.
namespace TaleWorlds.MountAndBlade
{
    public static class GameNetwork
    {
        public static bool IsClient { get; set; }
        public static bool IsServer { get; set; }
        public static bool IsDedicatedServer { get; set; }
        public static bool IsSessionActive { get; set; }
        public static System.Collections.Generic.List<NetworkCommunicator> NetworkPeers { get; set; } =
            new System.Collections.Generic.List<NetworkCommunicator>();
        public static bool NativeHasParticipants { get; set; }
        public static bool DoesDedicatedServerHaveAnyNetworkPeersOrBots() => NativeHasParticipants;
    }
    public sealed class NetworkCommunicator
    {
        public bool IsServerPeer { get; set; }
        public bool IsConnectionActive { get; set; }
    }
    public enum MissionMode { StartUp, Battle, Deployment }
    public sealed class Mission
    {
        public enum State { NewlyCreated, Initializing, Continuing, Over }
        public static Mission Current { get; set; }
        public string SceneName { get; set; }
        public State CurrentState { get; set; }
        public MissionMode Mode { get; set; }
        public bool MissionEnded { get; set; }
    }
    public sealed class MissionState
    {
        public Mission CurrentMission { get; set; }
        public float LastDelta { get; private set; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void TickMission(float realDt)
        {
            float delta = realDt;
            if (!GameNetwork.DoesDedicatedServerHaveAnyNetworkPeersOrBots()) delta = 0f;
            LastDelta = delta;
        }
        public void TickForContract(float realDt) => TickMission(realDt);
    }
}
namespace CoopSpectator.Infrastructure
{
    internal static class ModLogger
    {
        public static void Info(string message) { }
    }
    internal static class BattleSnapshotRuntimeState
    {
        public static Network.Messages.BattleSnapshotMessage Snapshot { get; set; }
        public static Network.Messages.BattleSnapshotMessage GetCurrent() => Snapshot;
    }
    internal enum CoopBattlePhase { None, Loading, SideSelection, UnitSelection, Deployment, PreBattleHold, BattleActive, BattleEnded }
    internal static class CoopBattlePhaseRuntimeState
    {
        public static CoopBattlePhase Phase { get; set; }
        public static CoopBattlePhase GetPhase() => Phase;
    }
}
