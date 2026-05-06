using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Analysis;

namespace CodeMetricsToolkit.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Count == 0)
        {
            await WriteUsageAsync(error).ConfigureAwait(false);
            return 1;
        }

        return args[0] switch
        {
            "analyze" => await RunAnalyzeAsync(args.Skip(1).ToArray(), output, error, cancellationToken)
                .ConfigureAwait(false),
            _ => await UnknownCommandAsync(args[0], error).ConfigureAwait(false)
        };
    }

    private static async Task<int> RunAnalyzeAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            await error.WriteLineAsync("Missing path for analyze command.").ConfigureAwait(false);
            await WriteUsageAsync(error).ConfigureAwait(false);
            return 1;
        }

        string inputPath = args[0];
        string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "autoresearch");
        bool includeGeneratedCode = false;
        bool includeChunkText = false;
        bool syntaxOnly = false;
        int top = 20;

        for (int index = 1; index < args.Count; index++)
        {
            string option = args[index];

            switch (option)
            {
                case "--output":
                case "-o":
                    if (index + 1 >= args.Count)
                    {
                        await error.WriteLineAsync($"{option} requires a value.").ConfigureAwait(false);
                        return 1;
                    }

                    outputPath = args[++index];
                    break;

                case "--include-generated":
                    includeGeneratedCode = true;
                    break;

                case "--include-chunk-text":
                    includeChunkText = true;
                    break;

                case "--syntax-only":
                    syntaxOnly = true;
                    break;

                case "--top":
                    if (index + 1 >= args.Count || !int.TryParse(args[++index], out top) || top < 0)
                    {
                        await error.WriteLineAsync("--top requires a non-negative integer.").ConfigureAwait(false);
                        return 1;
                    }

                    break;

                default:
                    await error.WriteLineAsync($"Unknown analyze option: {option}").ConfigureAwait(false);
                    return 1;
            }
        }

        try
        {
            AnalysisRunResult result = await CodeMetricsAnalyzer.AnalyzeAsync(
                new AnalyzeRequest
                {
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    IncludeGeneratedCode = includeGeneratedCode,
                    IncludeChunkText = includeChunkText,
                    SyntaxOnly = syntaxOnly,
                    Top = top
                },
                cancellationToken).ConfigureAwait(false);

            await output.WriteLineAsync($"Wrote analysis artifacts to {result.OutputPath}.")
                .ConfigureAwait(false);
            await output.WriteLineAsync(
                    $"Files: {result.Summary.FileCount}; Types: {result.Summary.TypeCount}; Members: {result.Summary.MemberCount}; Metrics: {result.Summary.MetricResultCount}.")
                .ConfigureAwait(false);

            return 0;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Analysis canceled.").ConfigureAwait(false);
            return 130;
        }
        catch (DirectoryNotFoundException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
        catch (IOException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> UnknownCommandAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"Unknown command: {command}").ConfigureAwait(false);
        await WriteUsageAsync(error).ConfigureAwait(false);

        return 1;
    }

    private static Task WriteUsageAsync(TextWriter writer)
    {
        return writer.WriteLineAsync("Usage: codemetrics analyze <path> --output <dir>");
    }
}
