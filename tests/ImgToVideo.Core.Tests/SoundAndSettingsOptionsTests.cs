using ImgToVideo.Core.Models;
using ImgToVideo.Core.Options;
using Xunit;

namespace ImgToVideo.Core.Tests;

public class SoundAndSettingsOptionsTests
{
    [Fact]
    public void A_saved_personal_pop_default_seeds_a_new_project()
    {
        var options = new ProjectOptions();

        Assert.True(options.Sound.ApplyPersonalDefault("pop_marimba"));
        Assert.Equal("pop_marimba", options.Sound.DefaultPop);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not_a_sound")]
    public void A_missing_or_unknown_personal_default_leaves_the_built_in_one(string? saved)
    {
        var options = new ProjectOptions();

        Assert.False(options.Sound.ApplyPersonalDefault(saved));
        Assert.Equal(SoundCatalog.DefaultPopId, options.Sound.DefaultPop);
    }

    [Fact]
    public void Personal_default_matching_ignores_case_and_stores_the_canonical_id()
    {
        var options = new ProjectOptions();

        Assert.True(options.Sound.ApplyPersonalDefault("POP_PING"));
        Assert.Equal("pop_ping", options.Sound.DefaultPop);
    }

    [Fact]
    public void Default_pop_survives_the_options_file_round_trip()
    {
        var options = new ProjectOptions();
        options.Sound.DefaultPop = "pop_thump";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "i2v-sound-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            OptionsJson.Save(options, path);
            var text = System.IO.File.ReadAllText(path);

            Assert.Contains("default_pop", text);
            Assert.Contains("pop_thump", text);
            Assert.Equal("pop_thump", OptionsJson.Load(path).Sound.DefaultPop);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void An_options_file_without_a_sound_section_gets_the_built_in_default()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "i2v-nosound-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            System.IO.File.WriteAllText(path, "{ \"schema_version\": 1 }");

            Assert.Equal(SoundCatalog.DefaultPopId, OptionsJson.Load(path).Sound.DefaultPop);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Saving_settings_keeps_the_shotlist_limits_the_dialog_has_no_fields_for()
    {
        var previous = new ProjectOptions();
        previous.Shotlist.MasterPromptMaxChars = 1234;
        previous.Shotlist.PromptMaxChars = 777;

        // What the dialog builds: every section it edits, Shotlist left at its defaults.
        var rebuilt = new ProjectOptions();
        rebuilt.KeepSectionsNotEditedInSettings(previous);

        Assert.Equal(1234, rebuilt.Shotlist.MasterPromptMaxChars);
        Assert.Equal(777, rebuilt.Shotlist.PromptMaxChars);
    }
}
