using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace CoopSpectator.Infrastructure
{
    // Only audited thread-local grammar state: immutable scalars or flat
    // collections of strings/value tuples. Never a general object graph copier.
    public sealed class CoopCampaignMapTooltipThreadStateScope : IDisposable
    {
        public const int MaxFields = 8;
        public const int MaxEntries = 4096;
        private readonly FieldInfo[] _fields;
        private readonly object[] _originals;
        private readonly int _owner;
        private bool _disposed;

        private CoopCampaignMapTooltipThreadStateScope(FieldInfo[] fields, object[] originals, int owner)
        { _fields = fields; _originals = originals; _owner = owner; }

        public static bool TryBegin(FieldInfo[] fields, int owner, out CoopCampaignMapTooltipThreadStateScope scope)
        {
            scope = null;
            if (Thread.CurrentThread.ManagedThreadId != owner || fields == null || fields.Length < 1 || fields.Length > MaxFields) return false;
            var originals = new object[fields.Length];
            var working = new object[fields.Length];
            var unique = new HashSet<FieldInfo>();
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || !unique.Add(field) || !field.IsStatic || field.IsInitOnly || field.IsLiteral ||
                    !field.IsDefined(typeof(ThreadStaticAttribute), false) || !SupportedType(field.FieldType)) return false;
                originals[i] = field.GetValue(null);
                if (!TryClone(field.FieldType, originals[i], out working[i])) return false;
            }
            // Publish the scope before any write: a caller's finally can restore
            // all fields even if a reflection write unexpectedly throws midway.
            scope = new CoopCampaignMapTooltipThreadStateScope((FieldInfo[])fields.Clone(), originals, owner);
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, working[i]);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (Thread.CurrentThread.ManagedThreadId != _owner)
                throw new InvalidOperationException("Tooltip grammar scope must finish on its owner thread.");
            Exception failure = null;
            for (int i = _fields.Length - 1; i >= 0; i--)
            {
                try { _fields[i].SetValue(null, _originals[i]); }
                catch (Exception ex) { failure = failure ?? ex; }
            }
            if (failure != null) throw new InvalidOperationException("Tooltip grammar restoration failed.", failure);
            _disposed = true;
        }

        private static bool SupportedType(Type type)
        {
            return type == typeof(bool) || type == typeof(int) || type.IsEnum ||
                type == typeof(List<int>) || type == typeof(List<string>) || type == typeof(List<ValueTuple<string, int>>) ||
                type == typeof(Dictionary<string, ValueTuple<string, int>>) ||
                type == typeof(Dictionary<string, ValueTuple<string, int, bool>>);
        }

        private static bool TryClone(Type type, object original, out object working)
        {
            working = null;
            if (type.IsValueType) { working = original; return original != null && original.GetType() == type; }
            // Leave null null: native lazy accessors create their ordinary state.
            if (original == null) return true;
            if (original.GetType() != type) return false;
            if (original is Dictionary<string, ValueTuple<string, int>> pairs)
            {
                if (pairs.Count > MaxEntries) return false;
                working = new Dictionary<string, ValueTuple<string, int>>(pairs, pairs.Comparer);
            }
            else if (original is Dictionary<string, ValueTuple<string, int, bool>> triples)
            {
                if (triples.Count > MaxEntries) return false;
                working = new Dictionary<string, ValueTuple<string, int, bool>>(triples, triples.Comparer);
            }
            else if (original is IList list)
            {
                if (list.Count > MaxEntries) return false;
                var copy = (IList)Activator.CreateInstance(type);
                foreach (object entry in list) copy.Add(entry);
                working = copy;
            }
            else return false;
            return true;
        }
    }
}
