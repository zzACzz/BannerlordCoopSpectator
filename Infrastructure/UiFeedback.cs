namespace CoopSpectator.Infrastructure // Infrastructure helpers shared across patches and systems
{ // Begin namespace
    /// <summary>
    /// UI feedback helpers that defer messages to a later tick to avoid losing them
    /// during menu transitions or input handling.
    /// </summary>
    public static class UiFeedback // Static utility; no instance state needed
    { // Begin class
        public static void ShowMessageDeferred(string text) // Show message on a later tick reliably
        { // Begin method
            // Defer by two ticks: first enqueue schedules the second enqueue, which then shows. // Explain double deferral
            MainThreadDispatcher.Enqueue(() => // Tick N: schedule the show for tick N+1
            { // Begin action
                MainThreadDispatcher.Enqueue(() => // Tick N+1: actually show the message
                { // Begin nested action
                    GameUi.ShowMessage(text); // Display the message through the shared UI helper
                }); // End nested action
            }); // End action
        } // End method
    } // End class
} // End namespace

