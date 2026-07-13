namespace CavaPlus.Config;

public sealed class CavaConfig
{
    public GeneralSection General { get; set; } = new();
    public BarsSection Bars { get; set; } = new();
    public ColorSection Color { get; set; } = new();

    public sealed class GeneralSection
    {
        public int Framerate { get; set; } = 60;
        public float Sensitivity { get; set; } = 100f;
        public string Channels { get; set; } = "stereo";
    }

    public sealed class BarsSection
    {
        public string Style { get; set; } = "classic";
        public int Width { get; set; } = 2;
        public int Gap { get; set; } = 1;
    }

    public sealed class ColorSection
    {
        public string Mode { get; set; } = "gradient";
        public string Solid { get; set; } = "#00FF00";
        public List<string> Gradient { get; set; } = new() { "#00FF00", "#FFFF00", "#FF0000" };
    }
}
