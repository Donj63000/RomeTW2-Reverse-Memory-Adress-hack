namespace Rome2Explorer.Signatures;

public readonly record struct AobByte(byte? Value)
{
    public bool IsWildcard => Value is null;
}
