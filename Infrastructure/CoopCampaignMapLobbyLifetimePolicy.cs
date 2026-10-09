namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapLobbyLifetimePolicy
    {
        // A passive campaign map has no match winner or spawn session. Its owner
        // ends it explicitly; stock waiting and ending transitions stay native.
        public static bool ShouldRunNativeTick(
            bool isServer, bool isReplicaMap, bool isPlaying)
        {
            return !(isServer && isReplicaMap && isPlaying);
        }
    }
}
