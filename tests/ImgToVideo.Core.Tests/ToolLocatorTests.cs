using ImgToVideo.Ffmpeg;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ToolLocatorTests
{
    [Fact]
    public void Rooted_existing_path_is_returned_as_is()
    {
        var existing = Path.Combine(Path.GetTempPath(), "toollocator-tests");
        Directory.CreateDirectory(existing);

        Assert.Equal(existing, ToolLocator.Resolve("ffmpeg", existing));
    }

    [Fact]
    public void Rooted_missing_path_is_returned_as_is()
    {
        var missing = @"Z:\nowhere\ffmpeg.exe";

        Assert.Equal(missing, ToolLocator.Resolve("ffmpeg", missing));
    }

    [Fact]
    public void Resolves_windows_built_in_tools_from_path()
    {
        var resolved = ToolLocator.Resolve("cmd", "cmd");

        Assert.EndsWith("cmd.exe", resolved, StringComparison.OrdinalIgnoreCase);
    }
}
