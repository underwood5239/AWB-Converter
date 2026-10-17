# AWB Audio Converter

> A small dark-themed Windows tool that extracts `.awb` (and 100+ other game audio container formats) into `.wav`.

Built for exporting BGM from the game *Girls' Frontline 2: Exilium*, whose `.awb` files often pack **two or more tracks into a single archive**. Dragging such a file onto `vgmstream-cli.exe` only exports the first track and silently drops the rest — this tool fixes that.

---

## Features

- **All tracks exported** — every subsong in an `.awb` becomes `name_01.wav`, `name_02.wav`, …
- **Two output formats** — `WAV 16-bit PCM` (smaller) or `WAV 32-bit float` (lossless from source)
- **Two loop modes** — `2 loops + 10s fade` (default) / `original length` (no loop, no fade). Only affects files that actually declare loop points.
- **Chinese UI** with per-track info: sample rate, channels, encoding, bitrate, loop points, and an estimated output duration for the selected loop mode
- **Live export log** — command, per-track parameters, produced files, elapsed time, result
- **Single portable exe** — vgmstream is embedded inside the binary; no installation required
- **High DPI aware** — crisp text at 125% / 150% scaling
- **Remembers settings** — output format and destination folder are restored on next launch
- **Drag & drop** supported

## Quick Start

1. Download `AWB转换器.exe` **and** `AWB转换器.exe.config` from [Releases](../../releases)
   > ⚠️ Both files must sit in the same folder. The `.config` enables high-DPI support.
2. Run `AWB转换器.exe`
3. Pick a file, choose the output format and destination folder, click **开始导出** (Start export)

On first launch a folder named `AWB转换器_工具_<hash>` is extracted next to the exe (13 files, ~10.7 MB). That is the bundled vgmstream runtime — keep it. Subsequent launches reuse it.

## Build from source

Requirements: Windows + **.NET Framework 4.x** (ships with Windows 10/11). No Visual Studio or .NET SDK needed.

```powershell
git clone https://github.com/<your-name>/AWB-Converter.git
cd AWB-Converter
powershell -NoProfile -ExecutionPolicy Bypass -File src\build.ps1
```

The script packs `tools\` into an embedded resource and compiles `dist\AWB转换器.exe` with `csc.exe`.

## Layout

```
src/Program.cs      main program (C#, WinForms, ~660 lines)
src/build.ps1       one-click build script
src/app.manifest    DPI awareness + Win10 compatibility
src/app.config      high-DPI settings (copied to exe.config)
tools/              vgmstream-cli + dependencies (LGPL-2.1)
dist/               build output (gitignored)
```

## Hidden CLI mode

```powershell
AWB转换器.exe --export "D:\test.awb" --format pcm16  --out "D:\out"
AWB转换器.exe --export "D:\test.awb" --format float32 --mode original --out "D:\out"
```

`--mode` accepts `loop2` (default) or `original`. Returns 0 on success.

## Credits

- **[vgmstream](https://github.com/vgmstream/vgmstream)** (r2117) does all the actual decoding. vgmstream's own code uses a permissive **ISC-style license** (see `tools/vgmstream-COPYING.txt`); the four FFmpeg-derived DLLs (`avcodec`/`avformat`/`avutil`/`swresample`) are **LGPL-2.1**. See `tools/vgmstream-LICENSE.txt`.
- The application source (dark theme, DPI scaling, embedded-resource packaging, build script) was written by an AI assistant from [DeepSeek Harness](https://github.com/) in a pair-programming session.

## License

- Source code in this repository: [MIT](../LICENSE)
- Files under `tools/`: vgmstream's own code is ISC-style permissive; the four FFmpeg-derived DLLs are LGPL-2.1. See `tools/vgmstream-LICENSE.txt`.

This tool performs format conversion only. It bundles no game assets and provides no download sources.
