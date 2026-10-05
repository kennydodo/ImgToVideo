using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Manifest;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Overrides;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ImgToVideo.CapCut;
using ImgToVideo.Ffmpeg;
using ImgToVideo.Premiere;
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

    // ---- 2x2 grid ----

    private const string FourGrid =
        "{\"cues\": \"1-4\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": {\"cues\": [1, 2, 3, 4], \"layout\": \"grid\"}}, " +
        "{\"cues\": \"5\", \"asset\": \"S01_02_SCN_ZI.png\"}";

    [Fact]
    public void Grid_reveal_needs_exactly_four_items()
    {
        var (manifest, issues) = Expand(
            "{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": {\"cues\": [1, 2, 3], \"layout\": \"grid\"}}");

        Assert.Null(manifest!.Timeline[0].RevealAtMs);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_REVEAL_INVALID");
    }

    [Fact]
    public void Unknown_layout_is_rejected()
    {
        var (manifest, issues) = Expand(
            "{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": {\"cues\": [1, 2, 3], \"layout\": \"circle\"}}");

        Assert.Null(manifest!.Timeline[0].RevealAtMs);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_REVEAL_INVALID");
    }

    [Fact]
    public void Grid_reveal_expands_into_four_stills_in_reading_order()
    {
        var (manifest, _) = Expand(FourGrid);
        Assert.Equal("grid", manifest!.Timeline[0].RevealLayout);

        var (timeline, _) = Plan(manifest);

        var steps = timeline.Scenes.SelectMany(s => s.Clips).Take(4).ToList();
        Assert.Equal([0L, 120L, 240L, 360L], steps.Select(c => c.StartFrame).ToArray());
        Assert.Equal([1, 2, 3, 4], steps.Select(c => c.Reveal!.Visible).ToArray());
        Assert.All(steps, c => Assert.Equal("grid", c.Reveal!.Layout));
        Assert.EndsWith("S01_01_CMP_ST_grid1of4.png", steps[0].FilePath);
        Assert.Equal(Images()[0].FilePath, steps[3].FilePath);
    }

    [Fact]
    public void Grid_ffmpeg_arguments_black_out_the_quadrants_not_yet_shown()
    {
        var one = RevealImageWriter.BuildGridArguments("in.png", 1920, 1080, 1, "o.png");
        var filter = one[one.ToList().IndexOf("-vf") + 1];
        Assert.Equal(
            "drawbox=x=960:y=0:w=960:h=540:color=black:t=fill," +
            "drawbox=x=0:y=540:w=960:h=540:color=black:t=fill," +
            "drawbox=x=960:y=540:w=960:h=540:color=black:t=fill", filter);

        var three = RevealImageWriter.BuildGridArguments("in.png", 1920, 1080, 3, "o.png");
        Assert.Equal("drawbox=x=960:y=540:w=960:h=540:color=black:t=fill",
            three[three.ToList().IndexOf("-vf") + 1]);
    }

    // ---- separate images, one after another ----

    private static List<ImageInfo> StackImages() =>
    [
        .. Images(),
        new(Path.Combine(Root, "S02_01.png"), new ParsedImageName("S02_01", 2, 1, null, false), 1920, 1080),
        new(Path.Combine(Root, "S02_02.png"), new ParsedImageName("S02_02", 2, 2, null, false), 1920, 1080),
        new(Path.Combine(Root, "S02_03.png"), new ParsedImageName("S02_03", 2, 3, null, false), 1920, 1080),
    ];

    private static (VisualManifest? Manifest, List<ValidationIssue> Issues) ExpandStack(string shots)
    {
        var issues = new List<ValidationIssue>();
        var document = ShotListParser.Parse("{\"shots\": [" + shots + "]}", issues);
        var manifest = ShotListExpander.Expand(
            document, Subtitles(), StackImages().Select(i => Path.GetFileName(i.FilePath)).ToList(),
            new TransitionOptions(), issues);
        return (manifest, issues);
    }

    private static ManifestPlanResult PlanStack(VisualManifest manifest, long audioFrames = 600)
    {
        var options = new ProjectOptions();
        options.Motion.MotionDurationMs = 0;
        return ManifestPlanner.Plan(manifest, StackImages(), options, audioFrames, new ProjectOverrides());
    }

    private const string ThreeImages =
        "{\"cues\": \"1-3\", \"asset\": \"S02_01.png\", \"reveal\": " +
        "{\"cues\": [1, 2, 3], \"assets\": [\"S02_01.png\", \"S02_02.png\", \"S02_03.png\"]}}, " +
        "{\"cues\": \"4-5\", \"asset\": \"S01_02_SCN_ZI.png\"}";

    [Fact]
    public void Separate_images_build_up_one_composite_per_step()
    {
        var (manifest, issues) = ExpandStack(ThreeImages);
        Assert.NotNull(manifest);
        Assert.DoesNotContain(issues, i => i.Severity != ValidationSeverity.Info);

        var result = PlanStack(manifest!);

        var steps = result.Timeline!.Scenes.SelectMany(s => s.Clips).Take(3).ToList();
        Assert.Equal([0L, 120L, 240L], steps.Select(c => c.StartFrame).ToArray());
        Assert.Equal([1, 2, 3], steps.Select(c => c.Reveal!.Visible).ToArray());
        Assert.All(steps, c => Assert.True(c.Reveal!.IsStack));
        Assert.Equal(3, steps[0].Reveal!.Sources.Count);
        Assert.EndsWith("_1of3.png", steps[0].FilePath);
        Assert.EndsWith("_3of3.png", steps[2].FilePath);   // a composite too: the images differ
        Assert.True(steps[0].FilePath != steps[1].FilePath);
    }


    [Fact]
    public void Separate_images_need_one_image_per_cue()
    {
        var (manifest, issues) = ExpandStack(
            "{\"cues\": \"1-3\", \"asset\": \"S02_01.png\", \"reveal\": " +
            "{\"cues\": [1, 2, 3], \"assets\": [\"S02_01.png\", \"S02_02.png\"]}}");

        Assert.Null(manifest!.Timeline[0].RevealAtMs);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_REVEAL_INVALID");
    }

    [Fact]
    public void A_missing_image_of_a_build_up_is_reported_like_any_missing_asset()
    {
        var (manifest, _) = ExpandStack(
            "{\"cues\": \"1-3\", \"asset\": \"S02_01.png\", \"reveal\": " +
            "{\"cues\": [1, 2, 3], \"assets\": [\"S02_01.png\", \"S02_02.png\", \"S09_09.png\"]}}, " +
            "{\"cues\": \"4-5\", \"asset\": \"S01_02_SCN_ZI.png\"}");

        var result = PlanStack(manifest!);

        Assert.Contains(result.Coverage!.MissingAssets, m => m.Contains("S09_09"));
    }

    [Fact]
    public void Stack_slots_split_the_canvas_for_rows_and_grids()
    {
        var row = RevealImageWriter.SlotRects(1920, 1080, 3, "row");
        Assert.Equal([(0, 0, 640, 1080), (640, 0, 640, 1080), (1280, 0, 640, 1080)], row.ToArray());

        var grid = RevealImageWriter.SlotRects(1920, 1080, 4, "grid");
        Assert.Equal([(0, 0, 960, 540), (960, 0, 960, 540), (0, 540, 960, 540), (960, 540, 960, 540)], grid.ToArray());
    }

    [Fact]
    public void Stack_ffmpeg_arguments_cover_crop_each_image_into_its_slot()
    {
        var args = RevealImageWriter.BuildStackArguments(["a.png", "b.png"], 1920, 1080, 3, "row", "o.png");

        var graph = args[args.ToList().IndexOf("-filter_complex") + 1];
        Assert.Equal(
            "[1:v]scale=640:1080:force_original_aspect_ratio=increase,crop=640:1080,setsar=1[s0];" +
            "[0:v][s0]overlay=0:0[o0];" +
            "[2:v]scale=640:1080:force_original_aspect_ratio=increase,crop=640:1080,setsar=1[s1];" +
            "[o0][s1]overlay=640:0[o1]", graph);
        Assert.Equal("[o1]", args[args.ToList().IndexOf("-map") + 1]);
    }

    // ---- sound effects ----

    [Fact]
    public void Sfx_accepts_a_name_or_a_list_and_rejects_nonsense()
    {
        var (manifest, issues) = Expand(
            "{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"sfx\": \"pop\"}, " +
            "{\"cues\": \"4\", \"asset\": \"S01_02_SCN_ZI.png\", \"sfx\": [\"ding\", \"whoosh\"]}, " +
            "{\"cues\": \"5\", \"asset\": \"S01_01_CMP_ST.png\", \"sfx\": \"../evil\"}");

        Assert.Equal(["pop"], manifest!.Timeline[0].Sfx!.ToArray());
        Assert.Equal(["ding", "whoosh"], manifest.Timeline[1].Sfx!.ToArray());
        Assert.Null(manifest.Timeline[2].Sfx);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_SFX_INVALID");
    }

    [Fact]
    public void A_normal_shot_plays_its_sound_when_it_starts()
    {
        var (manifest, _) = Expand(
            "{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"sfx\": \"pop\"}, " +
            "{\"cues\": \"4-5\", \"asset\": \"S01_02_SCN_ZI.png\"}");

        var (timeline, _) = Plan(manifest!);

        var sound = Assert.Single(timeline.Sounds);
        Assert.Equal("pop", sound.Name);
        Assert.Equal(0, sound.StartFrame);
    }

    [Fact]
    public void A_reveal_shot_plays_one_sound_per_item_and_repeats_the_last_name()
    {
        var (manifest, _) = Expand(
            "{\"cues\": \"1-3\", \"asset\": \"S01_01_CMP_ST.png\", \"reveal\": [1, 2, 3], \"sfx\": [\"ding\", \"pop\"]}, " +
            "{\"cues\": \"4-5\", \"asset\": \"S01_02_SCN_ZI.png\"}");

        var (timeline, _) = Plan(manifest!);

        Assert.Equal(["ding", "pop", "pop"], timeline.Sounds.Select(s => s.Name).ToArray());
        Assert.Equal([0L, 120L, 240L], timeline.Sounds.Select(s => s.StartFrame).ToArray());
    }

    [Fact]
    public void A_single_sfx_name_plays_at_every_reveal_step()
    {
        var result = PlanStack(ExpandStack(
            "{\"cues\": \"1-3\", \"asset\": \"S02_01.png\", \"sfx\": \"pop\", \"reveal\": " +
            "{\"cues\": [1, 2, 3], \"assets\": [\"S02_01.png\", \"S02_02.png\", \"S02_03.png\"]}}, " +
            "{\"cues\": \"4-5\", \"asset\": \"S01_02_SCN_ZI.png\"}").Manifest!);

        Assert.Equal(3, result.Timeline!.Sounds.Count);
        Assert.All(result.Timeline.Sounds, s => Assert.Equal("pop", s.Name));
    }

    private static Timeline TimelineWithSounds()
    {
        var folder = Path.Combine(Path.GetTempPath(), "itv-sfx-" + Guid.NewGuid().ToString("N"));
        var image = Path.Combine(folder, "images", "A.png");
        var timeline = new Timeline
        {
            ProjectName = "t",
            Fps = 30,
            Audio = new AudioTrack { FilePath = Path.Combine(folder, "n.wav"), DurationFrames = 300 },
        };
        timeline.Scenes.Add(new Scene
        {
            Id = "S01", StartFrame = 0, EndFrame = 300,
            Clips = { new VideoClip { FilePath = image, SceneId = "S01", StartFrame = 0, DurationFrames = 300 } },
        });
        timeline.Sounds.Add(new SoundEffect { Name = "pop", FilePath = Path.Combine(folder, "sfx", "pop.wav"), StartFrame = 30, DurationFrames = 30 });
        timeline.Sounds.Add(new SoundEffect { Name = "pop", FilePath = Path.Combine(folder, "sfx", "pop.wav"), StartFrame = 45, DurationFrames = 30 });
        timeline.Sounds.Add(new SoundEffect { Name = "ding", FilePath = Path.Combine(folder, "sfx", "ding.wav"), StartFrame = 120, DurationFrames = 30 });
        return timeline;
    }

    [Fact]
    public void Premiere_xml_puts_overlapping_sounds_on_separate_audio_tracks()
    {
        var timeline = TimelineWithSounds();
        var images = new List<ImageInfo>
        {
            new(timeline.Scenes[0].Clips[0].FilePath, new ParsedImageName("A", 1, 1, null, false), 1920, 1080),
        };

        var xml = XDocument.Parse(Fcp7XmlExporter.Export(timeline, images, new PremiereExportOptions()));

        var audioTracks = xml.Descendants("audio").First(a => a.Parent!.Name == "media").Elements("track").ToList();
        Assert.Equal(3, audioTracks.Count);   // narration + 2 (the first two sounds overlap)
        var starts = audioTracks[1].Elements("clipitem").Select(c => (string)c.Element("start")!).ToArray();
        Assert.Equal(["30", "120"], starts);
        Assert.Equal(["45"], audioTracks[2].Elements("clipitem").Select(c => (string)c.Element("start")!).ToArray());
    }

    [Fact]
    public void CapCut_draft_gets_a_segment_per_sound_on_extra_audio_tracks()
    {
        var timeline = TimelineWithSounds();
        var images = new List<ImageInfo>
        {
            new(timeline.Scenes[0].Clips[0].FilePath, new ParsedImageName("A", 1, 1, null, false), 1920, 1080),
        };
        var folder = Path.Combine(Path.GetTempPath(), "itv-cc-" + Guid.NewGuid().ToString("N"));
        CapCutDraftExporter.Export(timeline, images, folder);
        var content = Directory.GetFiles(folder, "draft_content.json", SearchOption.AllDirectories).First();
        var root = JsonNode.Parse(File.ReadAllText(content))!;

        var tracks = root["tracks"]!.AsArray().Where(t => (string?)t!["type"] == "audio").ToList();
        Assert.Equal(3, tracks.Count);
        Assert.Equal(4, root["materials"]!["audios"]!.AsArray().Count);   // narration + 3 sounds
        var segmentCount = tracks.Sum(t => t!["segments"]!.AsArray().Count);
        Assert.Equal(4, segmentCount);
    }

    [Fact]
    public void Preview_mix_delays_each_sound_and_keeps_the_narration_length()
    {
        var args = PreviewRenderPlanFactory.BuildMuxArguments(
            "rough.mp4", "n.wav", "out.mp4", TimelineWithSounds().Sounds, 30);

        var graph = args[args.ToList().IndexOf("-filter_complex") + 1];
        Assert.Contains(new[] { graph }, g => g.Contains("adelay=1000:all=1"));    // 30 frames = 1000 ms
        Assert.Contains(new[] { graph }, g => g.Contains("adelay=1500:all=1"));
        Assert.Contains(new[] { graph }, g => g.Contains("adelay=4000:all=1"));
        Assert.Contains(new[] { graph }, g => g.Contains("amix=inputs=4:duration=first"));
        Assert.Equal("[aout]", args[args.ToList().LastIndexOf("-map") + 1]);
    }

    [Fact]
    public void Preview_mix_without_sounds_is_the_plain_narration_mux()
    {
        var args = PreviewRenderPlanFactory.BuildMuxArguments("rough.mp4", "n.wav", "out.mp4");

        Assert.Equal(-1, args.ToList().IndexOf("-filter_complex"));
        Assert.Equal("1:a:0", args[args.ToList().LastIndexOf("-map") + 1]);
    }

    [Fact]
    public void Built_in_sounds_have_ffmpeg_recipes_and_names_are_case_insensitive()
    {
        Assert.True(SoundEffectLibrary.IsBuiltIn("Pop"));
        Assert.True(SoundEffectLibrary.IsBuiltIn("whoosh"));
        Assert.True(!SoundEffectLibrary.IsBuiltIn("nonsense"));
        Assert.NotNull(SoundEffectLibrary.BuildArguments("ding", "o.wav"));
    }
}
