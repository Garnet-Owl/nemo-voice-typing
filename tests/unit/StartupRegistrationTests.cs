using NemoVoiceTyping.Services;
using Xunit;

namespace NemoVoiceTyping.Tests;

public class StartupRegistrationTests
{
    [Fact]
    public void Given_a_process_path_When_building_the_run_command_Then_it_is_the_quoted_path()
    {
        // Given
        var processPath = @"C:\Apps\Nemo\Nemo Voice Typing.exe";

        // When
        var command = StartupRegistration.BuildRunCommand(processPath, @"C:\ignored");

        // Then
        Assert.Equal("\"C:\\Apps\\Nemo\\Nemo Voice Typing.exe\"", command);
    }

    [Fact]
    public void Given_no_process_path_When_building_the_run_command_Then_it_falls_back_to_the_app_directory()
    {
        // Given — single-file hosts can, in edge cases, report no process path
        string? processPath = null;

        // When
        var command = StartupRegistration.BuildRunCommand(processPath, @"C:\Apps\Nemo");

        // Then
        Assert.Equal("\"C:\\Apps\\Nemo\\Nemo Voice Typing.exe\"", command);
    }

    [Fact]
    public void Given_an_empty_process_path_When_building_the_run_command_Then_it_never_registers_an_empty_command()
    {
        // Given — Assembly.Location returns "" in single-file publishes (IL3000);
        // this guards against that class of bug regressing.
        var processPath = "";

        // When
        var command = StartupRegistration.BuildRunCommand(processPath, @"C:\Apps\Nemo");

        // Then
        Assert.NotEqual("\"\"", command);
        Assert.EndsWith("Nemo Voice Typing.exe\"", command);
    }
}
