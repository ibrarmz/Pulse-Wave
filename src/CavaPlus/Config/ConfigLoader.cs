using System.Text.Json;
using Tomlyn;

namespace CavaPlus.Config;

public static class ConfigLoader
{
    private static readonly TomlSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
        if (config.Color.Mode == "gradient" && config.Color.Gradient.Count < 2)
            errors.Add("color.gradient debe tener al menos 2 colores cuando color.mode es 'gradient'");

        if (errors.Count > 0)
        {
            throw new ConfigException($"Configuración inválida en '{path}':\n- {string.Join("\n- ", errors)}");
        }
    }
}
