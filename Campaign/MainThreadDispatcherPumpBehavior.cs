using CoopSpectator.Infrastructure; // Access dispatcher to execute queued actions
using TaleWorlds.CampaignSystem; // CampaignBehaviorBase + CampaignEvents

namespace CoopSpectator.Campaign // Keep campaign-related behaviors together
{ // Begin namespace
    /// <summary>
    /// Pumps MainThreadDispatcher from the campaign tick to ensure queued UI actions
    /// run even when application ticks are unreliable during campaign/menu flow.
    /// </summary>
    public sealed class MainThreadDispatcherPumpBehavior : CampaignBehaviorBase
    { // Begin class
        public override void RegisterEvents() // Register campaign events
        { // Begin method
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick); // Execute every campaign tick
        } // End method

        public override void SyncData(IDataStore dataStore) // No persistence needed
        { // Begin method
        } // End method

        private static void OnTick(float dt) // Called by campaign each tick
        { // Begin method
            MainThreadDispatcher.ExecutePending(); // Run queued actions on the main thread
        } // End method
    } // End class
} // End namespace

