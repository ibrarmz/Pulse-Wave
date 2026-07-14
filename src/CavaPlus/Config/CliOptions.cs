using System.Globalization;

namespace CavaPlus.Config;

public static class CliOptions
{
    public static void ApplyOverrides(CavaConfig config, string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--style":
                    config.Bars.Style = RequireValue(args, ref i, "--style");
                    break;
                case "--channels":
                    config.General.Channels = RequireValue(args, ref i, "--channels");
                    break;
                case "--sensitivity":
                    config.General.Sensitivity = float.Parse(
                        RequireValue(args, ref i, "--sensitivity"), CultureInfo.InvariantCulture);
                    break;
                case "--config":
                    i++; // handled separately by ExtractConfigPathOverride before config is loaded
                    break;
                default:
                    throw new ConfigException($"Argumento desconocido: '{args[i]}'");
            }
        }
    }

    public static string? ExtractConfigPathOverride(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--config")
            {
                if (i + 1 >= args.Length)
                {
                    throw new ConfigException("--config requiere una ruta como valor");
                }
                return args[i + 1];
            }
        }
        return null;
    }

    private static string RequireValue(string[] args, ref int i, string flagName)
    {
        if (i + 1 >= args.Length)
        {
            throw new ConfigException($"{flagName} requiere un valor");
        }
        i++;
        return args[i];
    }
}
