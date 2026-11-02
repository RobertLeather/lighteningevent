public static class ProcessHost
{
    public static async Task<int> RunUntilStoppedAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        try
        {
            await work(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return 0;
    }
}
