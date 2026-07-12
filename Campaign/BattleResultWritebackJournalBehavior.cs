using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace CoopSpectator.Campaign
{
    public sealed class BattleResultWritebackJournalBehavior : CampaignBehaviorBase
    {
        private const int MaxRememberedResultIds = 64;
        private static BattleResultWritebackJournalBehavior _instance;

        private List<string> _consumedResultIds = new List<string>();
        private readonly HashSet<string> _consumedResultIdSet = new HashSet<string>(StringComparer.Ordinal);

        public BattleResultWritebackJournalBehavior()
        {
            _instance = this;
        }

        public override void RegisterEvents()
        {
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("CoopSpectatorConsumedBattleResultIds", ref _consumedResultIds);
            if (_consumedResultIds == null)
                _consumedResultIds = new List<string>();

            _consumedResultIdSet.Clear();
            foreach (string resultId in _consumedResultIds)
            {
                if (!string.IsNullOrWhiteSpace(resultId))
                    _consumedResultIdSet.Add(resultId);
            }
        }

        public static bool IsConsumed(string resultId)
        {
            return !string.IsNullOrWhiteSpace(resultId) &&
                   _instance != null &&
                   _instance._consumedResultIdSet.Contains(resultId);
        }

        public static void MarkConsumed(string resultId)
        {
            if (string.IsNullOrWhiteSpace(resultId) || _instance == null || !_instance._consumedResultIdSet.Add(resultId))
                return;

            _instance._consumedResultIds.Add(resultId);
            while (_instance._consumedResultIds.Count > MaxRememberedResultIds)
            {
                string removed = _instance._consumedResultIds[0];
                _instance._consumedResultIds.RemoveAt(0);
                _instance._consumedResultIdSet.Remove(removed);
            }
        }
    }
}
