using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public static class BattleCatalogParityState
    {
        private static readonly object Sync = new object();

        private static int _clientPreparedTransmissionId;
        private static bool _clientPreparedSuccessfully;
        private static string _clientPreparedCatalogHash = string.Empty;
        private static string _clientPreparedSummary = "not-prepared";

        public static bool TryPrepareCurrentBattleCatalog(
            int transmissionId,
            string source,
            out string catalogHash,
            out string summary)
        {
            catalogHash = string.Empty;
            summary = "not-prepared";

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
            if (runtimeState == null || snapshot == null || objectManager == null)
            {
                summary =
                    "BattleId=" + (snapshot?.BattleId ?? "null") +
                    " RuntimeState=" + (runtimeState != null) +
                    " ObjectManager=" + (objectManager != null);
                ObserveClientPreparation(transmissionId, preparedSuccessfully: false, string.Empty, summary);
                return false;
            }

            ExactCampaignObjectCatalogBootstrap.EnsureLoaded("battle-catalog-parity:" + (source ?? "unknown"));
            ExactCampaignRuntimeItemRegistry.EnsureLoadedFromState(runtimeState, "battle-catalog-parity:" + (source ?? "unknown"));

            List<string> requiredItemIds = ExactCampaignRuntimeItemRegistry
                .EnumerateBattleEquipmentItemIds(runtimeState)
                .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(itemId => itemId, StringComparer.Ordinal)
                .ToList();

            List<string> requiredShellCharacterIds = EnumerateRequiredShellCharacterIds(runtimeState)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(characterId => characterId, StringComparer.Ordinal)
                .ToList();

            Dictionary<string, int> itemIndexesById = BuildObjectIndexById(objectManager.GetObjectTypeList<ItemObject>());
            Dictionary<string, int> characterIndexesById = BuildObjectIndexById(objectManager.GetObjectTypeList<BasicCharacterObject>());

            var descriptors = new List<string>(requiredItemIds.Count + requiredShellCharacterIds.Count);
            var missingItems = new List<string>();
            var missingCharacters = new List<string>();

            foreach (string itemId in requiredItemIds)
            {
                if (!itemIndexesById.TryGetValue(itemId, out int itemIndex))
                {
                    missingItems.Add(itemId);
                    descriptors.Add("I:" + itemId + "@missing");
                    continue;
                }

                descriptors.Add("I:" + itemId + "@" + itemIndex);
            }

            foreach (string characterId in requiredShellCharacterIds)
            {
                if (!characterIndexesById.TryGetValue(characterId, out int characterIndex))
                {
                    missingCharacters.Add(characterId);
                    descriptors.Add("C:" + characterId + "@missing");
                    continue;
                }

                descriptors.Add("C:" + characterId + "@" + characterIndex);
            }

            catalogHash = ComputeSha256Hex(descriptors);
            int itemCatalogCount = TryGetObjectTypeCount<ItemObject>(objectManager);
            int characterCatalogCount = TryGetObjectTypeCount<BasicCharacterObject>(objectManager);
            summary =
                "BattleId=" + (snapshot.BattleId ?? "null") +
                " TransmissionId=" + transmissionId +
                " RequiredItems=" + requiredItemIds.Count +
                " RequiredShells=" + requiredShellCharacterIds.Count +
                " MissingItems=" + missingItems.Count +
                " MissingShells=" + missingCharacters.Count +
                " ItemCatalogCount=" + itemCatalogCount +
                " CharacterCatalogCount=" + characterCatalogCount +
                " Hash=" + catalogHash;

            if (missingItems.Count > 0 || missingCharacters.Count > 0)
            {
                summary +=
                    " MissingItemIds=[" + string.Join(", ", missingItems.Take(16)) + "]" +
                    " MissingShellIds=[" + string.Join(", ", missingCharacters.Take(16)) + "]";
            }

            bool preparedSuccessfully = missingItems.Count <= 0 && missingCharacters.Count <= 0;
            ObserveClientPreparation(transmissionId, preparedSuccessfully, catalogHash, summary);
            ModLogger.Info(
                "BattleCatalogParityState: prepared local battle catalog parity state. " +
                "PreparedSuccessfully=" + preparedSuccessfully +
                " Source=" + (source ?? "unknown") + " " +
                summary);
            return preparedSuccessfully;
        }

        public static bool TryGetClientPreparedCatalogState(
            int transmissionId,
            out string catalogHash,
            out string summary)
        {
            lock (Sync)
            {
                catalogHash = _clientPreparedCatalogHash ?? string.Empty;
                summary = _clientPreparedSummary ?? "not-prepared";
                return transmissionId > 0 &&
                       _clientPreparedSuccessfully &&
                       _clientPreparedTransmissionId == transmissionId &&
                       !string.IsNullOrWhiteSpace(_clientPreparedCatalogHash);
            }
        }

        public static void ResetClient(string reason)
        {
            lock (Sync)
            {
                _clientPreparedTransmissionId = 0;
                _clientPreparedSuccessfully = false;
                _clientPreparedCatalogHash = string.Empty;
                _clientPreparedSummary = "reset:" + (reason ?? "unknown");
            }
        }

        private static void ObserveClientPreparation(
            int transmissionId,
            bool preparedSuccessfully,
            string catalogHash,
            string summary)
        {
            if (!GameNetwork.IsClient)
                return;

            lock (Sync)
            {
                _clientPreparedTransmissionId = transmissionId;
                _clientPreparedSuccessfully = preparedSuccessfully;
                _clientPreparedCatalogHash = catalogHash ?? string.Empty;
                _clientPreparedSummary = summary ?? "prepared";
            }
        }

        private static IEnumerable<string> EnumerateRequiredShellCharacterIds(BattleRuntimeState runtimeState)
        {
            if (runtimeState?.EntriesById == null)
                yield break;

            foreach (RosterEntryState entry in runtimeState.EntriesById.Values
                .Where(candidate => candidate != null)
                .OrderBy(candidate => candidate.EntryId, StringComparer.Ordinal))
            {
                string shellCharacterId = !string.IsNullOrWhiteSpace(entry.SpawnTemplateId)
                    ? entry.SpawnTemplateId
                    : entry.CharacterId;
                if (!string.IsNullOrWhiteSpace(shellCharacterId))
                    yield return shellCharacterId;
            }
        }

        private static Dictionary<string, int> BuildObjectIndexById<TObject>(IList<TObject> objects)
            where TObject : MBObjectBase
        {
            var indexesById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (objects == null)
                return indexesById;

            for (int index = 0; index < objects.Count; index++)
            {
                TObject obj = objects[index];
                string stringId = obj?.StringId;
                if (string.IsNullOrWhiteSpace(stringId) || indexesById.ContainsKey(stringId))
                    continue;

                indexesById[stringId] = index;
            }

            return indexesById;
        }

        private static int TryGetObjectTypeCount<TObject>(MBObjectManager objectManager)
            where TObject : MBObjectBase
        {
            if (objectManager == null)
                return -1;

            try
            {
                return objectManager.GetObjectTypeList<TObject>()?.Count ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private static string ComputeSha256Hex(IEnumerable<string> parts)
        {
            string payload = string.Join("|", parts ?? Enumerable.Empty<string>());
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
