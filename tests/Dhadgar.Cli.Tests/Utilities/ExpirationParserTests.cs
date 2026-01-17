using Dhadgar.Cli.Utilities;
using FluentAssertions;
using Xunit;

namespace Dhadgar.Cli.Tests.Utilities;

public class ExpirationParserTests
{
    private static readonly DateTime FixedUtcNow = new(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("1h", 1)]
    [InlineData("2h", 2)]
    [InlineData("12h", 12)]
    [InlineData("24h", 24)]
    public void Parse_HourFormat_ReturnsCorrectTime(string input, int expectedHours)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().NotBeNull();
        result.Should().Be(FixedUtcNow.AddHours(expectedHours));
    }

    [Theory]
    [InlineData("1d", 1)]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    [InlineData("365d", 365)]
    public void Parse_DayFormat_ReturnsCorrectTime(string input, int expectedDays)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().NotBeNull();
        result.Should().Be(FixedUtcNow.AddDays(expectedDays));
    }

    [Theory]
    [InlineData("1w", 7)]
    [InlineData("2w", 14)]
    [InlineData("4w", 28)]
    public void Parse_WeekFormat_ReturnsCorrectTime(string input, int expectedDays)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().NotBeNull();
        result.Should().Be(FixedUtcNow.AddDays(expectedDays));
    }

    [Theory]
    [InlineData("1m", 1)]
    [InlineData("3m", 3)]
    [InlineData("12m", 12)]
    public void Parse_MonthFormat_ReturnsCorrectTime(string input, int expectedMonths)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().NotBeNull();
        result.Should().Be(FixedUtcNow.AddMonths(expectedMonths));
    }

    [Fact]
    public void Parse_Iso8601Format_ReturnsValidDate()
    {
        // Use UTC format to avoid timezone issues
        var result = ExpirationParser.Parse("2025-12-31T12:00:00Z", FixedUtcNow);

        result.Should().NotBeNull();
        result!.Value.Month.Should().Be(12);
        result.Value.Day.Should().Be(31);
    }

    [Theory]
    [InlineData("2025-06-15")]
    [InlineData("2025-12-31T23:59:59Z")]
    public void Parse_Iso8601Variations_ReturnsNotNull(string input)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_NullOrWhitespace_ReturnsNull(string? input)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("0h")]
    [InlineData("0d")]
    [InlineData("-1h")]
    [InlineData("-1d")]
    [InlineData("abc123")]
    [InlineData("h")]
    [InlineData("d")]
    public void Parse_InvalidFormat_ReturnsNull(string input)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("  1h  ")]
    [InlineData("  7D  ")]
    [InlineData(" 30d")]
    public void Parse_WithWhitespace_TrimsAndParses(string input)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("1H")]
    [InlineData("7D")]
    [InlineData("2W")]
    [InlineData("1M")]
    public void Parse_UppercaseSuffix_ParsesCorrectly(string input)
    {
        var result = ExpirationParser.Parse(input, FixedUtcNow);

        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData("1h")]
    [InlineData("7d")]
    [InlineData("2025-12-31")]
    public void IsValid_ValidInput_ReturnsTrue(string input)
    {
        var result = ExpirationParser.IsValid(input);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData(null)]
    public void IsValid_InvalidInput_ReturnsFalse(string? input)
    {
        var result = ExpirationParser.IsValid(input);

        result.Should().BeFalse();
    }
}
