using SquareSnap.Core.Cli;
using LauncherProgram = SquareSnap.Launcher.Program;

namespace SquareSnap.Tests;

/// <summary>SquareSnap.com, the terminal helper: it must pass the arguments on exactly as typed.</summary>
public class LauncherTests
{
    [Theory]
    [InlineData(@"SquareSnap.com", "")]
    [InlineData(@"SquareSnap", "")]
    [InlineData(@"""C:\Program Files\SquareSnap\SquareSnap.com""", "")]
    [InlineData(@"""C:\Program Files\SquareSnap\SquareSnap.com""   ", "")]
    [InlineData(@"SquareSnap.com --help", " --help")]
    [InlineData(@".\SquareSnap capture --size 512", " capture --size 512")]
    [InlineData(@"""D:\My Tools\SquareSnap.com"" capture --out ""D:\My Shots""", @" capture --out ""D:\My Shots""")]
    [InlineData("SquareSnap.com\t--version", " --version")]
    [InlineData(@"SquareSnap.com  --name ""a b"" --out C:\x\", @" --name ""a b"" --out C:\x\")]
    public void Arguments_pass_through_unchanged(string commandLine, string expected)
        => Assert.Equal(expected, LauncherProgram.ArgumentsPart(commandLine));

    // FindTarget (step 6, BejView's 5 tests): the zip, the WinGet layout (Links symlink -> cli\squaresnap.exe,
    // SquareSnap.exe one folder up) and a broken install where SquareSnap.exe is really the helper. Paths are built
    // with Path.Combine so the tests run on Linux too.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "sqs-find");
    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    [Fact]
    public void Finds_SquareSnap_exe_next_to_the_helper()
    {
        string exe = P("zip", "SquareSnap.exe");
        Assert.Equal(exe, LauncherProgram.FindTarget(P("zip", "SquareSnap.com"), f => f == exe, f => f));
    }

    [Fact]
    public void Finds_SquareSnap_exe_through_the_WinGet_link_one_folder_up()
    {
        string link = P("Links", "squaresnap.exe"), helper = P("pkg", "cli", "squaresnap.exe"), exe = P("pkg", "SquareSnap.exe");
        Assert.Equal(exe, LauncherProgram.FindTarget(link, f => f == exe || f == helper, f => f == link ? helper : f));
    }

    [Fact]
    public void Prefers_the_folder_of_the_helper_over_the_folder_above()
    {
        string here = P("a", "b", "SquareSnap.exe"), above = P("a", "SquareSnap.exe");
        Assert.Equal(here, LauncherProgram.FindTarget(P("a", "b", "SquareSnap.com"), f => f == here || f == above, f => f));
    }

    [Fact]
    public void Never_starts_itself_when_SquareSnap_exe_is_really_the_helper()
    {
        // What a WinGet hard link named squaresnap.exe would do: SquareSnap.exe replaced by a link to the helper.
        string helper = P("pkg", "SquareSnap.com"), exe = P("pkg", "SquareSnap.exe");
        Assert.Null(LauncherProgram.FindTarget(helper, f => f == exe, f => f == exe ? helper : f));
    }

    [Fact]
    public void Reports_nothing_when_SquareSnap_exe_is_missing()
        => Assert.Null(LauncherProgram.FindTarget(P("x", "SquareSnap.com"), _ => false, f => f));

    [Fact]
    public void Start_failed_code_matches_the_app() => Assert.Equal(ExitCodes.StartFailed, LauncherProgram.StartFailed);

    [Fact]
    public void Help_mentions_the_helper_and_exit_code_5()
    {
        Assert.Contains("SquareSnap.com", CommandLine.HelpText);
        Assert.Contains("5  couldn't start: SquareSnap.com couldn't find SquareSnap.exe", CommandLine.HelpText);
        Assert.EndsWith("Enjoy SquareSnap!", CommandLine.HelpText.TrimEnd());
    }

    [Fact]
    public void About_says_who_made_it_in_the_users_words() =>
        Assert.Equal("Developed by Richard Bejtlich. Version 1.0 programmed by Claude Sonnet 3.7. Version 2.0 programmed by Claude Opus 5.5.",
            SquareSnap.App.MainWindow.AboutCredits);
}
