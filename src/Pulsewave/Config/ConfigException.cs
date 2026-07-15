namespace Pulsewave.Config;

public sealed class ConfigException : Exception
{
    public ConfigException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
