using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Manifest;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Overrides;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ManifestPlannerTests
{
    private static VisualManifest SampleManifest(long shot4EndMs = 13480) => new(
        "1.0",
        new VisualManifestVideo("interval-walking", "Interval Walking", 530000, 30),
        [
            new VisualAsset("IMG001", "S01_B01_01.png", "S01", ["B01"], "closeup",
                ["ZI", "ZO", "STATIC"], false, null),
            new VisualAsset("IMG002", "S01_B02_01.png", "S01", ["B02"], "lifestyle",
                ["ZI", "ZO", "PL", "PR", "STATIC"], false, null),
            new VisualAsset("IMG003", "S01_B03_01_INFO.png", "S01", ["B03", "B04"], "comparison",
                ["STATIC", "ZI"], true,
                [
                    new FocalRegion("left", 0.0, 0.0, 0.5, 1.0),
                    new FocalRegion("right", 0.5, 0.0, 0.5, 1.0),
                ]),
        ],
        [
            new VisualShot("SH001", 0, 3240, "S01", "B01", [1, 2],
                "Most people think walking is simply about getting more steps.",
                "hook", "opening_visual", "IMG001",
                new VisualFraming("close", null),
                new VisualMotion("ZI", 1.0, 1.1, null),
                new VisualTransition("CUT", 0)),
            new VisualShot("SH002", 3240, 6810, "S01", "B02", [2, 3],
                "But there may be another part of walking that matters.",
                "new_point", "new_claim", "IMG002",
                new VisualFraming("wide", null),
                new VisualMotion("PR", null, null, null),
                new VisualTransition("CUT", 0)),
            new VisualShot("SH003", 6810, 10150, "S01", "B03", [4],
                "Researchers compared easier walking with periods of faster walking.",
                "comparison", "new_comparison", "IMG003",
                new VisualFraming("crop", "left"),
                new VisualMotion("STATIC", null, null, null),
                new VisualTransition("CUT", 0)),
            new VisualShot("SH004", 10150, shot4EndMs, "S01", "B04", [4, 5],
                "The faster intervals created a very different pattern of effort.",
                "detail", "same_topic_new_focus", "IMG003",
                new VisualFraming("crop", "right"),
                new VisualMotion("ZI", 1.0, 1.08, null),
                new VisualTransition("CROSSFADE", 180)),
        ]);

    private static List<ImageInfo> Images()
    {
        var list = new List<ImageInfo>();
        foreach (var file in new[] { "S01_B01_01.png", "S01_B02_01.png", "S01_B03_01_INFO.png" })
        {
            list.Add(new ImageInfo(
                Path.Combine("images", file),
                new ParsedImageName(file, 1, 0, null, false),
                1920,
                1080));
        }

        return list;
    }

    private static ProjectOptions Options()
    {
        var options = new ProjectOptions();
        options.Motion.MotionDurationMs = 0; // full-clip motion unless the shot overrides it
        return options;
    }

    private static (Timeline Timeline, ManifestCoverage Coverage, List<ValidationIssue> Issues) Plan(
        VisualManifest? manifest = null, long audioFrames = 405,
        ProjectOverrides? overrides = null, long shot4EndMs = 13480)
    {
        var options = Options();
        var result = ManifestPlanner.Plan(
            manifest ?? SampleManifest(shot4EndMs), Images(), options, audioFrames,
            overrides ?? new ProjectOverrides());
        Assert.NotNull(result.Timeline);
        return (result.Timeline!, result.Coverage!, result.Issues.ToList());
    }

    [Fact]
    public void Plans_shots_into_one_scene_with_manifest_durations()
    {
        var (timeline, coverage, issues) = Plan();

        var scene = Assert.Single(timeline.Scenes);
        Assert.Equal(4, scene.Clips.Count);
        Assert.Equal(97, scene.Clips[0].DurationFrames);   // 3240ms @ 30fps
        Assert.Equal(107, scene.Clips[1].DurationFrames);  // 3570ms
        Assert.Equal(100, scene.Clips[2].DurationFrames);  // 3330ms
        Assert.Equal(101, scene.Clips[3].DurationFrames);  // 3330ms + tail fix to 405
        Assert.Equal(405, scene.Clips.Sum(c => c.DurationFrames));
        Assert.All(scene.Clips, c => Assert.Equal("S01", c.SceneId));
        Assert.NotNull(scene.Clips[0].ShotId);
        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
        Assert.Equal(3, coverage.UniqueAssetsUsed);
        Assert.Equal(3, coverage.AssetCount);
    }

    [Fact]
    public void Maps_scales_and_framing_to_viewports()
    {
        var (timeline, _, _) = Plan();
        var clips = timeline.Scenes[0].Clips;

        // SH001: close framing (70% band) with ZI 1.0 -> 1.1.
        var sh1 = clips[0];
        Assert.Equal(1080 * 0.70, sh1.StartViewport.Height, 1);
        Assert.True(sh1.EndViewport.Height < sh1.StartViewport.Height);

        // SH003: crop left half of a 16:9 image — re-framed to a 16:9 window inside it.
        var sh3 = clips[2];
        Assert.Equal(MotionType.Static, sh3.Motion);
        Assert.Equal(960, sh3.StartViewport.Width, 1);
        Assert.Equal(960 / (16.0 / 9.0), sh3.StartViewport.Height, 1);
        Assert.Contains(sh3.StartViewport.Width, new[] { 960.0 });
    }

    [Fact]
    public void Scene_editor_overrides_win_over_shotlist_and_settings()
    {
        var manifest = SampleManifest();
        var overrides = new ProjectOverrides();
        overrides.Clips.Add(new ClipOverride
        {
            File = "images/S01_B01_01.png",
            Shot = "SH001",
            Motion = MotionType.PanRight,
            Easing = EasingMode.EaseIn,
        });
        overrides.Cuts.Add(new CutOverride
        {
            BeforeFile = Path.Combine("images", "S01_B03_01_INFO.png"),
            Transition = TransitionKind.FadeBlack,
        });

        var (timeline, _, _) = Plan(manifest, overrides: overrides);
        var clips = timeline.Scenes[0].Clips;

        // SH001 shotlist says ZI; the editor override says PanRight and wins,
        // along with the editor easing over the project easing.
        Assert.Equal(MotionType.PanRight, clips[0].Motion);
        Assert.Equal(EasingMode.EaseIn, clips[0].Easing);

        // SH003 declares transition_out CROSSFADE, but the editor cut override
        // on the incoming clip wins.
        Assert.Equal(TransitionKind.FadeBlack, clips[3].Transition!.Kind);
    }

    [Fact]
    public void Zoom_without_explicit_scales_uses_motion_settings()
    {
        // Settings -> Motion -> push-in percents drive the zoom range when the
        // shotlist does not provide start_scale/end_scale.
        var manifest = SampleManifest();
        var shots = manifest.Timeline.ToList();
        shots[0] = shots[0] with { Motion = new VisualMotion("ZI", null, null, null) };
        var (timeline, _, _) = Plan(manifest with { Timeline = shots });
        var sh1 = timeline.Scenes[0].Clips[0];

        // Close framing = 70% band (756 px of 1080); push-in defaults 100% -> 106%.
        Assert.Equal(1080 * 0.70, sh1.StartViewport.Height, 1);
        Assert.Equal(1080 * 0.70 / 1.06, sh1.EndViewport.Height, 1);
    }

    [Fact]
    public void Applies_transitions_from_previous_shot()
    {
        // transition_out on shot N drives the join between N and N+1; a non-CUT
        // transition_out on the LAST shot has no "next" join and is ignored.
        // Joins without an explicit transition fall back to the project's
        // transition settings (within-scene kind for same-scene joins).
        var manifest = SampleManifest();
        var shots = manifest.Timeline.ToList();
        shots[2] = shots[2] with { TransitionOut = new VisualTransition("CROSSFADE", 180) };
        var (timeline, _, _) = Plan(manifest with { Timeline = shots });
        var clips = timeline.Scenes[0].Clips;

        Assert.Null(clips[0].Transition); // first clip: no incoming join
        Assert.Equal(TransitionKind.Crossfade, clips[1].Transition!.Kind); // settings fallback
        Assert.Equal(15, clips[1].Transition!.DurationFrames); // 0.5 s @ 30 fps
        Assert.Equal(TransitionKind.Crossfade, clips[2].Transition!.Kind); // settings fallback
        Assert.NotNull(clips[3].Transition); // SH003 CROSSFADE 180ms -> 5-6 frames
        Assert.Equal(TransitionKind.Crossfade, clips[3].Transition!.Kind);
        Assert.Equal(5, clips[3].Transition.DurationFrames); // 180ms @ 30fps = 5.4 -> 5
    }

    [Fact]
    public void Pans_delegate_to_motion_engine()
    {
        var (timeline, _, _) = Plan();
        var sh2 = timeline.Scenes[0].Clips[1];

        Assert.Equal(MotionType.PanRight, sh2.Motion);
        Assert.Equal(MotionSource.Override, sh2.MotionSource);
        Assert.True(sh2.EndViewport.X > sh2.StartViewport.X);
    }

    [Fact]
    public void Missing_asset_produces_generation_hint_and_time_is_absorbed()
    {
        var images = Images().Where(i => !i.FilePath.EndsWith("S01_B02_01.png")).ToList();
        var options = Options();
        var result = ManifestPlanner.Plan(SampleManifest(), images, options, 405, new ProjectOverrides());

        Assert.NotNull(result.Timeline);
        var hint = Assert.Single(result.Coverage!.MissingAssets);
        Assert.Contains("S01_B02_01.png", hint);
        Assert.Contains("generate it", hint);
        Assert.Contains("But there may be another part of walking", hint);
        Assert.Contains(result.Issues, i => i.Severity == ValidationSeverity.Warning);
        Assert.Equal(3, result.Timeline.Scenes[0].Clips.Count);
        Assert.Equal(405, result.Timeline.Scenes[0].Clips.Sum(c => c.DurationFrames));
        // The dropped shot's 3570ms is absorbed by the previous shot (SH001: 0→6810ms).
        Assert.Equal(204, result.Timeline.Scenes[0].Clips[0].DurationFrames);
        Assert.Equal(100, result.Timeline.Scenes[0].Clips[1].DurationFrames);
    }

    [Fact]
    public void Unsafe_motion_warns()
    {
        var manifest = SampleManifest();
        var shots = manifest.Timeline.ToList();
        shots[1] = shots[1] with { Motion = new VisualMotion("PV", null, null, null) };
        var broken = manifest with { Timeline = shots };

        var (_, _, issues) = Plan(broken);

        Assert.Contains(issues, i => i.Code == "MANIFEST_UNSAFE_MOTION");
    }

    [Fact]
    public void Motion_duration_cap_is_set_from_options()
    {
        var options = Options();
        options.Motion.MotionDurationMs = 2000; // 60 frames < shot length
        var result = ManifestPlanner.Plan(SampleManifest(), Images(), options, 405, new ProjectOverrides());

        var clips = result.Timeline!.Scenes[0].Clips;
        Assert.Equal(60, clips[0].MotionDurationFrames);
        Assert.Null(clips[2].MotionDurationFrames); // STATIC never caps
    }

    [Fact]
    public void Per_shot_duration_overrides_apply_and_bad_sums_are_ignored()
    {
        // Nudge-style transfer: SH003 +10 frames, SH004 -10 — sum preserved.
        var overrides = new ProjectOverrides();
        overrides.Clips.Add(new ClipOverride { Shot = "SH003", File = "images/S01_B03_01_INFO.png", DurationFrames = 110 });
        overrides.Clips.Add(new ClipOverride { Shot = "SH004", File = "images/S01_B03_01_INFO.png", DurationFrames = 91 });

        var (timeline, _, issues) = Plan(overrides: overrides);

        var clips = timeline.Scenes[0].Clips;
        Assert.Equal(110, clips[2].DurationFrames);
        Assert.DoesNotContain(issues, i => i.Code == "MANIFEST_OVERRIDE_IGNORED");

        // Broken sum: only one shot edited.
        var brokenOverrides = new ProjectOverrides();
        brokenOverrides.Clips.Add(new ClipOverride { Shot = "SH003", File = "images/S01_B03_01_INFO.png", DurationFrames = 200 });
        var (brokenTimeline, _, brokenIssues) = Plan(overrides: brokenOverrides);

        Assert.Contains(brokenIssues, i => i.Code == "MANIFEST_OVERRIDE_IGNORED");
        Assert.Equal(100, brokenTimeline.Scenes[0].Clips[2].DurationFrames); // reverted to manifest value
    }

    [Fact]
    public void Excluded_shot_is_dropped_and_time_absorbed()
    {
        var overrides = new ProjectOverrides();
        overrides.Clips.Add(new ClipOverride { Shot = "SH002", File = "images/S01_B02_01.png", Exclude = true });

        var (timeline, _, issues) = Plan(overrides: overrides);

        Assert.Contains(issues, i => i.Code == "SHOT_EXCLUDED");
        var clips = timeline.Scenes[0].Clips;
        Assert.Equal(3, clips.Count);
        Assert.Equal(405, clips.Sum(c => c.DurationFrames));
        // SH003 absorbs SH002's 3570ms: SH001 covers 0→6810ms (204 frames).
        Assert.Equal(204, clips[0].DurationFrames);
    }

    [Fact]
    public void Timeline_tail_is_pinned_to_audio_duration()
    {
        // Last shot ends at 14.5s but audio is exactly 13.48s -> trimmed with warning.
        var (timeline, _, issues) = Plan(shot4EndMs: 14500);

        Assert.Contains(issues, i => i.Code == "MANIFEST_TIMING_FIXED");
        Assert.Equal(405, timeline.Scenes[0].Clips.Sum(c => c.DurationFrames));
        Assert.Equal(101, timeline.Scenes[0].Clips[3].DurationFrames);
    }
}
