using Microsoft.Extensions.Logging;

namespace MacroKeyboard.Backend;

internal static class TaskExtensions
{
    /// <summary>
    /// Dispatches a Task as fire-and-forget. Any exception that escapes the task
    /// (i.e. leaked past an inner catch block) is logged via <paramref name="logger"/>
    /// instead of crashing the process through the unobserved-exception path.
    /// </summary>
    internal static void FireAndForget(this Task task, ILogger logger) =>
        task.ContinueWith(
            t => logger.LogError(t.Exception!.InnerException ?? t.Exception,
                "Unhandled exception in fire-and-forget event handler"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    /// <summary>
    /// Runs <paramref name="action"/>, catching and logging any exception at Error level.
    /// Use for event-handler bodies where exceptions must never propagate.
    /// </summary>
    internal static async Task TryCatchAsync(this ILogger logger, Func<Task> action, string errorMessage)
    {
        try { await action(); }
        catch (Exception ex) { logger.LogError(ex, "{ErrorMessage}", errorMessage); }
    }
}
