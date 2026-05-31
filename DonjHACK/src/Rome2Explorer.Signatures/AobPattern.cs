namespace Rome2Explorer.Signatures;

public sealed record AobPattern(string Source, IReadOnlyList<AobByte> Bytes)
{
    public static AobPattern Parse(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("La signature AOB ne peut pas etre vide.", nameof(pattern));
        }

        var tokens = pattern.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new List<AobByte>(tokens.Length);

        foreach (var token in tokens)
        {
            if (token is "?" or "??")
            {
                bytes.Add(new AobByte(null));
                continue;
            }

            if (token.Length != 2 || !byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                throw new FormatException($"Token AOB invalide : '{token}'. Format attendu : '8B ?? 90'.");
            }

            bytes.Add(new AobByte(value));
        }

        return new AobPattern(pattern, bytes);
    }
}
