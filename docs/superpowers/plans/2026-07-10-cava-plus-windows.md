# Pulsewave (Windows audio visualizer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `Pulsewave`, a C#/.NET 8 console app that visualizes Windows system audio (WASAPI loopback) as cava-style spectrum bars in Windows Terminal.

**Architecture:** A capture thread (NAudio `WasapiLoopbackCapture`) writes stereo float samples into a thread-safe `SampleRingBuffer`. A render loop, running independently at a fixed framerate, reads the latest window from the buffer, runs FFT (`NAudio.Dsp.FastFourierTransform`), maps magnitudes to bar heights with logarithmic frequency grouping (`BarMapper`), builds a pure in-memory frame of glyphs+colors (`BarFrameBuilder`), and draws it with Spectre.Console (`ConsoleRenderer`). Configuration loads from a TOML file with CLI flag overrides.

**Tech Stack:** .NET 8, NAudio (WASAPI loopback capture + FFT), Spectre.Console (terminal rendering), Tomlyn (TOML parsing), xUnit (tests).

**Reference spec:** `docs/superpowers/specs/2026-07-10-cava-plus-windows-design.md`

---

## File structure

```
Pulsewave.sln
src/Pulsewave/
  Pulsewave.csproj
  Program.cs
  Audio/
    SampleRingBuffer.cs      # thread-safe circular buffer of stereo float samples
    AudioCapture.cs          # WasapiLoopbackCapture wrapper, writes into SampleRingBuffer
  Spectrum/
    SpectrumProcessor.cs     # windowing + FFT -> magnitude bins
  Bars/
    BarMapper.cs             # magnitude bins -> smoothed 0..1 bar heights (log frequency grouping)
  Rendering/
    ColorGradient.cs         # pure color-stop interpolation
    BarFrameBuilder.cs       # bar heights + style/color config -> BarCell[,] grid (pure)
    ConsoleRenderer.cs       # BarCell[,] -> Spectre.Console output (side effects)
  Config/
    CavaConfig.cs            # POCO matching config.toml shape, with defaults
    ConfigException.cs       # config-specific error type
    ConfigLoader.cs          # load/create-default/save/validate config.toml (Tomlyn)
    CliOptions.cs            # parse CLI args, apply overrides on top of CavaConfig
tests/Pulsewave.Tests/
  Pulsewave.Tests.csproj
  Audio/SampleRingBufferTests.cs
  Spectrum/SpectrumProcessorTests.cs
  Bars/BarMapperTests.cs
  Rendering/ColorGradientTests.cs
  Rendering/BarFrameBuilderTests.cs
  Config/ConfigLoaderTests.cs
  Config/CliOptionsTests.cs
```

`AudioCapture` and `ConsoleRenderer` touch real hardware/console and are validated manually (per the spec's testing section), not with automated unit tests. Everything else is pure and gets unit tests.

---

### Task 1: Scaffold solution and projects

**Files:**
- Create: `Pulsewave.sln`
- Create: `src/Pulsewave/Pulsewave.csproj`
- Create: `src/Pulsewave/Program.cs`
- Create: `tests/Pulsewave.Tests/Pulsewave.Tests.csproj`
- Create: `tests/Pulsewave.Tests/SanityTests.cs`

- [ ] **Step 1: Create the solution and both projects**

Run from the repo root (`C:\path\to\Pulse-Wave`):

```bash
dotnet new sln -n Pulsewave
dotnet new console -n Pulsewave -o src/Pulsewave --framework net8.0
dotnet new xunit -n Pulsewave.Tests -o tests/Pulsewave.Tests --framework net8.0
dotnet sln add src/Pulsewave/Pulsewave.csproj tests/Pulsewave.Tests/Pulsewave.Tests.csproj
dotnet add tests/Pulsewave.Tests/Pulsewave.Tests.csproj reference src/Pulsewave/Pulsewave.csproj
```

- [ ] **Step 2: Add NuGet packages to the app project**

```bash
dotnet add src/Pulsewave/Pulsewave.csproj package NAudio
dotnet add src/Pulsewave/Pulsewave.csproj package Spectre.Console
dotnet add src/Pulsewave/Pulsewave.csproj package Tomlyn
```

- [ ] **Step 3: Replace the generated `Program.cs` with a placeholder entry point**

`src/Pulsewave/Program.cs`:

```csharp
Console.WriteLine("Pulsewave scaffold ready.");
return 0;
```

(This gets replaced with the real pipeline in Task 11.)

- [ ] **Step 4: Write a sanity test**

`tests/Pulsewave.Tests/SanityTests.cs`:

```csharp
namespace Pulsewave.Tests;

public class SanityTests
{
    [Fact]
    public void Sanity_ProjectWiringWorks()
    {
        Assert.Equal(4, 2 + 2);
    }
}
```

- [ ] **Step 5: Run the build and the test suite**

```bash
dotnet build Pulsewave.sln
dotnet test Pulsewave.sln
```

Expected: build succeeds, 1 test passes.

- [ ] **Step 6: Commit**

```bash
git add Pulsewave.sln src/Pulsewave tests/Pulsewave.Tests
git commit -m "chore: scaffold Pulsewave solution with app and test projects"
```

---

### Task 2: SampleRingBuffer

**Files:**
- Create: `src/Pulsewave/Audio/SampleRingBuffer.cs`
- Create: `tests/Pulsewave.Tests/Audio/SampleRingBufferTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Pulsewave.Tests/Audio/SampleRingBufferTests.cs`:

```csharp
using Pulsewave.Audio;

namespace Pulsewave.Tests.Audio;

public class SampleRingBufferTests
{
    [Fact]
    public void ReadLatest_BeforeAnyWrite_ReturnsZeros()
    {
        var buffer = new SampleRingBuffer(capacityPerChannel: 8);
        var left = new float[4];
        var right = new float[4];

        buffer.ReadLatest(4, left, right);

        Assert.All(left, v => Assert.Equal(0f, v));
        Assert.All(right, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void ReadLatest_AfterPartialWrite_PadsWithZerosAndPreservesOrder()
    {
        var buffer = new SampleRingBuffer(capacityPerChannel: 8);
        buffer.WriteInterleavedStereo(new float[] { 1f, -1f, 2f, -2f }); // 2 frames: (1,-1), (2,-2)
        var left = new float[4];
        var right = new float[4];

        buffer.ReadLatest(4, left, right);

        Assert.Equal(new float[] { 0f, 0f, 1f, 2f }, left);
        Assert.Equal(new float[] { 0f, 0f, -1f, -2f }, right);
    }

    [Fact]
    public void WriteInterleavedStereo_WrapsAroundWhenExceedingCapacity()
    {
        var buffer = new SampleRingBuffer(capacityPerChannel: 4);
        // 6 frames into a 4-frame-capacity buffer -> only the last 4 survive
        buffer.WriteInterleavedStereo(new float[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 });
        var left = new float[4];
        var right = new float[4];

        buffer.ReadLatest(4, left, right);

        Assert.Equal(new float[] { 3f, 4f, 5f, 6f }, left);
        Assert.Equal(new float[] { 3f, 4f, 5f, 6f }, right);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test Pulsewave.sln --filter SampleRingBufferTests
```

Expected: FAIL — `Pulsewave.Audio.SampleRingBuffer` does not exist.

- [ ] **Step 3: Implement `SampleRingBuffer`**

`src/Pulsewave/Audio/SampleRingBuffer.cs`:

```csharp
namespace Pulsewave.Audio;

public sealed class SampleRingBuffer
{
    private readonly float[] _left;
    private readonly float[] _right;
    private readonly int _capacity;
    private readonly object _lock = new();
    private int _writePosition;
    private int _samplesWritten;

    public SampleRingBuffer(int capacityPerChannel)
    {
        _capacity = capacityPerChannel;
        _left = new float[capacityPerChannel];
        _right = new float[capacityPerChannel];
    }

    public void WriteInterleavedStereo(float[] interleaved)
    {
        lock (_lock)
        {
            int frames = interleaved.Length / 2;
            for (int i = 0; i < frames; i++)
            {
                _left[_writePosition] = interleaved[i * 2];
                _right[_writePosition] = interleaved[i * 2 + 1];
                _writePosition = (_writePosition + 1) % _capacity;
            }
            _samplesWritten = Math.Min(_samplesWritten + frames, _capacity);
        }
    }

    public void ReadLatest(int frameCount, float[] leftOut, float[] rightOut)
    {
        lock (_lock)
        {
            int available = Math.Min(frameCount, _samplesWritten);
            int startPos = (_writePosition - available + _capacity) % _capacity;
            int padding = frameCount - available;

            Array.Clear(leftOut, 0, padding);
            Array.Clear(rightOut, 0, padding);

            for (int i = 0; i < available; i++)
            {
                int pos = (startPos + i) % _capacity;
                leftOut[padding + i] = _left[pos];
                rightOut[padding + i] = _right[pos];
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test Pulsewave.sln --filter SampleRingBufferTests
```

Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Pulsewave/Audio/SampleRingBuffer.cs tests/Pulsewave.Tests/Audio/SampleRingBufferTests.cs
git commit -m "feat: add thread-safe SampleRingBuffer for audio samples"
```

---

### Task 3: SpectrumProcessor (windowing + FFT)

**Files:**
- Create: `src/Pulsewave/Spectrum/SpectrumProcessor.cs`
- Create: `tests/Pulsewave.Tests/Spectrum/SpectrumProcessorTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Pulsewave.Tests/Spectrum/SpectrumProcessorTests.cs`:

```csharp
using Pulsewave.Spectrum;

namespace Pulsewave.Tests.Spectrum;

public class SpectrumProcessorTests
{
    [Fact]
    public void ComputeMagnitudes_PureSineWave_PeaksAtExpectedBin()
    {
        const int sampleRate = 44100;
        const int fftLength = 1024;
        const float frequencyHz = 440f;

        var samples = new float[fftLength];
        for (int i = 0; i < fftLength; i++)
        {
            samples[i] = MathF.Sin(2 * MathF.PI * frequencyHz * i / sampleRate);
        }

        var processor = new SpectrumProcessor(fftLength);
        var magnitudes = processor.ComputeMagnitudes(samples);

        int expectedBin = (int)MathF.Round(frequencyHz * fftLength / sampleRate);

        int peakBin = 1;
        for (int i = 2; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > magnitudes[peakBin]) peakBin = i;
        }

        Assert.InRange(peakBin, expectedBin - 1, expectedBin + 1);
    }

    [Fact]
    public void Constructor_WithNonPowerOfTwoLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SpectrumProcessor(1000));
    }

    [Fact]
    public void ComputeMagnitudes_WithWrongSampleLength_Throws()
    {
        var processor = new SpectrumProcessor(1024);
        Assert.Throws<ArgumentException>(() => processor.ComputeMagnitudes(new float[500]));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test Pulsewave.sln --filter SpectrumProcessorTests
```

Expected: FAIL — `Pulsewave.Spectrum.SpectrumProcessor` does not exist.

- [ ] **Step 3: Implement `SpectrumProcessor`**

`src/Pulsewave/Spectrum/SpectrumProcessor.cs`:

```csharp
using NAudio.Dsp;

namespace Pulsewave.Spectrum;

public sealed class SpectrumProcessor
{
    private readonly int _fftLength;
    private readonly int _m;
    private readonly float[] _window;

    public SpectrumProcessor(int fftLength)
    {
        if (fftLength <= 0 || (fftLength & (fftLength - 1)) != 0)
        {
            throw new ArgumentException("fftLength must be a power of two", nameof(fftLength));
        }

        _fftLength = fftLength;
        _m = (int)Math.Log2(fftLength);
        _window = HannWindow(fftLength);
    }

    public float[] ComputeMagnitudes(float[] samples)
    {
        if (samples.Length != _fftLength)
        {
            throw new ArgumentException($"Expected {_fftLength} samples, got {samples.Length}", nameof(samples));
        }

        var complex = new Complex[_fftLength];
        for (int i = 0; i < _fftLength; i++)
        {
            complex[i].X = samples[i] * _window[i];
            complex[i].Y = 0;
        }

        FastFourierTransform.FFT(true, _m, complex);

        var magnitudes = new float[_fftLength / 2];
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = MathF.Sqrt(complex[i].X * complex[i].X + complex[i].Y * complex[i].Y);
        }

        return magnitudes;
    }

    private static float[] HannWindow(int length)
    {
        var window = new float[length];
        for (int i = 0; i < length; i++)
        {
            window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (length - 1)));
        }
        return window;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test Pulsewave.sln --filter SpectrumProcessorTests
```

Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Pulsewave/Spectrum/SpectrumProcessor.cs tests/Pulsewave.Tests/Spectrum/SpectrumProcessorTests.cs
git commit -m "feat: add SpectrumProcessor for windowed FFT magnitude computation"
```

---

### Task 4: BarMapper (log-frequency grouping, sensitivity, smoothing)

**Files:**
- Create: `src/Pulsewave/Bars/BarMapper.cs`
- Create: `tests/Pulsewave.Tests/Bars/BarMapperTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Pulsewave.Tests/Bars/BarMapperTests.cs`:

```csharp
using Pulsewave.Bars;

namespace Pulsewave.Tests.Bars;

public class BarMapperTests
{
    [Fact]
    public void MapToBars_ClampsHeightsToUnitRange()
    {
        var mapper = new BarMapper(sampleRate: 44100, fftLength: 1024, sensitivity: 500f, smoothingFactor: 0f);
        var magnitudes = new float[512];
        Array.Fill(magnitudes, 1000f);

        var heights = mapper.MapToBars(magnitudes, barCount: 4);

        Assert.All(heights, h => Assert.InRange(h, 0f, 1f));
    }

    [Fact]
    public void MapToBars_SmoothsBetweenConsecutiveFrames()
    {
        var mapper = new BarMapper(sampleRate: 44100, fftLength: 1024, sensitivity: 100f, smoothingFactor: 0.6f);
        var silence = new float[512];
        var loud = new float[512];
        Array.Fill(loud, 1000f);

        mapper.MapToBars(silence, barCount: 8); // establishes near-zero previous heights

        var secondFrame = mapper.MapToBars(loud, barCount: 8);

        foreach (var height in secondFrame)
        {
            Assert.True(height < 1.0f, "Smoothing should prevent an instant jump to the clamped max");
            Assert.True(height > 0f);
        }
    }

    [Fact]
    public void MapToBars_LowFrequencyEnergyMapsToEarlyBars()
    {
        var mapper = new BarMapper(sampleRate: 44100, fftLength: 1024, sensitivity: 100f, smoothingFactor: 0f);
        var magnitudes = new float[512];
        magnitudes[2] = 1000f; // very low-frequency bin (~86 Hz at this sample rate/fft length)

        var heights = mapper.MapToBars(magnitudes, barCount: 8);

        float lowHalfMax = heights.Take(4).Max();
        float highHalfMax = heights.Skip(4).Max();
        Assert.True(lowHalfMax > highHalfMax);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test Pulsewave.sln --filter BarMapperTests
```

Expected: FAIL — `Pulsewave.Bars.BarMapper` does not exist.

- [ ] **Step 3: Implement `BarMapper`**

`src/Pulsewave/Bars/BarMapper.cs`:

```csharp
namespace Pulsewave.Bars;

public sealed class BarMapper
{
    // Heuristic scale between raw FFT magnitude and a 0..1 bar height: NAudio's magnitudes
    // aren't calibrated to a fixed loudness reference, so this needs re-tuning by ear once
    // real WASAPI audio is wired in (Task 9+).
    private const float MagnitudeNormalization = 50f;

    private readonly int _sampleRate;
    private readonly int _fftLength;
    private readonly float _sensitivity;
    private readonly float _smoothingFactor;
    private float[]? _previousHeights;

    public BarMapper(int sampleRate, int fftLength, float sensitivity, float smoothingFactor = 0.6f)
    {
        _sampleRate = sampleRate;
        _fftLength = fftLength;
        _sensitivity = sensitivity;
        _smoothingFactor = smoothingFactor;
    }

    public float[] MapToBars(float[] magnitudes, int barCount)
    {
        var edges = ComputeLogFrequencyEdges(barCount, magnitudes.Length);
        var rawHeights = new float[barCount];

        for (int bar = 0; bar < barCount; bar++)
        {
            int startBin = edges[bar];
            int endBin = Math.Max(startBin + 1, edges[bar + 1]);
            float sum = 0f;
            int count = 0;
            for (int bin = startBin; bin < endBin && bin < magnitudes.Length; bin++)
            {
                sum += magnitudes[bin];
                count++;
            }

            float average = count > 0 ? sum / count : 0f;
            float scaled = average * _sensitivity / 100f / MagnitudeNormalization;
            rawHeights[bar] = Math.Clamp(scaled, 0f, 1f);
        }

        if (_previousHeights == null || _previousHeights.Length != barCount)
        {
            _previousHeights = new float[barCount];
        }

        var smoothed = new float[barCount];
        for (int bar = 0; bar < barCount; bar++)
        {
            smoothed[bar] = _previousHeights[bar] * _smoothingFactor + rawHeights[bar] * (1 - _smoothingFactor);
        }
        _previousHeights = smoothed;

        return smoothed;
    }

    private int[] ComputeLogFrequencyEdges(int barCount, int totalBins)
    {
        const float minFrequencyHz = 20f;
        float maxFrequencyHz = Math.Min(_sampleRate / 2f, 20000f);
        float binWidthHz = (float)_sampleRate / _fftLength;

        var edges = new int[barCount + 1];
        for (int i = 0; i <= barCount; i++)
        {
            double t = (double)i / barCount;
            double frequencyHz = minFrequencyHz * Math.Pow(maxFrequencyHz / minFrequencyHz, t);
            int bin = (int)Math.Round(frequencyHz / binWidthHz);
            edges[i] = Math.Clamp(bin, 1, totalBins);
        }
        return edges;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test Pulsewave.sln --filter BarMapperTests
```

Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Pulsewave/Bars/BarMapper.cs tests/Pulsewave.Tests/Bars/BarMapperTests.cs
git commit -m "feat: add BarMapper with log-frequency grouping, sensitivity and smoothing"
```

---

### Task 5: ColorGradient

**Files:**
- Create: `src/Pulsewave/Rendering/ColorGradient.cs`
- Create: `tests/Pulsewave.Tests/Rendering/ColorGradientTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Pulsewave.Tests/Rendering/ColorGradientTests.cs`:

```csharp
using Pulsewave.Rendering;
using Spectre.Console;

namespace Pulsewave.Tests.Rendering;

public class ColorGradientTests
{
    private static readonly List<Color> Stops = new()
    {
        new Color(0, 255, 0),
        new Color(255, 255, 0),
        new Color(255, 0, 0),
    };

    [Fact]
    public void Interpolate_AtZero_ReturnsFirstStop()
    {
        var result = ColorGradient.Interpolate(Stops, 0f);
        Assert.Equal(new Color(0, 255, 0), result);
    }

    [Fact]
    public void Interpolate_AtOne_ReturnsLastStop()
    {
        var result = ColorGradient.Interpolate(Stops, 1f);
        Assert.Equal(new Color(255, 0, 0), result);
    }

    [Fact]
    public void Interpolate_AtMidpoint_ReturnsMiddleStop()
    {
        var result = ColorGradient.Interpolate(Stops, 0.5f);
        Assert.Equal(new Color(255, 255, 0), result);
    }

    [Fact]
    public void Interpolate_BetweenFirstTwoStops_BlendsLinearly()
    {
        var twoStops = new List<Color> { new Color(0, 0, 0), new Color(100, 100, 100) };
        var result = ColorGradient.Interpolate(twoStops, 0.5f);
        Assert.Equal(new Color(50, 50, 50), result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test Pulsewave.sln --filter ColorGradientTests
```

Expected: FAIL — `Pulsewave.Rendering.ColorGradient` does not exist.

- [ ] **Step 3: Implement `ColorGradient`**

`src/Pulsewave/Rendering/ColorGradient.cs`:

```csharp
using Spectre.Console;

namespace Pulsewave.Rendering;

public static class ColorGradient
{
    public static Color Interpolate(IReadOnlyList<Color> stops, float t)
    {
        if (stops.Count == 0)
        {
            throw new ArgumentException("At least one color stop is required", nameof(stops));
        }
        if (stops.Count == 1)
        {
            return stops[0];
        }

        t = Math.Clamp(t, 0f, 1f);
        float scaled = t * (stops.Count - 1);
        int index = (int)MathF.Floor(scaled);
        if (index >= stops.Count - 1)
        {
            return stops[^1];
        }

        float localT = scaled - index;
        var a = stops[index];
        var b = stops[index + 1];

        byte r = (byte)(a.R + (b.R - a.R) * localT);
        byte g = (byte)(a.G + (b.G - a.G) * localT);
        byte bl = (byte)(a.B + (b.B - a.B) * localT);
        return new Color(r, g, bl);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test Pulsewave.sln --filter ColorGradientTests
```

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Pulsewave/Rendering/ColorGradient.cs tests/Pulsewave.Tests/Rendering/ColorGradientTests.cs
git commit -m "feat: add ColorGradient stop interpolation"
```

---

### Task 6: BarFrameBuilder (pure bar-heights -> glyph/color grid)

**Files:**
- Create: `src/Pulsewave/Rendering/BarFrameBuilder.cs`
- Create: `tests/Pulsewave.Tests/Rendering/BarFrameBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Pulsewave.Tests/Rendering/BarFrameBuilderTests.cs`:

```csharp
using Pulsewave.Rendering;
using Spectre.Console;

namespace Pulsewave.Tests.Rendering;

public class BarFrameBuilderTests
{
    [Fact]
    public void BuildFrame_ClassicStyle_FullHeightBarFillsAllRowsWithSolidBlock()
    {
        var builder = new BarFrameBuilder(BarStyle.Classic, barWidth: 1, gap: 0,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 1f }, terminalHeightRows: 4);

        for (int row = 0; row < 4; row++)
        {
            Assert.Equal('█', frame[row, 0].Glyph);
        }
    }

    [Fact]
    public void BuildFrame_ClassicStyle_HalfHeightBarFillsBottomRowsOnly()
    {
        var builder = new BarFrameBuilder(BarStyle.Classic, barWidth: 1, gap: 0,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 0.5f }, terminalHeightRows: 4);

        Assert.Equal(' ', frame[0, 0].Glyph);
        Assert.Equal(' ', frame[1, 0].Glyph);
        Assert.Equal('█', frame[2, 0].Glyph);
        Assert.Equal('█', frame[3, 0].Glyph);
    }

    [Fact]
    public void BuildFrame_GradientStyle_ZeroHeightBarIsBlank()
    {
        var builder = new BarFrameBuilder(BarStyle.Gradient, barWidth: 1, gap: 0,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 0f }, terminalHeightRows: 4);

        for (int row = 0; row < 4; row++)
        {
            Assert.Equal(' ', frame[row, 0].Glyph);
        }
    }

    [Fact]
    public void BuildFrame_GradientStyle_PartialHeightProducesSubCharacterGlyph()
    {
        var builder = new BarFrameBuilder(BarStyle.Gradient, barWidth: 1, gap: 0,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 0.5f }, terminalHeightRows: 1);

        Assert.Equal('▄', frame[0, 0].Glyph);
    }

    [Fact]
    public void BuildFrame_WithGap_LeavesBlankColumnsBetweenBars()
    {
        var builder = new BarFrameBuilder(BarStyle.Classic, barWidth: 1, gap: 1,
            colorStops: new List<Color> { Color.Green }, solidColor: true);

        var frame = builder.BuildFrame(new float[] { 1f, 1f }, terminalHeightRows: 1);

        Assert.Equal('█', frame[0, 0].Glyph); // bar 0
        Assert.Equal(' ', frame[0, 1].Glyph); // gap
        Assert.Equal('█', frame[0, 2].Glyph); // bar 1
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test Pulsewave.sln --filter BarFrameBuilderTests
```

Expected: FAIL — `Pulsewave.Rendering.BarFrameBuilder`, `BarStyle`, `BarCell` do not exist.

- [ ] **Step 3: Implement `BarStyle`, `BarCell` and `BarFrameBuilder`**

`src/Pulsewave/Rendering/BarFrameBuilder.cs`:

```csharp
using Spectre.Console;

namespace Pulsewave.Rendering;

public enum BarStyle
{
    Classic,
    Gradient,
}

public readonly record struct BarCell(char Glyph, Color Color);

public sealed class BarFrameBuilder
{
    private static readonly char[] SubCharacterLevels = { ' ', '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    private readonly BarStyle _style;
    private readonly int _barWidth;
    private readonly int _gap;
    private readonly IReadOnlyList<Color> _colorStops;
    private readonly bool _solidColor;

    public BarFrameBuilder(BarStyle style, int barWidth, int gap, IReadOnlyList<Color> colorStops, bool solidColor)
    {
        _style = style;
        _barWidth = barWidth;
        _gap = gap;
        _colorStops = colorStops;
        _solidColor = solidColor;
    }

    public BarCell[,] BuildFrame(float[] barHeights, int terminalHeightRows)
    {
        int columns = barHeights.Length * (_barWidth + _gap);
        var frame = new BarCell[terminalHeightRows, columns];

        for (int bar = 0; bar < barHeights.Length; bar++)
        {
            float height = Math.Clamp(barHeights[bar], 0f, 1f);
            int baseColumn = bar * (_barWidth + _gap);

            for (int row = 0; row < terminalHeightRows; row++)
            {
                float rowBottomFraction = 1f - (float)row / terminalHeightRows;
                char glyph = ResolveGlyph(height, row, terminalHeightRows);
                Color color = _solidColor ? _colorStops[0] : ColorGradient.Interpolate(_colorStops, rowBottomFraction);

                for (int col = 0; col < _barWidth; col++)
                {
                    frame[row, baseColumn + col] = new BarCell(glyph, color);
                }
                for (int col = _barWidth; col < _barWidth + _gap && baseColumn + col < columns; col++)
                {
                    frame[row, baseColumn + col] = new BarCell(' ', color);
                }
            }
        }

        return frame;
    }

    private char ResolveGlyph(float height, int rowFromTop, int totalRows)
    {
        float filledRows = height * totalRows;
        int rowFromBottom = totalRows - 1 - rowFromTop;

        if (_style == BarStyle.Classic)
        {
            return rowFromBottom < filledRows ? '█' : ' ';
        }

        float remaining = filledRows - rowFromBottom;
        if (remaining <= 0) return ' ';
        if (remaining >= 1) return '█';

        int levelIndex = (int)MathF.Round(remaining * (SubCharacterLevels.Length - 1));
        return SubCharacterLevels[Math.Clamp(levelIndex, 0, SubCharacterLevels.Length - 1)];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test Pulsewave.sln --filter BarFrameBuilderTests
```

Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Pulsewave/Rendering/BarFrameBuilder.cs tests/Pulsewave.Tests/Rendering/BarFrameBuilderTests.cs
git commit -m "feat: add BarFrameBuilder for classic and gradient bar styles"
```

---

### Task 7: Config (CavaConfig, ConfigException, ConfigLoader)

**Files:**
- Create: `src/Pulsewave/Config/CavaConfig.cs`
- Create: `src/Pulsewave/Config/ConfigException.cs`
- Create: `src/Pulsewave/Config/ConfigLoader.cs`
- Create: `tests/Pulsewave.Tests/Config/ConfigLoaderTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Pulsewave.Tests/Config/ConfigLoaderTests.cs`:

```csharp
using Pulsewave.Config;

namespace Pulsewave.Tests.Config;

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
        config.Color.Gradient = new List<string> { "#FFFFFF" };

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(config, "config.toml"));
        Assert.Contains("gradient", ex.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test Pulsewave.sln --filter ConfigLoaderTests
```

Expected: FAIL — `Pulsewave.Config.*` types do not exist.

- [ ] **Step 3: Implement `CavaConfig`**

`src/Pulsewave/Config/CavaConfig.cs`:

```csharp
namespace Pulsewave.Config;

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
```

- [ ] **Step 4: Implement `ConfigException`**

`src/Pulsewave/Config/ConfigException.cs`:

```csharp
namespace Pulsewave.Config;

public sealed class ConfigException : Exception
{
    public ConfigException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
```

- [ ] **Step 5: Implement `ConfigLoader`**

`src/Pulsewave/Config/ConfigLoader.cs`:

```csharp
using Tomlyn;

namespace Pulsewave.Config;

public static class ConfigLoader
{
    public static CavaConfig LoadOrCreateDefault(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new CavaConfig();
            Save(defaultConfig, path);
            return defaultConfig;
        }

        string text = File.ReadAllText(path);
        CavaConfig config;
        try
        {
            config = Toml.ToModel<CavaConfig>(text);
        }
        catch (Exception ex)
        {
            throw new ConfigException($"No se pudo leer el archivo de configuración '{path}': {ex.Message}", ex);
        }

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
        File.WriteAllText(path, Toml.FromModel(config));
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
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test Pulsewave.sln --filter ConfigLoaderTests
```

Expected: PASS (4 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Pulsewave/Config/CavaConfig.cs src/Pulsewave/Config/ConfigException.cs src/Pulsewave/Config/ConfigLoader.cs tests/Pulsewave.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat: add CavaConfig model and TOML load/save/validate"
```

---

### Task 8: CliOptions

**Files:**
- Create: `src/Pulsewave/Config/CliOptions.cs`
- Create: `tests/Pulsewave.Tests/Config/CliOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Pulsewave.Tests/Config/CliOptionsTests.cs`:

```csharp
using Pulsewave.Config;

namespace Pulsewave.Tests.Config;

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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test Pulsewave.sln --filter CliOptionsTests
```

Expected: FAIL — `Pulsewave.Config.CliOptions` does not exist.

- [ ] **Step 3: Implement `CliOptions`**

`src/Pulsewave/Config/CliOptions.cs`:

```csharp
using System.Globalization;

namespace Pulsewave.Config;

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
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test Pulsewave.sln --filter CliOptionsTests
```

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Pulsewave/Config/CliOptions.cs tests/Pulsewave.Tests/Config/CliOptionsTests.cs
git commit -m "feat: add CLI flag parsing and overrides"
```

---

### Task 9: AudioCapture (WASAPI loopback)

**Files:**
- Create: `src/Pulsewave/Audio/AudioCapture.cs`

This wraps real Windows audio hardware — it cannot run in an automated test environment (no WASAPI device in CI). It's validated manually, per the spec's testing section.

- [ ] **Step 1: Implement `AudioCapture`**

`src/Pulsewave/Audio/AudioCapture.cs`:

```csharp
using NAudio.Wave;

namespace Pulsewave.Audio;

public sealed class AudioCapture : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly SampleRingBuffer _buffer;

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public AudioCapture(SampleRingBuffer buffer)
    {
        _buffer = buffer;
        _capture = new WasapiLoopbackCapture();

        if (_capture.WaveFormat.Channels != 2)
        {
            throw new NotSupportedException(
                $"Pulsewave solo soporta dispositivos de salida estéreo (2 canales); " +
                $"el dispositivo activo reporta {_capture.WaveFormat.Channels} canal(es).");
        }
        if (_capture.WaveFormat.BitsPerSample != 32 ||
            _capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            throw new NotSupportedException(
                "Pulsewave espera un formato de captura WASAPI IEEE float de 32 bits, " +
                $"pero el dispositivo reporta {_capture.WaveFormat.BitsPerSample} bits / {_capture.WaveFormat.Encoding}.");
        }

        _capture.DataAvailable += OnDataAvailable;
    }

    public void Start() => _capture.StartRecording();

    public void Stop() => _capture.StopRecording();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int sampleCount = e.BytesRecorded / bytesPerSample;
        var samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            samples[i] = BitConverter.ToSingle(e.Buffer, i * bytesPerSample);
        }
        _buffer.WriteInterleavedStereo(samples);
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
dotnet build Pulsewave.sln
```

Expected: build succeeds.

- [ ] **Step 3: Manual verification**

Write a temporary throwaway snippet in `src/Pulsewave/Program.cs` (do not commit this version — Task 11 replaces `Program.cs` for good):

```csharp
using Pulsewave.Audio;

var buffer = new SampleRingBuffer(capacityPerChannel: 4096);
using var capture = new AudioCapture(buffer);
capture.Start();
Console.WriteLine($"Capturing at {capture.WaveFormat.SampleRate}Hz, {capture.WaveFormat.Channels}ch. Press Enter to stop.");
Console.ReadLine();
capture.Stop();

var left = new float[1024];
var right = new float[1024];
buffer.ReadLatest(1024, left, right);
Console.WriteLine($"Max abs sample seen: {Math.Max(left.Max(Math.Abs), right.Max(Math.Abs))}");
```

Run `dotnet run --project src/Pulsewave`, play some audio (e.g. a YouTube video) while it's capturing, press Enter, and confirm the printed max sample is non-zero (evidence loopback capture is actually receiving audio). Revert `Program.cs` back to the Task 1 placeholder afterward — do not commit the throwaway snippet.

- [ ] **Step 4: Commit**

```bash
git add src/Pulsewave/Audio/AudioCapture.cs
git commit -m "feat: add WASAPI loopback AudioCapture writing into SampleRingBuffer"
```

---

### Task 10: ConsoleRenderer

**Files:**
- Create: `src/Pulsewave/Rendering/ConsoleRenderer.cs`

This writes directly to the real console — validated manually (visual output), not with automated tests.

- [ ] **Step 1: Implement `ConsoleRenderer`**

`src/Pulsewave/Rendering/ConsoleRenderer.cs`:

```csharp
using System.Text;
using Spectre.Console;

namespace Pulsewave.Rendering;

public sealed class ConsoleRenderer
{
    public void Prepare()
    {
        Console.CursorVisible = false;
        AnsiConsole.Console.Clear();
    }

    public void Render(BarCell[,] frame)
    {
        int rows = frame.GetLength(0);
        int columns = frame.GetLength(1);

        Console.SetCursorPosition(0, 0);
        var builder = new StringBuilder();

        for (int row = 0; row < rows; row++)
        {
            BarCell? current = null;
            var segment = new StringBuilder();

            for (int col = 0; col < columns; col++)
            {
                var cell = frame[row, col];
                if (current is null || cell.Color != current.Value.Color)
                {
                    if (current is not null)
                    {
                        AppendSegment(builder, current.Value.Color, segment.ToString());
                        segment.Clear();
                    }
                    current = cell;
                }
                segment.Append(cell.Glyph);
            }
            if (current is not null)
            {
                AppendSegment(builder, current.Value.Color, segment.ToString());
            }

            if (row < rows - 1)
            {
                builder.Append('\n');
            }
        }

        AnsiConsole.Markup(builder.ToString());
    }

    public void Restore()
    {
        Console.CursorVisible = true;
        AnsiConsole.Console.Clear();
    }

    private static void AppendSegment(StringBuilder builder, Color color, string text)
    {
        builder.Append($"[#{color.ToHex()}]{text.EscapeMarkup()}[/]");
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
dotnet build Pulsewave.sln
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Pulsewave/Rendering/ConsoleRenderer.cs
git commit -m "feat: add ConsoleRenderer for drawing BarCell grids via Spectre.Console"
```

---

### Task 11: Program wiring (the real entry point)

**Files:**
- Modify: `src/Pulsewave/Program.cs`

This is the integration point for every component above. It's validated manually end-to-end (per the spec, no automated integration tests) — Step 3 below is the manual test.

- [ ] **Step 1: Replace `Program.cs` with the full pipeline**

`src/Pulsewave/Program.cs`:

```csharp
using Pulsewave.Audio;
using Pulsewave.Bars;
using Pulsewave.Config;
using Pulsewave.Rendering;
using Pulsewave.Spectrum;
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
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulsewave", "config.toml");

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
```

- [ ] **Step 2: Build and run the full test suite**

```bash
dotnet build Pulsewave.sln
dotnet test Pulsewave.sln
```

Expected: build succeeds, all unit tests from Tasks 2-8 still pass (Program.cs itself has no unit tests — it's the manual-verification integration point).

- [ ] **Step 3: Manual end-to-end verification**

```bash
dotnet run --project src/Pulsewave
```

In Windows Terminal, with some audio playing (e.g. music or a video):
- Confirm bars appear and move in response to the audio.
- Confirm the config file was created at `%APPDATA%\Pulsewave\config.toml`.
- Try `dotnet run --project src/Pulsewave -- --style gradient --channels mono` and confirm the style/channel change takes effect.
- Resize the terminal window while it's running and confirm the bars re-layout to the new width/height without crashing.
- Press Ctrl+C and confirm the cursor becomes visible again and the terminal is left in a clean state.

- [ ] **Step 4: Commit**

```bash
git add src/Pulsewave/Program.cs
git commit -m "feat: wire audio capture, spectrum processing, and rendering into the main pipeline"
```

---

### Task 12: Self-contained publish

**Files:** none (build/packaging step only)

- [ ] **Step 1: Publish a self-contained single-file executable**

```bash
dotnet publish src/Pulsewave/Pulsewave.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Expected: `publish\Pulsewave.exe` is produced.

- [ ] **Step 2: Manual verification on a clean run**

```bash
publish\Pulsewave.exe
```

Confirm it runs and renders bars the same as `dotnet run` did in Task 11, without requiring the .NET SDK/runtime to be separately installed (the point of `--self-contained true`).

- [ ] **Step 3: Commit build configuration if anything changed**

If Step 1 required no source changes, there is nothing to commit — publishing is a build output, not source. If you added publish-related MSBuild properties to `Pulsewave.csproj` to simplify the command (e.g. baking in `<PublishSingleFile>true</PublishSingleFile>` and `<SelfContained>true</SelfContained>`), commit that:

```bash
git add src/Pulsewave/Pulsewave.csproj
git commit -m "chore: bake self-contained single-file publish settings into Pulsewave.csproj"
```

---

## Self-review notes

- **Spec coverage:** every spec section maps to a task — components (Tasks 2-10), config file + CLI (Tasks 7-8), bar styles + color (Tasks 5-6), error handling (config validation in Task 7, audio format checks in Task 9, resize/Ctrl+C handling in Task 11), testing philosophy (unit tests for pure components, manual verification for hardware/console-facing ones, called out explicitly in Tasks 9-11), distribution (Task 12).
- **Type consistency checked:** `BarMapper` constructor signature `(sampleRate, fftLength, sensitivity, smoothingFactor = 0.6f)` used identically in Task 4's tests and Task 11's wiring. `BarFrameBuilder` constructor `(style, barWidth, gap, colorStops, solidColor)` used identically in Task 6's tests and Task 11. `ConfigLoader.LoadOrCreateDefault` / `.Validate` / `.Save` signatures match between Task 7's tests and Task 11's usage. `CliOptions.ApplyOverrides` / `.ExtractConfigPathOverride` match between Task 8's tests and Task 11.
- **Out of scope reminder:** device selection for input/microphone, configurable framerate beyond the config value already wired, fixed bar counts, noise reduction, and hot-reload are explicitly not built here, matching the spec's "Fuera de alcance" section.
