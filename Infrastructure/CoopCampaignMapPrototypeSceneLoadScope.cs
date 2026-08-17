using System;
using System.Threading;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Marks the narrow synchronous Scene.Read corridor used by the isolated
    /// client Main_map renderer. The scope is thread-local so unrelated scene
    /// initialization on another thread is never affected.
    /// </summary>
    public static class CoopCampaignMapPrototypeSceneLoadScope
    {
        private static readonly AsyncLocal<int> Depth = new AsyncLocal<int>();

        public static bool IsActive => Depth.Value > 0;

        public static IDisposable Enter()
        {
            Depth.Value = Depth.Value + 1;
            return new ScopeLease();
        }

        private sealed class ScopeLease : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                Depth.Value = Math.Max(0, Depth.Value - 1);
            }
        }
    }
}
