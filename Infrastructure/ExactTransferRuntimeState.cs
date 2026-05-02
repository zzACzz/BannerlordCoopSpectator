using System;

namespace CoopSpectator.Infrastructure
{
    public enum ExactTransferStage
    {
        None = 0,
        SnapshotResolved = 10,
        ContractBuilt = 20,
        ContractValidated = 30,
        PreSpawnPrepared = 40,
        CreateAgentPayloadObserved = 50,
        RiderMaterialized = 60,
        MountMaterialized = 70,
        MountLinkVerified = 80,
        PeerBound = 90,
        EquipmentSynchronized = 100,
        ExactReady = 110,
        CommanderReady = 120,
        DeathObserved = 130,
        CleanupComplete = 140,
        Failed = 900
    }

    public enum ExactTransferFailureReason
    {
        None = 0,
        MissingContractField,
        InvalidNativeClassResolution,
        CreateAgentHandlerException,
        RiderNotMaterialized,
        MountNotMaterialized,
        MountLinkMissing,
        SetAgentPeerMissing,
        EquipmentSyncMissing,
        InvalidAgentIndexReuse,
        DeathCleanupIncomplete,
        UnsupportedMountedRangedLayout,
        UnsupportedBodyMaterializationPolicy
    }

    public sealed class ExactTransferRuntimeState
    {
        public string EntryId { get; set; }
        public int? RiderAgentIndex { get; set; }
        public int? MountAgentIndex { get; set; }
        public ExactTransferStage Stage { get; set; }
        public ExactTransferFailureReason FailureReason { get; set; }
        public string FailureContext { get; set; }
        public bool RiderMaterialized { get; set; }
        public bool MountMaterialized { get; set; }
        public bool MountLinkVerified { get; set; }
        public bool PeerBound { get; set; }
        public bool EquipmentSynchronized { get; set; }
        public bool ExactVisualApplied { get; set; }
        public bool CommanderControlEnabled { get; set; }
        public bool IsMountedContract { get; set; }
        public DateTime LastTransitionUtc { get; set; }

        public ExactTransferRuntimeState()
        {
            Stage = ExactTransferStage.None;
            FailureReason = ExactTransferFailureReason.None;
            LastTransitionUtc = DateTime.MinValue;
        }

        public void MarkFailure(ExactTransferFailureReason failureReason, string failureContext)
        {
            FailureReason = failureReason;
            FailureContext = failureContext;
            Stage = ExactTransferStage.Failed;
            LastTransitionUtc = DateTime.UtcNow;
        }
    }
}
