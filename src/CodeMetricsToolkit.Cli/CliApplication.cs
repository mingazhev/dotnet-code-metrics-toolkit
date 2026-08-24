using CodeMetricsToolkit.Core;
using CodeMetricsToolkit.Core.Analysis;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Core.Validation;

namespace CodeMetricsToolkit.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            await WriteGlobalHelpAsync(output).ConfigureAwait(false);
            return CliExitCodes.Success;
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
            "help" => await RunHelpAsync(args.Skip(1).ToArray(), output, error).ConfigureAwait(false),
            "--help" or "-h" => await RunGlobalHelpOptionAsync(args.Skip(1).ToArray(), output, error)
                .ConfigureAwait(false),
            "--version" => await RunVersionAsync(args.Skip(1).ToArray(), output, error).ConfigureAwait(false),
            _ => await UnknownCommandAsync(args[0], error).ConfigureAwait(false)
        };
    }

    private static async Task<int> RunValidateOutputAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Length == 1 && IsHelpOption(args[0]))
        {
            await WriteValidateOutputHelpAsync(output).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        if (args.Length != 1)
        {
            await error.WriteLineAsync("validate-output requires exactly one artifact directory.").ConfigureAwait(false);
            await WriteValidateOutputHelpAsync(error).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }

        try
        {
            OutputValidationResult result = OutputValidator.Validate(args[0], cancellationToken);

            if (result.IsValid)
            {
                await output.WriteLineAsync("Output artifacts are valid.").ConfigureAwait(false);
                return CliExitCodes.Success;
            }

            foreach (var validationError in result.Errors)
            {
                await error.WriteLineAsync(validationError).ConfigureAwait(false);
            }

            return CliExitCodes.InvalidArtifacts;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Validation canceled.").ConfigureAwait(false);
            return CliExitCodes.Canceled;
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }
    }

    private static async Task<int> RunAnalyzeAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Any(IsHelpOption))
        {
            await WriteAnalyzeHelpAsync(output).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        if (args.Length == 0)
        {
            await error.WriteLineAsync("Missing path for analyze command.").ConfigureAwait(false);
            await WriteAnalyzeHelpAsync(error).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }

        var inputPath = args[0];
        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "codemetrics");
        var includeGeneratedCode = false;
        var includeChunkText = false;
        var syntaxOnly = false;
        var noRestore = false;
        var isolateInput = false;
        var allowDegraded = false;
        var allowEmpty = false;
        var top = 20;
        var includePatterns = new List<string>();
        var excludePatterns = new List<string>();

        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];

            switch (option)
            {
                case "--output":
                case "-o":
                    if (index + 1 >= args.Length)
                    {
                        await error.WriteLineAsync($"{option} requires a value.").ConfigureAwait(false);
                        return CliExitCodes.InputError;
                    }

                    outputPath = args[++index];
                    break;

                case "--include-generated":
                    includeGeneratedCode = true;
                    break;

                case "--include":
                    if (index + 1 >= args.Length)
                    {
                        await error.WriteLineAsync($"{option} requires a value.").ConfigureAwait(false);
                        return CliExitCodes.InputError;
                    }

                    var includeValue = args[++index];
                    if (string.IsNullOrWhiteSpace(includeValue))
                    {
                        await error.WriteLineAsync("--include requires a non-empty glob.").ConfigureAwait(false);
                        return CliExitCodes.InputError;
                    }

                    includePatterns.Add(includeValue);
                    break;

                case "--exclude":
                    if (index + 1 >= args.Length)
                    {
                        await error.WriteLineAsync($"{option} requires a value.").ConfigureAwait(false);
                        return CliExitCodes.InputError;
                    }

                    var excludeValue = args[++index];
                    if (string.IsNullOrWhiteSpace(excludeValue))
                    {
                        await error.WriteLineAsync("--exclude requires a non-empty glob.").ConfigureAwait(false);
                        return CliExitCodes.InputError;
                    }

                    excludePatterns.Add(excludeValue);
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

                case "--allow-degraded":
                    allowDegraded = true;
                    break;

                case "--allow-empty":
                    allowEmpty = true;
                    break;

                case "--top":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out top) || top < 0)
                    {
                        await error.WriteLineAsync("--top requires a non-negative integer.").ConfigureAwait(false);
                        return CliExitCodes.InputError;
                    }

                    break;

                default:
                    await error.WriteLineAsync($"Unknown analyze option: {option}").ConfigureAwait(false);
                    return CliExitCodes.InputError;
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

            foreach (var warning in result.Warnings)
            {
                await error.WriteLineAsync($"Warning: {warning}").ConfigureAwait(false);
            }

            var syntaxOnlyResult = string.Equals(
                result.Summary.Health.AnalysisQuality,
                "syntax_only",
                StringComparison.Ordinal);
            var analysisRejected = !allowDegraded &&
                (string.Equals(result.Summary.Health.AnalysisQuality, "degraded", StringComparison.Ordinal) ||
                    (!syntaxOnlyResult && !result.Summary.Health.TrustedDiagnostics));
            var emptyRejected = !allowEmpty && result.Summary.FileCount == 0;

            if (analysisRejected)
            {
                await error.WriteLineAsync(
                        "Analysis artifacts were written, but their quality is degraded. Re-run with --allow-degraded to accept degraded output.")
                    .ConfigureAwait(false);
            }

            if (emptyRejected)
            {
                await error.WriteLineAsync(
                        "Analysis artifacts were written, but no C# source files matched. Re-run with --allow-empty to accept an empty source set.")
                    .ConfigureAwait(false);
            }

            return analysisRejected || emptyRejected
                ? CliExitCodes.AnalysisRejected
                : CliExitCodes.Success;
        }
        catch (OperationCanceledException exception)
        {
            await error.WriteLineAsync("Analysis canceled.").ConfigureAwait(false);
            if (!string.Equals(exception.Message, "The operation was canceled.", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(exception.Message))
            {
                await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            }

            return CliExitCodes.Canceled;
        }
        catch (DirectoryNotFoundException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }
        catch (IOException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }
        catch (UnauthorizedAccessException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }
        catch (InvalidDataException exception)
        {
            return await WriteInvalidDataAsync(error, exception).ConfigureAwait(false);
        }
        catch (AggregateException exception)
        {
            InvalidDataException? invalidData = exception.Flatten()
                .InnerExceptions
                .OfType<InvalidDataException>()
                .FirstOrDefault();
            if (invalidData is not null)
            {
                return await WriteInvalidDataAsync(error, invalidData).ConfigureAwait(false);
            }

            throw;
        }
    }

    private static async Task<int> WriteInvalidDataAsync(TextWriter error, InvalidDataException exception)
    {
        await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
        return exception.Message.StartsWith("Staged artifact set is invalid", StringComparison.Ordinal)
            ? CliExitCodes.InvalidArtifacts
            : CliExitCodes.InputError;
    }

    private static async Task<int> RunListMetricsAsync(
        string[] args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Length == 1 && IsHelpOption(args[0]))
        {
            await WriteListMetricsHelpAsync(output).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        if (args.Length != 0)
        {
            await error.WriteLineAsync("list-metrics does not accept positional arguments.").ConfigureAwait(false);
            await WriteListMetricsHelpAsync(error).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }

        foreach (MetricDescriptor metric in MetricCatalog.All.OrderBy(metric => metric.Id, StringComparer.Ordinal))
        {
            await output.WriteLineAsync(
                    $"{metric.Id}@{metric.Version}\t{string.Join(",", metric.TargetKinds)}\t{metric.AnalysisMode}\t{metric.Unit}")
                .ConfigureAwait(false);
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> RunExplainAsync(
        string[] args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Length == 1 && IsHelpOption(args[0]))
        {
            await WriteExplainHelpAsync(output).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        if (args.Length != 1)
        {
            await error.WriteLineAsync("explain requires exactly one metric identifier.").ConfigureAwait(false);
            await WriteExplainHelpAsync(error).ConfigureAwait(false);
            return CliExitCodes.InputError;
        }

        MetricDescriptor? metric = MetricCatalog.Find(args[0]);

        if (metric is null)
        {
            await error.WriteLineAsync($"Unknown metric: {args[0]}").ConfigureAwait(false);
            return CliExitCodes.InputError;
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

            foreach (var limitation in metric.KnownLimitations)
            {
                await output.WriteLineAsync($"- {limitation}").ConfigureAwait(false);
            }
        }

        return CliExitCodes.Success;
    }

    private static async Task<int> RunHelpAsync(
        string[] args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Length == 0)
        {
            await WriteGlobalHelpAsync(output).ConfigureAwait(false);
            return CliExitCodes.Success;
        }

        if (args.Length != 1)
        {
            await error.WriteLineAsync("help accepts at most one command name.").ConfigureAwait(false);
            return CliExitCodes.InputError;
        }

        Task? help = args[0] switch
        {
            "analyze" => WriteAnalyzeHelpAsync(output),
            "list-metrics" => WriteListMetricsHelpAsync(output),
            "explain" => WriteExplainHelpAsync(output),
            "validate-output" => WriteValidateOutputHelpAsync(output),
            _ => null
        };

        if (help is null)
        {
            await error.WriteLineAsync($"Unknown command: {args[0]}").ConfigureAwait(false);
            return CliExitCodes.InputError;
        }

        await help.ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static async Task<int> RunGlobalHelpOptionAsync(
        string[] args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Length != 0)
        {
            await error.WriteLineAsync("--help does not accept arguments; use 'codemetrics help <command>'.")
                .ConfigureAwait(false);
            return CliExitCodes.InputError;
        }

        await WriteGlobalHelpAsync(output).ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static async Task<int> RunVersionAsync(
        string[] args,
        TextWriter output,
        TextWriter error)
    {
        if (args.Length != 0)
        {
            await error.WriteLineAsync("--version does not accept arguments.").ConfigureAwait(false);
            return CliExitCodes.InputError;
        }

        await output.WriteLineAsync(ToolkitInfo.Version).ConfigureAwait(false);
        return CliExitCodes.Success;
    }

    private static async Task<int> UnknownCommandAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"Unknown command: {command}").ConfigureAwait(false);
        await WriteGlobalHelpAsync(error).ConfigureAwait(false);

        return CliExitCodes.InputError;
    }

    private static bool IsHelpOption(string value)
    {
        return string.Equals(value, "--help", StringComparison.Ordinal) ||
            string.Equals(value, "-h", StringComparison.Ordinal);
    }

    private static Task WriteGlobalHelpAsync(TextWriter writer)
    {
        return writer.WriteLineAsync(
            $"""
            {ToolkitInfo.Name} {ToolkitInfo.Version}
            Deterministic structural and semantic metrics for C# and .NET codebases.

            Usage:
              codemetrics <command> [options]

            Commands:
              analyze <path>                 Analyze a source tree, project, or solution.
              list-metrics                   List metric identifiers and versions.
              explain <metric-id>            Describe a metric and its limitations.
              validate-output <artifact-dir> Validate a generated artifact set.
              help [command]                 Show global or command-specific help.

            Global options:
              -h, --help                     Show this help.
              --version                      Print the tool version.

            Exit codes:
              0    Success.
              1    Invalid command, arguments, input, or file-system operation.
              2    Artifact validation failed.
              3    Analysis completed, but trust or empty-input gates rejected it.
              70   Unexpected internal failure.
              130  Operation canceled.

            Run 'codemetrics help <command>' for command-specific options.
            """);
    }

    private static Task WriteAnalyzeHelpAsync(TextWriter writer)
    {
        return writer.WriteLineAsync(
            """
            Analyze C# source and write a versioned artifact set.

            Usage:
              codemetrics analyze <path> [options]

            Options:
              -o, --output <dir>        Output directory (default: artifacts/codemetrics).
              --include <glob>          Include matching source paths; repeatable.
              --exclude <glob>          Exclude matching source paths; repeatable.
              --include-generated       Include recognized generated C# files.
              --include-chunk-text      Include source text in chunks.ndjson.
              --semantic                Request semantic analysis (default).
              --syntax-only             Skip restore and semantic analysis.
              --no-restore              Skip the pre-analysis dotnet restore.
              --isolate-input           Analyze a temporary copy of the input tree.
              --top <count>             Number of hotspots in summary.json (default: 20).
              --allow-degraded          Return success for unexpectedly degraded output.
              --allow-empty             Return success when no C# source files match.
              -h, --help                Show this help.

            Semantic analysis registers one MSBuild SDK per process. Analyze a different
            SDK in a fresh process.

            Artifacts are preserved when trust or empty-input gates return exit code 3.
            """);
    }

    private static Task WriteListMetricsHelpAsync(TextWriter writer)
    {
        return writer.WriteLineAsync(
            """
            List every supported metric with its version, target kinds, analysis mode, and unit.

            Usage:
              codemetrics list-metrics
            """);
    }

    private static Task WriteExplainHelpAsync(TextWriter writer)
    {
        return writer.WriteLineAsync(
            """
            Describe one metric, including its formula and known limitations.

            Usage:
              codemetrics explain <metric-id|metric-id@version>
            """);
    }

    private static Task WriteValidateOutputHelpAsync(TextWriter writer)
    {
        return writer.WriteLineAsync(
            """
            Validate artifact schemas and cross-file contract invariants.

            Usage:
              codemetrics validate-output <artifact-dir>
            """);
    }
}
