using System.Runtime.InteropServices;

namespace Rome2Explorer.Memory;

[StructLayout(LayoutKind.Sequential)]
internal struct MemoryBasicInformation
{
    public nuint BaseAddress;
    public nuint AllocationBase;
    public uint AllocationProtect;
    public nuint RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
}
