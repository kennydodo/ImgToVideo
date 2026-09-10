using System.Globalization;
using System.Text.RegularExpressions;
using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;

namespace ImgToVideo.Core.Parsing;

public sealed class ImageFilenameParser
{
    public static ImageFilenameParser Default { get; } = new(new NamingOptions());

    private readonly NamingOptions _naming;
    private readonly Regex _pattern;
    private readonly string _separator;

    public ImageFilenameParser(NamingOptions? naming = null)
    {
        _naming = naming ?? new NamingOptions();
        _separator = _naming.Separator;
        _pattern = BuildPattern(_naming);
    }

    public bool TryParse(string fileName, out ParsedImageName? parsed)
    {
        parsed = null;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var match = _pattern.Match(stem);
        if (!match.Success)
        {
            return false;
        }

        var scene = int.Parse(match.Groups["scene"].Value, CultureInfo.InvariantCulture);
        var image = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);

        MotionType? code = null;
        ImageType? type = null;
        var HasUnknownSuffix = false;
        var suffixesGroup = match.Groups["suffixes"];
        if (suffixesGroup.Success)
        {
            var segments = suffixesGroup.Value
                .Split(_separator, StringSplitOptions.RemoveEmptyEntries);
            if (!ClassifySuffixes(segments, out code, out type, out HasUnknownSuffix))
            {
                return false;
            }
        }

        parsed = new ParsedImageName(stem, scene, image, code, HasUnknownSuffix, type);
        return true;
    }

    private bool ClassifySuffixes(
        IReadOnlyList<string> segments,
        out MotionType? code,
        out ImageType? type,
        out bool HasUnknownSuffix)
    {
        code = null;
        type = null;
        HasUnknownSuffix = false;

        var position = segments.Count - 1;

        if (position >= 0 && MotionCodes.IsKnownSuffix(segments[position]))
        {
            if (!_naming.MotionCodesEnabled)
            {
                return false;
            }

            MotionCodes.TryFromSuffix(segments[position], out var motion);
            code = motion;
            position--;
        }

        if (position >= 0 && ImageTypes.IsKnownCode(segments[position]))
        {
            if (!_naming.TypeCodesEnabled)
            {
                return false;
            }

            ImageTypes.TryFromCode(segments[position], out var imageType);
            type = imageType;
            position--;
        }

        if (position >= 0)
        {
            HasUnknownSuffix = true;
        }

        return true;
    }

    public string FormatExample(int scene = 8, int index = 2, string? type = "SCN", string? code = "PR")
    {
        var pad = new string('0', Math.Clamp(_naming.NumberPadding, 1, 4));
        var name = $"{_naming.ScenePrefix}{scene.ToString(pad)}{_naming.Separator}{index.ToString(pad)}";
        if (type is not null && _naming.TypeCodesEnabled)
        {
            name += $"{_naming.Separator}{type.ToUpperInvariant()}";
        }

        if (code is not null && _naming.MotionCodesEnabled)
        {
            name += $"{_naming.Separator}{code.ToUpperInvariant()}";
        }

        return name;
    }

    private static Regex BuildPattern(NamingOptions naming)
    {
        var prefix = Regex.Escape(naming.ScenePrefix);
        var separator = Regex.Escape(naming.Separator);
        var suffixPart = naming.MotionCodesEnabled || naming.TypeCodesEnabled
            ? $"(?:{separator}(?<suffixes>[A-Za-z]{{2,4}}(?:{separator}[A-Za-z]{{2,4}})*))?"
            : string.Empty;
        return new Regex(
            $"^{prefix}(?<scene>\\d+){separator}(?<index>\\d+){suffixPart}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
