using CavaPlus.Config;

namespace CavaPlus.Tests.Config;

public class ConfigLoaderTests
{
    private static string TempConfigPath() =>
        Path.Combine(Path.GetTempPath(), $"cavaplus-test-{Guid.NewGuid():N}.toml");

    [Fact]
    public void LoadOrCreateDefault_WhenFileMissing_CreatesFileAndReturnsDefaults()
    {
        string path = TempConfigPath();
        try
        {
            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.True(File.Exists(path));
            Assert.Equal(60, config.General.Framerate);
            Assert.Equal("stereo", config.General.Channels);
            Assert.Equal("classic", config.Bars.Style);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreateDefault_WithExistingValidFile_ReturnsParsedValues()
    {
        string path = TempConfigPath();
        try
        {
            File.WriteAllText(path, """
                [general]
                framerate = 30
                sensitivity = 150.0
                channels = "mono"

                [bars]
                style = "gradient"
                width = 3
                gap = 0

                [color]
                mode = "solid"
                solid = "#112233"
                gradient = ["#000000", "#FFFFFF"]
                """);

            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.Equal(30, config.General.Framerate);
            Assert.Equal("mono", config.General.Channels);
            Assert.Equal("gradient", config.Bars.Style);
            Assert.Equal("solid", config.Color.Mode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_WithInvalidChannels_ThrowsConfigException()
    {
        var config = new CavaConfig();
        config.General.Channels = "surround";

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(config, "config.toml"));
        Assert.Contains("channels", ex.Message);
    }

    [Fact]
    public void Validate_WithGradientModeAndOneColor_ThrowsConfigException()
    {
        var config = new CavaConfig();
        config.Color.Mode = "gradient";
        config.Color.Palette = "custom";
        config.Color.Gradient = new List<string> { "#FFFFFF" };

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(config, "config.toml"));
        Assert.Contains("gradient", ex.Message);
    }

    [Fact]
    public void LoadOrCreateDefault_WhenFileMissing_IncludesNowPlayingDefaults()
    {
        string path = TempConfigPath();
        try
        {
            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.True(config.NowPlaying.Enabled);
            Assert.Equal("— sin reproducción —", config.NowPlaying.Placeholder);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreateDefault_WithExistingValidFile_ParsesNowPlayingSection()
    {
        string path = TempConfigPath();
        try
        {
            File.WriteAllText(path, """
                [general]
                framerate = 30
                sensitivity = 150.0
                channels = "mono"

                [bars]
                style = "gradient"
                width = 3
                gap = 0

                [color]
                mode = "solid"
                solid = "#112233"
                gradient = ["#000000", "#FFFFFF"]

                [nowPlaying]
                enabled = false
                placeholder = "nada sonando"
                """);

            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.False(config.NowPlaying.Enabled);
            Assert.Equal("nada sonando", config.NowPlaying.Placeholder);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreateDefault_WhenFileMissing_IncludesColorReactivityDefaults()
    {
        string path = TempConfigPath();
        try
        {
            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.Equal("rainbow", config.Color.Palette);
            Assert.True(config.Color.Reactive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreateDefault_WithExistingValidFile_ParsesPaletteAndReactive()
    {
        string path = TempConfigPath();
        try
        {
            File.WriteAllText(path, """
                [general]
                framerate = 30
                sensitivity = 150.0
                channels = "mono"

                [bars]
                style = "gradient"
                width = 3
                gap = 0

                [color]
                mode = "gradient"
                solid = "#112233"
                gradient = ["#000000", "#FFFFFF"]
                palette = "fire"
                reactive = false
                """);

            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.Equal("fire", config.Color.Palette);
            Assert.False(config.Color.Reactive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_WithInvalidPalette_ThrowsConfigException()
    {
        var config = new CavaConfig();
        config.Color.Palette = "bogus";

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(config, "config.toml"));
        Assert.Contains("palette", ex.Message);
    }

    [Fact]
    public void Validate_WithNamedPaletteAndTooFewGradientColors_DoesNotThrow()
    {
        var config = new CavaConfig();
        config.Color.Mode = "gradient";
        config.Color.Palette = "fire";
        config.Color.Gradient = new List<string> { "#FFFFFF" };

        var exception = Record.Exception(() => ConfigLoader.Validate(config, "config.toml"));

        Assert.Null(exception);
    }
}
