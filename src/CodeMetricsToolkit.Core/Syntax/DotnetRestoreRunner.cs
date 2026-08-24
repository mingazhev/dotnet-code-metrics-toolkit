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

        string muxerPath;
        try
        {
            muxerPath = DotnetMuxer.Resolve();
        }
        catch (InvalidOperationException exception)
        {
            messages.Add(exception.Message);
            return new RestoreResult("failed", messages);
        }

        foreach (var targetPath in targetPaths)
        {
            ProcessResult result = await RunProcessAsync(
                    muxerPath,
                    ["restore", targetPath],
                    sources.RootPath,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                messages.Add($"dotnet restore failed for {Path.GetFileName(targetPath)} with exit code {result.ExitCode}.");
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
                RedirectStandardInput = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var outputBuffer = new BoundedProcessOutput();
        var errorBuffer = new BoundedProcessOutput();
        Task? output = null;
        Task? error = null;

        try
        {
            process.Start();
            output = DrainProcessOutputAsync(process.StandardOutput, outputBuffer);
            error = DrainProcessOutputAsync(process.StandardError, errorBuffer);
            using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
                static state => TryKillProcessTree((Process)state!),
                process);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(output, error)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            TryKillProcessTree(process);
            if (output is not null && error is not null)
            {
                await ObserveCanceledProcessAsync(process, output, error).ConfigureAwait(false);
            }
            if (exception is OperationCanceledException canceled)
            {
                throw canceled;
            }

            throw new TimeoutException("Timed out draining restore process output.", exception);
        }
        finally
        {
            TryKillProcessTree(process);
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
            TryKillProcessTree(process);
            try
            {
                process.StandardOutput.Close();
                process.StandardError.Close();
            }
            catch (Exception closeException) when (closeException is IOException or ObjectDisposedException)
            {
                // Closing redirected readers unblocks abandoned drains.
            }

            try
            {
                await Task.WhenAll(output, error)
                    .WaitAsync(TimeSpan.FromSeconds(1))
                    .ConfigureAwait(false);
            }
            catch (Exception drainException) when (drainException is
                IOException or
                InvalidOperationException or
                ObjectDisposedException or
                TimeoutException)
            {
                // Abandoned drains must not replace the original cancellation exception.
            }
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

    internal sealed record RestoreResult(
        string Status,
        IReadOnlyList<string> Messages);

    internal sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
