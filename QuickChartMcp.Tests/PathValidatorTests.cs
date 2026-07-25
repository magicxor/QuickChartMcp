using QuickChartMcp.Validation;
using Xunit;

namespace QuickChartMcp.Tests;

public class PathValidatorTests
{
    [Theory]
    [InlineData("report")]
    [InlineData("report.md")]
    [InlineData("a.txt")]
    [InlineData("no-extension")]
    [InlineData("naïve.md")]            // non-ASCII letters are fine
    [InlineData("my report.md")]        // an interior space is fine
    public void ValidateFileName_AcceptsPortableNames(string fileName)
    {
        PathValidator.ValidateFileName(fileName); // does not throw
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]                 // all dots
    [InlineData("sub/report.md")]       // POSIX separator
    [InlineData("sub\\report.md")]      // Windows separator (rejected even when running on Linux)
    [InlineData("bad|name.txt")]        // pipe
    [InlineData("a<b.txt")]
    [InlineData("a>b.txt")]
    [InlineData("a:b.txt")]             // colon (also alternate-data-stream on Windows)
    [InlineData("a\"b.txt")]
    [InlineData("a?b.txt")]
    [InlineData("a*b.txt")]
    [InlineData("tab\tname.txt")]       // control character
    [InlineData("CON")]                 // reserved Windows device name
    [InlineData("con.md")]              // reserved, case-insensitive, with extension
    [InlineData("LPT1.log")]
    [InlineData("report.")]             // trailing dot (Windows strips it)
    [InlineData("report ")]             // trailing space (Windows strips it)
    public void ValidateFileName_RejectsUnsafeNames(string fileName)
    {
        Assert.Throws<PathValidationException>(() => PathValidator.ValidateFileName(fileName));
    }

    [Theory]
    [InlineData("/srv/out")]            // POSIX absolute
    [InlineData("/srv/out/")]           // single trailing separator is benign
    [InlineData("/")]                   // POSIX root
    [InlineData("C:\\data\\out")]       // Windows drive-absolute
    [InlineData("C:\\data\\out\\")]
    [InlineData("C:\\")]                // drive root
    [InlineData("\\\\server\\share\\folder")] // UNC
    public void ValidateDirectory_AcceptsAbsolutePaths(string path)
    {
        PathValidator.ValidateDirectory(path, "outputDirectory"); // does not throw
    }

    [Theory]
    [InlineData("relative/path")]       // not rooted
    [InlineData("relative\\path")]      // not rooted
    [InlineData("out")]                 // not rooted
    [InlineData("C:")]                  // drive-relative ("C:" != "C:\")
    [InlineData("C:data\\out")]         // drive-relative
    [InlineData("\\data\\out")]         // single leading '\' is drive-relative on Windows
    [InlineData("/data/../out")]        // ".." segment
    [InlineData("C:\\data\\..\\out")]   // ".." segment
    [InlineData("/data/./out")]         // "." segment
    [InlineData("/data/.../out")]       // all-dots segment
    [InlineData("/data//out")]          // empty segment (double separator)
    [InlineData("/data/out//")]         // doubled trailing separator (empty segment)
    [InlineData("/data/ou<t")]          // invalid character in segment
    [InlineData("C:\\data\\ou|t")]      // invalid character in segment
    public void ValidateDirectory_RejectsInvalidPaths(string path)
    {
        Assert.Throws<PathValidationException>(() => PathValidator.ValidateDirectory(path, "outputDirectory"));
    }

    [Fact]
    public void ValidateDirectory_RejectsEmpty()
    {
        Assert.Throws<PathValidationException>(() => PathValidator.ValidateDirectory("", "outputDirectory"));
    }
}
