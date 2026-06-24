using System.Text;
using Parser.Parsing;

namespace Parser.Tests;

public sealed class CommandParserTests
{

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Parse_SetCommand_ReturnsCorrect()
    {
        byte[] input = Bytes("SET user:1 somedata");

        var result = CommandParser.Parse(input);

        // Assert
        Assert.Equal("SET",      Encoding.UTF8.GetString(result.Command));
        Assert.Equal("user:1",   Encoding.UTF8.GetString(result.Key));
        Assert.Equal("somedata", Encoding.UTF8.GetString(result.Value));
        Assert.False(result.IsDefault);
    }

    [Fact]
    public void Parse_GetCommand_ReturnsCorrect()
    {
        byte[] input = Bytes("GET user:1");

        ParsedCommand result = CommandParser.Parse(input);

        Assert.Equal("GET",    Encoding.UTF8.GetString(result.Command));
        Assert.Equal("user:1", Encoding.UTF8.GetString(result.Key));
        Assert.True(result.Value.IsEmpty);
        Assert.False(result.IsDefault);
    }

    [Fact]
    public void Parse_OnlySpaces_ReturnsDefault()
    {
        byte[] input = Bytes("   ");

        ParsedCommand result = CommandParser.Parse(input);

        Assert.True(result.IsDefault);
    }

    [Fact]
    public void Parse_SetCommand_ReturnsDefault()
    {
        byte[] input = Bytes("SET   ");

        ParsedCommand result = CommandParser.Parse(input);

        Assert.True(result.IsDefault);
    }

    [Fact]
    public void Parse_MultipleSpaces_ReturnsCorrect()
    {
        byte[] input = Bytes("SET   somekey   somevalue ");

        ParsedCommand result = CommandParser.Parse(input);

        Assert.Equal("SET",     Encoding.UTF8.GetString(result.Command));
        Assert.Equal("somekey",   Encoding.UTF8.GetString(result.Key));
        Assert.Equal("somevalue", Encoding.UTF8.GetString(result.Value));
    }
}
