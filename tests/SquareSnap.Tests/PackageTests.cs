using SquareSnap.Core.Platform;

namespace SquareSnap.Tests;

/// <summary>
/// The Microsoft Store package (step 6, BejView's tests): when the package keeps a private copy of %APPDATA%\SquareSnap, paths and
/// Help > Open the log file show the private copy, because that's where Explorer and Notepad find the files.
/// </summary>
public class PackageTests
{
    private const string Family = "RichardBejtlich.SquareSnap_kz4qzpv342p6y";
    private const string Copy = @"C:\Users\alex\AppData\Local\Packages\" + Family + @"\LocalCache\Roaming\SquareSnap\squaresnap.log";
    private const string Log = @"C:\Users\alex\AppData\Roaming\SquareSnap\squaresnap.log";

    private static string? Env(string name) => name switch
    {
        "APPDATA" => @"C:\Users\alex\AppData\Roaming",
        "LOCALAPPDATA" => @"C:\Users\alex\AppData\Local\",
        _ => null,
    };

    [Fact]
    public void Packaged_with_a_private_copy_shows_the_copy()
        => Assert.Equal(Copy, AppPaths.RealLocation(Log, Family, Env, p => p == Copy));

    [Fact]
    public void Packaged_without_a_private_copy_keeps_the_real_path()
        => Assert.Equal(Log, AppPaths.RealLocation(Log, Family, Env, _ => false)); // redirection turned off

    [Fact]
    public void Not_packaged_keeps_the_path()
        => Assert.Equal(Log, AppPaths.RealLocation(Log, null, Env, _ => true)); // zip, WinGet, Linux

    [Fact]
    public void A_log_outside_AppData_is_never_moved()
    {
        const string mine = @"D:\Logs\squaresnap.log";
        Assert.Equal(mine, AppPaths.RealLocation(mine, Family, Env, _ => true));
    }

    [Fact]
    public void A_folder_whose_name_only_starts_like_AppData_is_never_moved()
    {
        const string other = @"C:\Users\alex\AppData\RoamingX\SquareSnap\squaresnap.log";
        Assert.Equal(other, AppPaths.RealLocation(other, Family, Env, _ => true));
    }

    [Fact]
    public void Matching_ignores_case_like_Windows()
    {
        const string upper = @"C:\USERS\ALEX\APPDATA\ROAMING\SquareSnap\squaresnap.log";
        Assert.Equal(@"C:\Users\alex\AppData\Local\Packages\" + Family + @"\LocalCache\Roaming\SquareSnap\squaresnap.log",
            AppPaths.RealLocation(upper, Family, Env, _ => true));
    }

    [Fact]
    public void The_test_process_is_not_packaged()
        => Assert.Null(PackageIdentity.FamilyName);
}
