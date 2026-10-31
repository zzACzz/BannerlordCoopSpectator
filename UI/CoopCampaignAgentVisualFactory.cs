using System;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

namespace CoopSpectator.UI
{
    internal sealed class CoopCampaignVisualCleanupException : Exception
    {
        internal CoopCampaignVisualCleanupException(Exception creation, Exception cleanup)
            : base("A partially constructed campaign visual could not be retired.",
                new AggregateException(creation, cleanup)) { }
    }

    internal static class CoopCampaignAgentVisualFactory
    {
        internal static AgentVisuals Create(AgentVisualsData data, string name,
            bool isRandomProgress, bool needBatchedVersionForWeaponMeshes, bool forceUseFaceCache)
        {
            return ConstructOwned(
                () => CoopCampaignMapVisualLifetimeTrace.Create(data, name, isRandomProgress,
                    needBatchedVersionForWeaponMeshes, forceUseFaceCache),
                () => data?.AgentVisuals,
                partial =>
                {
                    // The installed native constructor attaches an entity before
                    // returning, but managed Refresh may throw before returning
                    // the AgentVisuals owner. Retire that exact allocation using
                    // the same public Reset operation as a completed visual.
                    CoopCampaignMapVisualLifetimeTrace.PartialCleanup("partial-reset-begin", partial);
                    partial.Reset();
                    data.AgentVisuals = null;
                    CoopCampaignMapVisualLifetimeTrace.PartialCleanup("partial-reset-return", partial);
                });
        }

        // Also exercised with detached managed owners: no game/native constructor
        // is needed to verify exception ownership and cleanup-failure propagation.
        internal static TResult ConstructOwned<TPartial, TResult>(Func<TResult> create,
            Func<TPartial> getPartial, Action<TPartial> retire) where TPartial : class
        {
            try { return create(); }
            catch (Exception creationFailure)
            {
                TPartial partial = getPartial();
                if (!ReferenceEquals(partial, null))
                {
                    try { retire(partial); }
                    catch (Exception cleanupFailure)
                    {
                        throw new CoopCampaignVisualCleanupException(creationFailure, cleanupFailure);
                    }
                }
                throw;
            }
        }
    }
}
