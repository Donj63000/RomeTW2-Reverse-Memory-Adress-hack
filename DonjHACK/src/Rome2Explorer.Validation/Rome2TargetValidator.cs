using System.Diagnostics;

namespace Rome2Explorer.Validation;

public static class Rome2TargetValidator
{
    public const string ExpectedProcessName = "Rome2";
    public const string ExpectedExecutableName = "Rome2.exe";

    public static bool IsRome2ProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return string.Equals(processName, ExpectedProcessName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, ExpectedExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureRome2Process(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!IsRome2ProcessName(process.ProcessName))
        {
            throw new InvalidOperationException(
                $"Processus refuse : '{process.ProcessName}'. DonjHACK V1 accepte uniquement {ExpectedExecutableName}.");
        }

        var executableName = TryGetExecutableName(process);
        if (executableName is not null && !IsRome2ProcessName(executableName))
        {
            throw new InvalidOperationException(
                $"Executable refuse : '{executableName}'. DonjHACK V1 accepte uniquement {ExpectedExecutableName}.");
        }
    }

    private static string? TryGetExecutableName(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
