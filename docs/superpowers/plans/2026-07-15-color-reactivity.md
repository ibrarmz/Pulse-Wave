# Color Reactivity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add selectable color palettes (rainbow/neon/fire/ice/mono/custom) and audio-reactive color (brightness that breathes with overall volume, plus a brief flash on bass hits) to CavaPlus's bar display.

**Architecture:** Four new small, independently testable units slot into the existing render pipeline without touching `BarFrameBuilder`: `ColorPalettes` (pure lookup, named palette -> color list, plus the hex-parsing helper factored out of `Program.cs`), `LoudnessTracker` (stateful, samples -> smoothed brightness factor, same adaptive-peak trick `BarMapper` already uses), `BassBeatDetector` (stateful, bass-band magnitudes -> decaying flash intensity), and `ColorReactivity` (pure, mutates an already-built `BarCell[,]` frame — same style as `OverlayText`). `Program.cs`'s per-frame loop becomes `BuildFrame -> ColorReactivity.Apply -> OverlayText.Apply -> Render`, in that order so the now-playing title never dims or flashes.

**Tech Stack:** .NET 8 (`net8.0-windows10.0.19041.0`), the existing Spectre.Console/Tomlyn/xUnit stack. No new dependencies.

**Reference spec:** `docs/superpowers/specs/2026-07-15-color-reactivity-design.md`
**Builds on:** `docs/superpowers/plans/2026-07-14-now-playing-overlay.md` (already implemented and working on this same branch, `feature/cavaplus-mvp`)

---

## File structure

```
src/CavaPlus/
  Program.cs                          # wire LoudnessTracker + BassBeatDetector + ColorReactivity in; palette-resolved color stops
  Rendering/
    ColorPalettes.cs                  # new: named preset -> List<Color>, plus shared hex-parsing helper
    ColorReactivity.cs                # new: pure BarCell[,] + brightness + flash -> mutated frame
  Audio/
    LoudnessTracker.cs                # new: stateful, samples -> smoothed brightness factor
  Spectrum/
    BassBeatDetector.cs               # new: stateful, bass magnitudes -> decaying flash intensity
  Config/
    CavaConfig.cs                     # + ColorSection.Palette, ColorSection.Reactive
    ConfigLoader.cs                   # + palette validation, gradient-count check scoped to palette=="custom"
tests/CavaPlus.Tests/
  Rendering/ColorPalettesTests.cs     # new
  Rendering/ColorReactivityTests.cs   # new
  Audio/LoudnessTrackerTests.cs       # new
  Spectrum/BassBeatDetectorTests.cs   # new
  Config/ConfigLoaderTests.cs         # + 4 new tests, 1 existing test modified (see Task 5)
```

`Program.cs` wiring has no automated tests (same as every prior integration-point task in this codebase) — validated manually in Task 7.

---

### Task 1: ColorPalettes

**Files:**
- Create: `src/CavaPlus/Rendering/ColorPalettes.cs`
- Create: `tests/CavaPlus.Tests/Rendering/ColorPalettesTests.cs`

Pure lookup + hex parsing, no I/O — fully unit-testable, follows TDD.

- [ ] **Step 1: Write the failing tests**

`tests/CavaPlus.Tests/Rendering/ColorPalettesTests.cs`:

```csharp
using CavaPlus.Rendering;
using Spectre.Console;

namespace CavaPlus.Tests.Rendering;

public class ColorPalettesTests
{
    [Fact]
    public void Resolve_WithRainbowPreset_ReturnsExpectedColors()
    {
        var colors = ColorPalettes.Resolve("rainbow", customGradientHex: new List<string>());

        Assert.Equal(7, colors.Count);
        Assert.Equal(new Color(0xFF, 0x00, 0x00), colors[0]);
        Assert.Equal(new Color(0x94, 0x00, 0xD3), colors[^1]);
    }

    [Fact]
    public void Resolve_WithFirePreset_ReturnsExpectedColors()
    {
        var colors = ColorPalettes.Resolve("fire", customGradientHex: new List<string>());

        Assert.Equal(4, colors.Count);
        Assert.Equal(new Color(0x3D, 0x00, 0x00), colors[0]);
        Assert.Equal(new Color(0xFF, 0xD4, 0x00), colors[^1]);
    }

    [Fact]
    public void Resolve_WithNeonAndIcePresets_ReturnEndpointsMatchingSpec()
    {
        var neon = ColorPalettes.Resolve("neon", customGradientHex: new List<string>());
        var ice = ColorPalettes.Resolve("ice", customGradientHex: new List<string>());

        Assert.Equal(new Color(0xFF, 0x00, 0xFF), neon[0]);
        Assert.Equal(new Color(0x00, 0xD4, 0xFF), neon[^1]);
        Assert.Equal(new Color(0x00, 0x1A, 0x4D), ice[0]);
        Assert.Equal(new Color(0xFF, 0xFF, 0xFF), ice[^1]);
    }

    [Fact]
    public void Resolve_WithMonoPreset_ReturnsExpectedColors()
    {
        var colors = ColorPalettes.Resolve("mono", customGradientHex: new List<string>());

        Assert.Equal(new Color(0x00, 0x33, 0x00), colors[0]);
        Assert.Equal(new Color(0xCC, 0xFF, 0xCC), colors[^1]);
    }

    [Fact]
    public void Resolve_WithCustom_ParsesProvidedGradient()
    {
        var colors = ColorPalettes.Resolve("custom", customGradientHex: new List<string> { "#112233", "#445566" });

        Assert.Equal(2, colors.Count);
        Assert.Equal(new Color(0x11, 0x22, 0x33), colors[0]);
        Assert.Equal(new Color(0x44, 0x55, 0x66), colors[1]);
    }

    [Fact]
    public void ParseHexColor_ParsesSixDigitHex()
    {
        var color = ColorPalettes.ParseHexColor("#00FF7F");

        Assert.Equal(new Color(0x00, 0xFF, 0x7F), color);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test CavaPlus.sln --filter ColorPalettesTests
```

Expected: FAIL — `CavaPlus.Rendering.ColorPalettes` does not exist.

- [ ] **Step 3: Implement `ColorPalettes`**

`src/CavaPlus/Rendering/ColorPalettes.cs`:

```csharp
using Spectre.Console;

namespace CavaPlus.Rendering;

public static class ColorPalettes
{
    private static readonly Dictionary<string, string[]> Presets = new()
    {
        ["rainbow"] = new[] { "#FF0000", "#FF7F00", "#FFFF00", "#00FF00", "#0000FF", "#4B0082", "#9400D3" },
        ["neon"] = new[] { "#FF00FF", "#B84DFF", "#7B61FF", "#00D4FF" },
        ["fire"] = new[] { "#3D0000", "#B22200", "#FF6A00", "#FFD400" },
        ["ice"] = new[] { "#001A4D", "#0066CC", "#33CCFF", "#FFFFFF" },
        ["mono"] = new[] { "#003300", "#00AA00", "#00FF00", "#CCFFCC" },
    };

    public static List<Color> Resolve(string paletteName, List<string> customGradientHex)
    {
        IEnumerable<string> hexColors = Presets.TryGetValue(paletteName, out var preset)
            ? preset
            : customGradientHex;

        return hexColors.Select(ParseHexColor).ToList();
    }

    public static Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return new Color(r, g, b);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test CavaPlus.sln --filter ColorPalettesTests
```

Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CavaPlus/Rendering/ColorPalettes.cs tests/CavaPlus.Tests/Rendering/ColorPalettesTests.cs
git commit -m "feat: add ColorPalettes with rainbow/neon/fire/ice/mono presets"
```

---

### Task 2: ColorReactivity

**Files:**
- Create: `src/CavaPlus/Rendering/ColorReactivity.cs`
- Create: `tests/CavaPlus.Tests/Rendering/ColorReactivityTests.cs`

Pure grid/color math, no I/O — fully unit-testable, follows TDD.

- [ ] **Step 1: Write the failing tests**

`tests/CavaPlus.Tests/Rendering/ColorReactivityTests.cs`:

```csharp
using CavaPlus.Rendering;
using Spectre.Console;

namespace CavaPlus.Tests.Rendering;

public class ColorReactivityTests
{
    [Fact]
    public void Apply_WithFullBrightnessAndNoFlash_LeavesFrameUnchanged()
    {
        var frame = new BarCell[1, 1];
        frame[0, 0] = new BarCell('█', new Color(100, 150, 200));

        ColorReactivity.Apply(frame, brightnessFactor: 1.0f, flashIntensity: 0f);

        Assert.Equal(new Color(100, 150, 200), frame[0, 0].Color);
        Assert.Equal('█', frame[0, 0].Glyph);
    }

    [Fact]
    public void Apply_WithReducedBrightness_ScalesEachChannel()
    {
        var frame = new BarCell[1, 1];
        frame[0, 0] = new BarCell('█', new Color(200, 100, 50));

        ColorReactivity.Apply(frame, brightnessFactor: 0.5f, flashIntensity: 0f);

        Assert.Equal(new Color(100, 50, 25), frame[0, 0].Color);
    }

    [Fact]
    public void Apply_WithFullFlash_MixesTowardWhite()
    {
        var frame = new BarCell[1, 1];
        frame[0, 0] = new BarCell('█', new Color(0, 0, 0));

        ColorReactivity.Apply(frame, brightnessFactor: 1.0f, flashIntensity: 1.0f);

        Assert.Equal(new Color(153, 153, 153), frame[0, 0].Color);
    }

    [Fact]
    public void Apply_PreservesGlyphs()
    {
        var frame = new BarCell[1, 2];
        frame[0, 0] = new BarCell('▄', new Color(10, 20, 30));
        frame[0, 1] = new BarCell(' ', new Color(0, 0, 0));

        ColorReactivity.Apply(frame, brightnessFactor: 0.8f, flashIntensity: 0.2f);

        Assert.Equal('▄', frame[0, 0].Glyph);
        Assert.Equal(' ', frame[0, 1].Glyph);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test CavaPlus.sln --filter ColorReactivityTests
```

Expected: FAIL — `CavaPlus.Rendering.ColorReactivity` does not exist.

- [ ] **Step 3: Implement `ColorReactivity`**

`src/CavaPlus/Rendering/ColorReactivity.cs`:

```csharp
using Spectre.Console;

namespace CavaPlus.Rendering;

public static class ColorReactivity
{
    // At flashIntensity == 1.0, cells mix 60% toward white - a visible flash, not a whiteout.
    private const float FlashWhiteMixStrength = 0.6f;

    public static void Apply(BarCell[,] frame, float brightnessFactor, float flashIntensity)
    {
        int rows = frame.GetLength(0);
        int columns = frame.GetLength(1);
        float whiteMix = Math.Clamp(flashIntensity, 0f, 1f) * FlashWhiteMixStrength;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                var cell = frame[row, col];
                byte r = ScaleChannel(cell.Color.R, brightnessFactor, whiteMix);
                byte g = ScaleChannel(cell.Color.G, brightnessFactor, whiteMix);
                byte b = ScaleChannel(cell.Color.B, brightnessFactor, whiteMix);
                frame[row, col] = new BarCell(cell.Glyph, new Color(r, g, b));
            }
        }
    }

    private static byte ScaleChannel(byte value, float brightnessFactor, float whiteMix)
    {
        float scaled = value * brightnessFactor;
        float mixed = scaled + (255f - scaled) * whiteMix;
        return (byte)Math.Clamp(mixed, 0f, 255f);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test CavaPlus.sln --filter ColorReactivityTests
```

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CavaPlus/Rendering/ColorReactivity.cs tests/CavaPlus.Tests/Rendering/ColorReactivityTests.cs
git commit -m "feat: add ColorReactivity for brightness/flash post-processing on a frame"
```

---

### Task 3: LoudnessTracker

**Files:**
- Create: `src/CavaPlus/Audio/LoudnessTracker.cs`
- Create: `tests/CavaPlus.Tests/Audio/LoudnessTrackerTests.cs`

Stateful but deterministic given synthetic sample arrays — no real audio needed, follows TDD.

- [ ] **Step 1: Write the failing tests**

`tests/CavaPlus.Tests/Audio/LoudnessTrackerTests.cs`:

```csharp
using CavaPlus.Audio;

namespace CavaPlus.Tests.Audio;

public class LoudnessTrackerTests
{
    [Fact]
    public void Update_WithSustainedLoudAudio_ConvergesNearMaximumBrightness()
    {
        var tracker = new LoudnessTracker();
        var loud = new float[256];
        Array.Fill(loud, 0.8f);

        float brightness = 0f;
        for (int i = 0; i < 30; i++)
        {
            brightness = tracker.Update(loud, loud);
        }

        Assert.True(brightness > 0.95f, $"Expected brightness near 1.0 after sustained loud audio, got {brightness}");
    }

    [Fact]
    public void Update_WithSilenceAfterLoudAudio_ConvergesNearMinimumBrightness()
    {
        var tracker = new LoudnessTracker();
        var loud = new float[256];
        Array.Fill(loud, 0.8f);
        var silence = new float[256];

        for (int i = 0; i < 30; i++)
        {
            tracker.Update(loud, loud);
        }

        float brightness = 0f;
        for (int i = 0; i < 30; i++)
        {
            brightness = tracker.Update(silence, silence);
        }

        Assert.True(brightness < 0.72f, $"Expected brightness near 0.7 after silence, got {brightness}");
    }

    [Fact]
    public void Update_ChangesGradually_NotInstantly()
    {
        var tracker = new LoudnessTracker();
        var loud = new float[256];
        Array.Fill(loud, 0.8f);

        float brightness = tracker.Update(loud, loud);

        Assert.InRange(brightness, 0.7f, 0.95f);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test CavaPlus.sln --filter LoudnessTrackerTests
```

Expected: FAIL — `CavaPlus.Audio.LoudnessTracker` does not exist.

- [ ] **Step 3: Implement `LoudnessTracker`**

`src/CavaPlus/Audio/LoudnessTracker.cs`:

```csharp
namespace CavaPlus.Audio;

public sealed class LoudnessTracker
{
    // Same relative-peak trick as BarMapper: raw RMS has no fixed loudness reference (it depends
    // on system/app volume), so brightness is scaled against a slowly-decaying recent peak instead
    // of an absolute threshold.
    private const float PeakDecayPerFrame = 0.995f;
    private const float MinPeak = 1e-4f;
    private const float MinBrightness = 0.7f;
    private const float MaxBrightness = 1.0f;
    private const float SmoothingFactor = 0.15f;

    private float _peak = MinPeak;
    private float _smoothedBrightness = MinBrightness;

    public float Update(float[] leftSamples, float[] rightSamples)
    {
        float sumSquares = 0f;
        foreach (float sample in leftSamples)
        {
            sumSquares += sample * sample;
        }
        foreach (float sample in rightSamples)
        {
            sumSquares += sample * sample;
        }

        int count = leftSamples.Length + rightSamples.Length;
        float rms = count > 0 ? MathF.Sqrt(sumSquares / count) : 0f;

        _peak = Math.Max(MinPeak, Math.Max(rms, _peak * PeakDecayPerFrame));
        float loudnessFraction = Math.Clamp(rms / _peak, 0f, 1f);
        float target = MinBrightness + (MaxBrightness - MinBrightness) * loudnessFraction;

        _smoothedBrightness += (target - _smoothedBrightness) * SmoothingFactor;
        return _smoothedBrightness;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test CavaPlus.sln --filter LoudnessTrackerTests
```

Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CavaPlus/Audio/LoudnessTracker.cs tests/CavaPlus.Tests/Audio/LoudnessTrackerTests.cs
git commit -m "feat: add LoudnessTracker for volume-reactive brightness"
```

---

### Task 4: BassBeatDetector

**Files:**
- Create: `src/CavaPlus/Spectrum/BassBeatDetector.cs`
- Create: `tests/CavaPlus.Tests/Spectrum/BassBeatDetectorTests.cs`

Stateful but deterministic given synthetic magnitude arrays — no real audio needed, follows TDD.

- [ ] **Step 1: Write the failing tests**

`tests/CavaPlus.Tests/Spectrum/BassBeatDetectorTests.cs`:

```csharp
using CavaPlus.Spectrum;

namespace CavaPlus.Tests.Spectrum;

public class BassBeatDetectorTests
{
    // sampleRate=44100, fftLength=1024 -> bin width ~43.07 Hz -> bass cutoff (150 Hz) covers bins 0-2.
    private static float[] MakeMagnitudes(float bassBinValue)
    {
        var magnitudes = new float[512];
        magnitudes[0] = bassBinValue;
        magnitudes[1] = bassBinValue;
        magnitudes[2] = bassBinValue;
        return magnitudes;
    }

    [Fact]
    public void Update_OnFirstCall_InitializesBaselineWithoutTriggering()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);

        float flash = detector.Update(MakeMagnitudes(10f));

        Assert.Equal(0f, flash);
    }

    [Fact]
    public void Update_WithStableBass_NeverTriggers()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(10f)); // initializes baseline

        for (int i = 0; i < 5; i++)
        {
            float flash = detector.Update(MakeMagnitudes(10f));
            Assert.Equal(0f, flash);
        }
    }

    [Fact]
    public void Update_WithSuddenSpike_TriggersFullFlash()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(10f)); // initializes baseline
        detector.Update(MakeMagnitudes(10f)); // confirms flat baseline, no trigger yet

        float flash = detector.Update(MakeMagnitudes(50f)); // well above baseline * 1.6

        Assert.Equal(1f, flash);
    }

    [Fact]
    public void Update_AfterSpike_FlashDecaysOverSubsequentFrames()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(10f));
        detector.Update(MakeMagnitudes(10f));
        detector.Update(MakeMagnitudes(50f)); // triggers, flash = 1.0

        float firstDecay = detector.Update(MakeMagnitudes(10f));
        float secondDecay = detector.Update(MakeMagnitudes(10f));

        Assert.Equal(0.5f, firstDecay, precision: 3);
        Assert.Equal(0.25f, secondDecay, precision: 3);
    }

    [Fact]
    public void Update_WithSpikeBelowMinimumFloor_DoesNotTrigger()
    {
        var detector = new BassBeatDetector(sampleRate: 44100, fftLength: 1024);
        detector.Update(MakeMagnitudes(0.0001f)); // initializes a very quiet baseline

        // 0.0005 clears the ratio threshold (0.0001 * 1.6 = 0.00016) but stays under the
        // absolute minimum floor (0.001), so near-total silence should never "flash".
        float flash = detector.Update(MakeMagnitudes(0.0005f));

        Assert.Equal(0f, flash);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test CavaPlus.sln --filter BassBeatDetectorTests
```

Expected: FAIL — `CavaPlus.Spectrum.BassBeatDetector` does not exist.

- [ ] **Step 3: Implement `BassBeatDetector`**

`src/CavaPlus/Spectrum/BassBeatDetector.cs`:

```csharp
namespace CavaPlus.Spectrum;

public sealed class BassBeatDetector
{
    // Frequencies below this rough cutoff (kick/bass energy) drive beat detection; higher
    // frequencies are ignored so hi-hats/vocals don't trigger false flashes.
    private const float BassCutoffHz = 150f;
    private const float BaselineSmoothingFactor = 0.05f;
    private const float TriggerRatio = 1.6f;
    private const float MinimumBassFloor = 1e-3f;
    private const int CooldownFrames = 6;
    private const float DecayRate = 0.5f;

    private readonly int _bassBinCount;
    private bool _initialized;
    private float _baseline;
    private float _flashIntensity;
    private int _cooldownRemaining;

    public BassBeatDetector(int sampleRate, int fftLength)
    {
        float binWidthHz = (float)sampleRate / fftLength;
        _bassBinCount = Math.Max(1, (int)(BassCutoffHz / binWidthHz));
    }

    public float Update(float[] magnitudes)
    {
        float bassEnergy = ComputeBassEnergy(magnitudes);

        // First call has no history to compare against - treat it as establishing the baseline
        // rather than evaluating a trigger, otherwise the very first frame of any audio would
        // look like a "spike" against a baseline of zero.
        if (!_initialized)
        {
            _baseline = bassEnergy;
            _initialized = true;
            return _flashIntensity;
        }

        bool triggered = _cooldownRemaining == 0
            && bassEnergy > MinimumBassFloor
            && bassEnergy > _baseline * TriggerRatio;

        _baseline += (bassEnergy - _baseline) * BaselineSmoothingFactor;

        if (triggered)
        {
            _flashIntensity = 1f;
            _cooldownRemaining = CooldownFrames;
        }
        else
        {
            _flashIntensity *= DecayRate;
            if (_cooldownRemaining > 0)
            {
                _cooldownRemaining--;
            }
        }

        return _flashIntensity;
    }

    private float ComputeBassEnergy(float[] magnitudes)
    {
        int bins = Math.Min(_bassBinCount, magnitudes.Length);
        float sum = 0f;
        for (int i = 0; i < bins; i++)
        {
            sum += magnitudes[i];
        }
        return bins > 0 ? sum / bins : 0f;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test CavaPlus.sln --filter BassBeatDetectorTests
```

Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CavaPlus/Spectrum/BassBeatDetector.cs tests/CavaPlus.Tests/Spectrum/BassBeatDetectorTests.cs
git commit -m "feat: add BassBeatDetector for bass-onset flash triggering"
```

---

### Task 5: Config — palette and reactive fields

**Files:**
- Modify: `src/CavaPlus/Config/CavaConfig.cs`
- Modify: `src/CavaPlus/Config/ConfigLoader.cs`
- Modify: `tests/CavaPlus.Tests/Config/ConfigLoaderTests.cs`

This task also narrows an existing validation rule: "`gradient` needs >= 2 colors when `mode` is `gradient`" must now only apply when `palette` is `"custom"` (a named preset ignores `gradient` entirely). The existing test for that rule must be updated in the same task, otherwise it silently stops testing what it claims to.

- [ ] **Step 1: Modify the existing gradient-validation test to pin `palette = "custom"`**

In `tests/CavaPlus.Tests/Config/ConfigLoaderTests.cs`, replace this existing test:

```csharp
    [Fact]
    public void Validate_WithGradientModeAndOneColor_ThrowsConfigException()
    {
        var config = new CavaConfig();
        config.Color.Mode = "gradient";
        config.Color.Gradient = new List<string> { "#FFFFFF" };

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(config, "config.toml"));
        Assert.Contains("gradient", ex.Message);
    }
```

with:

```csharp
    [Fact]
    public void Validate_WithGradientModeAndOneColor_ThrowsConfigException()
    {
        var config = new CavaConfig();
        config.Color.Mode = "gradient";
        config.Color.Palette = "custom";
        config.Color.Gradient = new List<string> { "#FFFFFF" };

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(config, "config.toml"));
        Assert.Contains("gradient", ex.Message);
    }
```

(Without this change, this test would start passing for the wrong reason once Task 5 Step 4 lands: `Palette` defaults to `"rainbow"`, and a named preset now skips the gradient-count check entirely — the test would still pass today because it currently checks unconditionally, but silently stop testing the custom-gradient path it's named for.)

- [ ] **Step 2: Append the new failing tests**

Add these four tests to `tests/CavaPlus.Tests/Config/ConfigLoaderTests.cs` (inside the `ConfigLoaderTests` class, alongside the existing tests):

```csharp
    [Fact]
    public void LoadOrCreateDefault_WhenFileMissing_IncludesColorReactivityDefaults()
    {
        string path = TempConfigPath();
        try
        {
            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.Equal("rainbow", config.Color.Palette);
            Assert.True(config.Color.Reactive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreateDefault_WithExistingValidFile_ParsesPaletteAndReactive()
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
                mode = "gradient"
                solid = "#112233"
                gradient = ["#000000", "#FFFFFF"]
                palette = "fire"
                reactive = false
                """);

            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.Equal("fire", config.Color.Palette);
            Assert.False(config.Color.Reactive);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_WithInvalidPalette_ThrowsConfigException()
    {
        var config = new CavaConfig();
        config.Color.Palette = "bogus";

        var ex = Assert.Throws<ConfigException>(() => ConfigLoader.Validate(config, "config.toml"));
        Assert.Contains("palette", ex.Message);
    }

    [Fact]
    public void Validate_WithNamedPaletteAndTooFewGradientColors_DoesNotThrow()
    {
        var config = new CavaConfig();
        config.Color.Mode = "gradient";
        config.Color.Palette = "fire";
        config.Color.Gradient = new List<string> { "#FFFFFF" };

        var exception = Record.Exception(() => ConfigLoader.Validate(config, "config.toml"));

        Assert.Null(exception);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test CavaPlus.sln --filter ConfigLoaderTests
```

Expected: FAIL — `CavaConfig.ColorSection` has no `Palette`/`Reactive` members (compile error).

- [ ] **Step 4: Add `Palette` and `Reactive` to `CavaConfig.ColorSection`**

In `src/CavaPlus/Config/CavaConfig.cs`, modify the `ColorSection` class:

```csharp
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
```

(Only `GeneralSection`, `BarsSection`, and `NowPlayingSection` stay exactly as they are — this only adds two properties to `ColorSection`.)

- [ ] **Step 5: Update `ConfigLoader.Validate`**

In `src/CavaPlus/Config/ConfigLoader.cs`, add a static array right below the existing `Options` field:

```csharp
    private static readonly string[] ValidPalettes = { "rainbow", "neon", "fire", "ice", "mono", "custom" };
```

Then replace the `Validate` method body with:

```csharp
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
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test CavaPlus.sln --filter ConfigLoaderTests
```

Expected: PASS (10 tests: 6 pre-existing plus these 4 new ones).

- [ ] **Step 7: Run the full suite**

```bash
dotnet test CavaPlus.sln
```

Expected: PASS (58 tests total: 36 pre-existing + 6 `ColorPalettesTests` + 4 `ColorReactivityTests` + 3 `LoudnessTrackerTests` + 5 `BassBeatDetectorTests` + 4 new `ConfigLoaderTests`).

- [ ] **Step 8: Commit**

```bash
git add src/CavaPlus/Config/CavaConfig.cs src/CavaPlus/Config/ConfigLoader.cs tests/CavaPlus.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat: add color.palette and color.reactive config fields"
```

---

### Task 6: Program.cs wiring

**Files:**
- Modify: `src/CavaPlus/Program.cs`

Integration point — no automated tests (same as every prior `Program.cs` task). Build/test verification plus manual end-to-end check (Task 7) are the appropriate checks here. This task also removes the local `ParseHexColor` function, replacing it with the shared `ColorPalettes.ParseHexColor` added in Task 1 — two copies of the same hex-parsing logic would otherwise exist side by side.

- [ ] **Step 1: Replace `Program.cs` with the full content below**

`src/CavaPlus/Program.cs`:

```csharp
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
            // kick/bass energy, without running a third FFT.
            bassMagnitudes = leftMagnitudes.Zip(rightMagnitudes, (l, r) => l + r).ToArray();
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
```

(This drops the trailing `static Color ParseHexColor(string hex) { ... }` local function entirely — it's now `ColorPalettes.ParseHexColor`, used both here and inside `ColorPalettes.Resolve`.)

- [ ] **Step 2: Build and run the full test suite**

```bash
dotnet build CavaPlus.sln
dotnet test CavaPlus.sln
```

Expected: build succeeds, all 58 tests pass (`Program.cs` itself has no unit tests — it's the manual-verification integration point).

- [ ] **Step 3: Commit**

```bash
git add src/CavaPlus/Program.cs
git commit -m "feat: wire palette resolution and audio-reactive color into the render loop"
```

---

### Task 7: Manual end-to-end verification and republish

**Files:** none (verification + build output only)

- [ ] **Step 1: Republish the self-contained exe**

```bash
dotnet publish src/CavaPlus/CavaPlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Expected: `publish\CavaPlus.exe` is produced (overwriting the previous build).

- [ ] **Step 2: Manual verification in a real terminal**

Run `publish\CavaPlus.exe` (or `dotnet run --project src/CavaPlus`) in an actual Windows Terminal window with music playing, and confirm:

- With `color.reactive = true` (default): bars look dimmer during quiet passages and brighter during loud passages, without being distracting; a bass hit/kick produces a brief, visible flash across the bars that fades out within a couple of frames.
- The now-playing title/artist row (if enabled) never dims or flashes — it stays legible white throughout.
- Setting `color.palette` in `%APPDATA%\CavaPlus\config.toml` to each of `rainbow`, `neon`, `fire`, `ice`, `mono`, and `custom` (with a custom `gradient` list) produces the expected gradient in each case.
- Setting `color.reactive = false` makes the bars look exactly as they did before this feature — only the palette colors change, no brightness pulsing or flashing.

- [ ] **Step 3: No commit needed**

This task only produces build output (`publish/`, already gitignored) — nothing to commit unless manual verification surfaces a bug, in which case fix it as a normal follow-up commit and re-run Steps 1-2.

---

## Self-review notes

- **Spec coverage:** `ColorPalettes` with all 5 named presets + `custom` (Task 1), `ColorReactivity` pure brightness/flash mutation (Task 2), `LoudnessTracker` adaptive-peak-normalized smoothed brightness (Task 3), `BassBeatDetector` baseline/threshold/cooldown/decay (Task 4), `color.palette`/`color.reactive` config fields with palette-enum validation and gradient-count check scoped to `palette == "custom"` (Task 5), `Program.cs` wiring in the `BuildFrame -> ColorReactivity -> OverlayText -> Render` order with the `color.reactive` on/off switch and combined-channel bass magnitudes (Task 6), manual visual verification covering every bullet from the spec's "Verificación manual" section (Task 7). The spec's "fuera de alcance" items (decorative frame, bottom status bar, non-bass beat detection, additional/named custom palettes, configurable brightness/flash ranges) are correctly not built anywhere in this plan.
- **Placeholder scan:** none found — every step has literal, complete code or exact commands.
- **Type consistency:** `ColorPalettes.Resolve(string, List<string>) -> List<Color>` and `ColorPalettes.ParseHexColor(string) -> Color` (Task 1) match their call sites in `Program.cs` (Task 6) exactly. `ColorReactivity.Apply(BarCell[,], float, float)` (Task 2) matches its call site (Task 6). `LoudnessTracker.Update(float[], float[]) -> float` (Task 3) and `BassBeatDetector(int, int)` / `.Update(float[]) -> float` (Task 4) match their construction and call sites (Task 6) exactly. `CavaConfig.ColorSection.Palette`/`.Reactive` (Task 5) match their usage in `Program.cs` (Task 6) exactly.
- **Regression guard caught during planning, not after:** the existing test `Validate_WithGradientModeAndOneColor_ThrowsConfigException` would keep passing even if the new `palette == "custom"` scoping were implemented backwards (e.g. inverted), because it doesn't set `Palette` and would coincidentally hit the `custom`-shaped default only if the default were `"custom"` — it isn't (`"rainbow"`), so the test needed an explicit `Palette = "custom"` to keep testing what it claims to (Task 5, Step 1), rather than silently degrading into a test that no longer exercises the custom-gradient path.
