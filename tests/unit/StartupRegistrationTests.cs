using NemoVoiceTyping.Services;
using Xunit;
using static NemoVoiceTyping.Tests.GivenWhenThen;

namespace NemoVoiceTyping.Tests;

public class StartupRegistrationTests
{
    [Fact]
    public void Returns_the_quoted_process_path_when_one_exists()
    {
        string? processPath = null;
        string command = "";

        using (Given("a live process path"))
        {
            processPath = @"C:\Apps\Nemo\Nemo Voice Typing.exe";
        }

        using (When("building the run-at-startup command"))
        {
            command = StartupRegistration.BuildRunCommand(processPath, @"C:\ignored");
        }

        using (Then("the command is the quoted process path"))
        {
            Assert.Equal("\"C:\\Apps\\Nemo\\Nemo Voice Typing.exe\"", command);
        }
    }

    [Fact]
    public void Falls_back_to_the_app_directory_when_the_process_path_is_missing()
    {
        string? processPath = null;
        string command = "";

        using (Given("no process path, as single-file hosts can report in edge cases"))
        {
            processPath = null;
        }

        using (When("building the run-at-startup command"))
        {
            command = StartupRegistration.BuildRunCommand(processPath, @"C:\Apps\Nemo");
        }

        using (Then("the command points at the exe inside the app directory"))
        {
            Assert.Equal("\"C:\\Apps\\Nemo\\Nemo Voice Typing.exe\"", command);
        }
    }

    [Fact]
    public void Never_registers_an_empty_command_when_the_process_path_is_empty()
    {
        var processPath = "";
        var command = "";

        using (Given("an empty process path, like Assembly.Location in single-file publishes (IL3000)"))
        {
            processPath = "";
        }

        using (When("building the run-at-startup command"))
        {
            command = StartupRegistration.BuildRunCommand(processPath, @"C:\Apps\Nemo");
        }

        using (Then("the command is never the empty quoted string"))
        {
            Assert.NotEqual("\"\"", command);
            Assert.EndsWith("Nemo Voice Typing.exe\"", command);
        }
    }
}
