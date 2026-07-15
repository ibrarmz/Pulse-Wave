namespace Pulsewave.Config;

public sealed class CavaConfig
{
    public GeneralSection General { get; set; } = new();
    public BarsSection Bars { get; set; } = new();
    public ColorSection Color { get; set; } = new();
    public NowPlayingSection NowPlaying { get; set; } = new();

    public sealed class GeneralSection
    {
        public int Framerate { get; set; } = 60;
        public float Sensitivity { get; set; } = 100f;
        public string Channels { get; set; } = "stereo";
    }

    public sealed class BarsSection
    {
        public string Style { get; set; } = "classic";
        public int Width { get; set; } = 4;
        public int Gap { get; set; } = 2;
    }

    public sealed class ColorSection
    {
        public string Mode { get; set; } = "gradient";
        public string Solid { get; set; } = "#00FF00";
        // Bottom -> top rainbow (red at the base rising through the spectrum to violet at the peak).
        public List<string> Gradient { get; set; } = new()
        {
            "#FF0000", "#FF7F00", "#FFFF00", "#00FF00", "#0000FF", "#4B0082", "#9400D3",
        };
        public string Palette { get; set; } = "rainbow";
        public bool Reactive { get; set; } = true;
    }

    public sealed class NowPlayingSection
    {
        public bool Enabled { get; set; } = true;
        public string Placeholder { get; set; } = "— sin reproducción —";
    }
}
