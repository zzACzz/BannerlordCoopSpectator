using System;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactTransferStageMachine
    {
        public static bool CanTransition(
            ExactTransferStage current,
            ExactTransferStage target,
            bool requiresMountLink)
        {
            if (target == ExactTransferStage.Failed)
                return current != ExactTransferStage.CleanupComplete;

            switch (current)
            {
                case ExactTransferStage.None:
                    return target == ExactTransferStage.SnapshotResolved;

                case ExactTransferStage.SnapshotResolved:
                    return target == ExactTransferStage.ContractBuilt;

                case ExactTransferStage.ContractBuilt:
                    return target == ExactTransferStage.ContractValidated;

                case ExactTransferStage.ContractValidated:
                    return target == ExactTransferStage.PreSpawnPrepared;

                case ExactTransferStage.PreSpawnPrepared:
                    return target == ExactTransferStage.CreateAgentPayloadObserved;

                case ExactTransferStage.CreateAgentPayloadObserved:
                    return target == ExactTransferStage.RiderMaterialized;

                case ExactTransferStage.RiderMaterialized:
                    if (requiresMountLink)
                        return target == ExactTransferStage.MountMaterialized;
                    return target == ExactTransferStage.PeerBound;

                case ExactTransferStage.MountMaterialized:
                    return target == ExactTransferStage.MountLinkVerified;

                case ExactTransferStage.MountLinkVerified:
                    return target == ExactTransferStage.PeerBound;

                case ExactTransferStage.PeerBound:
                    return target == ExactTransferStage.EquipmentSynchronized;

                case ExactTransferStage.EquipmentSynchronized:
                    return target == ExactTransferStage.ExactReady;

                case ExactTransferStage.ExactReady:
                    return target == ExactTransferStage.CommanderReady ||
                           target == ExactTransferStage.DeathObserved;

                case ExactTransferStage.CommanderReady:
                    return target == ExactTransferStage.DeathObserved;

                case ExactTransferStage.DeathObserved:
                    return target == ExactTransferStage.CleanupComplete;

                case ExactTransferStage.CleanupComplete:
                case ExactTransferStage.Failed:
                    return false;

                default:
                    return false;
            }
        }

        public static bool TryAdvance(
            ExactTransferRuntimeState state,
            ExactTransferStage target,
            bool requiresMountLink,
            string failureContext)
        {
            if (state == null)
                return false;

            if (!CanTransition(state.Stage, target, requiresMountLink))
                return false;

            state.Stage = target;
            state.LastTransitionUtc = DateTime.UtcNow;

            switch (target)
            {
                case ExactTransferStage.RiderMaterialized:
                    state.RiderMaterialized = true;
                    break;
                case ExactTransferStage.MountMaterialized:
                    state.MountMaterialized = true;
                    break;
                case ExactTransferStage.MountLinkVerified:
                    state.MountLinkVerified = true;
                    break;
                case ExactTransferStage.PeerBound:
                    state.PeerBound = true;
                    break;
                case ExactTransferStage.EquipmentSynchronized:
                    state.EquipmentSynchronized = true;
                    break;
                case ExactTransferStage.ExactReady:
                    state.ExactVisualApplied = true;
                    break;
                case ExactTransferStage.CommanderReady:
                    state.CommanderControlEnabled = true;
                    break;
                case ExactTransferStage.CleanupComplete:
                    state.ExactVisualApplied = false;
                    state.CommanderControlEnabled = false;
                    break;
            }

            if (target != ExactTransferStage.Failed)
            {
                state.FailureReason = ExactTransferFailureReason.None;
                state.FailureContext = failureContext;
            }

            return true;
        }
    }
}
