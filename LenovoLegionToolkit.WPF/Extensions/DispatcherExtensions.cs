using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class DispatcherExtensions
{
    public static void InvokeTask(this Dispatcher dispatcher, Func<Task> action) => dispatcher.Invoke(async () => await action());

    /// <summary>
    /// Invokes an action on the dispatcher thread asynchronously without blocking
    /// </summary>
    public static Task InvokeAsync(this Dispatcher dispatcher, Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, priority).Task;
    }

    /// <summary>
    /// Invokes a function on the dispatcher thread asynchronously without blocking
    /// </summary>
    public static Task<T> InvokeAsync<T>(this Dispatcher dispatcher, Func<T> func, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (dispatcher.CheckAccess())
        {
            return Task.FromResult(func());
        }

        return dispatcher.InvokeAsync(func, priority).Task;
    }

    /// <summary>
    /// Invokes an async action on the dispatcher thread
    /// </summary>
    public static async Task InvokeAsync(this Dispatcher dispatcher, Func<Task> asyncAction, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (dispatcher.CheckAccess())
        {
            await asyncAction().ConfigureAwait(false);
        }
        else
        {
            await dispatcher.InvokeAsync(async () => await asyncAction().ConfigureAwait(false), priority);
        }
    }
}
