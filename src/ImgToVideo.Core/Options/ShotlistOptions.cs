namespace ImgToVideo.Core.Options;

/// <summary>Prompt length budgets for the LLM shotlist: the batch image app's
/// master box and per-card boxes hold a fixed number of characters, and an
/// overflow would truncate or fail at generation time. 0 disables a check.</summary>
public sealed class ShotlistOptions
{
    /// <summary>Maximum characters for the shotlist master prompt (style).</summary>
    public int MasterPromptMaxChars { get; set; } = 1500;

    /// <summary>Maximum characters for a single image prompt (card).</summary>
    public int PromptMaxChars { get; set; } = 2400;
}
