# Pulsewave: audio visualizer para terminal en Windows

**Fecha:** 2026-07-10
**Estado:** Aprobado para planificación de implementación

## Contexto y objetivo

[cava](https://github.com/karlstav/cava) es un visualizador de audio de consola para Linux, basado en ALSA/PulseAudio. No existe una versión nativa equivalente para Windows. Este proyecto (`Pulsewave`) construye un visualizador de barras de espectro de audio para terminal, específicamente para Windows, capturando el audio de salida del sistema y dibujando barras estilo cava en tiempo real.

**Alcance de esta v1 (MVP):** foco en que la experiencia de uso cotidiano (visualizar lo que suena en Windows Terminal) funcione bien y de forma fluida, dejando fuera del alcance ajustes finos avanzados (framerate configurable, curvas de suavizado custom, noise reduction, número de barras fijo) para una v2 futura.

## Stack técnico

- **Lenguaje:** C# / .NET 8
- **Captura de audio:** NAudio, `WasapiLoopbackCapture` (audio de salida del sistema — lo que suena en los altavoces, sin importar qué app lo produce)
- **FFT:** `NAudio.Dsp.FastFourierTransform` (evita agregar una dependencia extra solo para esto)
- **Render de terminal:** Spectre.Console (truecolor, buen control de posicionamiento, sin manejo manual de ANSI)
- **Terminal objetivo principal:** Windows Terminal (truecolor + Unicode completo). PowerShell/cmd clásico y WSL no son objetivo explícito de esta v1, aunque probablemente funcionen con degradación visual.
- **Distribución:** `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true` → ejecutable único sin dependencia del runtime .NET instalado.

## Arquitectura

### Componentes

- **`AudioCapture`** — envuelve `WasapiLoopbackCapture`. Se suscribe a `DataAvailable` y escribe las muestras a un `SampleRingBuffer` compartido thread-safe. No conoce FFT ni render — su única responsabilidad es mover bytes de audio del dispositivo al buffer.
- **`SpectrumProcessor`** — lee la ventana más reciente del ring buffer, aplica ventaneo Hann y FFT. Devuelve magnitudes por bin de frecuencia, separadas por canal (izq/der) cuando el modo es estéreo.
- **`BarMapper`** — agrupa bins de FFT en N barras (N derivado del ancho de terminal y el ancho de barra configurado), aplica escala logarítmica de frecuencia (igual que cava, para que las barras bajas no dominen visualmente), sensibilidad/ganancia configurable, y suavizado simple entre frames consecutivos para evitar saltos brúscos.
- **`Renderer`** — dibuja el frame actual con Spectre.Console según el estilo de barra y el color/gradiente configurados. Corre en su propio loop a framerate fijo (~60fps).
- **`Config`** — carga `config.toml`, aplica overrides de flags CLI, valida rangos. Se resuelve una única vez al arrancar (sin hot-reload en el MVP).
- **`Program`** — entry point: parsea CLI, carga config, instancia los componentes, maneja ciclo de vida (Ctrl+C, resize de terminal).

### Modelo de concurrencia

Captura y render corren en hilos separados y desacoplados (el mismo patrón que usa cava original):

- El hilo de captura de WASAPI solo escribe muestras al `SampleRingBuffer` — nunca bloquea esperando al render.
- El hilo de render corre a framerate fijo (~60fps) independientemente del timing de llegada de audio: en cada tick, lee el estado más reciente del ring buffer (sin esperar a que "llegue algo nuevo"), corre `SpectrumProcessor` → `BarMapper` → `Renderer`.

Esto mantiene la animación fluida incluso si el audio llega en ráfagas irregulares (comportamiento normal de los callbacks de WASAPI).

### Flujo de datos

```
WASAPI loopback → AudioCapture (hilo captura) → SampleRingBuffer
                                                        │
                            Renderer loop (60fps) ──────┤
                                    │                   │
                            SpectrumProcessor ←──────────
                                    │
                              BarMapper
                                    │
                            Spectre.Console draw
```

## Configuración

### Archivo de config

Formato TOML en `%APPDATA%\Pulsewave\config.toml`. Se genera automáticamente con valores default si no existe al primer arranque.

```toml
[general]
framerate = 60
sensitivity = 100        # % de ganancia, 100 = neutral
channels = "stereo"      # "mono" | "stereo"

[bars]
style = "classic"        # "classic" | "gradient"
width = 2                # ancho de cada barra en columnas
gap = 1                  # espacio entre barras (solo estilo "classic")

[color]
mode = "gradient"        # "solid" | "gradient"
solid = "#00FF00"
gradient = ["#00FF00", "#FFFF00", "#FF0000"]   # bottom → top, 2-3 stops
```

### Flags CLI (overrides puntuales, no persisten)

```
cavaplus --style gradient --channels mono --sensitivity 150 --config path\to\config.toml
```

Config inválido (TOML mal formado o valores fuera de rango) aborta el arranque con un mensaje de error específico (archivo + detalle), en vez de continuar con valores parcialmente corruptos.

## Canales

Configurable vía `general.channels`:
- `mono`: una sola franja de barras combinando ambos canales.
- `stereo`: barras reflejadas izquierda/derecha (como el modo estéreo de cava).

## Estilos de barra

Dos estilos soportados en esta v1:

- **`classic`** — bloque `█` sólido por celda de altura entera, con `gap` columnas de espacio entre barras. El look tradicional de cava.
- **`gradient`** — usa los 8 niveles de bloque Unicode (▁▂▃▄▅▆▇█) para resolución sub-carácter en la altura, permitiendo barras más "suaves" verticalmente.

## Color

Dos modos vía `color.mode`:
- **`solid`** — un color fijo (`color.solid`) para todas las barras.
- **`gradient`** — interpolación de 2-3 stops de color (`color.gradient`) aplicada de abajo hacia arriba según la altura de cada barra, renderizada con truecolor vía Spectre.Console.

## Manejo de errores

- **Sin dispositivo de audio de salida / WASAPI no disponible:** mensaje claro en consola, salida limpia (sin stack trace crudo).
- **Silencio total:** no es un error — las barras simplemente se muestran en su altura mínima/plana.
- **Resize de terminal:** el `Renderer` detecta el nuevo ancho/alto en cada frame y recalcula el número de barras dinámicamente — no requiere reiniciar la app.
- **Config inválido:** aborta el arranque con mensaje específico (ver sección de configuración).
- **Ctrl+C:** restaura el estado de la consola (cursor visible, colores reseteados) antes de salir.

## Testing

- `SpectrumProcessor` y `BarMapper` son unit-testeables de forma pura (input: arrays de floats/magnitudes → output: alturas de barra), sin dependencias de consola ni de audio real. Se testean con datos sintéticos (ej. una señal seno pura de 440Hz, verificando que el bin de frecuencia esperado se activa).
- `AudioCapture` y `Renderer` dependen de hardware de audio real y de la consola — se validan manualmente, no con unit tests automatizados. No se finge cobertura que no es realista para estos componentes.
- Sin tests de integración automatizados en el MVP (mockear WASAPI no vale la pena para el alcance actual).

## Fuera de alcance (v1)

- Selección de dispositivo de entrada (micrófono) como fuente alterna — solo loopback de salida.
- Framerate configurable, número de barras fijo (vs. auto-ajustado al ancho de terminal), curvas de suavizado personalizadas, noise reduction, modos "integral"/"monstercat" de cava.
- Soporte explícito para PowerShell/cmd clásico o WSL (pueden funcionar con degradación visual, pero no son objetivo de testing).
- Hot-reload de configuración mientras la app corre.
- Distribución como .NET global tool (se usa publish self-contained).
