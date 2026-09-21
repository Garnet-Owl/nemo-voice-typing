using NemoVoiceTyping.Services;
using Xunit;
using static NemoVoiceTyping.Tests.GivenWhenThen;

namespace NemoVoiceTyping.Tests;

public class AudioCaptureGainTests
{
    [Fact]
    public void Boosts_quiet_input_close_to_linearly()
    {
        using (Given("a quiet sample well below full scale"))
        using (When("gain is applied"))
        using (Then("it comes out roughly 4x louder"))
        {
            var boosted = AudioCapture.ApplyGain(0.02f);
            Assert.InRange(boosted, 0.075f, 0.08f);
        }
    }

    [Fact]
    public void Never_exceeds_full_scale_for_loud_input()
    {
        using (Given("input already at or above full scale"))
        using (When("gain is applied"))
        using (Then("the result stays within [-1, 1]"))
        {
            Assert.InRange(AudioCapture.ApplyGain(1.0f), -1f, 1f);
            Assert.InRange(AudioCapture.ApplyGain(-1.0f), -1f, 1f);
        }
    }
}
