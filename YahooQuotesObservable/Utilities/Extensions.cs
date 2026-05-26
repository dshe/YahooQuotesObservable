namespace YahooQuotesObservable;

public static class Extension
{
    internal static void Forget(this Task task, ILogger? logger = null)
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
