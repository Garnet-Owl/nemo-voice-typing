using NemoVoiceTyping.Services;
using Xunit;
using static NemoVoiceTyping.Tests.GivenWhenThen;

namespace NemoVoiceTyping.Tests;

public class TextInjectionModeTests
{
    [Fact]
    public void Pastes_into_Notepads_editor_when_automatic()
    {
        var mode = "";
        var focusedClass = "";
        var pastes = false;

        using (Given("automatic mode, with Notepad's editor focused"))
        {
            mode = "auto";
            focusedClass = "RichEditD2DPT";
        }

        using (When("deciding how to type"))
        {
            pastes = TextInjector.ShouldPaste(mode, focusedClass);
        }

        using (Then("the text is pasted"))
        {
            Assert.True(pastes);
        }
    }

    [Fact]
    public void Keeps_typing_keystrokes_into_other_controls_when_automatic()
    {
        var mode = "";
        string[] focusedClasses = [];
        var pastes = new List<bool>();

        using (Given("automatic mode, with VS Code or Windows Terminal focused"))
        {
            mode = "auto";
            focusedClasses = ["Chrome_WidgetWin_1", "Windows.UI.Input.InputSite.WindowClass"];
        }

        using (When("deciding how to type"))
        {
            pastes.AddRange(focusedClasses.Select(focused => TextInjector.ShouldPaste(mode, focused)));
        }

        using (Then("keystrokes are used, as before"))
        {
            Assert.All(pastes, Assert.False);
        }
    }

    [Fact]
    public void A_forced_mode_overrides_the_focused_control()
    {
        var pastesIntoVsCode = false;
        var pastesIntoNotepad = true;

        using (Given("a mode forced in config.json, in any capitalisation"))
        {
        }

        using (When("deciding how to type"))
        {
            pastesIntoVsCode = TextInjector.ShouldPaste("Clipboard", "Chrome_WidgetWin_1");
            pastesIntoNotepad = TextInjector.ShouldPaste("keystrokes", "RichEditD2DPT");
        }

        using (Then("the forced mode wins"))
        {
            Assert.True(pastesIntoVsCode);
            Assert.False(pastesIntoNotepad);
        }
    }
}
