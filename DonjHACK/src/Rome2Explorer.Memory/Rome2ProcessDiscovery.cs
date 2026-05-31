using System.ComponentModel;
using System.Diagnostics;
using Rome2Explorer.Domain;
using Rome2Explorer.Validation;

namespace Rome2Explorer.Memory;

public sealed class Rome2ProcessDiscovery
{
    public IReadOnlyList<Rome2ProcessCandidate> FindRunningProcesses()
    {
        var candidates = Process.GetProcessesByName(Rome2TargetValidator.ExpectedProcessName)
            .Select(CreateCandidate)
            .ToArray();

        return MarkRecommended(candidates);
    }

    public static IReadOnlyList<Rome2ProcessCandidate> MarkRecommended(IEnumerable<Rome2ProcessCandidate> candidates)
    {
        var ordered = candidates
            .OrderByDescending(candidate => candidate.HasMainWindow)
            .ThenByDescending(candidate => candidate.StartTime ?? DateTimeOffset.MinValue)
            .ThenByDescending(candidate => candidate.ProcessId)
            .ToArray();

        if (ordered.Length == 0)
        {
            return Array.Empty<Rome2ProcessCandidate>();
        }

        var recommendedProcessId = ordered[0].ProcessId;

        // Je marque un seul processus recommande : s'il y a plusieurs Rome2.exe, je privilegie celui
        // qui a une fenetre principale, puis le plus recent. L'utilisateur garde toujours le choix final.
        return ordered
            .Select(candidate => candidate with
            {
                IsRecommended = candidate.ProcessId == recommendedProcessId,
                RecommendationReason = candidate.ProcessId == recommendedProcessId
                    ? "Recommande : fenetre active ou lancement le plus recent."
                    : "Non recommande par defaut : verifie le PID avant l'attache."
            })
            .ToArray();
    }

    private static Rome2ProcessCandidate CreateCandidate(Process process)
    {
        return new Rome2ProcessCandidate(
            ProcessId: process.Id,
            Name: process.ProcessName,
            Path: TryGetProcessPath(process),
            StartTime: TryGetStartTime(process),
            MainWindowTitle: TryGetMainWindowTitle(process),
            HasMainWindow: process.MainWindowHandle != 0,
            IsRecommended: false,
            RecommendationReason: string.Empty);
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }
    }

    private static string? TryGetMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
