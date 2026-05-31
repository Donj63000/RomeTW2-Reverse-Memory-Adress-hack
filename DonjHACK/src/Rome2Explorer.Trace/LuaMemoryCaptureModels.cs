namespace Rome2Explorer.Trace;

public sealed record LuaMemoryCaptureEnvelope
{
    public DateTimeOffset CreatedAt { get; init; }

    public string? Tool { get; init; }

    public string? FeatureId { get; init; }

    public LuaCaptureProcessInfo? Process { get; init; }

    public LuaCaptureScenario? Scenario { get; init; }

    public LuaCaptureKnownValues? KnownValues { get; init; }

    public IReadOnlyList<CapturedCandidateWindow> Candidates { get; init; } = Array.Empty<CapturedCandidateWindow>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record LuaCaptureProcessInfo
{
    public int ProcessId { get; init; }

    public string? ProcessName { get; init; }

    public string? Architecture { get; init; }

    public IReadOnlyList<LuaCaptureModuleInfo> Modules { get; init; } = Array.Empty<LuaCaptureModuleInfo>();
}

public sealed record LuaCaptureModuleInfo
{
    public string? Name { get; init; }

    public ulong BaseAddress { get; init; }

    public string? BaseAddressHex { get; init; }

    public int Size { get; init; }

    public string? Path { get; init; }
}

public sealed record LuaCaptureScenario
{
    public string? ScenarioId { get; init; }

    public string? StepId { get; init; }

    public string? Action { get; init; }
}

public sealed record LuaCaptureKnownValues
{
    public int UiTreasury { get; init; }

    public IReadOnlyList<int> ValueHistory { get; init; } = Array.Empty<int>();
}

public sealed record CapturedCandidateWindow
{
    public string? CandidateId { get; init; }

    public ulong Address { get; init; }

    public string? AddressHex { get; init; }

    public LuaCaptureRegion? Region { get; init; }

    public ulong ContextStart { get; init; }

    public string? ContextStartHex { get; init; }

    public int ContextByteCount { get; init; }

    public string? ContextBytesHex { get; init; }

    public IReadOnlyList<CapturedInt32Field> DecodedInt32Fields { get; init; } = Array.Empty<CapturedInt32Field>();

    public IReadOnlyList<CapturedPointerLikeValue> PointerLikeValues { get; init; } = Array.Empty<CapturedPointerLikeValue>();

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record LuaCaptureRegion
{
    public ulong BaseAddress { get; init; }

    public string? BaseAddressHex { get; init; }

    public ulong Size { get; init; }

    public string? State { get; init; }

    public string? Protection { get; init; }

    public string? Type { get; init; }

    public bool IsReadable { get; init; }

    public bool IsWritable { get; init; }

    public bool IsExecutable { get; init; }
}

public sealed record CapturedInt32Field
{
    public int RelativeOffset { get; init; }

    public string? RelativeOffsetHex { get; init; }

    public ulong Address { get; init; }

    public string? AddressHex { get; init; }

    public int Value { get; init; }

    public bool MatchesUiValue { get; init; }
}

public sealed record CapturedPointerLikeValue
{
    public int RelativeOffset { get; init; }

    public string? RelativeOffsetHex { get; init; }

    public ulong Address { get; init; }

    public string? AddressHex { get; init; }

    public ulong Value { get; init; }

    public string? ValueHex { get; init; }

    public string? TargetRegionBaseHex { get; init; }
}
