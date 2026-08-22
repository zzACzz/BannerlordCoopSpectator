using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;

namespace CoopSpectator.Campaign
{
    public sealed class BattleResultWritebackJournalBehavior : CampaignBehaviorBase
    {
        private readonly object _sync = new object();
        private string _campaignId;
        private List<string> _consumedResultIds = new List<string>();
        private readonly HashSet<string> _consumedResultIdSet = new HashSet<string>(StringComparer.Ordinal);

        public override void RegisterEvents()
        {
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore == null)
                return;

            lock (_sync)
            {
                if (dataStore.IsSaving)
                    EnsureCampaignIdLocked();

                dataStore.SyncData("CoopSpectatorBattleResultCampaignId", ref _campaignId);
                dataStore.SyncData("CoopSpectatorConsumedBattleResultIds", ref _consumedResultIds);

                EnsureCampaignIdLocked();
                RebuildJournalLocked();
            }
        }

        public static bool TryGetActiveCampaignId(out string campaignId)
        {
            campaignId = null;
            BattleResultWritebackJournalBehavior behavior = ResolveActiveBehavior();
            if (behavior == null)
                return false;

            lock (behavior._sync)
            {
                behavior.EnsureCampaignIdLocked();
                campaignId = behavior._campaignId;
                return CoopBattleResultCampaignGuardContract.IsValidCampaignId(campaignId);
            }
        }

        public static bool IsConsumed(string resultId)
        {
            if (string.IsNullOrWhiteSpace(resultId))
                return false;

            BattleResultWritebackJournalBehavior behavior = ResolveActiveBehavior();
            if (behavior == null)
                return false;

            lock (behavior._sync)
                return behavior._consumedResultIdSet.Contains(resultId);
        }

        public static void MarkConsumed(string resultId)
        {
            TryMarkConsumedAfterSuccess(resultId);
        }

        public static bool TryMarkConsumedAfterSuccess(string resultId)
        {
            if (string.IsNullOrWhiteSpace(resultId))
                return false;

            BattleResultWritebackJournalBehavior behavior = ResolveActiveBehavior();
            if (behavior == null)
                return false;

            lock (behavior._sync)
            {
                if (behavior._consumedResultIdSet.Contains(resultId))
                    return true;

                behavior._consumedResultIds.Add(resultId);
                behavior.RebuildJournalLocked();
                return behavior._consumedResultIdSet.Contains(resultId);
            }
        }

        private static BattleResultWritebackJournalBehavior ResolveActiveBehavior()
        {
            TaleWorlds.CampaignSystem.Campaign campaign =
                TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null)
                return null;

            try
            {
                return campaign.GetCampaignBehavior<BattleResultWritebackJournalBehavior>();
            }
            catch
            {
                return null;
            }
        }

        private void EnsureCampaignIdLocked()
        {
            if (!CoopBattleResultCampaignGuardContract.IsValidCampaignId(_campaignId))
                _campaignId = Guid.NewGuid().ToString("N");
        }

        private void RebuildJournalLocked()
        {
            _consumedResultIds = CoopBattleResultCampaignGuardContract.NormalizeJournal(
                _consumedResultIds);

            _consumedResultIdSet.Clear();
            foreach (string resultId in _consumedResultIds)
                _consumedResultIdSet.Add(resultId);
        }
    }
}
