using NemoVoiceTyping.Services;
using Xunit;

namespace NemoVoiceTyping.Tests;

public class DurationTextTests
{
    [Fact]
    public void Given_a_bare_number_When_parsing_Then_it_is_read_as_seconds()
    {
        // Given
        var input = "45";

        // When
        var ok = DurationText.TryParseSeconds(input, out var seconds);

        // Then
        Assert.True(ok);
        Assert.Equal(45, seconds);
    }

    [Fact]
    public void Given_suffixed_values_When_parsing_Then_units_are_converted_to_seconds()
    {
        // Given / When / Then
        Assert.True(DurationText.TryParseSeconds("90s", out var s) && s == 90);
        Assert.True(DurationText.TryParseSeconds("2m", out var m) && m == 120);
        Assert.True(DurationText.TryParseSeconds("1h", out var h) && h == 3600);
        Assert.True(DurationText.TryParseSeconds(" 1.5m ", out var f) && f == 90);
    }

    [Fact]
    public void Given_garbage_or_empty_input_When_parsing_Then_it_is_rejected()
    {
        // Given / When / Then
        Assert.False(DurationText.TryParseSeconds(null, out _));
        Assert.False(DurationText.TryParseSeconds("", out _));
        Assert.False(DurationText.TryParseSeconds("soon", out _));
        Assert.False(DurationText.TryParseSeconds("5x", out _));
    }

    [Fact]
    public void Given_out_of_range_values_When_parsing_Then_they_are_rejected()
    {
        // Given — below the 5s floor and above the 24h ceiling
        // When / Then
        Assert.False(DurationText.TryParseSeconds("2", out _));
        Assert.False(DurationText.TryParseSeconds("0", out _));
        Assert.False(DurationText.TryParseSeconds("-30", out _));
        Assert.False(DurationText.TryParseSeconds("25h", out _));
    }

    [Fact]
    public void Given_seconds_When_formatting_Then_the_largest_clean_unit_is_used()
    {
        // Given / When / Then
        Assert.Equal("15s", DurationText.Format(15));
        Assert.Equal("30s", DurationText.Format(30));
        Assert.Equal("5m", DurationText.Format(300));
        Assert.Equal("1h", DurationText.Format(3600));
        Assert.Equal("90s", DurationText.Format(90));
    }
}
