using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CoopSpectator.Infrastructure;
using SandBox.View.Map.Visuals;
using Missile = TaleWorlds.CampaignSystem.Siege.SiegeEvent.SiegeEngineMissile;

namespace CoopSpectator.Campaign
{
    // Caller owns siege/visibility admission and invokes Begin/Finish around the
    // native parallel join. Never retained beyond that batch; not installed yet.
    internal sealed class CoopCampaignMapSiegeFlightObservation
    {
        private sealed class ReferenceComparer : IEqualityComparer<Missile>
        {
            public bool Equals(Missile x, Missile y) => ReferenceEquals(x, y);
            public int GetHashCode(Missile value) => RuntimeHelpers.GetHashCode(value);
        }
        private sealed class Shot
        {
            internal long Id;
            internal double CollisionHours;
        }
        private sealed class Batch
        {
            internal SettlementVisual Visual;
            internal CoopCampaignMapSiegeFlightBuffer.Token Token;
            internal Dictionary<Missile, Shot> Shots;
        }
        private readonly int _ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly CoopCampaignMapSiegeFlightBuffer _buffer = new CoopCampaignMapSiegeFlightBuffer();
        private Batch _active;

        // IDs must be supplied by the SAME registry used for projectile publication.
        internal bool Begin(SettlementVisual visual, IReadOnlyList<Missile> missiles, IReadOnlyList<long> ids)
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread) return false;
            Clear();
            if (visual == null || missiles == null || ids == null || missiles.Count != ids.Count ||
                missiles.Count > CoopCampaignMapSiegeFlightBuffer.Capacity) return false;
            try
            {
                var shots = new Dictionary<Missile, Shot>(new ReferenceComparer());
                var uniqueIds = new HashSet<long>();
                for (int i = 0; i < missiles.Count; i++)
                {
                    var missile = missiles[i];
                    if (missile == null || ids[i] <= 0 || !uniqueIds.Add(ids[i]) || shots.ContainsKey(missile)) return false;
                    double hours = missile.CollisionTime.ToHours;
                    if (double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0) return false;
                    shots.Add(missile, new Shot { Id = ids[i], CollisionHours = hours });
                }
                Volatile.Write(ref _active, new Batch { Visual = visual, Shots = shots, Token = _buffer.Begin() });
                return true;
            }
            catch (Exception) { Clear(); return false; }
        }

        // Worker callback: only exact pre-admitted references and boxed value data.
        // Must not read Campaign.Current, native visibility or missile fields here.
        internal void Observe(SettlementVisual visual, Missile missile, object prepared)
        {
            var batch = Volatile.Read(ref _active);
            if (batch == null || !ReferenceEquals(visual, batch.Visual) || missile == null) return;
            try
            {
                if (!batch.Shots.TryGetValue(missile, out var shot)) return;
                if (!CoopCampaignMapSiegeFlightReader.TryRead(prepared, shot.CollisionHours, out var flight))
                { _buffer.TryAdd(batch.Token, shot.Id, null); return; }
                _buffer.TryAdd(batch.Token, shot.Id, flight);
            }
            catch (Exception)
            {
                // Fail this batch without throwing through native visual ticking.
                _buffer.TryAdd(batch.Token, 0, null);
            }
        }

        internal bool Finish(out Dictionary<long, CoopCampaignMapSiegeFlight> flights)
        {
            flights = null;
            if (Thread.CurrentThread.ManagedThreadId != _ownerThread) return false;
            var batch = Interlocked.Exchange(ref _active, null);
            return batch != null && _buffer.TryFinish(batch.Token, out flights);
        }

        internal void Clear()
        {
            // Managed-only revocation is also safe from a failing worker callback.
            Interlocked.Exchange(ref _active, null);
            _buffer.Clear();
        }
    }
}
