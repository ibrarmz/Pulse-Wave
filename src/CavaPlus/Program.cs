using CavaPlus.Audio;
using CavaPlus.Bars;
using CavaPlus.Config;
using CavaPlus.Rendering;
using CavaPlus.Spectrum;
using Spectre.Console;

string? configPathOverride;
try
{
    configPathOverride = CliOptions.ExtractConfigPathOverride(args);
}
catch (ConfigException ex)
{
    AnsiConsole.MarkupLine($"[red]Error de configuración:[/] {ex.Message.EscapeMarkup()}");
    return 1;
}

string configPath = configPathOverride ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CavaPlus", "config.toml");

CavaConfig config;
try
{
    config = ConfigLoader.LoadOrCreateDefault(configPath);
    CliOptions.ApplyOverrides(config, args);
    ConfigLoader.Validate(config, configPath);
}
catch (ConfigException ex)
{
    AnsiConsole.MarkupLine($"[red]Error de configuración:[/] {ex.Message.EscapeMarkup()}");
    return 1;
}

const int fftLength = 1024;

var ringBuffer = new SampleRingBuffer(capacityPerChannel: fftLength * 4);

AudioCapture capture;
try
{
    capture = new AudioCapture(ringBuffer);
}
catch (NotSupportedException ex)
{
    AnsiConsole.MarkupLine($"[red]Error de audio:[/] {ex.Message.EscapeMarkup()}");
    return 1;
}

int sampleRate = capture.WaveFormat.SampleRate;
var spectrumProcessor = new SpectrumProcessor(fftLength);
var barMapperLeft = new BarMapper(sampleRate, fftLength, config.General.Sensitivity);
var barMapperRight = new BarMapper(sampleRate, fftLength, config.General.Sensitivity);

var barStyle = config.Bars.Style == "gradient" ? BarStyle.Gradient : BarStyle.Classic;
var colorStops = config.Color.Mode == "solid"
    ? new List<Color> { ParseHexColor(config.Color.Solid) }
    : config.Color.Gradient.Select(ParseHexColor).ToList();

var frameBuilder = new BarFrameBuilder(barStyle, config.Bars.Width, config.Bars.Gap, colorStops, config.Color.Mode == "solid");
var renderer = new ConsoleRenderer();

bool running = true;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    running = false;
};

renderer.Prepare();
capture.Start();

var leftSamples = new float[fftLength];
var rightSamples = new float[fftLength];
var frameDelay = TimeSpan.FromSeconds(1.0 / config.General.Framerate);

try
{
    while (running)
    {
        var frameStart = DateTime.UtcNow;

        ringBuffer.ReadLatest(fftLength, leftSamples, rightSamples);

        int barCount = Math.Max(1, Console.WindowWidth / (config.Bars.Width + config.Bars.Gap));
        int rows = Math.Max(1, Console.WindowHeight);

        float[] heights;
        if (config.General.Channels == "mono")
        {
            var mono = new float[fftLength];
            for (int i = 0; i < fftLength; i++)
            {
                mono[i] = (leftSamples[i] + rightSamples[i]) / 2f;
            }
            var magnitudes = spectrumProcessor.ComputeMagnitudes(mono);
            heights = barMapperLeft.MapToBars(magnitudes, barCount);
        }
        else
        {
            var leftMagnitudes = spectrumProcessor.ComputeMagnitudes(leftSamples);
            var rightMagnitudes = spectrumProcessor.ComputeMagnitudes(rightSamples);
            var leftHeights = barMapperLeft.MapToBars(leftMagnitudes, barCount / 2);
            var rightHeights = barMapperRight.MapToBars(rightMagnitudes, barCount / 2);
            heights = leftHeights.Concat(rightHeights.Reverse()).ToArray();
        }

        var frame = frameBuilder.BuildFrame(heights, rows);
        renderer.Render(frame);

        var elapsed = DateTime.UtcNow - frameStart;
        var remaining = frameDelay - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            Thread.Sleep(remaining);
        }
    }
}
finally
{
    capture.Stop();
    capture.Dispose();
    renderer.Restore();
}

return 0;

static Color ParseHexColor(string hex)
{
    hex = hex.TrimStart('#');
    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
    return new Color(r, g, b);
}
