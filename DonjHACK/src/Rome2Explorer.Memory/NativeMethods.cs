using System.Runtime.InteropServices;

namespace Rome2Explorer.Memory;

internal static class NativeMethods
{
    internal const uint ProcessVmOperation = 0x0008;
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessVmWrite = 0x0020;
    internal const uint ProcessQueryInformation = 0x0400;

    internal const uint MemCommit = 0x1000;
    internal const uint MemReserve = 0x2000;
    internal const uint MemFree = 0x10000;
    internal const uint MemPrivate = 0x20000;
    internal const uint MemMapped = 0x40000;
    internal const uint MemImage = 0x1000000;

    internal const uint PageNoAccess = 0x01;
    internal const uint PageReadonly = 0x02;
    internal const uint PageReadwrite = 0x04;
    internal const uint PageWritecopy = 0x08;
    internal const uint PageExecute = 0x10;
    internal const uint PageExecuteRead = 0x20;
    internal const uint PageExecuteReadwrite = 0x40;
    internal const uint PageExecuteWritecopy = 0x80;
    internal const uint PageGuard = 0x100;
    internal const uint PageNoCache = 0x200;
    internal const uint PageWriteCombine = 0x400;
    internal const uint PageProtectionMask = 0xFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(nint processHandle, nuint address, out MemoryBasicInformation buffer, nuint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        nint processHandle,
        nuint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint numberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteProcessMemory(
        nint processHandle,
        nuint baseAddress,
        [In] byte[] buffer,
        nuint size,
        out nuint numberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWow64Process(nint processHandle, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);
}
