namespace CoopSpectator.Infrastructure
{
    // Dispatch on the frame AFTER release, so a newly created stock escape-menu
    // layer cannot consume that same release and immediately close itself.
    public sealed class CoopCampaignMapEscapeInputPolicy
    {
        private bool _pending;

        public bool Tick(bool ownsInput, bool released)
        {
            bool dispatch = ownsInput && _pending;
            _pending = ownsInput && released && !dispatch;
            return dispatch;
        }

        public void Reset() => _pending = false;
    }
}
