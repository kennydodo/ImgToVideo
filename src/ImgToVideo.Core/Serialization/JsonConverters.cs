using System.Text.Json;
using System.Text.Json.Serialization;
using ImgToVideo.Core.Models;

namespace ImgToVideo.Core.Serialization;

public sealed class MotionTypeJsonConverter : JsonConverter<MotionType>
{
    public override MotionType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("Motion value is null.");
        return value switch
        {
            "static" => MotionType.Static,
            "zoom_in" => MotionType.ZoomIn,
            "zoom_out" => MotionType.ZoomOut,
            "pan_left" => MotionType.PanLeft,
            "pan_right" => MotionType.PanRight,
            "pan_up" => MotionType.PanUp,
            "pan_down" => MotionType.PanDown,
            "pan_reveal" => MotionType.PanReveal,
            _ => throw new JsonException($"Unknown motion \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, MotionType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            MotionType.Static => "static",
            MotionType.ZoomIn => "zoom_in",
            MotionType.ZoomOut => "zoom_out",
            MotionType.PanLeft => "pan_left",
            MotionType.PanRight => "pan_right",
            MotionType.PanUp => "pan_up",
            MotionType.PanDown => "pan_down",
            MotionType.PanReveal => "pan_reveal",
            _ => throw new JsonException($"Unhandled motion {value}."),
        });
    }
}

public sealed class MotionSourceJsonConverter : JsonConverter<MotionSource>
{
    public override MotionSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("Motion source value is null.");
        return value switch
        {
            "explicit_code" => MotionSource.ExplicitCode,
            "auto_selected" => MotionSource.AutoSelected,
            "override" => MotionSource.Override,
            _ => throw new JsonException($"Unknown motion source \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, MotionSource value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            MotionSource.ExplicitCode => "explicit_code",
            MotionSource.AutoSelected => "auto_selected",
            MotionSource.Override => "override",
            _ => throw new JsonException($"Unhandled motion source {value}."),
        });
    }
}

public sealed class EasingModeJsonConverter : JsonConverter<EasingMode>
{
    public override EasingMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("Easing value is null.");
        return value switch
        {
            "linear" => EasingMode.Linear,
            "ease_in" => EasingMode.EaseIn,
            "ease_out" => EasingMode.EaseOut,
            "ease_in_out" => EasingMode.EaseInOut,
            _ => throw new JsonException($"Unknown easing \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, EasingMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            EasingMode.Linear => "linear",
            EasingMode.EaseIn => "ease_in",
            EasingMode.EaseOut => "ease_out",
            EasingMode.EaseInOut => "ease_in_out",
            _ => throw new JsonException($"Unhandled easing {value}."),
        });
    }
}

public sealed class TransitionAlignmentJsonConverter : JsonConverter<TransitionAlignment>
{
    public override TransitionAlignment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("Transition alignment value is null.");
        return value switch
        {
            "centered" => TransitionAlignment.Centered,
            "late" => TransitionAlignment.Late,
            _ => throw new JsonException($"Unknown transition alignment \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, TransitionAlignment value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            TransitionAlignment.Centered => "centered",
            TransitionAlignment.Late => "late",
            _ => throw new JsonException($"Unhandled transition alignment {value}."),
        });
    }
}

public sealed class TransitionKindJsonConverter : JsonConverter<TransitionKind>
{
    public override TransitionKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("Transition kind value is null.");
        return value switch
        {
            "none" => TransitionKind.None,
            "crossfade" => TransitionKind.Crossfade,
            "fade_black" => TransitionKind.FadeBlack,
            "fade_white" => TransitionKind.FadeWhite,
            "wipe_left" => TransitionKind.WipeLeft,
            "wipe_right" => TransitionKind.WipeRight,
            "wipe_up" => TransitionKind.WipeUp,
            "wipe_down" => TransitionKind.WipeDown,
            "slide_left" => TransitionKind.SlideLeft,
            "slide_right" => TransitionKind.SlideRight,
            "dissolve" => TransitionKind.Dissolve,
            "circle_open" => TransitionKind.CircleOpen,
            "circle_close" => TransitionKind.CircleClose,
            "smooth_left" => TransitionKind.SmoothLeft,
            "smooth_right" => TransitionKind.SmoothRight,
            _ => throw new JsonException($"Unknown transition kind \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, TransitionKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            TransitionKind.None => "none",
            TransitionKind.Crossfade => "crossfade",
            TransitionKind.FadeBlack => "fade_black",
            TransitionKind.FadeWhite => "fade_white",
            TransitionKind.WipeLeft => "wipe_left",
            TransitionKind.WipeRight => "wipe_right",
            TransitionKind.WipeUp => "wipe_up",
            TransitionKind.WipeDown => "wipe_down",
            TransitionKind.SlideLeft => "slide_left",
            TransitionKind.SlideRight => "slide_right",
            TransitionKind.Dissolve => "dissolve",
            TransitionKind.CircleOpen => "circle_open",
            TransitionKind.CircleClose => "circle_close",
            TransitionKind.SmoothLeft => "smooth_left",
            TransitionKind.SmoothRight => "smooth_right",
            _ => throw new JsonException($"Unhandled transition kind {value}."),
        });
    }
}

public sealed class ImageTypeJsonConverter : JsonConverter<ImageType>
{
    public override ImageType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? throw new JsonException("Image type value is null.");
        return value switch
        {
            "scene" => ImageType.Scene,
            "close_up" => ImageType.CloseUp,
            "infographic" => ImageType.Infographic,
            "comparison" => ImageType.Comparison,
            "process" => ImageType.Process,
            "hybrid" => ImageType.Hybrid,
            "overview" => ImageType.Overview,
            _ => throw new JsonException($"Unknown image type \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, ImageType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ImageType.Scene => "scene",
            ImageType.CloseUp => "close_up",
            ImageType.Infographic => "infographic",
            ImageType.Comparison => "comparison",
            ImageType.Process => "process",
            ImageType.Hybrid => "hybrid",
            ImageType.Overview => "overview",
            _ => throw new JsonException($"Unhandled image type {value}."),
        });
    }
}
