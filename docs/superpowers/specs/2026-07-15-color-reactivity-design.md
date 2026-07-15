# Pulsewave: paletas y color reactivo al audio

**Fecha:** 2026-07-15
**Estado:** Aprobado para planificación de implementación
**Depende de:** `docs/superpowers/specs/2026-07-10-cava-plus-windows-design.md` (spec original), `docs/superpowers/specs/2026-07-14-now-playing-overlay-design.md` (overlay de now-playing, ya implementado)

## Contexto y objetivo

Pulsewave ya tiene el pipeline de visualización funcionando (WASAPI loopback → FFT → barras con gradiente por altura → overlay de now-playing → render en terminal), sobre la rama `feature/cavaplus-mvp`. Esta feature agrega dos cosas independientes para que la visualización se sienta más viva:

1. **Paletas de color seleccionables** — además del arcoíris actual (ahora nombrado `rainbow`), agregar `neon`, `fire`, `ice` y `mono` como opciones de `config.toml`.
2. **Color reactivo al audio** — el brillo de las barras respira con el volumen general de la canción, y un flash breve ilumina todas las barras en golpes fuertes de graves (kick/bajo).

Ambas ideas se descartaron explícitamente de este alcance: un marco/borde decorativo alrededor de las barras (se probó en mockup y no aportaba) y una barra de estado inferior con info técnica.

## Arquitectura

El pipeline de render por frame pasa de `BuildFrame → OverlayText → Render` a `BuildFrame → ColorReactivity → OverlayText → Render`. El texto de now-playing se aplica **después** de la reactividad de color para que el título/artista nunca se atenúe ni parpadee con el flash — siempre se lee en blanco fijo, igual que hoy.

### Componentes nuevos

- **`ColorPalettes`** (`src/Pulsewave/Rendering/ColorPalettes.cs`) — lookup estático puro:

  ```csharp
  public static List<Color> Resolve(string paletteName, List<string> customGradientHex)
  ```

  Presets (de grave a agudo, mismo orden que el `gradient` actual):
  - `rainbow` (default, igual al gradiente actual): `#FF0000, #FF7F00, #FFFF00, #00FF00, #0000FF, #4B0082, #9400D3`
  - `neon`: `#FF00FF, #B84DFF, #7B61FF, #00D4FF`
  - `fire`: `#3D0000, #B22200, #FF6A00, #FFD400`
  - `ice`: `#001A4D, #0066CC, #33CCFF, #FFFFFF`
  - `mono`: `#003300, #00AA00, #00FF00, #CCFFCC`
  - `custom`: devuelve `customGradientHex` (parseado), tal cual funciona hoy `config.Color.Gradient`.

  Solo se usa cuando `config.Color.Mode == "gradient"`. En modo `solid`, `config.Color.Solid` sigue siendo la única fuente de color, sin cambios.

- **`LoudnessTracker`** (`src/Pulsewave/Audio/LoudnessTracker.cs`) — clase con estado (mismo estilo que `BarMapper`, que ya usa escalado de pico adaptativo):
  - `Update(float[] leftSamples, float[] rightSamples) -> float brightnessFactor`.
  - Calcula el RMS combinado de ambos canales del frame actual.
  - Mantiene un pico interno que decae lentamente (`peak = Math.Max(currentRms, peak * 0.995)` por frame) — sube instantáneo en partes fuertes, baja gradual en partes tranquilas, para que la reactividad sea relativa a la dinámica de la canción y no al volumen absoluto del sistema.
  - `loudnessFraction = peak > 0 ? currentRms / peak : 0`, clamped a `[0, 1]`.
  - Brillo objetivo: `0.7 + 0.3 * loudnessFraction` (rango sutil, decidido en brainstorming).
  - Suaviza el resultado frame a frame con una media móvil exponencial (factor `0.15`) para que "respire" en vez de saltar.

- **`BassBeatDetector`** (`src/Pulsewave/Spectrum/BassBeatDetector.cs`) — clase con estado:
  - `Update(float[] magnitudes) -> float flashIntensity` (recibe las magnitudes FFT ya calculadas por `SpectrumProcessor` cada frame; usa la sub-banda de graves, aprox. por debajo de ~150 Hz, calculada a partir de `sampleRate`/`fftLength` igual que ya hace `BarMapper` para mapear bins a frecuencia).
  - Mantiene una línea base (`baseline`) de energía de graves, suavizada lentamente (factor `0.05`) en todo momento.
  - Dispara `flashIntensity = 1.0` cuando la energía actual de graves supera `baseline * 1.6` **y** supera un piso mínimo absoluto (evita falsos positivos en silencio casi total, donde cualquier ruido parecería un "pico" relativo) **y** no está en cooldown.
  - Cooldown de ~6 frames tras un disparo, para no re-disparar en una nota de bajo sostenida.
  - Sin disparo nuevo, `flashIntensity` decae multiplicándose por `0.5` cada frame (se apaga en 2-3 frames).

- **`ColorReactivity`** (`src/Pulsewave/Rendering/ColorReactivity.cs`) — función estática pura:

  ```csharp
  public static void Apply(BarCell[,] frame, float brightnessFactor, float flashIntensity)
  ```

  Para cada celda: escala R/G/B por `brightnessFactor` (clamped a `[0,255]`), luego mezcla el resultado hacia blanco proporcional a `flashIntensity * 0.6` (así un flash al 100% se ve como un destello, no un whiteout total). Mismo estilo que `OverlayText`: mutación directa del grid, sin I/O, 100% unit-testeable.

### Flujo por frame en `Program.cs`

```
ringBuffer.ReadLatest(...) → leftSamples, rightSamples   (ya existe)
magnitudes = spectrumProcessor.ComputeMagnitudes(...)     (ya existe)
brightnessFactor = loudnessTracker.Update(leftSamples, rightSamples)
flashIntensity   = bassBeatDetector.Update(magnitudes)
frame = frameBuilder.BuildFrame(heights, rows)            (colorStops ahora vienen de ColorPalettes.Resolve)
if (config.Color.Reactive) ColorReactivity.Apply(frame, brightnessFactor, flashIntensity)
if (config.NowPlaying.Enabled) OverlayText.Apply(frame, ...)   (ya existe, sin cambios)
renderer.Render(frame)                                    (ya existe)
```

Cuando `config.Color.Reactive == false`, simplemente no se llama a `ColorReactivity.Apply` — el frame queda igual que hoy, sin ninguna rama especial dentro de las clases nuevas.

En modo estéreo, `magnitudes` para `BassBeatDetector` usa la suma de las magnitudes izquierda+derecha del frame (misma fuente que ya se calcula para las barras, no se agrega ningún cómputo de FFT extra).

## Configuración

Nuevos campos en la sección `[color]` existente:

```toml
[color]
mode = "gradient"
solid = "#00FF00"
gradient = ["#FF0000", "#FF7F00", "#FFFF00", "#00FF00", "#0000FF", "#4B0082", "#9400D3"]
palette = "rainbow"   # rainbow | neon | fire | ice | mono | custom
reactive = true
```

- `palette` (default `"rainbow"`): nombre del preset. Solo se consulta `gradient` cuando `palette = "custom"`.
- `reactive` (default `true`): apaga brillo-reactivo y flash de graves si es `false`. No afecta paletas ni modo solid/gradient.

### Cambios en `ConfigLoader.Validate`

- `palette` debe ser uno de `rainbow, neon, fire, ice, mono, custom`; cualquier otro valor lanza `ConfigException` mencionando `"palette"` (mismo patrón que la validación existente de `channels`).
- La regla ya existente "`gradient` necesita al menos 2 colores cuando `mode = gradient`" ahora **solo aplica cuando `palette = "custom"`** — con un preset con nombre, el contenido de `gradient` es irrelevante y no se valida.

## Manejo de errores

No hay modos de falla nuevos: todo el código nuevo es matemática pura sobre datos que el pipeline ya calcula cada frame (muestras de audio, magnitudes FFT). No hay I/O, no hay excepciones esperadas más allá de la validación de config ya cubierta arriba.

## Testing

- **`ColorPalettes.Resolve`** (pura): cada nombre de preset devuelve la lista de colores exacta documentada arriba; `"custom"` devuelve el `gradient` parseado tal cual.
- **`ColorReactivity.Apply`** (pura, mismo estilo que `OverlayTextTests`): `brightnessFactor = 1.0, flashIntensity = 0` deja el frame sin cambios; `brightnessFactor = 0.7` escala cada canal por 0.7; `flashIntensity = 1.0` mezcla hacia blanco en la proporción esperada.
- **`LoudnessTracker.Update`** (con arrays de muestras sintéticas, sin audio real): una racha de muestras en silencio produce brillo cercano a `0.7`; una racha de muestras a volumen alto sostenido produce brillo cercano a `1.0`; el cambio entre un frame y el siguiente es gradual (no salta de golpe), verificable comparando frames consecutivos.
- **`BassBeatDetector.Update`** (con arrays de magnitudes sintéticas): una serie de magnitudes de graves estables no dispara flash; un pico repentino muy por encima de la línea base dispara `flashIntensity = 1.0` ese frame y decae en los frames siguientes; un pico repentino pero en silencio casi total (por debajo del piso mínimo) no dispara.
- **`ConfigLoader`**: nombre de paleta inválido lanza `ConfigException`; `palette = "custom"` con un solo color en `gradient` sigue lanzando la validación existente; `palette` con un preset con nombre y `gradient` inválido/vacío NO lanza (el campo se ignora).
- **`Program.cs`**: sin tests automatizados (igual que el resto del wiring), validado manualmente — ver sección siguiente.

## Verificación manual

Con música sonando y `config.Color.Reactive = true`:
- Las barras se ven más tenues en partes tranquilas y más brillantes en partes fuertes, sin ser distractoras.
- Un golpe de bajo/kick produce un destello breve visible en todas las barras, que se apaga en 2-3 frames.
- El texto de now-playing arriba nunca cambia de brillo ni parpadea con el flash.
- Cambiar `palette` entre `rainbow/neon/fire/ice/mono/custom` en config produce el gradiente esperado sin reiniciar la lógica de barras.
- Con `reactive = false`, las barras se ven exactamente igual que antes de esta feature (solo el color de paleta cambia, sin brillo dinámico ni flash).

## Fuera de alcance (esta versión)

- Marco/borde decorativo alrededor del área de visualización (se evaluó con mockups y se descartó).
- Barra de estado inferior (bitrate, FPS, volumen).
- Detección de beat más allá de graves (no se detectan snares/hi-hats ni tempo/BPM).
- Paletas adicionales más allá de las 5 listadas, o paletas custom nombradas (solo `custom` = lista libre de colores, sin nombre propio).
- Intensidad de brillo o de flash configurable por el usuario (los rangos `[0.7, 1.0]` y el multiplicador `0.6` del flash son fijos en esta versión).
