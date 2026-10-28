using System;
using System.Collections;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    // Read-only managed metadata, not a native probe or ID repair. If the audited
    // layout changes, detached portrait preparation must not start.
    internal static class CoopItemRegistryCounter
    {
        internal static bool TryRead(MBObjectManager manager, out uint value)
        {
            value = 0;
            if (manager == null) return false;
            try
            {
            var records = typeof(MBObjectManager).GetField("ObjectTypeRecords", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(records?.GetValue(manager) is IEnumerable list)) return false;
            foreach (object record in list)
            {
                var type = record?.GetType();
                if (type == null || !type.IsGenericType || type.GetGenericArguments().Length != 1 ||
                    type.GetGenericArguments()[0] != typeof(ItemObject)) continue;
                var counter = type.GetField("_objCount", BindingFlags.Instance | BindingFlags.NonPublic);
                if (counter?.FieldType != typeof(uint)) return false;
                value = (uint)counter.GetValue(record); return true;
            }
            return false;
            }
            catch (Exception) { return false; }
        }
    }
}
