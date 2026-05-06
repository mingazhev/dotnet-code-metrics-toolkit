using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Analysis;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Core.Validation;

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
            "list-metrics" => await RunListMetricsAsync(args.Skip(1).ToArray(), output, error)
                .ConfigureAwait(false),
            "explain" => await RunExplainAsync(args.Skip(1).ToArray(), output, error)
                .ConfigureAwait(false),
            "validate-output" => await RunValidateOutputAsync(args.Skip(1).ToArray(), output, error, cancellationToken)
                .ConfigureAwait(false),
            _ => await UnknownCommandAsync(args[0], error).ConfigureAwait(false)
        };
    }

    private static async Task<int> RunValidateOutputAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Count != 1)
        {
            await error.WriteLineAsync("Usage: codemetrics validate-output <artifact-dir>").ConfigureAwait(false);
            return 1;
        }

        try
        {
            OutputValidationResult result = OutputValidator.Validate(args[0], cancellationToken);

            if (result.IsValid)
            {
                await output.WriteLineAsync("Output artifacts are valid.").ConfigureAwait(false);
                return 0;
            }

            foreach (string validationError in result.Errors)
            {
                await error.WriteLineAsync(validationError).ConfigureAwait(false);
            }

            return 2;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Validation canceled.").ConfigureAwait(false);
            return 130;
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
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
        bool noRestore = false;
        bool isolateInput = false;
        int? maxDegreeOfParallelism = null;
        int top = 20;
        var includePatterns = new List<string>();
        var excludePatterns = new List<string>();

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

                case "--include":
                    if (index + 1 >= args.Count)
                    {
                        await error.WriteLineAsync($"{option} requires a value.").ConfigureAwait(false);
                        return 1;
                    }

                    includePatterns.Add(args[++index]);
                    break;

                case "--exclude":
                    if (index + 1 >= args.Count)
                    {
                        await error.WriteLineAsync($"{option} requires a value.").ConfigureAwait(false);
                        return 1;
                    }

                    excludePatterns.Add(args[++index]);
                    break;

                case "--include-chunk-text":
                    includeChunkText = true;
                    break;

                case "--syntax-only":
                    syntaxOnly = true;
                    break;

                case "--semantic":
                    syntaxOnly = false;
                    break;

                case "--no-restore":
                    noRestore = true;
                    break;

                case "--isolate-input":
                    isolateInput = true;
                    break;

                case "--max-degree-of-parallelism":
                    if (index + 1 >= args.Count || !int.TryParse(args[++index], out int parsedMaxDegreeOfParallelism) || parsedMaxDegreeOfParallelism <= 0)
                    {
                        await error.WriteLineAsync("--max-degree-of-parallelism requires a positive integer.").ConfigureAwait(false);
                        return 1;
                    }

                    maxDegreeOfParallelism = parsedMaxDegreeOfParallelism;
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
                    IncludePatterns = includePatterns,
                    ExcludePatterns = excludePatterns,
                    IncludeGeneratedCode = includeGeneratedCode,
                    IncludeChunkText = includeChunkText,
                    SyntaxOnly = syntaxOnly,
                    NoRestore = noRestore,
                    IsolateInput = isolateInput,
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                    Top = top
                },
                cancellationToken).ConfigureAwait(false);

            await output.WriteLineAsync($"Wrote analysis artifacts to {result.OutputPath}.")
                .ConfigureAwait(false);
            await output.WriteLineAsync(
                    $"Files: {result.Summary.FileCount}; Types: {result.Summary.TypeCount}; Members: {result.Summary.MemberCount}; Metrics: {result.Summary.MetricResultCount}.")
                .ConfigureAwait(false);
            await output.WriteLineAsync(
                    $"Analysis quality: {result.Summary.Health.AnalysisQuality}; Semantic model: {result.Summary.Health.SemanticModel}; Trusted diagnostics: {result.Summary.Health.TrustedDiagnostics}.")
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

    private static async Task<int> RunListMetricsAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Count != 0)
        {
            await error.WriteLineAsync("Usage: codemetrics list-metrics").ConfigureAwait(false);
            return 1;
        }

        foreach (MetricDescriptor metric in MetricCatalog.All.OrderBy(metric => metric.Id, StringComparer.Ordinal))
        {
            await output.WriteLineAsync(
                    $"{metric.Id}@{metric.Version}\t{string.Join(",", metric.TargetKinds)}\t{metric.AnalysisMode}\t{metric.Unit}")
                .ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<int> RunExplainAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Count != 1)
        {
            await error.WriteLineAsync("Usage: codemetrics explain <metric-id|metric-id@version>").ConfigureAwait(false);
            return 1;
        }

        MetricDescriptor? metric = MetricCatalog.Find(args[0]);

        if (metric is null)
        {
            await error.WriteLineAsync($"Unknown metric: {args[0]}").ConfigureAwait(false);
            return 1;
        }

        await output.WriteLineAsync($"{metric.Id}@{metric.Version}").ConfigureAwait(false);
        await output.WriteLineAsync($"Target kinds: {string.Join(", ", metric.TargetKinds)}").ConfigureAwait(false);
        await output.WriteLineAsync($"Analysis mode: {metric.AnalysisMode}").ConfigureAwait(false);
        await output.WriteLineAsync($"Unit: {metric.Unit}").ConfigureAwait(false);
        await output.WriteLineAsync($"Formula: {metric.Formula}").ConfigureAwait(false);
        await output.WriteLineAsync(metric.Description).ConfigureAwait(false);

        if (metric.KnownLimitations.Count > 0)
        {
            await output.WriteLineAsync("Known limitations:").ConfigureAwait(false);

            foreach (string limitation in metric.KnownLimitations)
            {
                await output.WriteLineAsync($"- {limitation}").ConfigureAwait(false);
            }
        }

        return 0;
    }

    private static async Task<int> UnknownCommandAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"Unknown command: {command}").ConfigureAwait(false);
        await WriteUsageAsync(error).ConfigureAwait(false);

        return 1;
    }

    private static Task WriteUsageAsync(TextWriter writer)
    {
        return writer.WriteLineAsync(
            "Usage: codemetrics analyze <path> --output <dir> [--isolate-input]\n" +
            "       codemetrics list-metrics\n" +
            "       codemetrics explain <metric-id|metric-id@version>\n" +
            "       codemetrics validate-output <artifact-dir>");
    }
}
