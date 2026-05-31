using Rome2Explorer.Domain;

namespace Rome2Explorer.Memory;

public interface IProcessMemoryReader : IDisposable
{
    Rome2ProcessInfo AttachToRome2();

    Rome2ProcessInfo Attach(int processId);

    IReadOnlyList<ModuleInfo> EnumerateModules();

    IReadOnlyList<MemoryRegionInfo> EnumerateRegions();

    MemoryMapSnapshot CaptureMemoryMap();

    byte[] ReadBytes(ulong address, int count);

    bool TryReadBytes(ulong address, int count, out byte[] bytes, out string? warning);

    bool TryReadInt32(ulong address, out int value, out string? warning);
}
