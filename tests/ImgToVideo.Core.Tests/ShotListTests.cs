using ImgToVideo.Core.Analysis;
using ImgToVideo.Core.Manifest;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using ImgToVideo.Core.Planning;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class ShotListTests
{
    private static List<SubtitleBlock> Subtitles() =>
    [
        new(1, 0.0, 5.0, "First line one."),
        new(2, 5.2, 9.5, "First line two."),
        new(3, 10.2, 15.0, "Second line one."),
        new(4, 15.2, 19.0, "Second line two."),
    ];

    private static IReadOnlyList<string> ImageFiles() => ["S01_01_SCN.png", "S01_02_CU_ZI.png"];

    private static (ShotListDocument Document, List<ValidationIssue> Issues) Parse(
        string json, int masterPromptMaxChars = 0, int promptMaxChars = 0)
    {
        var issues = new List<ValidationIssue>();
        return (ShotListParser.Parse(json, issues, masterPromptMaxChars, promptMaxChars), issues);
    }

    private static VisualManifest? Expand(
        string json,
        List<ValidationIssue>? issues = null,
        List<SubtitleBlock>? subtitles = null,
        IReadOnlyList<string>? imageFiles = null) =>
        ShotListExpander.Expand(
            Parse(json).Document,
            subtitles ?? Subtitles(),
            imageFiles ?? ImageFiles(),
            new TransitionOptions(),
            issues ?? []);

    [Fact]
    public void Style_field_is_captured_and_prompts_keep_empty_entries()
    {
        var (document, issues) = Parse(
            """
            {"style": "Warm documentary photo style",
             "images": [
               {"file": "S01_01_SCN.png", "prompt": "A walker at dawn."},
               {"file": "S01_02_CU_ZI.png"}
             ],
             "shots": [{"cues": "1", "asset": "S01_01_SCN.png"}]}
            """);

        Assert.Equal("Warm documentary photo style", document.Style);
        Assert.Equal(2, document.Prompts.Count);
        Assert.Equal("A walker at dawn.", document.Prompts["S01_01_SCN.png"]);
        Assert.Equal(string.Empty, document.Prompts["S01_02_CU_ZI.png"]);
        Assert.DoesNotContain(issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Over_limit_master_prompt_warns_with_actual_count()
    {
        var style = new string('s', 1600);
        var (_, issues) = Parse(
            "{\"style\": \"" + style + "\", \"shots\": [{\"cues\": \"1\", \"asset\": \"S01_01_SCN.png\"}]}",
            masterPromptMaxChars: 1500);

        var issue = Assert.Single(issues, i => i.Code == "SHOTLIST_MASTER_PROMPT_LONG");
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains("1600", issue.Message);
        Assert.Contains("1500", issue.Message);
    }

    [Fact]
    public void Over_limit_image_prompt_warns_per_entry_with_the_file_name()
    {
        var prompt = new string('p', 2500);
        var (_, issues) = Parse(
            "{\"images\": [{\"file\": \"S01_01_SCN.png\", \"prompt\": \"" + prompt + "\"}, " +
            "{\"file\": \"S01_02_CU_ZI.png\", \"prompt\": \"short\"}], " +
            "\"shots\": [{\"cues\": \"1\", \"asset\": \"S01_01_SCN.png\"}]}",
            promptMaxChars: 2400);

        var issue = Assert.Single(issues, i => i.Code == "SHOTLIST_PROMPT_LONG");
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Contains("S01_01_SCN.png", issue.Message);
        Assert.Contains("2500", issue.Message);
        Assert.DoesNotContain(issues, i => i.Message.Contains("S01_02_CU_ZI.png\" is 2"));
    }

    [Fact]
    public void Prompt_length_checks_are_disabled_when_limits_are_zero_or_not_exceeded()
    {
        var json = "{\"style\": \"short style\", \"images\": [{\"file\": \"S01_01_SCN.png\", \"prompt\": \"short\"}], " +
                   "\"shots\": [{\"cues\": \"1\", \"asset\": \"S01_01_SCN.png\"}]}";

        Assert.DoesNotContain(Parse(json, masterPromptMaxChars: 0, promptMaxChars: 0).Issues,
            i => i.Code is "SHOTLIST_MASTER_PROMPT_LONG" or "SHOTLIST_PROMPT_LONG");
        Assert.DoesNotContain(Parse(json, masterPromptMaxChars: 1500, promptMaxChars: 2400).Issues,
            i => i.Code is "SHOTLIST_MASTER_PROMPT_LONG" or "SHOTLIST_PROMPT_LONG");
    }

    [Fact]
    public void Cue_ranges_derive_timing_narration_and_defaults()
    {
        var manifest = Expand(
            """
            {"shots": [
              {"cues": "1-3", "asset": "S01_01_SCN.png", "motion": "ZI"},
              {"cues": "4", "asset": "S01_02_CU_ZI.png", "transition": "CROSSFADE"}
            ]}
            """);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Timeline.Count);

        var shot1 = manifest.Timeline[0];
        Assert.Equal("shot-001", shot1.ShotId);
        Assert.Equal(0, shot1.StartMs);
        Assert.Equal(15000, shot1.EndMs);
        Assert.Equal("S01", shot1.SceneId);
        Assert.Equal("c1", shot1.BeatId);
        Assert.Equal(new[] { 1, 2, 3 }, shot1.SrtCueIds);
        Assert.Equal("First line one. First line two. Second line one.", shot1.NarrationText);
        Assert.Equal("S01_01_SCN.png", manifest.Assets[0].File);
        Assert.Equal("ZI", shot1.Motion!.Type);
        Assert.Null(shot1.TransitionOut);

        var shot2 = manifest.Timeline[1];
        Assert.Equal(15200, shot2.StartMs);
        Assert.Equal(19000, shot2.EndMs);
        Assert.Equal("CROSSFADE", shot2.TransitionOut!.Type);
        Assert.Equal(500, shot2.TransitionOut.DurationMs);
    }

    [Fact]
    public void Array_and_comma_cue_forms_parse()
    {
        var manifest = Expand(
            """
            {"shots": [
              {"cues": [1, 2], "asset": "S01_01_SCN.png"},
              {"cues": "3,4", "asset": "S01_02_CU_ZI.png"}
            ]}
            """);

        Assert.NotNull(manifest);
        Assert.Equal(new[] { 1, 2 }, manifest!.Timeline[0].SrtCueIds);
        Assert.Equal(new[] { 3, 4 }, manifest.Timeline[1].SrtCueIds);
        Assert.Equal(9500, manifest.Timeline[0].EndMs);
        Assert.Equal(19000, manifest.Timeline[1].EndMs);
    }

    [Fact]
    public void Stems_match_without_extension()
    {
        var manifest = Expand(
            """
            {"shots": [
              {"cues": "1", "asset": "S01_02_CU_ZI"},
              {"cues": "2", "asset": "s01_01_scn"}
            ]}
            """);

        Assert.NotNull(manifest);
        Assert.Equal("S01_02_CU_ZI.png", manifest!.Assets[0].File);
        Assert.Equal("S01_01_SCN.png", manifest.Assets[1].File);
    }

    [Fact]
    public void Likely_typos_are_flagged_but_no_longer_block()
    {
        var issues = new List<ValidationIssue>();
        var manifest = Expand(
            """{"shots": [{"cues": "1", "asset": "S01_01_SCN_ZI.png"}]}""",
            issues,
            imageFiles: ["S01_01_SCN.png", "S01_02_CU_ZI.png"]);

        // With GENERATE-AHEAD a typo no longer blocks the plan: the shot becomes
        // a generate request (a wrong-prompt near-duplicate is visible in
        // review), and the suggestion stays as INFO — adjacent sub-beat names
        // from splits (S08_04 next to S08_03) are legitimate slots, not typos.
        Assert.NotNull(manifest);
        Assert.Equal("S01_01_SCN_ZI.png", manifest!.Assets[0].File);
        Assert.Contains(issues, i => i.Severity == ValidationSeverity.Info &&
            i.Code == "SHOTLIST_ASSET_TYPO" && i.Message.Contains("S01_01_SCN"));
    }

    [Fact]
    public void Missing_files_become_generate_requests_with_prompts_as_intent()
    {
        var issues = new List<ValidationIssue>();
        var manifest = Expand(
            """
            {"images": [
              {"file": "S02_01_INF_ZI.png", "prompt": "infographic: bar chart of results"}
            ],
            "shots": [
              {"cues": "1-2", "asset": "S01_01_SCN.png"},
              {"cues": "3-4", "asset": "S02_01_INF_ZI.png"}
            ]}
            """,
            issues,
            imageFiles: ["S01_01_SCN.png"]);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Timeline.Count);
        var shot = manifest.Timeline[1];
        Assert.Equal("S02_01_INF_ZI.png", shot.AssetId);
        Assert.Equal("infographic: bar chart of results", shot.VisualIntent);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_GENERATE_AHEAD" &&
            i.Message.Contains("S02_01_INF_ZI.png"));
    }

    [Fact]
    public void Cue_out_of_range_is_reported()
    {
        var issues = new List<ValidationIssue>();
        var manifest = Expand("""{"shots": [{"cues": "99", "asset": "S01_01_SCN.png"}]}""", issues);

        Assert.Null(manifest);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_CUE_UNKNOWN");
        Assert.Contains(issues, i => i.Code == "SHOTLIST_EMPTY");
    }

    [Fact]
    public void Partial_overlap_is_clamped()
    {
        var issues = new List<ValidationIssue>();
        var manifest = Expand(
            """
            {"shots": [
              {"cues": "1-3", "asset": "S01_01_SCN.png"},
              {"cues": "2-4", "asset": "S01_02_CU_ZI.png"}
            ]}
            """, issues);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest!.Timeline.Count);
        Assert.Equal(15000, manifest.Timeline[1].StartMs);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_CUES_OVERLAP");
    }

    [Fact]
    public void Fully_covered_shot_is_dropped()
    {
        var issues = new List<ValidationIssue>();
        var manifest = Expand(
            """
            {"shots": [
              {"cues": "1-3", "asset": "S01_01_SCN.png"},
              {"cues": "2", "asset": "S01_02_CU_ZI.png"}
            ]}
            """, issues);

        Assert.NotNull(manifest);
        Assert.Single(manifest!.Timeline);
        Assert.Equal(15000, manifest.Timeline[0].EndMs);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_CUES_OVERLAP");
    }

    [Fact]
    public void Entries_are_sorted_by_narration_time()
    {
        var issues = new List<ValidationIssue>();
        var manifest = Expand(
            """
            {"shots": [
              {"cues": "3-4", "asset": "S01_02_CU_ZI.png"},
              {"cues": "1-2", "asset": "S01_01_SCN.png"}
            ]}
            """, issues);

        Assert.NotNull(manifest);
        Assert.Equal(0, manifest!.Timeline[0].StartMs);
        Assert.Equal(10200, manifest.Timeline[1].StartMs);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_REORDERED");
        Assert.Equal("shot-002", manifest.Timeline[0].ShotId);
    }

    [Fact]
    public void Duplicate_shot_ids_drop_the_later_shot()
    {
        var issues = new List<ValidationIssue>();
        var manifest = Expand(
            """
            {"shots": [
              {"cues": "1", "asset": "S01_01_SCN.png", "shot_id": "x"},
              {"cues": "2", "asset": "S01_02_CU_ZI.png", "shot_id": "x"}
            ]}
            """, issues);

        Assert.NotNull(manifest);
        Assert.Single(manifest!.Timeline);
        Assert.Contains(issues, i => i.Code == "SHOTLIST_DUPLICATE_ID");
    }

    [Fact]
    public void Bad_cue_specs_are_reported_per_entry()
    {
        var (document, parseIssues) = Parse(
            """
            {"shots": [
              {"cues": "x", "asset": "S01_01_SCN.png"},
              {"cues": 0, "asset": "S01_01_SCN.png"},
              {"asset": "S01_01_SCN.png"}
            ]}
            """);

        Assert.Empty(document.Shots);
        Assert.Equal(3, parseIssues.Count(i => i.Code == "SHOTLIST_CUES_INVALID" || i.Code == "SHOTLIST_INVALID"));
    }

    [Fact]
    public void Prompts_without_file_are_reported()
    {
        var (document, parseIssues) = Parse(
            """
            {"images": [{"prompt": "no file here"}],
             "shots": [{"cues": "1", "asset": "S01_01_SCN.png"}]}
            """);

        Assert.Single(document.Shots);
        Assert.Empty(document.Prompts);
        Assert.Contains(parseIssues, i => i.Code == "SHOTLIST_INVALID");
    }

    [Fact]
    public void Missing_shots_array_throws()
    {
        Assert.Throws<InvalidDataException>(() => Parse("{\"nope\": []}"));
    }

    [Fact]
    public void Shotlist_expands_to_a_renderable_timeline_end_to_end()
    {
        using var project = new TempProject();
        project.WriteImage("S01_B01_IMG01.png", 1920, 1080);
        project.WriteImage("S01_B02_IMG01.png", 1920, 1080);
        project.WriteSrt();
        project.WriteAudio();
        project.WriteFile(
            "shotlist.json",
            """
            {"shots": [
              {"cues": "1-3", "asset": "S01_B01_IMG01.png", "motion": "ZI", "transition": "CROSSFADE"},
              {"cues": "4", "asset": "S01_B02_IMG01.png"}
            ]}
            """);

        var options = new ProjectOptions();
        var inventory = ProjectLoader.Load(project.Path, options);
        Assert.True(inventory.Issues.All(i => i.Severity != ValidationSeverity.Error),
            string.Join("\n", inventory.Issues.Select(i => $"[{i.Severity}] {i.Message}")));
        Assert.NotNull(inventory.Manifest);
        Assert.True(File.Exists(Path.Combine(project.Path, "visual_manifest.json")));

        var result = EditPlanner.Plan(inventory, 19.0, options);

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => $"[{i.Severity}] {i.Message}")));
        Assert.NotNull(result.Timeline);
        var clips = result.Timeline!.Scenes.SelectMany(s => s.Clips).ToList();
        Assert.Equal(2, clips.Count);
        Assert.Equal(0, clips[0].StartFrame);
        Assert.Equal(MotionType.ZoomIn, clips[0].Motion);
        Assert.Equal(15, clips[1].Transition!.DurationFrames);
        Assert.Equal(570, clips.Sum(c => c.DurationFrames));
    }
}
