using Microsoft.Build.Locator;

namespace CodeMetricsToolkit.Core.Analysis;

internal static class MSBuildRegistration
{
    private static readonly object RegistrationLock = new();
    private static RegistrationResult? registration;
    private static VisualStudioInstance? registeredInstance;

    public static RegistrationResult EnsureRegistered(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        lock (RegistrationLock)
        {
            var normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);

            if (registration is not null)
            {
                return IsCompatibleWithRegisteredInstance(normalizedWorkingDirectory)
                    ? registration
                    : RegistrationResult.Unavailable(
                        "The current process already registered a different MSBuild SDK. " +
                        "Run analysis for this repository in a fresh process.");
            }

            RegistrationResult result = Register(normalizedWorkingDirectory);
            if (result.IsAvailable)
            {
                registration = result;
            }

            return result;
        }
    }

    private static RegistrationResult Register(string workingDirectory)
    {
        if (MSBuildLocator.IsRegistered)
        {
            return RegistrationResult.Unavailable(
                "MSBuild is already registered by another component in this process. " +
                "Run semantic analysis in a fresh process.");
        }

        if (!MSBuildLocator.CanRegister)
        {
            return RegistrationResult.Unavailable(
                "MSBuild registration was unavailable because MSBuild assemblies were already loaded; semantic analysis was skipped.");
        }

        try
        {
            VisualStudioInstance? instance = QueryInstance(workingDirectory);

            if (instance is null)
            {
                return RegistrationResult.Unavailable(
                    $"No compatible MSBuild installation was found for '{workingDirectory}'; semantic analysis was skipped.");
            }

            MSBuildLocator.RegisterInstance(instance);
            registeredInstance = instance;
            return RegistrationResult.Available;
        }
        catch (InvalidOperationException exception)
        {
            return RegistrationResult.Unavailable(
                $"MSBuild registration failed; semantic analysis was skipped: {exception.Message}");
        }
    }

    private static bool IsCompatibleWithRegisteredInstance(string workingDirectory)
    {
        if (registeredInstance is null)
        {
            return true;
        }

        VisualStudioInstance? requestedInstance = QueryInstance(workingDirectory);

        return requestedInstance is not null && string.Equals(
            Path.GetFullPath(requestedInstance.MSBuildPath),
            Path.GetFullPath(registeredInstance.MSBuildPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static VisualStudioInstance? QueryInstance(string workingDirectory)
    {
        var queryOptions = new VisualStudioInstanceQueryOptions
        {
            DiscoveryTypes = DiscoveryType.DotNetSdk | DiscoveryType.VisualStudioSetup,
            WorkingDirectory = workingDirectory
        };

        return MSBuildLocator.QueryVisualStudioInstances(queryOptions).FirstOrDefault();
    }

    internal sealed record RegistrationResult(bool IsAvailable, string? FailureMessage)
    {
        public static RegistrationResult Available { get; } = new(true, null);

        public static RegistrationResult Unavailable(string message)
        {
            return new RegistrationResult(false, message);
        }
    }
}
