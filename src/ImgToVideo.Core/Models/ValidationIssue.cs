namespace ImgToVideo.Core.Models;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Code, string Message)
{
    public static bool HasErrors(IEnumerable<ValidationIssue> issues) =>
        issues.Any(i => i.Severity == ValidationSeverity.Error);
}
