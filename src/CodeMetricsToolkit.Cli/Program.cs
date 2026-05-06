using CodeMetricsToolkit.Cli;

int exitCode = await CliApplication.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);

return exitCode;
