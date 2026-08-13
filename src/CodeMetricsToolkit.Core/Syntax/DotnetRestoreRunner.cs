using System.Diagnostics;
using CodeMetricsToolkit.Core.Discovery;

namespace CodeMetricsToolkit.Core.Syntax;

internal static class DotnetRestoreRunner
{
    public static async Task<RestoreResult> RestoreAsync(
        DiscoveredSources sources,
        bool noRestore,
        CancellationToken cancellationToken)
    {
        if (noRestore)
        {
            return new RestoreResult("not_run", ["Restore was skipped because --no-restore was specified."]);
        }

        var targetPaths = SelectTargets(sources);
        if (targetPaths.Length == 0)
        {
            return new RestoreResult("not_run", ["Restore was skipped because no solution or project file was discovered."]);
        }

        var messages = new List<string>();

        foreach (var targetPath in targetPaths)
        {
            ProcessResult result = await RunProcessAsync(
                    "dotnet",
                    ["restore", targetPath],
                    sources.RootPath,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                messages.Add($"dotnet restore failed for {Path.GetFileName(targetPath)} with exit code {result.ExitCode}: {TrimProcessOutput(result)}");
                return new RestoreResult("failed", messages);
            }

            messages.Add($"dotnet restore succeeded for {Path.GetFileName(targetPath)}.");
        }

        return new RestoreResult("success", messages);
    }

    private static string[] SelectTargets(DiscoveredSources sources)
    {
        if (sources.SelectedSolutionPath is not null)
        {
            return [Path.Combine(sources.RootPath, sources.SelectedSolutionPath)];
        }

        return sources.ProjectPaths
            .Where(projectPath => !ProjectIdentity.IsSynthetic(projectPath))
            .Select(projectPath => Path.Combine(sources.RootPath, projectPath))
            .ToArray();
    }

    internal static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        var outputBuffer = new BoundedProcessOutput();
        var errorBuffer = new BoundedProcessOutput();
        Task output = DrainProcessOutputAsync(process.StandardOutput, outputBuffer);
        Task error = DrainProcessOutputAsync(process.StandardError, errorBuffer);
        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
            static state => TryKillProcessTree((Process)state!),
            process);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await ObserveCanceledProcessAsync(process, output, error).ConfigureAwait(false);
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            outputBuffer.ToString(),
            errorBuffer.ToString());
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Cancellation still propagates if the operating system denies the kill request.
        }
    }

    private static async Task ObserveCanceledProcessAsync(
        Process process,
        Task output,
        Task error)
    {
        try
        {
            await Task.WhenAll(
                    process.WaitForExitAsync(CancellationToken.None),
                    output,
                    error)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or
            InvalidOperationException or
            TimeoutException)
        {
            // Best-effort cleanup must not replace the original cancellation exception.
        }
    }

    private static async Task DrainProcessOutputAsync(
        StreamReader reader,
        BoundedProcessOutput buffer)
    {
        var characters = new char[4 * 1024];
        int read;
        while ((read = await reader.ReadAsync(characters, CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            buffer.AppendChunk(characters.AsSpan(0, read));
        }
    }

    private static string TrimProcessOutput(ProcessResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        var flattened = string.Join(" ", text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4));

        return string.IsNullOrWhiteSpace(flattened) ? "no process output" : flattened;
    }

    internal sealed record RestoreResult(
        string Status,
        IReadOnlyList<string> Messages);

    internal sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
