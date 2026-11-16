using SquareSnap.Core.Cli;
using SquareSnap.Core.Naming;

namespace SquareSnap.Tests;

public class CommandLineTests
{
    private static CliOptions Ok(params string[] args)
    {
        var r = CommandLine.Parse(args);
        Assert.True(r.Ok, r.Error);
        return r.Options!;
    }

    private static string Err(params string[] args)
    {
        var r = CommandLine.Parse(args);
        Assert.False(r.Ok);
        return r.Error!;
    }

    [Fact]
    public void NoArgsOpensWindow() => Assert.Equal(Command.Gui, Ok().Command);

    [Fact]
    public void FullCapture()
    {
        var o = Ok("capture", "--size", "1080", "--out", "C:\\Shots", "--format=jpg", "--no-clipboard", "--delay", "3", "-v", "--log", "x.log");
        Assert.Equal(Command.Capture, o.Command);
        Assert.Equal(1080, o.Size!.Value.Pixels);
        Assert.Equal("C:\\Shots", o.OutFolder);
        Assert.Equal(ImageFormat.Jpeg, o.Format);
        Assert.False(o.Clipboard);
        Assert.Equal(3, o.DelaySeconds);
        Assert.True(o.Verbose);
        Assert.Equal("x.log", o.LogFile);
    }

    [Fact]
    public void UnsetOptionsStayNull()
    {
        var o = Ok("capture");
        Assert.Null(o.Size);
        Assert.Null(o.Clipboard);
        Assert.Null(o.Format);
    }

    [Fact]
    public void HelpVersionPaths()
    {
        Assert.Equal(Command.Help, Ok("--help").Command);
        Assert.Equal(Command.Help, Ok("help").Command);
        Assert.Equal(Command.Version, Ok("--version").Command);
        Assert.Equal(Command.Paths, Ok("paths", "--verbose").Command);
        Assert.True(Ok("--wslg").Wslg);
    }

    [Theory]
    [InlineData("snap")]
    [InlineData("capture", "--bogus")]
    [InlineData("capture", "--size")]
    [InlineData("capture", "--size", "huge")]
    [InlineData("capture", "--delay", "-1")]
    [InlineData("capture", "--delay", "61")]
    [InlineData("capture", "--format", "bmp")]
    [InlineData("capture", "--name", "x/{n}")]
    [InlineData("capture", "--log")]
    [InlineData("capture", "--fixed", "2")]
    [InlineData("capture", "--last", "--fixed", "512")]
    [InlineData("--size", "1080")]
    public void BadInputGetsAMessage(params string[] args)
    {
        string e = Err(args);
        Assert.False(string.IsNullOrWhiteSpace(e));
    }

    [Fact]
    public void CaptureOptionWithoutCaptureCommandSaysHow()
    {
        Assert.Contains("squaresnap capture", Err("--out", "x"));
    }

    [Fact]
    public void HelpMentionsEveryOption()
    {
        foreach (string opt in new[] { "--size", "--out", "--name", "--format", "--clipboard", "--no-clipboard", "--delay", "--last", "--fixed", "--verbose", "--log", "--wslg" })
            Assert.Contains(opt, CommandLine.HelpText);
    }
}
