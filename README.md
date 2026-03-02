# TaikoNova - A free-to-play, open source, drum based rhythm game!

> [!IMPORTANT]
> The game is in very early development, not everything will work, and not everything is final.

i like to keep these readme files simple so here's the stuff you actually came here for!


# Building TaikoNova

A complete guide to compiling and running TaikoNova from source.

---

## Prerequisites

### .NET 8.0 SDK

TaikoNova targets **.NET 8.0**. Install the SDK for your platform:

| Platform | Download |
|----------|----------|
| **Windows** | [.NET 8.0 SDK — Windows](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) |
| **macOS** | [.NET 8.0 SDK — macOS](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (x64 & Arm64) |
| **Linux** | [.NET 8.0 SDK — Linux](https://learn.microsoft.com/en-us/dotnet/core/install/linux) |

Verify the installation:

```bash
dotnet --version
# Should output 8.0.x or higher
```

### OpenGL 3.3+

TaikoNova requires a GPU (or driver) that supports **OpenGL 3.3 Core Profile**. Most hardware from 2010 onward supports this. Check your driver version if you run into issues:

- **Windows:** Update your GPU drivers from [NVIDIA](https://www.nvidia.com/Download/index.aspx), [AMD](https://www.amd.com/en/support), or [Intel](https://www.intel.com/content/www/us/en/download-center/home.html)
- **macOS:** OpenGL 3.3 is supported on macOS 10.9+ (comes with the OS, no action needed)
- **Linux:** Install Mesa or proprietary drivers (`sudo apt install mesa-utils` on Debian/Ubuntu)

### FFmpeg (Optional — Video Backgrounds)

If you want video backgrounds to render during gameplay, install [FFmpeg](https://ffmpeg.org/download.html) and make sure it's on your `PATH`:

| Platform | Install Command |
|----------|----------------|
| **Windows** | Download from [ffmpeg.org](https://ffmpeg.org/download.html#build-windows) or use `winget install FFmpeg` |
| **macOS** | `brew install ffmpeg` ([Homebrew](https://brew.sh) required) |
| **Linux** | `sudo apt install ffmpeg` (Debian/Ubuntu) or `sudo dnf install ffmpeg` (Fedora) |

FFmpeg is **not required** to build or play the game — only for video background support.

---

## Building

### Clone the Repository

```bash
git clone https://github.com/YourUser/TaikoNova.git
cd TaikoNova
```

### Restore & Build

```bash
# Restore NuGet packages and build in Debug mode
dotnet build

# Or build in Release mode for better performance
dotnet build -c Release
```

All NuGet dependencies are restored automatically:

| Package | Version | Purpose |
|---------|---------|---------|
| [OpenTK](https://www.nuget.org/packages/OpenTK/4.8.2) | 4.8.2 | Windowing, OpenGL bindings, keyboard/mouse input (via GLFW) |
| [ManagedBass](https://www.nuget.org/packages/ManagedBass/4.0.2) | 4.0.2 | Audio playback wrapper around the [BASS](https://www.un4seen.com/) library |
| [StbImageSharp](https://www.nuget.org/packages/StbImageSharp/2.30.15) | 2.30.15 | Image loading (PNG, JPG, BMP) for textures |

### Run

```bash
# Run directly (Debug)
dotnet run

# Run in Release mode
dotnet run -c Release
```

The window opens at **1600×900** by default and supports HiDPI/Retina scaling.

### Publish a Self-Contained Build

To create a standalone executable that doesn't require .NET to be installed on the target machine:

```bash
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained

# macOS x64 (Intel)
dotnet publish -c Release -r osx-x64 --self-contained

# macOS arm64 (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained

# Linux x64
dotnet publish -c Release -r linux-x64 --self-contained
```

Published output will be in `bin/Release/net8.0/<runtime-id>/publish/`.

---

## Native Libraries

### BASS Audio Library

The BASS native library is **automatically downloaded** on first launch. No manual installation is needed. The loader in `Engine/Audio/BassNativeLoader.cs` detects your platform and downloads the correct binary from [un4seen.com](https://www.un4seen.com/):

| Platform | Library File |
|----------|-------------|
| Windows | `bass.dll` |
| macOS | `libbass.dylib` (universal binary — x64 + arm64) |
| Linux | `libbass.so` (x86_64, aarch64, armhf) |

The library is placed next to the executable. If automatic download fails (e.g., no internet), you can manually download BASS 2.4 from [un4seen.com](https://www.un4seen.com/) and place the library file in your build output directory.

---

## Beatmaps / Songs

TaikoNova plays osu!taiko beatmaps (`.osu` file format v3–v14+). Songs are resolved from multiple sources in this order:

1. **Local `Songs/` folder** — Place `.osz` archives or extracted beatmap folders here. Archives are auto-extracted on startup.
2. **osu! stable installation** (Windows only) — Detected via the Windows Registry (`HKCU\Software\osu!`) and common install paths. Respects custom `BeatmapDirectory` from `osu!.*.cfg`.
3. **osu! lazer file store** — Cross-platform scan of the lazer data directory (e.g., `%APPDATA%/osu` on Windows, `~/.local/share/osu` on Linux).
4. **Drag-and-drop** — Drop `.osz` files directly onto the game window.

To get started quickly, drop some `.osz` files into the `Songs/` directory or install [osu!](https://osu.ppy.sh/home/download) and TaikoNova will automatically find your beatmaps.

---

## Configuration

User settings are saved as JSON at:

| Platform | Path |
|----------|------|
| Windows | `%APPDATA%\TaikoNova\settings.json` |
| macOS | `~/Library/Application Support/TaikoNova/settings.json` |
| Linux | `~/.config/TaikoNova/settings.json` |

Default settings:

| Category | Setting | Default |
|----------|---------|---------|
| Audio | Master Volume | 80% |
| Audio | Music Volume | 70% |
| Audio | SFX Volume | 60% |
| Gameplay | Scroll Speed | 1.0 |
| Gameplay | Background Dim | 65% |
| Gameplay | Global Offset | 0 ms |
| Display | Fullscreen | Off |
| Display | VSync | On |
| Input | Don Keys | D, F |
| Input | Kat Keys | J, K |

Settings can be changed in-game via the Settings overlay.

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| **`dotnet` command not found** | Install the [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) and ensure it's on your PATH. |
| **OpenGL errors on launch** | Update your GPU drivers. TaikoNova requires OpenGL 3.3 Core. |
| **No audio / BASS init failed** | Check your internet connection (BASS downloads automatically). Or manually download from [un4seen.com](https://www.un4seen.com/) and place the library next to the executable. |
| **Video backgrounds not working** | Install [FFmpeg](https://ffmpeg.org/download.html) and ensure `ffmpeg` is on your PATH. |
| **No songs found** | Place `.osz` files in the `Songs/` folder, or install osu! and TaikoNova will detect it. |
| **Build fails with unsafe code error** | The project already sets `AllowUnsafeBlocks=true`. Make sure you're building from the project root with `dotnet build`. |
| **macOS: "app is damaged" warning** | Run `xattr -cr /path/to/TaikoNova` to clear the quarantine flag. |

---

## Project Structure

```
TaikoNova/
├── Program.cs              # Entry point
├── TaikoNova.csproj        # Project file & dependencies
├── Engine/                 # Rendering, audio, input, video engine
│   ├── GameEngine.cs       # OpenTK game window & main loop
│   ├── BackgroundManager.cs
│   ├── Audio/              # BASS audio playback & native loader
│   ├── GL/                 # OpenGL shader, sprite batch, textures
│   ├── Input/              # Keyboard input handling
│   ├── Text/               # Procedural bitmap font renderer
│   └── Video/              # FFmpeg-based video decoder
├── Game/                   # Game logic & screens
│   ├── TaikoGame.cs        # Screen management & game state
│   ├── Beatmap/            # osu! beatmap parsing & file resolution
│   ├── Screens/            # Menu, song select, gameplay, results
│   ├── Settings/           # JSON settings manager
│   ├── Skin/               # Visual theme configuration
│   └── Taiko/              # Hit objects, scoring, playfield
└── Songs/                  # Beatmap storage (local .osz files)
```

---

## License

TaikoNova is licensed under the [Mozilla Public License 2.0](LICENSE).
