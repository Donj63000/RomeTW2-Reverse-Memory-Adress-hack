using System.Text.Json;
using Rome2Explorer.Domain;

namespace Rome2Explorer.Trace;

public sealed class SavedFindingStore
{
    private const string FileName = "known-findings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public IReadOnlyList<SavedMemoryFinding> Load(string? donjHackRoot = null)
    {
        var path = ResolveFilePath(donjHackRoot);
        if (!File.Exists(path))
        {
            return Array.Empty<SavedMemoryFinding>();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SavedMemoryFinding[]>(json, JsonOptions) ?? Array.Empty<SavedMemoryFinding>();
        }
        catch (JsonException)
        {
            // Si le fichier est casse, je le garde pour diagnostic et je repars sur une liste vide
            // au lieu de bloquer le demarrage de DonjHACK.
            File.Copy(path, $"{path}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}", overwrite: false);
            return Array.Empty<SavedMemoryFinding>();
        }
    }

    public string Upsert(SavedMemoryFinding finding, string? donjHackRoot = null)
    {
        ArgumentNullException.ThrowIfNull(finding);

        var findings = Load(donjHackRoot)
            .Where(existing => !IsSameFinding(existing, finding))
            .Append(finding)
            .OrderBy(existing => existing.FeatureId)
            .ThenBy(existing => existing.Address)
            .ToArray();

        return SaveAll(findings, donjHackRoot);
    }

    public string SaveAll(IReadOnlyList<SavedMemoryFinding> findings, string? donjHackRoot = null)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var directory = DonjHackPathResolver.ResolveKnownFindingsDirectory(donjHackRoot);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var tempPath = $"{path}.tmp";
        var json = JsonSerializer.Serialize(findings, JsonOptions);

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);

        return path;
    }

    public static string ResolveFilePath(string? donjHackRoot = null)
    {
        return Path.Combine(DonjHackPathResolver.ResolveKnownFindingsDirectory(donjHackRoot), FileName);
    }

    private static bool IsSameFinding(SavedMemoryFinding left, SavedMemoryFinding right)
    {
        return string.Equals(left.FeatureId, right.FeatureId, StringComparison.OrdinalIgnoreCase)
            && left.Address == right.Address
            && string.Equals(left.Architecture, right.Architecture, StringComparison.OrdinalIgnoreCase);
    }
}
