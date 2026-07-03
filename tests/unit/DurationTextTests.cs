using NemoVoiceTyping.Services;
using Xunit;
using static NemoVoiceTyping.Tests.GivenWhenThen;

namespace NemoVoiceTyping.Tests;

public class DurationTextTests
{
    [Fact]
    public void Reads_a_bare_number_as_seconds()
    {
        var ok = false;
        var seconds = 0;

        using (Given("a bare number with no unit"))
        using (When("parsing \"45\""))
        {
            ok = DurationText.TryParseSeconds("45", out seconds);
        }

        using (Then("it is read as 45 seconds"))
        {
            Assert.True(ok);
            Assert.Equal(45, seconds);
        }
    }

    [Fact]
    public void Converts_unit_suffixes_to_seconds()
    {
        using (Given("values carrying s/m/h suffixes"))
        using (When("parsing each"))
        using (Then("units are converted to seconds"))
        {
            Assert.True(DurationText.TryParseSeconds("90s", out var s) && s == 90);
            Assert.True(DurationText.TryParseSeconds("2m", out var m) && m == 120);
            Assert.True(DurationText.TryParseSeconds("1h", out var h) && h == 3600);
            Assert.True(DurationText.TryParseSeconds(" 1.5m ", out var f) && f == 90);
        }
    }

    [Fact]
    public void Rejects_garbage_and_empty_input()
    {
        using (Given("inputs that are not durations"))
        using (When("parsing them"))
        using (Then("each is rejected as not a number"))
        {
            Assert.Equal(DurationParseResult.NotANumber, DurationText.Parse(null, out _));
            Assert.Equal(DurationParseResult.NotANumber, DurationText.Parse("", out _));
            Assert.Equal(DurationParseResult.NotANumber, DurationText.Parse("soon", out _));
            Assert.Equal(DurationParseResult.NotANumber, DurationText.Parse("5x", out _));
        }
    }

    [Fact]
    public void Enforces_the_five_second_floor_and_five_hour_ceiling()
    {
        using (Given("values below 5 seconds or above 5 hours"))
        using (When("parsing them"))
        using (Then("each is rejected with the matching reason and the 5h boundary is accepted"))
        {
            Assert.Equal(DurationParseResult.BelowMinimum, DurationText.Parse("2", out _));
            Assert.Equal(DurationParseResult.BelowMinimum, DurationText.Parse("0", out _));
            Assert.Equal(DurationParseResult.BelowMinimum, DurationText.Parse("-30", out _));
            Assert.Equal(DurationParseResult.AboveMaximum, DurationText.Parse("6h", out _));
            Assert.True(DurationText.TryParseSeconds("5h", out var max) && max == 18000);
        }
    }

    [Fact]
    public void Formats_seconds_using_the_largest_clean_unit()
    {
        using (Given("stored second counts"))
        using (When("formatting them for display"))
        using (Then("the largest clean unit is used"))
        {
            Assert.Equal("15s", DurationText.Format(15));
            Assert.Equal("30s", DurationText.Format(30));
            Assert.Equal("5m", DurationText.Format(300));
            Assert.Equal("1h", DurationText.Format(3600));
            Assert.Equal("90s", DurationText.Format(90));
        }
    }
}
