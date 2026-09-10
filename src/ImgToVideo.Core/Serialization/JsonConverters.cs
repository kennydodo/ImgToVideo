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
            _ => throw new JsonException($"Unknown motion source \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, MotionSource value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            MotionSource.ExplicitCode => "explicit_code",
            MotionSource.AutoSelected => "auto_selected",
            _ => throw new JsonException($"Unhandled motion source {value}."),
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
            _ => throw new JsonException($"Unknown transition kind \"{value}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, TransitionKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            TransitionKind.None => "none",
            TransitionKind.Crossfade => "crossfade",
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
