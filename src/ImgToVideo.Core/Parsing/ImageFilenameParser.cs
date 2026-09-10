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

    public ImageFilenameParser(NamingOptions? naming = null)
    {
        _naming = naming ?? new NamingOptions();
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
        var hasUnknownCode = false;
        var codeGroup = match.Groups["code"];
        if (codeGroup.Success)
        {
            if (MotionCodes.TryFromSuffix(codeGroup.Value, out var motion))
            {
                code = motion;
            }
            else
            {
                hasUnknownCode = true;
            }
        }

        parsed = new ParsedImageName(stem, scene, image, code, hasUnknownCode);
        return true;
    }

    public string FormatExample(int scene = 8, int index = 2, string? code = "PR")
    {
        var pad = new string('0', Math.Clamp(_naming.NumberPadding, 1, 4));
        var name = $"{_naming.ScenePrefix}{scene.ToString(pad)}{_naming.Separator}{index.ToString(pad)}";
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
        var codePart = naming.MotionCodesEnabled ? $"(?:{separator}(?<code>[A-Za-z]{{2}}))?" : string.Empty;
        return new Regex(
            $"^{prefix}(?<scene>\\d+){separator}(?<index>\\d+){codePart}$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
