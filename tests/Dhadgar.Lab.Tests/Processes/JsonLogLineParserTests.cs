using Dhadgar.Lab.Processes;

namespace Dhadgar.Lab.Tests.Processes;

public sealed class JsonLogLineParserTests
{
    private static readonly DateTimeOffset Fallback = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_ConsoleJsonLine_ReadsEveryField()
    {
        const string line = "{\"Timestamp\":\"2026-10-27T10:05:00.0000000+00:00\",\"EventId\":{\"Id\":42,\"Name\":\"Boot\"},\"LogLevel\":\"Warning\",\"Category\":\"Dhadgar.Nodes\",\"Message\":\"hello\"}";

        LogLine parsed = JsonLogLineParser.Parse(line, Fallback);

        Assert.Equal(new DateTimeOffset(2026, 10, 27, 10, 5, 0, TimeSpan.Zero), parsed.Timestamp);
        Assert.Equal("Warning", parsed.Level);
        Assert.Equal("Dhadgar.Nodes", parsed.Category);
        Assert.Equal(42, parsed.EventId);
        Assert.Equal("hello", parsed.Message);
    }

    [Fact]
    public void Parse_NumericEventId_ReadsTheNumber()
    {
        const string line = "{\"LogLevel\":\"Error\",\"Category\":\"c\",\"EventId\":7,\"Message\":\"m\"}";

        LogLine parsed = JsonLogLineParser.Parse(line, Fallback);

        Assert.Equal(7, parsed.EventId);
        Assert.Equal(Fallback, parsed.Timestamp);
    }

    [Fact]
    public void Parse_PlainTextLine_IsKeptAsRawMessage()
    {
        LogLine parsed = JsonLogLineParser.Parse("plain text line", Fallback);

        Assert.Equal("Raw", parsed.Level);
        Assert.Equal("plain text line", parsed.Message);
        Assert.Null(parsed.EventId);
    }

    [Fact]
    public void Parse_MalformedJson_IsKeptAsRawMessage()
    {
        LogLine parsed = JsonLogLineParser.Parse("{not json", Fallback);

        Assert.Equal("Raw", parsed.Level);
        Assert.Equal("{not json", parsed.Message);
    }

    [Fact]
    public void Parse_JsonWithoutMessage_IsKeptAsRawLine()
    {
        const string line = "{\"LogLevel\":\"Information\"}";

        LogLine parsed = JsonLogLineParser.Parse(line, Fallback);

        Assert.Equal("Raw", parsed.Level);
        Assert.Equal(line, parsed.Message);
    }
}
