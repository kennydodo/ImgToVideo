using ImgToVideo.Core.Models;
using ImgToVideo.Core.Reporting;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class DiagnosticsGrouperTests
{
    private static ValidationIssue Error(string code, string message) =>
        new(ValidationSeverity.Error, code, message);

    private static ValidationIssue Warning(string code, string message) =>
        new(ValidationSeverity.Warning, code, message);

    private static ValidationIssue Info(string code, string message) =>
        new(ValidationSeverity.Info, code, message);

    [Fact]
    public void Group_BucketsIssuesByCategoryPrefix()
    {
        var groups = DiagnosticsGrouper.Group(
        [
            Error("IMAGES_MISSING", "no images"),
            Warning("IMAGE_UNREADABLE", "bad png"),
            Error("AUDIO_MISSING", "no audio"),
            Error("SRT_MISSING", "no srt"),
        ]);

        Assert.Equal(4, groups.Count);
        Assert.Single(groups, g => g.Title == DiagnosticsGrouper.MissingImagesTitle);
        Assert.Single(groups, g => g.Title == "Images");
        Assert.Single(groups, g => g.Title == "Audio");
        Assert.Single(groups, g => g.Title == "Narration / SRT");
    }

    [Fact]
    public void Group_PlannedButUngeneratedImagesLandInMissingImages()
    {
        var groups = DiagnosticsGrouper.Group(
        [
            Info("SHOTLIST_GENERATE_AHEAD", "S17_04_INF_ZI.png is not in images\\ yet"),
            Info("SHOTLIST_ASSET_TYPO", "looks like a typo"),
        ]);

        var missing = Assert.Single(groups, g => g.Title == DiagnosticsGrouper.MissingImagesTitle);
        Assert.Equal("S17_04_INF_ZI.png is not in images\\ yet", Assert.Single(missing.Issues).Message);
        Assert.Single(groups, g => g.Title == "Shot list");
    }

    [Fact]
    public void Group_OrdersGroupsWithErrorsFirst()
    {
        var groups = DiagnosticsGrouper.Group(
        [
            Info("SHOTLIST_ASSET_TYPO", "typo"),
            Warning("TIMING_HOLD", "hold"),
            Error("IMAGES_MISSING", "no images"),
        ]);

        Assert.Equal(DiagnosticsGrouper.MissingImagesTitle, groups[0].Title);
        Assert.DoesNotContain(groups, g => g.Title == "Shot list" && g.HasErrors);
    }

    [Fact]
    public void Group_OrdersIssuesBySeverityThenOriginalOrder()
    {
        var groups = DiagnosticsGrouper.Group(
        [
            Info("IMAGE_EXCLUDED", "first info"),
            Warning("IMAGE_UNREADABLE", "first warning"),
            Error("IMAGE_UNKNOWN_CODE", "first error"),
            Info("IMAGE_EXCLUDED", "second info"),
        ]);

        var images = Assert.Single(groups);
        Assert.Equal(
            ["first error", "first warning", "first info", "second info"],
            images.Issues.Select(i => i.Message).ToArray());
    }

    [Fact]
    public void Group_SummarizesSeverityCounts()
    {
        var groups = DiagnosticsGrouper.Group(
        [
            Error("IMAGES_MISSING", "a"),
            Error("IMAGES_EMPTY", "b"),
            Warning("IMAGE_UNREADABLE", "c"),
        ]);

        var missing = Assert.Single(groups, g => g.Title == DiagnosticsGrouper.MissingImagesTitle);
        Assert.Equal(2, missing.ErrorCount);
        Assert.Equal(0, missing.WarningCount);
        Assert.True(missing.HasErrors);
        Assert.Equal("2 errors", missing.Summary());
    }

    [Fact]
    public void Group_UsesSingularNounsAndOmitsEmptySeverities()
    {
        var groups = DiagnosticsGrouper.Group([Warning("AUDIO_AMBIGUOUS", "ambiguous")]);

        var audio = Assert.Single(groups);
        Assert.Equal("1 warning", audio.Summary());
    }

    [Fact]
    public void Group_FallsBackToOtherForUnknownCode()
    {
        var groups = DiagnosticsGrouper.Group([Error("SOMETHING_NEW", "mystery")]);

        var other = Assert.Single(groups);
        Assert.Equal("Other", other.Title);
    }

    [Fact]
    public void Group_IsEmptyForNoIssues()
    {
        Assert.Empty(DiagnosticsGrouper.Group([]));
    }

    [Fact]
    public void Group_IsDeterministic()
    {
        ValidationIssue[] issues =
        [
            Error("IMAGES_MISSING", "a"),
            Warning("SRT_MISSING", "b"),
            Info("MOTION_FROM_FILENAME", "c"),
            Error("AUDIO_MISSING", "d"),
        ];

        var first = DiagnosticsGrouper.Group(issues);
        var second = DiagnosticsGrouper.Group(issues);

        Assert.Equal(
            first.Select(g => (g.Title, g.Summary())),
            second.Select(g => (g.Title, g.Summary())));
    }
}
