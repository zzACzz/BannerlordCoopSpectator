using System;
using System.IO;

namespace CoopSpectator.Infrastructure
{
    internal sealed class CoopCampaignContentIdentityCache
    {
        private object _owner;
        private string _identity;
        private string _failure;
        private DateTime _retryAfterUtc;

        internal string GetOrCreate(object owner, DateTime utcNow, Func<string> capture)
        {
            if (owner == null) throw new InvalidOperationException("No active game content owner.");
            if (!ReferenceEquals(_owner, owner))
            {
                _owner = owner; _identity = null; _failure = null; _retryAfterUtc = DateTime.MinValue;
            }
            if (_identity != null) return _identity;
            if (_failure == null || utcNow >= _retryAfterUtc)
            {
                try
                {
                    string identity = capture();
                    if (!CoopCampaignContentProfile.IsValid(identity))
                        throw new IOException("Local content identity is invalid.");
                    _identity = identity; _failure = null;
                    return identity;
                }
                catch (Exception ex)
                {
                    _failure = ex.Message;
                    _retryAfterUtc = utcNow.AddSeconds(2);
                }
            }
            throw new IOException("Campaign content is not ready: " + _failure);
        }
    }
}
