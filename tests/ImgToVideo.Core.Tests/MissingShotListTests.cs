using System.Text.Json;
using ImgToVideo.Core.Manifest;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class MissingShotListTests
{
    private const string ShotListJson = """
        {
          "style": "Master prompt.",
          "shots": [
            { "cues": "1-3", "asset": "S01_01_SCN_ZI.png", "scene": "S01" }
          ],
          "refs": {
            "maya": "D:/Refs/maya.png",
            "room": "D:/Refs/room.png"
          },
          "images": [
            { "file": "S01_01_SCN_ZI.png", "prompt": "on disk already" },
            { "file": "S01_02_CU_ZO.png", "prompt": "missing one", "refs": ["maya"] },
            { "file": "S02_01_INF_ST.png", "prompt": "missing two" }
          ]
        }
        """;

    [Fact]
    public void BuildFromJson_KeepsOnlyMissingImages()
    {
        var result = MissingShotList.BuildFromJson(ShotListJson, ["S01_01_SCN_ZI.png"]);

        Assert.Equal(2, result.Count);
        using var doc = JsonDocument.Parse(result.Json);
        var files = doc.RootElement.GetProperty("images").EnumerateArray()
            .Select(i => i.GetProperty("file").GetString()!).ToArray();
        Assert.Equal(["S01_02_CU_ZO.png", "S02_01_INF_ST.png"], files);
    }

    [Fact]
    public void BuildFromJson_PreservesStyleRefsAndPerImageRefs()
    {
        var result = MissingShotList.BuildFromJson(ShotListJson, ["S01_01_SCN_ZI.png"]);

        using var doc = JsonDocument.Parse(result.Json);
        var root = doc.RootElement;
        Assert.Equal("Master prompt.", root.GetProperty("style").GetString());
        Assert.True(root.GetProperty("refs").TryGetProperty("maya", out _));
        Assert.False(root.TryGetProperty("shots", out _));

        var first = root.GetProperty("images").EnumerateArray().First();
        Assert.Equal("maya", first.GetProperty("refs")[0].GetString());
    }

    [Fact]
    public void BuildFromJson_MatchesFileNamesCaseInsensitively()
    {
        var result = MissingShotList.BuildFromJson(ShotListJson, ["s01_01_scn_zi.PNG"]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void BuildFromJson_ReturnsZeroWhenEveryImageIsPresent()
    {
        var result = MissingShotList.BuildFromJson(
            ShotListJson,
            ["S01_01_SCN_ZI.png", "S01_02_CU_ZO.png", "S02_01_INF_ST.png"]);

        Assert.Equal(0, result.Count);
        using var doc = JsonDocument.Parse(result.Json);
        Assert.Empty(doc.RootElement.GetProperty("images").EnumerateArray());
    }

    [Fact]
    public void BuildFromJson_SkipsEntriesWithoutAUsableFile()
    {
        const string json = """
            { "images": [ { "prompt": "no file" }, { "file": "  " }, { "file": "S01_01.png" } ] }
            """;

        var result = MissingShotList.BuildFromJson(json, []);

        Assert.Equal(1, result.Count);
        using var doc = JsonDocument.Parse(result.Json);
        Assert.Equal(
            "S01_01.png",
            doc.RootElement.GetProperty("images")[0].GetProperty("file").GetString());
    }

    [Fact]
    public void BuildFromJson_PlainTextListsMasterPromptThenFilePromptLines()
    {
        var result = MissingShotList.BuildFromJson(ShotListJson, ["S01_01_SCN_ZI.png"]);

        var lines = result.PlainText.Split(Environment.NewLine);
        Assert.Equal("Master prompt: Master prompt.", lines[0]);
        Assert.Equal(string.Empty, lines[1]);
        Assert.Equal("\"file\": \"S01_02_CU_ZO.png\", \"prompt\": \"missing one\"", lines[2]);
        Assert.Equal("\"file\": \"S02_01_INF_ST.png\", \"prompt\": \"missing two\"", lines[3]);
    }

    [Fact]
    public void BuildFromJson_PlainTextOmitsMasterPromptWhenStyleIsMissing()
    {
        const string json = """
            { "images": [ { "file": "S09_01_SCN_ST.png", "prompt": "a prompt" } ] }
            """;

        var result = MissingShotList.BuildFromJson(json, []);

        Assert.Equal("\"file\": \"S09_01_SCN_ST.png\", \"prompt\": \"a prompt\"", result.PlainText);
    }

    [Fact]
    public void BuildForProject_ReturnsNullWithoutAShotList()
    {
        using var project = new TempProject();

        Assert.Null(MissingShotList.BuildForProject(project.Path));
    }

    [Fact]
    public void BuildForProject_ComparesAgainstTheImagesFolder()
    {
        using var project = new TempProject();
        project.WriteFile("shotlist.json", ShotListJson);
        project.WriteImage("S01_01_SCN_ZI.png", 4, 4);

        var result = MissingShotList.BuildForProject(project.Path);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
    }
}
