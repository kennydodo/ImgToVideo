using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImgToVideo.Core.Models;

namespace ImgToVideo.CapCut;

/// <summary>
/// Writes a CapCut desktop draft folder (draft_content.json + draft_meta_info.json)
/// from a planned timeline. Pure consumer of the timeline — no process spawns.
/// Schema verified against CapCut desktop 9.4.0.4015 (draft new_version 185.0.0);
/// see docs/spike-capcut-draft.md for the recorded schema surface and gotchas.
/// </summary>
public static class CapCutDraftExporter
{
    private const long StillMaterialDurationUs = 10800000000;
    private const string DraftNewVersion = "185.0.0";

    private static string NewId() => Guid.NewGuid().ToString().ToUpperInvariant();
    private static string NewLowerId() => Guid.NewGuid().ToString("N");

    private static long Us(long frames, double fps) => (long)Math.Round(frames * 1_000_000.0 / fps);

    /// <summary>Writes the draft folder. Returns the draft_content.json path.</summary>
    public static string Export(
        Timeline timeline,
        IReadOnlyList<ImageInfo> images,
        string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var draftName = new DirectoryInfo(outputFolder).Name;
        var contentPath = Path.Combine(outputFolder, "draft_content.json");
        WriteBomLess(contentPath, BuildDraftContent(timeline, images, draftName));
        WriteBomLess(
            Path.Combine(outputFolder, "draft_meta_info.json"),
            BuildMetaInfo(timeline, images, outputFolder, draftName));
        return contentPath;
    }

    private static void WriteBomLess(string path, string json) =>
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static string BuildDraftContent(
        Timeline timeline,
        IReadOnlyList<ImageInfo> images,
        string draftName)
    {
        var fps = timeline.Fps;
        var canvasW = timeline.Resolution.Width;
        var canvasH = timeline.Resolution.Height;
        var canvasAspect = (double)canvasW / canvasH;
        var clips = timeline.Scenes.SelectMany(s => s.Clips).ToList();
        var totalUs = Us(clips.Sum(c => c.DurationFrames), fps);
        var audioUs = Us(timeline.Audio.DurationFrames, fps);

        var dims = images.ToDictionary(
            i => i.FilePath, i => (i.Width, i.Height), StringComparer.OrdinalIgnoreCase);

        var videos = new List<JsonObject>();
        var canvases = new List<JsonObject>();
        var speeds = new List<JsonObject>();
        var soundMappings = new List<JsonObject>();
        var materialColors = new List<JsonObject>();
        var placeholderInfos = new List<JsonObject>();
        var vocalSeparations = new List<JsonObject>();
        var collections = new Dictionary<string, List<JsonObject>>
        {
            ["canvases"] = canvases,
            ["speeds"] = speeds,
            ["sound_channel_mappings"] = soundMappings,
            ["material_colors"] = materialColors,
            ["placeholder_infos"] = placeholderInfos,
            ["vocal_separations"] = vocalSeparations,
        };

        var materialIdByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var clip in clips)
        {
            if (materialIdByFile.ContainsKey(clip.FilePath))
            {
                continue;
            }

            var materialId = NewId();
            materialIdByFile[clip.FilePath] = materialId;
            var (w, h) = dims.TryGetValue(clip.FilePath, out var d) ? d : (canvasW, canvasH);
            videos.Add(VideoMaterial(materialId, clip.FilePath, w, h));
        }

        var videoSegments = new List<JsonObject>();
        foreach (var clip in clips)
        {
            var materialId = materialIdByFile[clip.FilePath];
            var durationUs = Us(clip.DurationFrames, fps);

            // Every segment carries six companion materials (canvas, speed,
            // sound mapping, color, placeholder, vocal separation) — verified
            // against the reference draft.
            var companionIds = new List<string>();
            foreach (var (kind, collection) in new[]
                     {
                         ("canvas", "canvases"),
                         ("speed", "speeds"),
                         ("sound", "sound_channel_mappings"),
                         ("color", "material_colors"),
                         ("placeholder", "placeholder_infos"),
                         ("vocal", "vocal_separations"),
                     })
            {
                var id = NewId();
                companionIds.Add(id);
                collections[collection].Add(CompanionMaterial(kind, id));
            }

            videoSegments.Add(new JsonObject
            {
                ["id"] = NewId(),
                ["material_id"] = materialId,
                ["target_timerange"] = new JsonObject
                {
                    ["start"] = Us(clip.StartFrame, fps),
                    ["duration"] = durationUs,
                },
                ["source_timerange"] = new JsonObject
                {
                    ["start"] = 0,
                    ["duration"] = durationUs,
                },
                ["extra_material_refs"] = new JsonArray(companionIds.Select(id => (JsonNode)id).ToArray()),
                ["clip"] = new JsonObject
                {
                    ["alpha"] = 1.0,
                    ["flip"] = new JsonObject { ["horizontal"] = false, ["vertical"] = false },
                    ["rotation"] = 0.0,
                    ["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 },
                    ["transform"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0 },
                },
                ["common_keyframes"] = ToNodeArray(BuildScaleKeyframes(
                    clip, dims, canvasAspect, durationUs) ?? new List<JsonObject>()),
                ["speed"] = 1.0,
            });
        }

        // Narration: an audio material + audio track segment.
        var audioMaterialId = NewId();
        var audioCompanions = new List<string>();
        foreach (var (kind, collection) in new[] { ("speed", "speeds"), ("sound", "sound_channel_mappings") })
        {
            var id = NewId();
            audioCompanions.Add(id);
            collections[collection].Add(CompanionMaterial(kind, id));
        }

        // Sound effects: one audio material and segment each, packed onto extra audio tracks
        // so that overlapping sounds never share a track.
        var soundMaterials = new List<JsonObject>();
        var soundTracks = new List<(long EndUs, List<JsonObject> Segments)>();
        foreach (var sound in timeline.Sounds
                     .Where(sd => !string.IsNullOrWhiteSpace(sd.FilePath))
                     .OrderBy(sd => sd.StartFrame))
        {
            var startUs = Us(sound.StartFrame, fps);
            if (startUs >= totalUs)
            {
                continue;
            }

            var lengthUs = Math.Min(
                sound.DurationFrames > 0 ? Us(sound.DurationFrames, fps) : 1_000_000, totalUs - startUs);
            var materialId = NewId();
            var companions = new List<string>();
            foreach (var (kind, collection) in new[] { ("speed", "speeds"), ("sound", "sound_channel_mappings") })
            {
                var id = NewId();
                companions.Add(id);
                collections[collection].Add(CompanionMaterial(kind, id));
            }

            soundMaterials.Add(new JsonObject
            {
                ["id"] = materialId,
                ["type"] = "extract_music",
                ["duration"] = sound.DurationFrames > 0 ? Us(sound.DurationFrames, fps) : lengthUs,
                ["path"] = ToForwardSlashes(sound.FilePath),
                ["material_name"] = Path.GetFileName(sound.FilePath),
                ["has_audio"] = true,
                ["category_name"] = "local",
                ["category_id"] = "",
                ["source_platform"] = 0,
            });

            var index = soundTracks.FindIndex(t => t.EndUs <= startUs);
            if (index < 0)
            {
                soundTracks.Add((0, new List<JsonObject>()));
                index = soundTracks.Count - 1;
            }

            soundTracks[index].Segments.Add(new JsonObject
            {
                ["id"] = NewId(),
                ["material_id"] = materialId,
                ["target_timerange"] = new JsonObject { ["start"] = startUs, ["duration"] = lengthUs },
                ["source_timerange"] = new JsonObject { ["start"] = 0, ["duration"] = lengthUs },
                ["extra_material_refs"] = new JsonArray(companions.Select(id => (JsonNode)id).ToArray()),
                ["clip"] = new JsonObject
                {
                    ["alpha"] = 1.0,
                    ["flip"] = new JsonObject { ["horizontal"] = false, ["vertical"] = false },
                    ["rotation"] = 0.0,
                    ["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 },
                    ["transform"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0 },
                },
                ["common_keyframes"] = new JsonArray(),
                ["speed"] = 1.0,
            });
            soundTracks[index] = (startUs + lengthUs, soundTracks[index].Segments);
        }

        var draft = new JsonObject
        {
            ["id"] = NewId(),
            ["version"] = "360000",
            ["new_version"] = DraftNewVersion,
            ["name"] = draftName,
            ["duration"] = Math.Max(totalUs, audioUs),
            ["create_time"] = 0,
            ["update_time"] = 0,
            ["fps"] = fps,
            ["is_drop_frame_timecode"] = false,
            ["color_space"] = 0,
            ["config"] = ParseTemplate(ConfigTemplate),
            ["canvas_config"] = new JsonObject
            {
                ["ratio"] = $"{canvasW}:{canvasH}",
                ["width"] = canvasW,
                ["height"] = canvasH,
                ["background"] = null,
            },
            ["tracks"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "video",
                    ["attribute"] = 0,
                    ["flag"] = 0,
                    ["segments"] = ToNodeArray(videoSegments),
                },
                new JsonObject
                {
                    ["type"] = "audio",
                    ["attribute"] = 0,
                    ["flag"] = 0,
                    ["segments"] = new JsonArray(
                        new JsonObject
                        {
                            ["id"] = NewId(),
                            ["material_id"] = audioMaterialId,
                            ["target_timerange"] = new JsonObject
                            {
                                ["start"] = 0,
                                ["duration"] = audioUs,
                            },
                            ["source_timerange"] = new JsonObject
                            {
                                ["start"] = 0,
                                ["duration"] = audioUs,
                            },
                            ["extra_material_refs"] = new JsonArray(
                                audioCompanions.Select(id => (JsonNode)id).ToArray()),
                            ["clip"] = new JsonObject
                            {
                                ["alpha"] = 1.0,
                                ["flip"] = new JsonObject { ["horizontal"] = false, ["vertical"] = false },
                                ["rotation"] = 0.0,
                                ["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 },
                                ["transform"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0 },
                            },
                            ["common_keyframes"] = new JsonArray(),
                            ["speed"] = 1.0,
                        }),
                }),
            ["group_container"] = new JsonArray(),
            ["materials"] = new JsonObject
            {
                ["videos"] = ToNodeArray(videos),
                ["audios"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = audioMaterialId,
                        ["type"] = "extract_music",
                        ["duration"] = audioUs,
                        ["path"] = ToForwardSlashes(timeline.Audio.FilePath),
                        ["material_name"] = Path.GetFileName(timeline.Audio.FilePath),
                        ["has_audio"] = true,
                        ["category_name"] = "local",
                        ["category_id"] = "",
                        ["source_platform"] = 0,
                    }),
                ["canvases"] = ToNodeArray(canvases),
                ["speeds"] = ToNodeArray(speeds),
                ["sound_channel_mappings"] = ToNodeArray(soundMappings),
                ["material_colors"] = ToNodeArray(materialColors),
                ["placeholder_infos"] = ToNodeArray(placeholderInfos),
                ["vocal_separations"] = ToNodeArray(vocalSeparations),
                ["transitions"] = new JsonArray(),
            },
            ["keyframes"] = ParseTemplate(KeyframesTemplate),
            ["keyframe_graph_list"] = new JsonArray(),
            ["platform"] = ParseTemplate(PlatformTemplate),
            ["last_modified_platform"] = ParseTemplate(PlatformTemplate),
            ["mutable_config"] = null,
            ["cover"] = null,
            ["retouch_cover"] = null,
            ["extra_info"] = null,
            ["relationships"] = new JsonArray(),
            ["mixed_track_mode_on"] = false,
            ["render_index_track_mode_on"] = true,
            ["free_render_index_mode_on"] = false,
            ["static_cover_image_path"] = "",
            ["source"] = "default",
            ["time_marks"] = new JsonArray(),
            ["path"] = "",
            ["lyrics_effects"] = new JsonArray(),
            ["uneven_animation_template_info"] = ParseTemplate(UnevenTemplate),
            ["draft_type"] = "video",
            ["smart_ads_info"] = ParseTemplate(SmartAdsTemplate),
            ["function_assistant_info"] = ParseTemplate(FunctionAssistantTemplate),
        };

        var draftTracks = (JsonArray)draft["tracks"]!;
        foreach (var (_, segments) in soundTracks)
        {
            draftTracks.Add(new JsonObject
            {
                ["type"] = "audio",
                ["attribute"] = 0,
                ["flag"] = 0,
                ["segments"] = ToNodeArray(segments),
            });
        }

        var draftAudios = (JsonArray)((JsonObject)draft["materials"]!)["audios"]!;
        foreach (var material in soundMaterials)
        {
            draftAudios.Add(material);
        }

        return draft.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// CapCut scale 1.0 == cover fit. For spec-compliant images the cover-fit
    /// width is image height × canvas aspect, so Scale(t) = coverFitW / viewportW.
    /// ZI/ZO become two keyframes; ST stays static; pans stay static until the
    /// transform-unit calibration capture (see the spike notes).
    /// </summary>
    private static List<JsonObject>? BuildScaleKeyframes(
        VideoClip clip,
        IReadOnlyDictionary<string, (int Width, int Height)> dims,
        double canvasAspect,
        long durationUs)
    {
        if (clip.Motion is not (MotionType.ZoomIn or MotionType.ZoomOut))
        {
            return null;
        }

        if (!dims.TryGetValue(clip.FilePath, out var size) || size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var startAspect = clip.StartViewport.Width / clip.StartViewport.Height;
        if (Math.Abs(startAspect - canvasAspect) > 0.02)
        {
            return null;
        }

        var coverFitWidth = size.Height * canvasAspect;
        var s0 = coverFitWidth / clip.StartViewport.Width;
        var s1 = coverFitWidth / clip.EndViewport.Width;
        if (clip.Motion == MotionType.ZoomOut)
        {
            (s0, s1) = (Math.Max(s0, s1), Math.Min(s0, s1));
        }
        else
        {
            (s0, s1) = (Math.Min(s0, s1), Math.Max(s0, s1));
        }

        JsonObject Keyframe(long offset, double value) => new()
        {
            ["id"] = NewId(),
            ["left_control"] = new JsonObject { ["x"] = 0.5, ["y"] = 0.5 },
            ["right_control"] = new JsonObject { ["x"] = 0.5, ["y"] = 0.5 },
            ["time_offset"] = offset,
            ["values"] = new JsonArray(value),
        };

        JsonObject Property(string propertyType, params JsonObject[] keyframes) => new()
        {
            ["id"] = NewId(),
            ["keyframe_list"] = new JsonArray(keyframes.Select(k => (JsonNode)k).ToArray()),
            ["material_id"] = "",
            ["property_type"] = propertyType,
        };

        return new List<JsonObject>
        {
            Property("KFTypeScaleX", Keyframe(0, s0), Keyframe(durationUs, s1)),
            Property("KFTypeScaleY", Keyframe(0, s0), Keyframe(durationUs, s1)),
        };
    }

    private static JsonObject VideoMaterial(string id, string filePath, int width, int height)
    {
        var node = ParseTemplate(VideoMaterialTemplate);
        node["id"] = id;
        node["path"] = ToForwardSlashes(filePath);
        node["width"] = width;
        node["height"] = height;
        node["material_name"] = Path.GetFileName(filePath);
        return node;
    }

    // Field shapes per collection mirror the reference draft exactly — e.g. a
    // material_colors entry has no "type" field, while every other companion
    // carries one.
    private static JsonObject CompanionMaterial(string kind, string id) => kind switch
    {
        "canvas" => new JsonObject
        {
            ["id"] = id,
            ["type"] = "canvas_color",
            ["color"] = "",
            ["blur"] = 0.0,
            ["image"] = "",
            ["album_image"] = "",
            ["image_id"] = "",
            ["image_name"] = "",
            ["source_platform"] = 0,
            ["team_id"] = "",
        },
        "speed" => new JsonObject
        {
            ["id"] = id,
            ["type"] = "speed",
            ["mode"] = 0,
            ["speed"] = 1.0,
            ["curve_speed"] = null,
        },
        "sound" => new JsonObject
        {
            ["id"] = id,
            ["type"] = "",
            ["audio_channel_mapping"] = 0,
            ["is_config_open"] = false,
        },
        "color" => new JsonObject
        {
            ["id"] = id,
            ["is_color_clip"] = false,
            ["is_gradient"] = false,
            ["solid_color"] = "",
            ["gradient_colors"] = new JsonArray(),
            ["gradient_percents"] = new JsonArray(),
            ["gradient_angle"] = 90.0,
            ["width"] = 0.0,
            ["height"] = 0.0,
        },
        "placeholder" => new JsonObject
        {
            ["id"] = id,
            ["type"] = "placeholder_info",
            ["meta_type"] = "none",
            ["res_path"] = "",
            ["res_text"] = "",
            ["error_path"] = "",
            ["error_text"] = "",
        },
        _ => new JsonObject
        {
            ["id"] = id,
            ["type"] = "vocal_separation",
            ["choice"] = 0,
            ["removed_sounds"] = new JsonArray(),
            ["time_range"] = null,
            ["production_path"] = "",
            ["final_algorithm"] = "",
            ["enter_from"] = "",
        },
    };

    private static string BuildMetaInfo(
        Timeline timeline,
        IReadOnlyList<ImageInfo> images,
        string outputFolder,
        string draftName)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nowUs = nowMs * 1000;
        var fps = timeline.Fps;
        var clips = timeline.Scenes.SelectMany(s => s.Clips).ToList();
        var totalUs = Us(clips.Sum(c => c.DurationFrames), fps);

        var framesByFile = clips.GroupBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.DurationFrames), StringComparer.OrdinalIgnoreCase);

        var mediaEntries = new JsonArray();
        foreach (var image in images)
        {
            var frames = framesByFile.TryGetValue(image.FilePath, out var f) ? f : 0;
            mediaEntries.Add(new JsonObject
            {
                ["ai_group_type"] = "",
                ["create_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["duration"] = Us(frames, fps),
                ["enter_from"] = 0,
                ["extra_info"] = Path.GetFileName(image.FilePath),
                ["file_Path"] = ToForwardSlashes(image.FilePath),
                ["height"] = image.Height,
                ["id"] = NewLowerId(),
                ["import_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["import_time_ms"] = nowUs,
                ["item_source"] = 1,
                ["material_color_tag"] = "",
                ["md5"] = "",
                ["metetype"] = "photo",
                ["roughcut_time_range"] = new JsonObject { ["duration"] = -1, ["start"] = -1 },
                ["sub_time_range"] = new JsonObject { ["duration"] = -1, ["start"] = -1 },
                ["type"] = 0,
                ["width"] = image.Width,
            });
        }

        var meta = new JsonObject
        {
            ["draft_id"] = NewLowerId(),
            ["draft_name"] = draftName,
            ["draft_fold_path"] = ToForwardSlashes(outputFolder),
            ["draft_root_path"] = ToForwardSlashes(
                Path.GetDirectoryName(outputFolder.TrimEnd(Path.DirectorySeparatorChar)) ?? outputFolder),
            ["draft_type"] = "video",
            ["draft_new_version"] = DraftNewVersion,
            ["draft_cover"] = "",
            ["draft_is_invisible"] = false,
            ["draft_removable_storage_device"] = "",
            ["draft_need_rename_folder"] = false,
            ["draft_timeline_materials_size_"] = 0,
            ["draft_materials"] = new JsonArray(new JsonObject { ["type"] = 0, ["value"] = mediaEntries }),
            ["draft_materials_copied_info"] = new JsonArray(),
            ["draft_segment_extra_info"] = new JsonArray(),
            ["tm_draft_create"] = nowUs,
            ["tm_draft_modified"] = nowUs,
            ["tm_draft_removed"] = 0,
            ["tm_duration"] = totalUs,
        };

        return meta.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonArray ToNodeArray(IEnumerable<JsonObject> nodes) =>
        new(nodes.Select(n => (JsonNode)n!).ToArray());

    private static string ToForwardSlashes(string path) => path.Replace('\\', '/');

    private static JsonObject ParseTemplate(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("template parse failed");

    private const string PlatformTemplate = """
        {"os":"windows","os_version":"10.0.26200","app_id":359289,"app_version":"9.4.0","app_source":"cc","device_id":"6f45715d6efed8ecb2e77032cf767506","hard_disk_id":"","mac_address":"9f93f49408ae4068db25f5abb1ca86f9,0e5b23b3b9352421ccd763ace55e560f,ce0c79f29b088c447fa991bce98194a7"}
        """;

    private const string ConfigTemplate = """
        {"video_mute":false,"record_audio_last_index":1,"extract_audio_last_index":1,"original_sound_last_index":1,"subtitle_recognition_id":"","subtitle_taskinfo":[],"lyrics_recognition_id":"","lyrics_taskinfo":[],"subtitle_sync":true,"lyrics_sync":true,"voice_change_sync":false,"sticker_max_index":1,"adjust_max_index":1,"material_save_mode":0,"export_range":null,"maintrack_adsorb":true,"combination_max_index":1,"attachment_info":[],"zoom_info_params":null,"system_font_list":[],"multi_language_mode":"none","multi_language_main":"none","multi_language_current":"none","multi_language_list":[],"subtitle_keywords_config":null,"use_float_render":false,"hdr_vivid":false}
        """;

    private const string KeyframesTemplate = """
        {"videos":[],"audios":[],"texts":[],"stickers":[],"filters":[],"adjusts":[],"handwrites":[],"effects":[]}
        """;

    private const string UnevenTemplate = """
        {"composition":"","content":"","order":"","sub_template_info_list":[]}
        """;

    private const string SmartAdsTemplate = """
        {"page_from":"","routine":"","draft_url":""}
        """;

    private const string FunctionAssistantTemplate = """
        {"smart_rec_applied":false,"fixed_rec_applied":false,"auto_adjust":false,"auto_adjust_segid_list":[],"color_correction":false,"color_correction_segid_list":[],"enhance_quality":false,"smooth_slow_motion":false,"deflicker_segid_list":[],"video_noise_segid_list":[],"enhance_quality_segid_list":[],"smart_segid_list":[],"retouch":false,"retouch_segid_list":[],"enhande_voice":false,"enhance_voice_segid_list":[],"audio_noise_segid_list":[],"auto_caption":false,"auto_caption_segid_list":[],"auto_caption_template_id":"","caption_opt":false,"caption_opt_segid_list":[],"eye_correction":false,"eye_correction_segid_list":[],"normalize_loudness":false,"normalize_loudness_audio_denoise_segid_list":[],"auto_adjust_fixed":false,"auto_adjust_fixed_value":50.0,"color_correction_fixed":false,"color_correction_fixed_value":50.0,"normalize_loudness_fixed":false,"enhande_voice_fixed":false,"retouch_fixed":false,"enhance_quality_fixed":false,"smooth_slow_motion_fixed":false,"fps":{"num":0,"den":1}}
        """;

    private const string VideoMaterialTemplate = """
        {"id":"","unique_id":"","type":"photo","duration":10800000000,"path":"","media_path":"","local_id":"","has_audio":false,"reverse_path":"","intensifies_path":"","reverse_intensifies_path":"","intensifies_audio_path":"","cartoon_path":"","width":0,"height":0,"category_id":"","category_name":"local","material_id":"","material_name":"","material_url":"","crop":{"upper_left_x":0.0,"upper_left_y":0.0,"upper_right_x":1.0,"upper_right_y":0.0,"lower_left_x":0.0,"lower_left_y":1.0,"lower_right_x":1.0,"lower_right_y":1.0},"crop_ratio":"free","audio_fade":null,"crop_scale":1.0,"extra_type_option":0,"stable":{"stable_level":0,"matrix_path":"","time_range":{"start":0,"duration":0}},"matting":{"flag":0,"path":"","interactiveTime":[],"has_use_quick_brush":false,"strokes":[],"has_use_quick_eraser":false,"expansion":0,"feather":0,"reverse":false,"custom_matting_id":"","enable_matting_stroke":false,"is_clould":false,"mask_video_path":"","cloud_product_fps":0.0},"source":0,"source_platform":0,"formula_id":"","check_flag":62978047,"video_algorithm":{"algorithms":[],"time_range":null,"path":"","gameplay_configs":[],"ai_in_painting_config":[],"complement_frame_config":null,"motion_blur_config":null,"deflicker":null,"noise_reduction":null,"quality_enhance":null,"super_resolution":null,"ai_background_configs":[],"smart_complement_frame":null,"aigc_generate":null,"aigc_generate_list":[],"mouth_shape_driver":null,"ai_expression_driven":null,"ai_motion_driven":null,"image_interpretation":null,"story_video_modify_video_config":null,"skip_algorithm_index":[]},"is_unified_beauty_mode":false,"is_set_beauty_mode":false,"object_locked":null,"smart_motion":null,"multi_camera_info":null,"freeze":null,"picture_from":"none","picture_set_category_id":"","picture_set_category_name":"","team_id":"","local_material_id":"","origin_material_id":"","request_id":"","has_sound_separated":false,"is_text_edit_overdub":false,"is_ai_generate_content":false,"is_video_copilot_aigc_content":false,"aigc_type":"none","is_copyright":false,"aigc_history_id":"","aigc_item_id":"","local_material_from":"","smart_match_info":null,"beauty_face_preset_infos":[],"beauty_body_preset_id":"","beauty_face_auto_preset":{"preset_id":"","name":"","rate_map":"","scene":""},"beauty_face_auto_preset_infos":[],"beauty_body_auto_preset":null,"live_photo_timestamp":-1,"live_photo_cover_path":"","content_feature_info":null,"corner_pin":null,"surface_trackings":[],"video_mask_stroke":{"resource_id":"","path":"","type":"","color":"","size":0.0,"alpha":0.0,"distance":0.0,"texture":0.0,"horizontal_shift":0.0,"vertical_shift":0.0},"video_mask_shadow":{"resource_id":"","path":"","color":"","alpha":0.0,"blur":0.0,"distance":0.0,"angle":0.0},"pre_applied_vip_materials":[],"workflow_node_id":""}
        """;
}
