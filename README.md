# Pulsewave

Visualizador de espectro de audio para la terminal de Windows, inspirado en [cava](https://github.com/karlstav/cava). Captura el audio que está sonando en el sistema (loopback WASAPI) y lo dibuja como barras en tiempo real directamente en la consola.

## Características

- Captura de audio por loopback WASAPI — visualiza cualquier sonido que esté saliendo por el dispositivo de salida por defecto (Spotify, YouTube, lo que sea).
- Barras estéreo con los graves centrados y los agudos hacia los bordes, o modo mono.
- Dos estilos de barra: `classic` (bloques completos) o `gradient` (subcaracteres para alturas suaves).
- Paletas de color: `rainbow`, `neon`, `fire`, `ice`, `mono`, o una paleta `custom` con tus propios colores.
- Color reactivo al audio: el brillo de las barras respira con el volumen general de la canción.
- Overlay de "reproduciendo ahora" (título y artista) en la fila superior, usando la sesión de medios activa de Windows.
- Configuración vía archivo TOML, con overrides por línea de comandos.

## Requisitos

- Windows 10 1903+ (build 10.0.19041 o superior) o Windows 11.
- Para compilar: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Para solo ejecutar el `.exe` publicado (self-contained): nada más, no requiere el runtime de .NET instalado.

## Uso rápido

```bash
dotnet run --project src/Pulsewave
```

O compila un ejecutable independiente:

```bash
dotnet publish src/Pulsewave/Pulsewave.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Esto genera `publish\Pulsewave.exe`, que corre sin necesitar el SDK ni el runtime instalados.

Para salir, `Ctrl+C`.

## Opciones de línea de comandos

Sobrescriben el valor equivalente del archivo de configuración para esa ejecución:

| Flag | Valores | Ejemplo |
| --- | --- | --- |
| `--style` | `classic`, `gradient` | `--style gradient` |
| `--channels` | `mono`, `stereo` | `--channels mono` |
| `--sensitivity` | número (porcentaje) | `--sensitivity 150` |
| `--config` | ruta a un archivo `.toml` | `--config C:\ruta\mi-config.toml` |

## Configuración

Al arrancar por primera vez, Pulsewave crea un archivo de configuración con los valores por defecto en:

```
%APPDATA%\Pulsewave\config.toml
```

Estructura completa:

```toml
[general]
framerate = 60
sensitivity = 100.0
channels = "stereo"          # "mono" | "stereo"

[bars]
style = "classic"            # "classic" | "gradient"
width = 4
gap = 2

[color]
mode = "gradient"             # "solid" | "gradient"
solid = "#00FF00"             # usado solo cuando mode = "solid"
gradient = ["#FF0000", "#FF7F00", "#FFFF00", "#00FF00", "#0000FF", "#4B0082", "#9400D3"]
palette = "rainbow"           # "rainbow" | "neon" | "fire" | "ice" | "mono" | "custom"
reactive = true               # brillo reactivo al volumen general

[nowPlaying]
enabled = true
placeholder = "— sin reproducción —"
```

Notas:
- `gradient` solo se usa cuando `palette = "custom"`; con cualquier otro nombre de paleta, sus colores predefinidos tienen prioridad.
- `reactive = false` deja las barras con brillo estático (solo aplica el color de la paleta, sin dinámica de volumen).
- `nowPlaying.enabled = false` desactiva por completo la lectura de sesiones de medios de Windows y la fila del overlay.

## Desarrollo

Ejecutar la suite de pruebas:

```bash
dotnet test Pulsewave.sln
```

### Estructura del proyecto

```
src/Pulsewave/
  Program.cs          # punto de entrada y loop de render
  Audio/               # captura WASAPI, ring buffer, brillo reactivo al volumen
  Spectrum/            # FFT / cómputo de magnitudes
  Bars/                # mapeo de magnitudes a alturas de barra
  Rendering/           # construcción del frame, paletas, gradientes, overlay de texto
  Media/               # integración con sesiones de medios de Windows (now-playing)
  Config/              # carga, validación y overrides de configuración
tests/Pulsewave.Tests/  # pruebas xUnit, misma estructura de carpetas que src/
docs/superpowers/       # specs y planes de implementación de cada feature
```
