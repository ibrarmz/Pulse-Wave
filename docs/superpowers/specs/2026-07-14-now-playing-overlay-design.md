# CavaPlus: overlay de "qué se está escuchando"

**Fecha:** 2026-07-14
**Estado:** Aprobado para planificación de implementación
**Depende de:** `docs/superpowers/specs/2026-07-10-cava-plus-windows-design.md` (spec original de CavaPlus)

## Contexto y objetivo

CavaPlus ya tiene un pipeline de visualización de audio funcionando de punta a punta (WASAPI loopback → FFT → barras → render en terminal), implementado en la rama `feature/cavaplus-mvp` (aún no mergeada a `master`). Esta feature agrega un overlay de texto en la fila superior de la pantalla mostrando "Título — Artista" de lo que sea que Windows esté reproduciendo actualmente (Spotify, un video en el navegador, un reproductor nativo, etc.), superpuesto sobre el grid de barras existente.

## Cambio de plataforma requerido

Para leer la metadata de "now playing" del sistema se usa `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` (la misma fuente de datos que usa el overlay multimedia nativo de Windows al presionar las teclas de volumen). Esta API requiere que el proyecto apunte a un TDF de Windows concreto:

`src/CavaPlus/CavaPlus.csproj`: `TargetFramework` cambia de `net8.0` a `net8.0-windows10.0.19041.0`.

CavaPlus ya es una app exclusiva de Windows (usa WASAPI y la consola de Windows Terminal), así que este cambio no reduce compatibilidad real.

## Arquitectura

### Componentes nuevos

- **`NowPlayingInfo`** (`src/CavaPlus/Media/NowPlayingInfo.cs`) — record inmutable `(string Title, string Artist)`.
- **`NowPlayingWatcher`** (`src/CavaPlus/Media/NowPlayingWatcher.cs`) — envuelve `GlobalSystemMediaTransportControlsSessionManager`:
  - `StartAsync()`: obtiene el manager una sola vez (`RequestAsync()`), se suscribe a su evento `CurrentSessionChanged`, y se suscribe al `MediaPropertiesChanged` de la sesión actualmente activa (si hay una). Cuando `CurrentSessionChanged` dispara, se desuscribe de la sesión anterior y se suscribe a la nueva.
  - Cada vez que `MediaPropertiesChanged` dispara (o al suscribirse a una sesión nueva), llama a `TryGetMediaPropertiesAsync()` de esa sesión y actualiza una propiedad thread-safe `Current` con un nuevo `NowPlayingInfo` (o `null` si la sesión no tiene título).
  - Si `RequestAsync()` u otra parte de la inicialización lanza una excepción (API no soportada en este Windows, activación WinRT falla, etc.), la excepción se captura dentro de `StartAsync()`, se ignora, y `Current` queda en `null` permanentemente — el resto de la app sigue funcionando sin overlay.
  - Patrón de concurrencia: igual que `SampleRingBuffer`/`AudioCapture` — un lado (los eventos de WinRT, en su propio hilo) escribe `Current` bajo un lock simple; el render loop lee `Current` cada frame sin bloquear.
- **`OverlayText`** (`src/CavaPlus/Rendering/OverlayText.cs`) — función estática pura: `Apply(BarCell[,] frame, string text, int row, Color color)`. Centra `text` horizontalmente respecto al número de columnas del frame, trunca con `…` al final si no cabe, y sobreescribe la fila `row` completa del grid (las columnas fuera del texto centrado quedan en blanco/espacio con el mismo color). Si `row` está fuera de rango del frame, no hace nada (no lanza excepción). No depende de Windows ni de audio — 100% unit-testeable con datos sintéticos.

### Wiring en `Program.cs`

Después de `frameBuilder.BuildFrame(...)` y antes de `renderer.Render(frame)`:

```
if (config.NowPlaying.Enabled)
{
    var current = nowPlayingWatcher.Current;
    string text = current is not null
        ? $"{current.Title} — {current.Artist}"
        : config.NowPlaying.Placeholder;
    OverlayText.Apply(frame, text, row: 0, color: Color.White);
}
```

`nowPlayingWatcher.StartAsync()` se llama una sola vez al inicio del programa (requiere que `Program.cs` use `await` en top-level statements, lo cual el compilador soporta generando un `Main` async automáticamente), envuelto en un `try/catch` que no es fatal si falla.

## Configuración

Nueva sección en `config.toml`:

```toml
[nowplaying]
enabled = true
placeholder = "— sin reproducción —"
```

- `nowplaying.enabled`: si es `false`, no se llama a `NowPlayingWatcher.StartAsync()` en absoluto y no se dibuja overlay — las barras se ven exactamente igual que hoy.
- `nowplaying.placeholder`: texto mostrado cuando no hay sesión de medios activa o la sesión no reporta título.

`ConfigLoader.Validate` no necesita reglas nuevas más allá de que ambos campos existan con sus defaults (no hay valores "inválidos" posibles para un `bool` o un `string` libre).

## Formato del texto

- **Con canción activa:** una línea centrada, `"{Título} — {Artista}"` (ej. `"Blinding Lights — The Weeknd"`).
- **Sin sesión activa / sin título:** se muestra `config.NowPlaying.Placeholder`.
- **Truncado:** si el texto (el real o el placeholder) excede el ancho de la terminal, se corta y se agrega `…` al final, nunca rompe el ancho del grid de columnas.
- **Color:** blanco fijo (`#FFFFFF`), no configurable en esta versión (para que se lea claro sobre cualquier color de barra debajo).
- **Fila:** siempre la fila 0 (la más alta), sobreescribiendo ahí lo que hubiera de las barras.

## Manejo de errores

- **API no disponible o falla de inicialización:** capturada dentro de `NowPlayingWatcher.StartAsync()`; el resto de la app sigue funcionando normalmente, mostrando siempre el placeholder (nunca hay `Current`).
- **`Program.cs` no debe fallar si el overlay falla:** el `try/catch` alrededor de la inicialización del watcher es no-fatal.
- **Cambio de sesión activa en caliente** (pausar una app y reproducir en otra): manejado por la resuscripción en `CurrentSessionChanged`, sin intervención del usuario.

## Testing

- **`OverlayText.Apply`** (unit-testeable, pura): centrado correcto con texto más corto que el ancho, truncado con `…` cuando el texto excede el ancho, comportamiento con texto vacío, no lanza excepción si `row` está fuera de rango del frame.
- **`NowPlayingWatcher`** (no unit-testeable, depende de WinRT/sesiones de medios reales de Windows): validado manualmente — correr la app con Spotify o un video de YouTube sonando y confirmar que el título/artista aparece arriba y cambia al cambiar de canción o de sesión activa; confirmar que con `nowplaying.enabled = false` no aparece nada.

## Fuera de alcance (esta versión)

- Barra de progreso de la canción (tiempo transcurrido/duración).
- Carátula del álbum (no aplica a una terminal de texto).
- Colores/estilo configurable para el texto del overlay.
- Selección manual de qué app/sesión de medios mostrar cuando hay varias simultáneas (se usa la que Windows considera "sesión actual").
