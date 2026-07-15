using System.Text.Json;
using CavaPlus.Rendering;
using Tomlyn;

namespace CavaPlus.Config;

public static class ConfigLoader
{
    // CamelCase only lowercases the first letter, so this maps e.g. Framerate -> framerate.
    // That happens to match every current config key because they're all single words;
    // a future multi-word property (e.g. MaxFrameRate -> maxFrameRate) would NOT match this
    // schema's flat lowercase TOML keys and would silently round-trip to its default value.
    private static readonly TomlSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] ValidPalettes = ColorPalettes.PresetNames.Append("custom").ToArray();

    public static CavaConfig LoadOrCreateDefault(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new CavaConfig();
            Save(defaultConfig, path);
            return defaultConfig;
        }

        string text = File.ReadAllText(path);
        CavaConfig? config;
        try
        {
            config = TomlSerializer.Deserialize<CavaConfig>(text, Options);
        }
        catch (Exception ex)
        {
            throw new ConfigException($"No se pudo leer el archivo de configuración '{path}': {ex.Message}", ex);
        }

        // Deserialize<T> is nullable-annotated; satisfies that, not a real observed failure mode
        // (malformed TOML syntax throws and is caught above, it doesn't deserialize to null).
        config ??= new CavaConfig();

        Validate(config, path);
        return config;
    }

    public static void Save(CavaConfig config, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, TomlSerializer.Serialize(config, Options));
    }

    public static void Validate(CavaConfig config, string path)
    {
        var errors = new List<string>();

        if (config.General.Framerate <= 0)
            errors.Add("general.framerate debe ser mayor a 0");
        if (config.General.Channels != "mono" && config.General.Channels != "stereo")
            errors.Add("general.channels debe ser 'mono' o 'stereo'");
        if (config.Bars.Style != "classic" && config.Bars.Style != "gradient")
            errors.Add("bars.style debe ser 'classic' o 'gradient'");
        if (config.Bars.Width <= 0)
            errors.Add("bars.width debe ser mayor a 0");
        if (config.Bars.Gap < 0)
            errors.Add("bars.gap no puede ser negativo");
        if (config.Color.Mode != "solid" && config.Color.Mode != "gradient")
            errors.Add("color.mode debe ser 'solid' o 'gradient'");
        if (!ValidPalettes.Contains(config.Color.Palette))
            errors.Add("color.palette debe ser 'rainbow', 'neon', 'fire', 'ice', 'mono' o 'custom'");
        if (config.Color.Mode == "gradient" && config.Color.Palette == "custom" && config.Color.Gradient.Count < 2)
            errors.Add("color.gradient debe tener al menos 2 colores cuando color.mode es 'gradient' y color.palette es 'custom'");

        if (errors.Count > 0)
        {
            throw new ConfigException($"Configuración inválida en '{path}':\n- {string.Join("\n- ", errors)}");
        }
    }
}
