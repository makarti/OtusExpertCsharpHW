namespace Parser.Parsing;

public readonly ref struct ParsedCommand
{
    public ReadOnlySpan<byte> Command { get; init; }

    public ReadOnlySpan<byte> Key { get; init; }

    public ReadOnlySpan<byte> Value { get; init; }

    public bool IsDefault => Command.IsEmpty && Key.IsEmpty && Value.IsEmpty;
}
