using Rome2Explorer.Domain;

namespace Rome2Explorer.Signatures;

public interface IAobScanner
{
    IReadOnlyList<Candidate> Scan(AobPattern pattern, MemoryMapSnapshot snapshot);
}
