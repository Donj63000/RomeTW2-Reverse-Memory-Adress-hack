namespace Rome2Explorer.Memory;

public interface IProcessMemoryWriter
{
    bool HasWriteAccess { get; }

    bool TryEnableWriteAccess(out string? warning);

    bool TryWriteBytes(ulong address, byte[] bytes, out string? warning);

    bool TryWriteInt32(ulong address, int value, out string? warning);
}
