using CodeMetricsToolkit.Cli;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Console.CancelKeyPress += cancelHandler;

try
{
    try
    {
        return await CliApplication.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
    }
    catch (OperationCanceledException)
    {
        await Console.Error.WriteLineAsync("Operation canceled.");
        return CliExitCodes.Canceled;
    }
    catch (Exception exception)
    {
        await Console.Error.WriteLineAsync($"Internal error: {exception.Message}");
        return CliExitCodes.InternalError;
    }
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
