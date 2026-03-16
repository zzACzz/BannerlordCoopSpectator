using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Explicit policy split between legacy TDM bootstrap surfaces and the
    /// authoritative coop entry path.
    /// </summary>
    public static class CoopBattleEntryPolicy
    {
        public sealed class ClientSnapshot
        {
            public BattleSideEnum BridgeSide { get; set; }
            public string BridgeSideRaw { get; set; }
            public string BridgeTroopOrEntryId { get; set; }
            public bool PlayerHasActiveAgent { get; set; }
            public bool IsSpectator { get; set; }

            public bool HasBridgeSide => BridgeSide != BattleSideEnum.None;
            public bool HasBridgeTroop => !string.IsNullOrWhiteSpace(BridgeTroopOrEntryId);

            // Legacy TDM UI is allowed only until the coop path has enough intent to take over.
            public bool UseAuthoritativeSidePath => HasBridgeSide || PlayerHasActiveAgent;
            public bool UseAuthoritativeTroopPath => HasBridgeTroop || PlayerHasActiveAgent;
            public bool UseAuthoritativeEntryPath => UseAuthoritativeSidePath || UseAuthoritativeTroopPath;

            public bool AllowLegacyVanillaTeamSelectionInteraction => !UseAuthoritativeSidePath;
            public bool AllowLegacyVanillaClassSelectionInteraction => !UseAuthoritativeTroopPath;

            public string Describe()
            {
                return
                    "BridgeSide=" + (HasBridgeSide ? BridgeSide.ToString() : "None") +
                    " RawSide=" + (BridgeSideRaw ?? "null") +
                    " BridgeTroop=" + (BridgeTroopOrEntryId ?? "null") +
                    " PlayerHasAgent=" + PlayerHasActiveAgent +
                    " IsSpectator=" + IsSpectator +
                    " UseAuthoritativeEntryPath=" + UseAuthoritativeEntryPath;
            }
        }

        public static ClientSnapshot BuildClientSnapshot(
            Mission mission,
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge)
        {
            BattleSideEnum parsedSide = BattleSideEnum.None;
            if (selectionBridge != null)
                TryParseBridgeSide(selectionBridge.Side, out parsedSide);

            bool playerHasAgent = Agent.Main != null && Agent.Main.IsActive();
            bool isSpectator = !playerHasAgent;

            return new ClientSnapshot
            {
                BridgeSide = parsedSide,
                BridgeSideRaw = selectionBridge?.Side,
                BridgeTroopOrEntryId = selectionBridge?.TroopOrEntryId,
                PlayerHasActiveAgent = playerHasAgent,
                IsSpectator = isSpectator
            };
        }

        private static bool TryParseBridgeSide(string sideRaw, out BattleSideEnum side)
        {
            side = BattleSideEnum.None;
            string normalized = (sideRaw ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == "attacker")
            {
                side = BattleSideEnum.Attacker;
                return true;
            }

            if (normalized == "defender")
            {
                side = BattleSideEnum.Defender;
                return true;
            }

            return false;
        }
    }
}
