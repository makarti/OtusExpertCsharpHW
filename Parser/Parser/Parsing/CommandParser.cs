namespace Parser.Parsing;

/// <summary>
/// Разбирает команды вида <c>COMMAND KEY [VALUE]</c> из буфера байт.
/// Все операции выполняются через <see cref="ReadOnlySpan{T}"/> без аллокаций.
/// </summary>
public static class CommandParser
{
    private const byte _space = (byte)' ';

    public static ParsedCommand Parse(ReadOnlySpan<byte> input)
    {
        // Убираем пробелы по краям
        var span = Trim(input);

        if (span.IsEmpty)
            return default;

        // COMMAND
        int cmdEnd = span.IndexOf(_space);

        if (cmdEnd < 0)
            return default;

        ReadOnlySpan<byte> command = span.Slice(0, cmdEnd);

        ReadOnlySpan<byte> keyStart = SkipSpaces(span.Slice(cmdEnd + 1));

        if (keyStart.IsEmpty)
            return default;

        // KEY
        int keyEnd = keyStart.IndexOf(_space);

        if (keyEnd < 0)
        {
            return new ParsedCommand
            {
                Command = command,
                Key = keyStart,
                Value = ReadOnlySpan<byte>.Empty
            };
        }

        ReadOnlySpan<byte> key = keyStart.Slice(0, keyEnd);

        ReadOnlySpan<byte> valuePart = SkipSpaces(keyStart.Slice(keyEnd + 1));

        // VALUE
        return new ParsedCommand
        {
            Command = command,
            Key = key,
            Value = valuePart
        };
    }

    private static ReadOnlySpan<byte> SkipSpaces(ReadOnlySpan<byte> span)
    {
        int i = 0;
        while (i < span.Length && span[i] == _space)
            i++;

        return span.Slice(i);
    }

    /// Убирает пробелы, \r и \n c двух сторон.
    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span)
    {
        int start = 0;
        while (start < span.Length && IsWhitespace(span[start]))
            start++;

        int end = span.Length - 1;
        while (end >= start && IsWhitespace(span[end]))
            end--;

        return start > end ? ReadOnlySpan<byte>.Empty : span.Slice(start, end + 1);
    }

    private static bool IsWhitespace(byte b) =>
        b == _space || b == (byte)'\r' || b == (byte)'\n';
}
