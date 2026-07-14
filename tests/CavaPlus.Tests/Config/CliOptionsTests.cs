using CavaPlus.Config;

namespace CavaPlus.Tests.Config;

public class CliOptionsTests
{
    [Fact]
    public void ApplyOverrides_WithStyleAndChannelsFlags_OverridesConfig()
    {
        var config = new CavaConfig();
        CliOptions.ApplyOverrides(config, new[] { "--style", "gradient", "--channels", "mono" });

        Assert.Equal("gradient", config.Bars.Style);
        Assert.Equal("mono", config.General.Channels);
    }

    [Fact]
    public void ApplyOverrides_WithSensitivityFlag_ParsesFloat()
    {
        var config = new CavaConfig();
        CliOptions.ApplyOverrides(config, new[] { "--sensitivity", "150.5" });

        Assert.Equal(150.5f, config.General.Sensitivity);
    }

    [Fact]
    public void ApplyOverrides_WithUnknownFlag_Throws()
    {
        var config = new CavaConfig();
        Assert.Throws<ConfigException>(() => CliOptions.ApplyOverrides(config, new[] { "--bogus" }));
    }

    [Fact]
    public void ExtractConfigPathOverride_WhenPresent_ReturnsPath()
    {
        string? path = CliOptions.ExtractConfigPathOverride(new[] { "--config", "C:\\custom\\config.toml" });
        Assert.Equal("C:\\custom\\config.toml", path);
    }

    [Fact]
    public void ExtractConfigPathOverride_WhenAbsent_ReturnsNull()
    {
        string? path = CliOptions.ExtractConfigPathOverride(new[] { "--style", "classic" });
        Assert.Null(path);
    }

    [Fact]
    public void ExtractConfigPathOverride_WhenValueMissing_Throws()
    {
        Assert.Throws<ConfigException>(() => CliOptions.ExtractConfigPathOverride(new[] { "--config" }));
    }
}
