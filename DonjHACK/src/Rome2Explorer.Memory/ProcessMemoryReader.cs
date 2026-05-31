using System.ComponentModel;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Rome2Explorer.Domain;
using Rome2Explorer.Validation;

namespace Rome2Explorer.Memory;

public sealed class ProcessMemoryReader : IProcessMemoryReader, IProcessMemoryWriter
{
    private nint _processHandle;
    private nint _writeProcessHandle;
    private Process? _process;
    private Rome2ProcessInfo? _processInfo;
    private bool _disposed;

    public bool HasWriteAccess => _writeProcessHandle != 0;

    public Rome2ProcessInfo AttachToRome2()
    {
        var process = new Rome2ProcessDiscovery()
            .FindRunningProcesses()
            .FirstOrDefault();

        if (process is null)
        {
            throw new InvalidOperationException("Rome2.exe n'est pas lance. Lance la campagne, puis retente l'attache.");
        }

        return Attach(process.ProcessId);
    }

    public Rome2ProcessInfo Attach(int processId)
    {
        ThrowIfDisposed();

        var process = Process.GetProcessById(processId);
        Rome2TargetValidator.EnsureRome2Process(process);

        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessVmRead,
            inheritHandle: false,
            process.Id);

        if (handle == 0)
        {
            throw CreateOpenProcessException();
        }

        CloseAttachedHandle();

        _process = process;
        _processHandle = handle;
        _processInfo = CreateProcessInfo(process);
        return _processInfo;
    }

    public IReadOnlyList<ModuleInfo> EnumerateModules()
    {
        EnsureAttached();

        var process = _process!;
        var modules = new List<ModuleInfo>();

        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                modules.Add(new ModuleInfo(
                    Name: module.ModuleName,
                    BaseAddress: unchecked((ulong)module.BaseAddress.ToInt64()),
                    Size: module.ModuleMemorySize,
                    Path: TryGetModulePath(module)));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return Array.Empty<ModuleInfo>();
        }

        return modules
            .OrderBy(module => module.BaseAddress)
            .ToArray();
    }

    public IReadOnlyList<MemoryRegionInfo> EnumerateRegions()
    {
        EnsureAttached();

        var regions = new List<MemoryRegionInfo>();
        var address = 0UL;
        var maxAddress = _processInfo!.Architecture == ProcessArchitecture.X86
            ? uint.MaxValue
            : 0x00007FFFFFFFFFFFUL;
        var mbiSize = (nuint)Marshal.SizeOf<MemoryBasicInformation>();

        while (address < maxAddress)
        {
            var result = NativeMethods.VirtualQueryEx(_processHandle, (nuint)address, out var mbi, mbiSize);
            if (result == 0)
            {
                break;
            }

            var baseAddress = (ulong)mbi.BaseAddress;
            var regionSize = (ulong)mbi.RegionSize;
            if (regionSize == 0)
            {
                break;
            }

            var classification = MemoryProtectionClassifier.Classify(mbi.Protect, mbi.State, mbi.Type);
            regions.Add(new MemoryRegionInfo(
                BaseAddress: baseAddress,
                Size: regionSize,
                State: classification.State,
                Protection: classification.Protection,
                Type: classification.Type,
                IsReadable: classification.IsReadable,
                IsWritable: classification.IsWritable,
                IsExecutable: classification.IsExecutable));

            var nextAddress = baseAddress + regionSize;
            if (nextAddress <= address)
            {
                break;
            }

            address = nextAddress;
        }

        return regions.ToArray();
    }

    public MemoryMapSnapshot CaptureMemoryMap()
    {
        EnsureAttached();

        var modules = EnumerateModules();
        var regions = EnumerateRegions();
        var summary = new MemoryMapSummary(
            ModuleCount: modules.Count,
            RegionCount: regions.Count,
            TotalReadableBytes: SumRegionBytes(regions, region => region.IsReadable),
            TotalWritableBytes: SumRegionBytes(regions, region => region.IsWritable),
            TotalExecutableBytes: SumRegionBytes(regions, region => region.IsExecutable));

        return new MemoryMapSnapshot(
            CreatedAt: DateTimeOffset.Now,
            Process: _processInfo!,
            Modules: modules,
            Regions: regions,
            Summary: summary);
    }

    public byte[] ReadBytes(ulong address, int count)
    {
        EnsureAttached();

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Le nombre d'octets a lire ne peut pas etre negatif.");
        }

        if (count == 0)
        {
            return Array.Empty<byte>();
        }

        var regions = EnumerateRegions();
        var output = new byte[count];
        var outputOffset = 0;
        var cursor = address;

        while (outputOffset < count)
        {
            var region = FindReadableRegion(regions, cursor);
            if (region is null)
            {
                throw new InvalidOperationException($"Lecture refusee : 0x{cursor:X} n'est pas dans une region lisible.");
            }

            var regionEnd = checked(region.BaseAddress + region.Size);
            var remainingInRegion = regionEnd - cursor;
            var chunkSize = Math.Min(count - outputOffset, checked((int)Math.Min(remainingInRegion, int.MaxValue)));
            var chunk = new byte[chunkSize];

            if (!NativeMethods.ReadProcessMemory(_processHandle, (nuint)cursor, chunk, (nuint)chunk.Length, out var bytesRead))
            {
                throw CreateWin32Exception($"ReadProcessMemory a echoue a 0x{cursor:X}.");
            }

            if ((int)bytesRead != chunk.Length)
            {
                throw new InvalidOperationException(
                    $"Lecture partielle a 0x{cursor:X} : {bytesRead} octets lus sur {chunk.Length} demandes.");
            }

            Buffer.BlockCopy(chunk, 0, output, outputOffset, chunk.Length);
            outputOffset += chunk.Length;
            cursor += (ulong)chunk.Length;
        }

        return output;
    }

    public bool TryReadBytes(ulong address, int count, out byte[] bytes, out string? warning)
    {
        EnsureAttached();

        bytes = Array.Empty<byte>();
        warning = null;

        if (count < 0)
        {
            warning = "Lecture ignoree : la taille demandee est negative.";
            return false;
        }

        if (count == 0)
        {
            return true;
        }

        var buffer = new byte[count];
        if (!NativeMethods.ReadProcessMemory(_processHandle, (nuint)address, buffer, (nuint)buffer.Length, out var bytesRead))
        {
            warning = $"Lecture impossible a 0x{address:X} sur {count} octets : {new Win32Exception(Marshal.GetLastWin32Error()).Message}.";
            return false;
        }

        if ((int)bytesRead != buffer.Length)
        {
            warning = $"Lecture partielle a 0x{address:X} : {bytesRead} octets lus sur {buffer.Length}.";
            return false;
        }

        bytes = buffer;
        return true;
    }

    public bool TryReadInt32(ulong address, out int value, out string? warning)
    {
        value = 0;

        if (!TryReadBytes(address, sizeof(int), out var bytes, out warning))
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return true;
    }

    public bool TryEnableWriteAccess(out string? warning)
    {
        EnsureAttached();

        warning = null;

        if (_writeProcessHandle != 0)
        {
            return true;
        }

        var process = _process!;
        // J'ouvre un deuxieme handle avec les droits d'ecriture seulement quand l'utilisateur
        // lance vraiment une modification. Le handle principal reste limite a la lecture.
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation
            | NativeMethods.ProcessVmRead
            | NativeMethods.ProcessVmWrite
            | NativeMethods.ProcessVmOperation,
            inheritHandle: false,
            process.Id);

        if (handle == 0)
        {
            warning = CreateOpenProcessWriteWarning();
            return false;
        }

        _writeProcessHandle = handle;
        return true;
    }

    public bool TryWriteBytes(ulong address, byte[] bytes, out string? warning)
    {
        EnsureAttached();

        warning = null;

        if (bytes is null)
        {
            warning = "Ecriture refusee : le buffer est null.";
            return false;
        }

        if (bytes.Length == 0)
        {
            return true;
        }

        if (!TryEnableWriteAccess(out warning))
        {
            return false;
        }

        // Je n'ecris que les octets explicitement demandes par le service metier.
        // Pour l'argent treasury, ce sera toujours 4 octets Int32 little-endian.
        if (!NativeMethods.WriteProcessMemory(_writeProcessHandle, (nuint)address, bytes, (nuint)bytes.Length, out var bytesWritten))
        {
            warning = $"Ecriture impossible a 0x{address:X} sur {bytes.Length} octets : {new Win32Exception(Marshal.GetLastWin32Error()).Message}.";
            return false;
        }

        if ((int)bytesWritten != bytes.Length)
        {
            warning = $"Ecriture partielle a 0x{address:X} : {bytesWritten} octets ecrits sur {bytes.Length}.";
            return false;
        }

        return true;
    }

    public bool TryWriteInt32(ulong address, int value, out string? warning)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return TryWriteBytes(address, bytes, out warning);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CloseAttachedHandle();
        _process?.Dispose();
        _disposed = true;
    }

    private static ulong SumRegionBytes(IEnumerable<MemoryRegionInfo> regions, Func<MemoryRegionInfo, bool> predicate)
    {
        ulong total = 0;
        foreach (var region in regions)
        {
            if (predicate(region))
            {
                total += region.Size;
            }
        }

        return total;
    }

    private static MemoryRegionInfo? FindReadableRegion(IEnumerable<MemoryRegionInfo> regions, ulong address)
    {
        return regions.FirstOrDefault(region =>
            region.IsReadable
            && address >= region.BaseAddress
            && address < region.BaseAddress + region.Size);
    }

    private Rome2ProcessInfo CreateProcessInfo(Process process)
    {
        return new Rome2ProcessInfo(
            ProcessId: process.Id,
            Name: process.ProcessName,
            Path: TryGetProcessPath(process),
            Architecture: DetectArchitecture(),
            StartTime: TryGetStartTime(process),
            MainWindowTitle: TryGetMainWindowTitle(process));
    }

    private ProcessArchitecture DetectArchitecture()
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return ProcessArchitecture.X86;
        }

        if (!NativeMethods.IsWow64Process(_processHandle, out var isWow64))
        {
            return ProcessArchitecture.Unknown;
        }

        return isWow64 ? ProcessArchitecture.X86 : ProcessArchitecture.X64;
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

    private static string? TryGetModulePath(ProcessModule module)
    {
        try
        {
            return module.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return null;
        }
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    private static Exception CreateOpenProcessException()
    {
        var errorCode = Marshal.GetLastWin32Error();
        if (errorCode == 5)
        {
            return new UnauthorizedAccessException(
                "Windows refuse l'attache a Rome2.exe car DonjHACK n'a pas les memes droits que le jeu. " +
                "Ferme cette fenetre et relance DonjHACK en administrateur. " +
                "Detail technique : OpenProcess erreur 5, droits demandes uniquement read-only PROCESS_QUERY_INFORMATION + PROCESS_VM_READ.");
        }

        return new Win32Exception(errorCode, "OpenProcess a echoue. DonjHACK demande uniquement les droits read-only.");
    }

    private static string CreateOpenProcessWriteWarning()
    {
        var errorCode = Marshal.GetLastWin32Error();
        if (errorCode == 5)
        {
            return "Windows refuse les droits d'ecriture sur Rome2.exe. Relance DonjHACK en administrateur et verifie que le jeu tourne avec les memes droits.";
        }

        return $"OpenProcess write a echoue : {new Win32Exception(errorCode).Message}.";
    }

    private void EnsureAttached()
    {
        ThrowIfDisposed();

        if (_process is null || _processInfo is null || _processHandle == 0)
        {
            throw new InvalidOperationException("Aucun processus attache. Clique d'abord sur Attach Rome2.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void CloseAttachedHandle()
    {
        if (_processHandle != 0)
        {
            NativeMethods.CloseHandle(_processHandle);
            _processHandle = 0;
        }

        if (_writeProcessHandle != 0)
        {
            NativeMethods.CloseHandle(_writeProcessHandle);
            _writeProcessHandle = 0;
        }
    }
}
