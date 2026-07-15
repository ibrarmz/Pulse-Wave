using CavaPlus.Audio;
using CavaPlus.Bars;
using CavaPlus.Config;
using CavaPlus.Media;
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
catch (Exception ex)
{
    // WasapiLoopbackCapture's own constructor (not AudioCapture's format checks) throws here
    // when there's no active default output device - a real, spec-called-out failure mode.
    AnsiConsole.MarkupLine($"[red]Error de audio:[/] No se pudo iniciar la captura de audio: {ex.Message.EscapeMarkup()}");
    return 1;
}

int sampleRate = capture.WaveFormat.SampleRate;
var spectrumProcessor = new SpectrumProcessor(fftLength);
var barMapperLeft = new BarMapper(sampleRate, fftLength, config.General.Sensitivity);
var barMapperRight = new BarMapper(sampleRate, fftLength, config.General.Sensitivity);
var loudnessTracker = new LoudnessTracker();
var bassBeatDetector = new BassBeatDetector(sampleRate, fftLength);

var barStyle = config.Bars.Style == "gradient" ? BarStyle.Gradient : BarStyle.Classic;

List<Color> colorStops;
try
{
    colorStops = config.Color.Mode == "solid"
        ? new List<Color> { ColorPalettes.ParseHexColor(config.Color.Solid) }
        : ColorPalettes.Resolve(config.Color.Palette, config.Color.Gradient);
}
catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or ArgumentException)
{
    AnsiConsole.MarkupLine(
        $"[red]Error de configuración:[/] color inválido en '{configPath}': {ex.Message.EscapeMarkup()}. " +
        "Los colores deben ser hexadecimales de 6 dígitos, por ejemplo '#00FF00'.");
    capture.Dispose();
    return 1;
}

var frameBuilder = new BarFrameBuilder(barStyle, config.Bars.Width, config.Bars.Gap, colorStops, config.Color.Mode == "solid");
var renderer = new ConsoleRenderer();

var nowPlayingWatcher = new NowPlayingWatcher();
if (config.NowPlaying.Enabled)
{
    try
    {
        await nowPlayingWatcher.StartAsync();
    }
    catch
    {
        // Non-fatal: if this throws unexpectedly, the overlay just keeps showing the
        // configured placeholder instead of real metadata - the rest of the app is unaffected.
    }
}

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
        float[] bassMagnitudes;
        if (config.General.Channels == "mono")
        {
            var mono = new float[fftLength];
            for (int i = 0; i < fftLength; i++)
            {
                mono[i] = (leftSamples[i] + rightSamples[i]) / 2f;
            }
            var magnitudes = spectrumProcessor.ComputeMagnitudes(mono);
            heights = barMapperLeft.MapToBars(magnitudes, barCount);
            bassMagnitudes = magnitudes;
        }
        else
        {
            var leftMagnitudes = spectrumProcessor.ComputeMagnitudes(leftSamples);
            var rightMagnitudes = spectrumProcessor.ComputeMagnitudes(rightSamples);
            // Split so the two halves sum back to barCount even when it's odd,
            // otherwise the frame renders one bar-slot narrower than the terminal.
            int rightBarCount = barCount / 2;
            int leftBarCount = barCount - rightBarCount;
            var leftHeights = barMapperLeft.MapToBars(leftMagnitudes, leftBarCount);
            var rightHeights = barMapperRight.MapToBars(rightMagnitudes, rightBarCount);
            // Each MapToBars result runs low frequency (bass) -> high frequency (treble).
            // Mirror it so bass sits at the center and treble at the outer edges (the
            // conventional stereo spectrum layout) instead of bass at both edges, which left
            // the middle looking flat since treble energy is usually much lower than bass.
            heights = leftHeights.Reverse().Concat(rightHeights).ToArray();
            // Sum both channels' magnitudes so bass-beat detection reacts to either channel's
            // kick/bass energy, without running a third FFT. leftMagnitudes is already fully
            // consumed by MapToBars above and isn't read again, so summing in place avoids an
            // extra per-frame allocation on this 60fps hot path.
            for (int i = 0; i < leftMagnitudes.Length; i++)
            {
                leftMagnitudes[i] += rightMagnitudes[i];
            }
            bassMagnitudes = leftMagnitudes;
        }

        float brightnessFactor = loudnessTracker.Update(leftSamples, rightSamples);
        float flashIntensity = bassBeatDetector.Update(bassMagnitudes);

        var frame = frameBuilder.BuildFrame(heights, rows);

        if (config.Color.Reactive)
        {
            ColorReactivity.Apply(frame, brightnessFactor, flashIntensity);
        }

        if (config.NowPlaying.Enabled)
        {
            var current = nowPlayingWatcher.Current;
            string nowPlayingText = current is not null
                ? $"{current.Title} — {current.Artist}"
                : config.NowPlaying.Placeholder;
            OverlayText.Apply(frame, nowPlayingText, row: 0, color: Color.White);
        }

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
