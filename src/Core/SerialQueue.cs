using System;
using System.Threading.Tasks;

namespace TvDesk.Core;

/// <summary>FIFO worker queue; enqueue order must survive ThreadPool scheduling.</summary>
public sealed class SerialQueue
{
    private readonly object _sync = new();
    private Task _tail = Task.CompletedTask;

    public Task Enqueue(Action action)
    {
        lock (_sync)
            return _tail = _tail.ContinueWith(_ => action(), default,
                TaskContinuationOptions.None, TaskScheduler.Default);
    }
}
