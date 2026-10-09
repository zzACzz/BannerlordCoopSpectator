using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Lossless bounded order of the host's existing settlement marker lists.
    /// Each three-bit digit is a type index plus one, least significant first.
    /// Zero terminates; holes, duplicates, unknown types and flag mismatches reject.
    /// No sorting or reconstruction from campaign quest state is permitted here.
    /// </summary>
    public static class CoopCampaignMapSettlementMarkerOrder
    {
        public const int MaxEventOrder = (1 << 21) - 1;
        public const int MaxQuestOrder = (1 << 15) - 1;

        public static bool TryEncodeEvents(IReadOnlyList<int> types,
            out int order, out int flags) => TryEncode(types, false, out order, out flags);

        public static bool TryEncodeQuests(IReadOnlyList<int> oneHotFlags,
            out int order, out int flags) => TryEncode(oneHotFlags, true, out order, out flags);

        public static bool IsValidEvents(int order, int flags) =>
            IsValid(order, flags, 7);

        public static bool IsValidQuests(int order, int flags) =>
            IsValid(order, flags, 5);

        public static bool TryDecodeEvents(int order, int flags, bool inRange,
            out int[] types) => TryDecode(order, flags, inRange, false, out types);

        public static bool TryDecodeQuests(int order, int flags, bool inRange,
            out int[] oneHotFlags) => TryDecode(order, flags, inRange, true, out oneHotFlags);

        private static bool TryEncode(IReadOnlyList<int> values, bool quests,
            out int order, out int flags)
        {
            order = 0;
            flags = 0;
            int count = quests ? 5 : 7;
            if (values == null || values.Count > count) return false;
            int encoded = 0;
            int seen = 0;
            for (int i = 0; i < values.Count; i++)
            {
                int type = values[i];
                if (quests)
                {
                    type = QuestTypeIndex(type);
                    if (type < 0) return false;
                }
                if (type < 0 || type >= count || (seen & (1 << type)) != 0)
                    return false;
                seen |= 1 << type;
                encoded |= (type + 1) << (3 * i);
            }
            order = encoded;
            flags = seen;
            return true;
        }

        private static int QuestTypeIndex(int flag)
        {
            switch (flag)
            {
                case 1: return 0;
                case 2: return 1;
                case 4: return 2;
                case 8: return 3;
                case 16: return 4;
                default: return -1;
            }
        }

        private static bool IsValid(int order, int flags, int typeCount)
        {
            int maximum = (1 << (3 * typeCount)) - 1;
            if (order < 0 || order > maximum || flags < 0 ||
                flags >= (1 << typeCount)) return false;
            int seen = 0;
            while (order != 0)
            {
                int digit = order & 7;
                if (digit == 0 || digit > typeCount) return false;
                int bit = 1 << (digit - 1);
                if ((seen & bit) != 0) return false;
                seen |= bit;
                order >>= 3;
            }
            return seen == flags;
        }

        private static bool TryDecode(int order, int flags, bool inRange,
            bool quests, out int[] values)
        {
            values = System.Array.Empty<int>();
            if (!IsValid(order, flags, quests ? 5 : 7)) return false;
            int admitted = quests
                ? CoopCampaignMapSettlementEventPolicy.FilterQuests(flags, inRange)
                : CoopCampaignMapSettlementEventPolicy.FilterEvents(flags, inRange);
            var result = new List<int>(quests ? 5 : 7);
            while (order != 0)
            {
                int type = (order & 7) - 1;
                int bit = 1 << type;
                if ((admitted & bit) != 0) result.Add(quests ? bit : type);
                order >>= 3;
            }
            values = result.ToArray();
            return true;
        }
    }
}
