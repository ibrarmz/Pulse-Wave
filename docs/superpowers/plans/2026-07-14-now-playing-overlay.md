# Now-Playing Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show "Título — Artista" of whatever is currently playing on Windows, centered on the top row of CavaPlus's bar display.

**Architecture:** `NowPlayingWatcher` wraps `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`, subscribing once to its session-change/media-property-change events and exposing a thread-safe `Current` snapshot (same producer/reader pattern already used by `SampleRingBuffer`). Each render frame, `Program.cs` reads that snapshot (non-blocking) and overwrites the top row of the `BarCell[,]` frame with centered text via a new pure `OverlayText` helper, before handing the frame to `ConsoleRenderer`.

**Tech Stack:** .NET 8 targeting `net8.0-windows10.0.19041.0` (adds the `Windows.Media.Control` WinRT projection used by no other part of the app), the existing `CavaConfig`/`ConfigLoader`/Spectre.Console/xUnit stack.

**Reference spec:** `docs/superpowers/specs/2026-07-14-now-playing-overlay-design.md`
**Builds on:** `docs/superpowers/plans/2026-07-10-cava-plus-windows.md` (already implemented and working on this same branch, `feature/cavaplus-mvp`)

---

## File structure

```
src/CavaPlus/
  CavaPlus.csproj                    # TargetFramework -> net8.0-windows10.0.19041.0
  Program.cs                         # wire NowPlayingWatcher + OverlayText into the render loop
  Media/
    NowPlayingInfo.cs                # record (Title, Artist)
    NowPlayingWatcher.cs             # wraps GlobalSystemMediaTransportControlsSessionManager
  Rendering/
    OverlayText.cs                   # pure: BarCell[,] + text + row + color -> mutated frame
  Config/
    CavaConfig.cs                    # + NowPlayingSection (Enabled, Placeholder)
tests/CavaPlus.Tests/
  Rendering/OverlayTextTests.cs      # new
  Config/ConfigLoaderTests.cs        # + 2 new tests for the NowPlaying section
```

`NowPlayingWatcher` wraps a real Windows Runtime API (live media sessions) and has no automated unit tests, same category as `AudioCapture`/`ConsoleRenderer` in the original plan — validated manually. `OverlayText` is pure text/grid math and is fully unit-tested.

---

### Task 1: Retarget the project to a Windows-specific TFM

**Files:**
- Modify: `src/CavaPlus/CavaPlus.csproj`

This is a prerequisite for every later task — `Windows.Media.Control` types don't resolve under the generic `net8.0` moniker.

- [ ] **Step 1: Change the TargetFramework**

`src/CavaPlus/CavaPlus.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NAudio" Version="2.3.0" />
    <PackageReference Include="Spectre.Console" Version="0.57.2" />
    <PackageReference Include="Tomlyn" Version="2.10.1" />
  </ItemGroup>

</Project>
```

(Only the `<TargetFramework>` line changes, from `net8.0` to `net8.0-windows10.0.19041.0`. No new NuGet package is required — `Windows.Media.Control` resolves directly from this TFM's bundled Windows SDK reference assemblies.)

- [ ] **Step 2: Build the whole solution and run the full test suite**

```bash
dotnet build CavaPlus.sln
dotnet test CavaPlus.sln
```

Expected: build succeeds (the test project stays on plain `net8.0` and can still reference the now Windows-targeted app project without issue), all 30 existing tests still pass.

- [ ] **Step 3: Commit**

```bash
git add src/CavaPlus/CavaPlus.csproj
git commit -m "chore: retarget CavaPlus to net8.0-windows10.0.19041.0 for Windows.Media.Control"
```

---

### Task 2: NowPlayingInfo + NowPlayingWatcher

**Files:**
- Create: `src/CavaPlus/Media/NowPlayingInfo.cs`
- Create: `src/CavaPlus/Media/NowPlayingWatcher.cs`

This wraps a real Windows Runtime API (live OS media sessions) — like `AudioCapture` in the original plan, it has no automated unit tests. It's implemented directly (no TDD ceremony) and validated with a manual smoke check.

- [ ] **Step 1: Implement `NowPlayingInfo`**

`src/CavaPlus/Media/NowPlayingInfo.cs`:

```csharp
namespace CavaPlus.Media;

public sealed record NowPlayingInfo(string Title, string Artist);
```

- [ ] **Step 2: Implement `NowPlayingWatcher`**

`src/CavaPlus/Media/NowPlayingWatcher.cs`:

```csharp
using Windows.Media.Control;

namespace CavaPlus.Media;

public sealed class NowPlayingWatcher
{
    private readonly object _lock = new();
    private NowPlayingInfo? _current;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _activeSession;

    public NowPlayingInfo? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => HandleSessionChanged();
            HandleSessionChanged();
        }
        catch
        {
            // API unavailable on this system (unsupported Windows version, WinRT activation
            // failure, etc.) - Current stays null forever and the caller just keeps showing
            // its configured placeholder; the rest of the app is unaffected.
        }
    }

    private void HandleSessionChanged()
    {
        if (_activeSession is not null)
        {
            _activeSession.MediaPropertiesChanged -= HandleMediaPropertiesChanged;
        }

        _activeSession = _manager!.GetCurrentSession();

        if (_activeSession is null)
        {
            SetCurrent(null);
            return;
        }

        _activeSession.MediaPropertiesChanged += HandleMediaPropertiesChanged;
        _ = RefreshCurrentAsync(_activeSession);
    }

    private void HandleMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession session,
        MediaPropertiesChangedEventArgs args)
    {
        _ = RefreshCurrentAsync(session);
    }

    private async Task RefreshCurrentAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            SetCurrent(string.IsNullOrWhiteSpace(properties.Title)
                ? null
                : new NowPlayingInfo(properties.Title, properties.Artist ?? string.Empty));
        }
        catch
        {
            SetCurrent(null);
        }
    }

    private void SetCurrent(NowPlayingInfo? info)
    {
        lock (_lock)
        {
            _current = info;
        }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

```bash
dotnet build CavaPlus.sln
```

Expected: build succeeds.

- [ ] **Step 4: Manual smoke test**

Temporarily write this into `src/CavaPlus/Program.cs` (do NOT commit this — Task 5 replaces `Program.cs` for real; revert to the current committed version before committing this task):

```csharp
using CavaPlus.Media;

var watcher = new NowPlayingWatcher();
await watcher.StartAsync();
Console.WriteLine("Watching for 10 seconds - play/change a song now...");
for (int i = 0; i < 10; i++)
{
    var current = watcher.Current;
    Console.WriteLine(current is null ? "(no session)" : $"{current.Title} — {current.Artist}");
    await Task.Delay(1000);
}
```

Run `dotnet run --project src/CavaPlus`, play something (e.g. a YouTube video or Spotify) during the 10 seconds, and confirm the title/artist appears and updates if you change tracks. This does NOT require a real console handle (no `Console.CursorVisible`/`SetCursorPosition` calls here), so it should run fine even in a non-interactive shell. After confirming, revert `Program.cs` to its last committed state (`git checkout -- src/CavaPlus/Program.cs`) before proceeding.

- [ ] **Step 5: Commit**

```bash
git add src/CavaPlus/Media/NowPlayingInfo.cs src/CavaPlus/Media/NowPlayingWatcher.cs
git commit -m "feat: add NowPlayingWatcher wrapping the system media session manager"
```

---

### Task 3: OverlayText

**Files:**
- Create: `src/CavaPlus/Rendering/OverlayText.cs`
- Create: `tests/CavaPlus.Tests/Rendering/OverlayTextTests.cs`

Pure grid/text math, no I/O — fully unit-testable, follows TDD.

- [ ] **Step 1: Write the failing tests**

`tests/CavaPlus.Tests/Rendering/OverlayTextTests.cs`:

```csharp
using CavaPlus.Rendering;
using Spectre.Console;

namespace CavaPlus.Tests.Rendering;

public class OverlayTextTests
{
    [Fact]
    public void Apply_CentersShortTextWithinFrame()
    {
        var frame = new BarCell[1, 10];
        for (int col = 0; col < 10; col++)
        {
            frame[0, col] = new BarCell('█', Color.Green);
        }

        OverlayText.Apply(frame, "hi", row: 0, color: Color.White);

        Assert.Equal(' ', frame[0, 0].Glyph);
        Assert.Equal(' ', frame[0, 3].Glyph);
        Assert.Equal('h', frame[0, 4].Glyph);
        Assert.Equal('i', frame[0, 5].Glyph);
        Assert.Equal(' ', frame[0, 6].Glyph);
        Assert.Equal(' ', frame[0, 9].Glyph);
        Assert.Equal(Color.White, frame[0, 0].Color);
        Assert.Equal(Color.White, frame[0, 4].Color);
    }

    [Fact]
    public void Apply_TruncatesTextLongerThanFrame_WithEllipsis()
    {
        var frame = new BarCell[1, 5];

        OverlayText.Apply(frame, "abcdefgh", row: 0, color: Color.White);

        Assert.Equal("abcd…", new string(new[]
        {
            frame[0, 0].Glyph, frame[0, 1].Glyph, frame[0, 2].Glyph, frame[0, 3].Glyph, frame[0, 4].Glyph,
        }));
    }

    [Fact]
    public void Apply_WithRowOutOfRange_LeavesFrameUnchanged()
    {
        var frame = new BarCell[2, 4];
        frame[0, 0] = new BarCell('█', Color.Green);
        frame[1, 2] = new BarCell('▄', Color.Red);

        OverlayText.Apply(frame, "hi", row: 5, color: Color.White);
        OverlayText.Apply(frame, "hi", row: -1, color: Color.White);

        Assert.Equal('█', frame[0, 0].Glyph);
        Assert.Equal('▄', frame[1, 2].Glyph);
    }

    [Fact]
    public void Apply_WithTextExactlyFillingFrame_ShowsFullText()
    {
        var frame = new BarCell[1, 5];

        OverlayText.Apply(frame, "hello", row: 0, color: Color.White);

        Assert.Equal("hello", new string(new[]
        {
            frame[0, 0].Glyph, frame[0, 1].Glyph, frame[0, 2].Glyph, frame[0, 3].Glyph, frame[0, 4].Glyph,
        }));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test CavaPlus.sln --filter OverlayTextTests
```

Expected: FAIL — `CavaPlus.Rendering.OverlayText` does not exist.

- [ ] **Step 3: Implement `OverlayText`**

`src/CavaPlus/Rendering/OverlayText.cs`:

```csharp
using Spectre.Console;

namespace CavaPlus.Rendering;

public static class OverlayText
{
    public static void Apply(BarCell[,] frame, string text, int row, Color color)
    {
        int rows = frame.GetLength(0);
        int columns = frame.GetLength(1);

        if (row < 0 || row >= rows)
        {
            return;
        }

        string display = Truncate(text, columns);
        int startColumn = Math.Max(0, (columns - display.Length) / 2);

        for (int col = 0; col < columns; col++)
        {
            int textIndex = col - startColumn;
            char glyph = textIndex >= 0 && textIndex < display.Length ? display[textIndex] : ' ';
            frame[row, col] = new BarCell(glyph, color);
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }
        if (text.Length <= maxLength)
        {
            return text;
        }
        if (maxLength == 1)
        {
            return "…";
        }
        return text[..(maxLength - 1)] + "…";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test CavaPlus.sln --filter OverlayTextTests
```

Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CavaPlus/Rendering/OverlayText.cs tests/CavaPlus.Tests/Rendering/OverlayTextTests.cs
git commit -m "feat: add OverlayText for centering text onto a BarCell frame row"
```

---

### Task 4: Config — NowPlaying section

**Files:**
- Modify: `src/CavaPlus/Config/CavaConfig.cs`
- Modify: `tests/CavaPlus.Tests/Config/ConfigLoaderTests.cs`

**Important naming detail:** `ConfigLoader` maps C# PascalCase property names to TOML keys via `JsonNamingPolicy.CamelCase`, which only lowercases the *first* letter (e.g. `Framerate` → `framerate`, and a two-word name like `NowPlaying` → `nowPlaying`, NOT `nowplaying`). The design spec's TOML example used `[nowplaying]`, but the real serializer produces `[nowPlaying]` for a C# property named `NowPlaying` — this task uses `[nowPlaying]` (verified against the actual behavior below) rather than the spec's illustrative all-lowercase example.

- [ ] **Step 1: Write the failing tests**

Add these two tests to the existing `tests/CavaPlus.Tests/Config/ConfigLoaderTests.cs` (append inside the `ConfigLoaderTests` class, alongside the existing tests — do not remove anything already there):

```csharp
    [Fact]
    public void LoadOrCreateDefault_WhenFileMissing_IncludesNowPlayingDefaults()
    {
        string path = TempConfigPath();
        try
        {
            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.True(config.NowPlaying.Enabled);
            Assert.Equal("— sin reproducción —", config.NowPlaying.Placeholder);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrCreateDefault_WithExistingValidFile_ParsesNowPlayingSection()
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

                [nowPlaying]
                enabled = false
                placeholder = "nada sonando"
                """);

            var config = ConfigLoader.LoadOrCreateDefault(path);

            Assert.False(config.NowPlaying.Enabled);
            Assert.Equal("nada sonando", config.NowPlaying.Placeholder);
        }
        finally
        {
            File.Delete(path);
        }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test CavaPlus.sln --filter ConfigLoaderTests
```

Expected: FAIL — `CavaConfig` has no `NowPlaying` member (compile error).

- [ ] **Step 3: Add `NowPlayingSection` to `CavaConfig`**

In `src/CavaPlus/Config/CavaConfig.cs`, add a new property to the `CavaConfig` class and a new nested section class (leave `GeneralSection`, `BarsSection`, `ColorSection` exactly as they are):

```csharp
namespace CavaPlus.Config;

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
    }

    public sealed class NowPlayingSection
    {
        public bool Enabled { get; set; } = true;
        public string Placeholder { get; set; } = "— sin reproducción —";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test CavaPlus.sln --filter ConfigLoaderTests
```

Expected: PASS (6 tests: the 4 pre-existing plus these 2 new ones).

If `LoadOrCreateDefault_WithExistingValidFile_ParsesNowPlayingSection` fails with parsed values still at their defaults (rather than a compile error), that means Tomlyn's deserializer doesn't merge partial TOML content the way assumed — stop and report this precisely (which values were expected vs. actual) rather than guessing at a fix, since it would mean adjusting how `ConfigLoader` deserializes, not just this test.

- [ ] **Step 5: Run the full suite**

```bash
dotnet test CavaPlus.sln
```

Expected: PASS (32 tests total — this confirms the pre-existing `LoadOrCreateDefault_WithExistingValidFile_ReturnsParsedValues` test, whose TOML text has no `[nowPlaying]` section, still passes with `NowPlaying` quietly taking its class defaults).

- [ ] **Step 6: Commit**

```bash
git add src/CavaPlus/Config/CavaConfig.cs tests/CavaPlus.Tests/Config/ConfigLoaderTests.cs
git commit -m "feat: add NowPlaying config section (enabled, placeholder)"
```

---

### Task 5: Program.cs wiring

**Files:**
- Modify: `src/CavaPlus/Program.cs`

Integration point — no automated tests (same as the original plan's Program.cs task). Build/test verification plus a manual end-to-end check (Task 6) are the appropriate checks here.

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

var barStyle = config.Bars.Style == "gradient" ? BarStyle.Gradient : BarStyle.Classic;

List<Color> colorStops;
try
{
    colorStops = config.Color.Mode == "solid"
        ? new List<Color> { ParseHexColor(config.Color.Solid) }
        : config.Color.Gradient.Select(ParseHexColor).ToList();
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
        }

        var frame = frameBuilder.BuildFrame(heights, rows);

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
dotnet build CavaPlus.sln
dotnet test CavaPlus.sln
```

Expected: build succeeds, all 32 tests pass (`Program.cs` itself has no unit tests — it's the manual-verification integration point, same as in the original plan).

- [ ] **Step 3: Commit**

```bash
git add src/CavaPlus/Program.cs
git commit -m "feat: overlay now-playing title/artist on the top row of the display"
```

---

### Task 6: Manual end-to-end verification and republish

**Files:** none (verification + build output only)

- [ ] **Step 1: Republish the self-contained exe**

```bash
dotnet publish src/CavaPlus/CavaPlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Expected: `publish\CavaPlus.exe` is produced (overwriting the previous build).

- [ ] **Step 2: Manual verification in a real terminal**

This cannot be done from a non-interactive shell (same console-handle limitation already established for `ConsoleRenderer` in the original plan — `Console.CursorVisible`/`Console.SetCursorPosition` throw outside a real attached console). Run `publish\CavaPlus.exe` (or `dotnet run --project src/CavaPlus`) in an actual Windows Terminal window, with `nowPlaying.enabled = true` (the default) in `%APPDATA%\CavaPlus\config.toml`, and confirm:

- With something playing (Spotify, a YouTube tab, etc.), the top row shows `"Título — Artista"` centered.
- Changing tracks updates the text without restarting the app.
- Pausing/stopping all media, or with `nowPlaying.enabled = false` in the config, shows the configured placeholder (or nothing extra beyond the normal bars, if disabled) instead of stale/incorrect text.
- The overlay text doesn't visually break the bar columns below it (only the top row is affected).

- [ ] **Step 3: No commit needed**

This task only produces build output (`publish/`, already gitignored) — nothing to commit unless manual verification surfaces a bug, in which case fix it as a normal follow-up commit and re-run Steps 1-2.

---

## Self-review notes

- **Spec coverage:** TFM change (Task 1), `NowPlayingWatcher` event-driven design + non-fatal error handling (Task 2), `OverlayText` centering/truncation/out-of-range behavior (Task 3), `[nowPlaying]` config section with `enabled`/`placeholder` (Task 4), `Program.cs` wiring including the "disabled means untouched bars" and "no session means placeholder" behaviors (Task 5), manual visual verification (Task 6). The spec's "fuera de alcance" items (progress bar, album art, configurable overlay color, manual session selection) are correctly not built anywhere in this plan.
- **Placeholder scan:** none found — every step has literal, complete code or exact commands.
- **Type consistency:** `NowPlayingInfo(string Title, string Artist)` (Task 2) matches its usage in `Program.cs` (`current.Title`, `current.Artist`, Task 5). `OverlayText.Apply(BarCell[,] frame, string text, int row, Color color)` (Task 3) matches its call site in `Program.cs` (Task 5) exactly. `CavaConfig.NowPlaying.Enabled`/`.Placeholder` (Task 4) match their usage in `Program.cs` (Task 5) exactly.
- **Naming-policy gotcha caught during planning, not after:** the spec's illustrative `[nowplaying]` TOML section name doesn't match what `JsonNamingPolicy.CamelCase` actually produces for a C# property named `NowPlaying` (it produces `nowPlaying`, capital P preserved) — this plan uses the verified-correct `[nowPlaying]` throughout (Task 4), rather than repeating the exact bug this project's own `ConfigLoader.cs` comment already warned about for future multi-word properties.
