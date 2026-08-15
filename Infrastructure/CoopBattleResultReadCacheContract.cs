using System;

namespace CoopSpectator.Infrastructure
{
    public struct CoopBattleResultFileStamp : IEquatable<CoopBattleResultFileStamp>
    {
        public CoopBattleResultFileStamp(string path, long length, long lastWriteUtcTicks)
        {
            Path = path;
            Length = length;
            LastWriteUtcTicks = lastWriteUtcTicks;
        }

        public string Path { get; }
        public long Length { get; }
        public long LastWriteUtcTicks { get; }

        public bool Equals(CoopBattleResultFileStamp other)
        {
            return string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase) &&
                   Length == other.Length &&
                   LastWriteUtcTicks == other.LastWriteUtcTicks;
        }

        public override bool Equals(object obj)
        {
            return obj is CoopBattleResultFileStamp other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(Path ?? string.Empty);
                hash = (hash * 397) ^ Length.GetHashCode();
                hash = (hash * 397) ^ LastWriteUtcTicks.GetHashCode();
                return hash;
            }
        }
    }

    public static class CoopBattleResultReadCacheContract
    {
        public static bool IsStable(
            CoopBattleResultFileStamp beforeRead,
            CoopBattleResultFileStamp afterRead)
        {
            return beforeRead.Equals(afterRead);
        }
    }

    public sealed class CoopBattleResultReadCache<TSnapshot>
        where TSnapshot : class
    {
        private readonly object _sync = new object();
        private bool _hasSnapshot;
        private CoopBattleResultFileStamp _stamp;
        private TSnapshot _snapshot;

        public bool TryGet(CoopBattleResultFileStamp stamp, out TSnapshot snapshot)
        {
            lock (_sync)
            {
                if (_hasSnapshot && _stamp.Equals(stamp))
                {
                    snapshot = _snapshot;
                    return true;
                }
            }

            snapshot = null;
            return false;
        }

        public bool TryStore(
            CoopBattleResultFileStamp beforeRead,
            CoopBattleResultFileStamp afterRead,
            TSnapshot snapshot)
        {
            if (snapshot == null ||
                !CoopBattleResultReadCacheContract.IsStable(beforeRead, afterRead))
            {
                return false;
            }

            lock (_sync)
            {
                _stamp = afterRead;
                _snapshot = snapshot;
                _hasSnapshot = true;
            }

            return true;
        }

        public void Invalidate()
        {
            lock (_sync)
            {
                _hasSnapshot = false;
                _stamp = default(CoopBattleResultFileStamp);
                _snapshot = null;
            }
        }
    }
}
