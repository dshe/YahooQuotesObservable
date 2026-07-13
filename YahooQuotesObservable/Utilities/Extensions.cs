namespace YahooQuotesObservable;

public static class Extension
{
    extension(Task task)
    {
        internal void Forget(ILogger? logger = null)
        {
            _ = ObserveAsync(task, logger);

            // Internal local function to handle the task
            async static Task ObserveAsync(Task task, ILogger? logger)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Forget: task failed.");
                }
            }
        }
    }
}
