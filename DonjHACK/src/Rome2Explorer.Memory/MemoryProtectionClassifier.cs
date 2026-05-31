namespace Rome2Explorer.Memory;

public static class MemoryProtectionClassifier
{
    public static MemoryProtectionClassification Classify(uint protect, uint state, uint type)
    {
        var baseProtection = protect & NativeMethods.PageProtectionMask;
        var hasGuard = (protect & NativeMethods.PageGuard) != 0;
        var isCommitted = state == NativeMethods.MemCommit;
        var isNoAccess = baseProtection == NativeMethods.PageNoAccess || baseProtection == 0;

        var isReadable = isCommitted
            && !hasGuard
            && !isNoAccess
            && (baseProtection is NativeMethods.PageReadonly
                or NativeMethods.PageReadwrite
                or NativeMethods.PageWritecopy
                or NativeMethods.PageExecuteRead
                or NativeMethods.PageExecuteReadwrite
                or NativeMethods.PageExecuteWritecopy);

        var isWritable = isCommitted
            && !hasGuard
            && (baseProtection is NativeMethods.PageReadwrite
                or NativeMethods.PageWritecopy
                or NativeMethods.PageExecuteReadwrite
                or NativeMethods.PageExecuteWritecopy);

        var isExecutable = isCommitted
            && !hasGuard
            && (baseProtection is NativeMethods.PageExecute
                or NativeMethods.PageExecuteRead
                or NativeMethods.PageExecuteReadwrite
                or NativeMethods.PageExecuteWritecopy);

        return new MemoryProtectionClassification(
            State: FormatState(state),
            Protection: FormatProtection(protect),
            Type: FormatType(type),
            IsReadable: isReadable,
            IsWritable: isWritable,
            IsExecutable: isExecutable);
    }

    private static string FormatState(uint state)
    {
        return state switch
        {
            NativeMethods.MemCommit => "MEM_COMMIT",
            NativeMethods.MemReserve => "MEM_RESERVE",
            NativeMethods.MemFree => "MEM_FREE",
            _ => $"0x{state:X}"
        };
    }

    private static string FormatProtection(uint protect)
    {
        if (protect == 0)
        {
            return "None";
        }

        var baseProtection = protect & NativeMethods.PageProtectionMask;
        var parts = new List<string>
        {
            baseProtection switch
            {
                NativeMethods.PageNoAccess => "PAGE_NOACCESS",
                NativeMethods.PageReadonly => "PAGE_READONLY",
                NativeMethods.PageReadwrite => "PAGE_READWRITE",
                NativeMethods.PageWritecopy => "PAGE_WRITECOPY",
                NativeMethods.PageExecute => "PAGE_EXECUTE",
                NativeMethods.PageExecuteRead => "PAGE_EXECUTE_READ",
                NativeMethods.PageExecuteReadwrite => "PAGE_EXECUTE_READWRITE",
                NativeMethods.PageExecuteWritecopy => "PAGE_EXECUTE_WRITECOPY",
                _ => $"0x{baseProtection:X}"
            }
        };

        if ((protect & NativeMethods.PageGuard) != 0)
        {
            parts.Add("PAGE_GUARD");
        }

        if ((protect & NativeMethods.PageNoCache) != 0)
        {
            parts.Add("PAGE_NOCACHE");
        }

        if ((protect & NativeMethods.PageWriteCombine) != 0)
        {
            parts.Add("PAGE_WRITECOMBINE");
        }

        return string.Join("|", parts);
    }

    private static string FormatType(uint type)
    {
        return type switch
        {
            NativeMethods.MemImage => "MEM_IMAGE",
            NativeMethods.MemMapped => "MEM_MAPPED",
            NativeMethods.MemPrivate => "MEM_PRIVATE",
            0 => "None",
            _ => $"0x{type:X}"
        };
    }
}
