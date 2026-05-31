namespace Rome2Explorer.Trace;

public static class DonjHackPathResolver
{
    public static string ResolveRoot(string? startDirectory = null)
    {
        var current = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);

        while (current is not null)
        {
            if (string.Equals(current.Name, "DonjHACK", StringComparison.OrdinalIgnoreCase)
                || File.Exists(Path.Combine(current.FullName, "DonjHACK.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.Combine(startDirectory ?? Directory.GetCurrentDirectory(), "DonjHACK");
    }

    public static string ResolveMemoryMapDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "memory-maps");
    }

    public static string ResolveCandidatesDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "candidates");
    }

    public static string ResolveKnownFindingsDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "known-findings");
    }

    public static string ResolveDiscriminatorDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "discriminator");
    }

    public static string ResolveLuaCapturesDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "lua-captures");
    }

    public static string ResolveStructureComparisonsDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "structure-comparisons");
    }

    public static string ResolveStructuresDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "structures");
    }

    public static string ResolveWritesDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "writes");
    }

    public static string ResolveValidationsDirectory(string? root = null)
    {
        var donjHackRoot = root ?? ResolveRoot();
        return Path.Combine(donjHackRoot, "evidence", "validations");
    }
}
