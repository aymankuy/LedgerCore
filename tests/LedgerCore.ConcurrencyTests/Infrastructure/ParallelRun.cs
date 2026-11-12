namespace LedgerCore.ConcurrencyTests.Infrastructure;

public static class ParallelRun
{
    /// <summary>
    /// Starts <paramref name="count"/> calls at the same moment and waits for all of them.
    /// Every call waits on one gate that opens only after all tasks exist, so they really overlap.
    /// </summary>
    public static async Task<T[]> AllAtOnceAsync<T>(int count, Func<int, Task<T>> call)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, count)
            .Select(async i =>
            {
                await gate.Task;
                return await call(i);
            })
            .ToArray();

        gate.SetResult();
        return await Task.WhenAll(tasks);
    }
}
