using Rome2Explorer.Domain;

namespace Rome2Explorer.Signatures;

public sealed class NoOpAobScanner : IAobScanner
{
    public IReadOnlyList<Candidate> Scan(AobPattern pattern, MemoryMapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(snapshot);

        // V1 garde seulement le contrat : aucun scan actif complexe n'est execute ici.
        return Array.Empty<Candidate>();
    }
}
