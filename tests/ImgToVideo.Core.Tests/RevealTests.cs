using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Manifest;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Overrides;
using ImgToVideo.Ffmpeg;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class RevealTests
{
    // 30 fps: cue 1 0-4s, cue 2 4-8s, cue 3 8-12s, cue 4 12-16s, cue 5 16-20s.
    private static List<SubtitleBlock> Subtitles() =>
    [
        new(1, 0.0, 4.0, "Item one."),
        new(2, 4.0, 8.0, "Item two."),
        new(3, 8.0, 12.0, "Item three."),
        new(4, 12.0, 16.0, "Another point."),
        new(5, 16.0, 20.0, "Closing."),
    ];

    private static readonly string Root = Path.Combine("proj", "images");

    private static List<ImageInfo> Images() =>
    [
        new(Path.Combine(Root, "S01_01_CMP_ST.png"),
            new ParsedImageName("S01_01_CMP_ST", 1, 1, MotionType.Static, false), 1920, 1080),
        new(Path.Combine(Root, "S01_02_SCN_ZI.png"),
            new ParsedImageName("S01_02_SCN_ZI", 1, 2, MotionType.ZoomIn, false), 1920, 1080),
    ];

    private static (VisualManifest? Manifest, List<ValidationIssue> Issues) Expand(string shots)
    {
        var issues = new List<ValidationIssue>();
        var document = ShotListParser.Parse("{\"shots\": [" + shots + "]}", issues);
        var manifest = ShotListExpander.Expand(
            document, Subtitles(), Images().Select(i => Path.GetFileName(i.FilePath)).ToList(),
            new TransitionOptions(), issues);
        return (manifest, issues);
    }

    private static (Timeline Timeline, List<ValidationIssue> Issues) Plan(VisualManifest manifest)
    {
        var options = new ProjectOptions();
        options.Motion.MotionDurationMs = 0;
        var result = ManifestPlanner.Plan(manifest, Images(), options, 600, new ProjectOverrides());
        Assert.NotNull(result.Timeline);
        return (result.Timeline!, result.Issues.ToList());
    }

    private const string ThreeItems =
        "{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": [1, 2, 3]}, " +
        "{\"cues\": \"4-5\", \"asset\": \"S01_02_SCN_ZI.png\"}";

    [Fact]
    public void Reveal_cues_become_absolute_start_times()
    {
        var (manifest, issues) = Expand(ThreeItems);

        Assert.NotNull(manifest);
        Assert.Equal([0L, 4000L, 8000L], manifest!.Timeline[0].RevealAtMs);
        Assert.Null(manifest.Timeline[1].RevealAtMs);
        Assert.DoesNotContain(issues, i => i.Severity != ValidationSeverity.Info);
    }

    [Fact]
    public void Reveal_object_form_is_accepted()
    {
        var (manifest, _) = Expand(
            "{\"cues\": \"1-2\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": {\"cues\": [1, 2]}}");

        Assert.Equal([0L, 4000L], manifest!.Timeline[0].RevealAtMs);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("[1, 2, 3, 4, 5]")]
    [InlineData("\"1,2\"")]
    [InlineData("[1, \"x\"]")]
    public void Unusable_reveal_warns_and_shows_the_whole_image(string reveal)
    {
        var (manifest, issues) = Expand(
            "{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": " + reveal + "}");

        Assert.Null(manifest!.Timeline[0].RevealAtMs);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_REVEAL_INVALID" &&
                                     i.Severity == ValidationSeverity.Warning);
    }

    [Theory]
    [InlineData("[1, 4]")]      // cue 4 is outside the shot's cues 1-3
    [InlineData("[2, 2]")]      // not increasing
    [InlineData("[3, 2]")]
    public void Reveal_cues_must_sit_inside_the_shot_and_increase(string reveal)
    {
        var (manifest, issues) = Expand(
            "{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": " + reveal + "}");

        Assert.Null(manifest!.Timeline[0].RevealAtMs);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_REVEAL_INVALID");
    }

    [Fact]
    public void Planner_turns_a_reveal_shot_into_consecutive_stills()
    {
        var (manifest, _) = Expand(ThreeItems);

        var (timeline, _) = Plan(manifest!);

        var clips = timeline.Scenes.SelectMany(s => s.Clips).ToList();
        Assert.Equal(4, clips.Count);   // 3 reveal steps + the next shot
        var steps = clips.Take(3).ToList();
        Assert.Equal(0, steps[0].StartFrame);
        Assert.Equal(120, steps[1].StartFrame);   // cue 2 at 4 s
        Assert.Equal(240, steps[2].StartFrame);   // cue 3 at 8 s
        Assert.Equal([120L, 120L, 120L], steps.Select(s => s.DurationFrames).ToArray());
        Assert.Equal(RevealPaths.StepPath(Images()[0].FilePath, 1, 3), steps[0].FilePath);
        Assert.Equal(RevealPaths.StepPath(Images()[0].FilePath, 2, 3), steps[1].FilePath);
        Assert.Equal(Images()[0].FilePath, steps[2].FilePath);   // last step = the whole image
        Assert.All(steps, s => Assert.Equal(MotionType.Static, s.Motion));
        Assert.Equal(600, clips.Sum(c => c.DurationFrames));

        // contiguous
        var cursor = 0L;
        foreach (var clip in clips)
        {
            Assert.Equal(cursor, clip.StartFrame);
            cursor += clip.DurationFrames;
        }
    }

    [Fact]
    public void Later_steps_fade_in_and_the_first_keeps_the_shots_own_transition()
    {
        var (manifest, _) = Expand(ThreeItems);

        var (timeline, _) = Plan(manifest!);

        var steps = timeline.Scenes.SelectMany(s => s.Clips).Take(3).ToList();
        Assert.Null(steps[0].Transition);
        Assert.Equal(TransitionKind.Crossfade, steps[1].Transition!.Kind);
        Assert.Equal(8, steps[1].Transition!.DurationFrames);   // 0.25 s at 30 fps rounds to 8
        Assert.Equal(TransitionKind.Crossfade, steps[2].Transition!.Kind);
    }

    [Fact]
    public void Zero_fade_makes_hard_cuts()
    {
        var (manifest, _) = Expand(ThreeItems);
        var options = new ProjectOptions();
        options.Motion.MotionDurationMs = 0;
        options.Transitions.RevealFadeSeconds = 0;

        var result = ManifestPlanner.Plan(manifest!, Images(), options, 600, new ProjectOverrides());

        var steps = result.Timeline!.Scenes.SelectMany(s => s.Clips).Take(3).ToList();
        Assert.All(steps, s => Assert.Null(s.Transition));
    }

    [Fact]
    public void Reveal_on_a_moving_shot_is_ignored_with_a_warning()
    {
        var (manifest, _) = Expand(
            "{\"cues\": \"1-3\", \"asset\": \"S01_02_SCN_ZI.png\", \"reveal\": [1, 2, 3]}");

        var (timeline, issues) = Plan(manifest!);

        Assert.Single(timeline.Scenes.SelectMany(s => s.Clips));
        Assert.Contains(issues, i => i.Code == "REVEAL_NEEDS_STATIC");
    }

    [Fact]
    public void Steps_too_close_together_are_skipped_but_the_slice_layout_is_kept()
    {
        // item 2 at cue 2 (4 s) and item 3 at cue 3 (8 s), but the shot ends at 8.05 s
        var subtitles = new List<SubtitleBlock>
        {
            new(1, 0.0, 4.0, "a"), new(2, 4.0, 8.0, "b"), new(3, 8.0, 8.05, "c"), new(4, 8.05, 12.0, "d"),
        };
        var issues = new List<ValidationIssue>();
        var document = ShotListParser.Parse(
            "{\"shots\": [{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": [1, 2, 3]}, " +
            "{\"cues\": \"4\", \"asset\": \"S01_02_SCN_ZI.png\"}]}", issues);
        var manifest = ShotListExpander.Expand(
            document, subtitles, Images().Select(i => Path.GetFileName(i.FilePath)).ToList(),
            new TransitionOptions(), issues);
        var options = new ProjectOptions();
        options.Motion.MotionDurationMs = 0;

        var result = ManifestPlanner.Plan(manifest!, Images(), options, 360, new ProjectOverrides());

        var clips = result.Timeline!.Scenes.SelectMany(s => s.Clips).ToList();
        Assert.Contains(result.Issues, i => i.Code == "REVEAL_STEP_SKIPPED");
        // the last kept step shows the whole image
        Assert.Equal(Images()[0].FilePath, clips[1].FilePath);
    }

    [Fact]
    public void Step_files_are_named_next_to_the_images_folder()
    {
        var path = RevealPaths.StepPath(Path.Combine("proj", "images", "S02_03_CMP_ST.png"), 2, 3);

        Assert.EndsWith(Path.Combine("proj", "out", "reveal", "S02_03_CMP_ST_reveal2of3.png"), path);
        Assert.Equal(Path.Combine("proj", "images", "S02_03_CMP_ST.png"),
            RevealPaths.StepPath(Path.Combine("proj", "images", "S02_03_CMP_ST.png"), 3, 3));
    }

    [Fact]
    public void Ffmpeg_arguments_keep_the_left_slices_and_pad_to_full_size()
    {
        var args = RevealImageWriter.BuildArguments("in.png", 2880, 1080, 2, 3, "out.png");

        var filter = args[args.ToList().IndexOf("-vf") + 1];
        Assert.Equal("crop=1920:1080:0:0,pad=2880:1080:0:0:color=black", filter);
    }
}
