# AWB Audio Converter

> A small Windows tool that batch-exports game audio archives (`.awb` and more) to `.wav` — dark UI, single file, ready to run.

Originally written for exporting audio from *Girls' Frontline 2: Exilium*. Its `.awb` files often **pack two or more tracks into a single archive**, and dragging such a file onto `vgmstream-cli.exe` exports only the first track and silently drops the rest — this tool exists to solve exactly that.

<p align="center">
  <img src="screenshot.png" alt="Screenshot" width="760">
</p>

---

## Features

- **Every track exported**: as many tracks as an `.awb` contains, named `name_01.wav`, `name_02.wav`, …
- **Two output formats**: `WAV 16bit (PCM, smaller)` / `WAV 32bit (float, lossless from source)`
- **Chinese UI**: per-track sample rate, channels, encoding, bitrate, loop points, original duration, export duration and track name
- **Logging**: decode command, per-track parameters, produced files, elapsed time and result, all in Chinese
- **Single-file program**: vgmstream is packed inside the exe; copy one file and run, no installation
- **Drag & drop**: you can drop a file straight into the window

## Quick start (end users)

1. Download `AWB转换器.exe` and `AWB转换器.exe.config` from [Releases](../../releases)
   > ⚠️ **Both files must be placed in the same folder.** The `.config` enables high-DPI support; without it the text becomes blurry.
2. Double-click `AWB转换器.exe`
3. Click **浏览…** (Browse) to pick an audio file (or drop it into the window)
4. Choose the output format → choose the destination folder → click **开始导出** (Start export)

On first launch a folder named `AWB转换器_工具_<fingerprint>` (about 10.7 MB, 13 files) is extracted next to the exe. It is the vgmstream runtime — **do not delete it**. Later launches reuse it.

Output goes into `<your folder>\<file name>\`, one folder per archive:

```
AWB导出\
└── BGM_Igla_Battle\
    ├── BGM_Igla_Battle_01.wav   (track 1)
    └── BGM_Igla_Battle_02.wav   (track 2)
```

## FAQ

**Q: Why no MP3 / FLAC?**
vgmstream can only output wav; converting to MP3/FLAC requires bundling an extra encoder (a static ffmpeg build is roughly 80 MB). To keep the download small, only wav is supported for now. You can always convert the exported wav files yourself.

**Q: Why is the exported file much longer than the original audio?**
Because the BGM declares loop points. By default it exports as "2 loops + 10 seconds fade" (vgmstream's default behaviour), which gives a complete listening experience. The info panel shows both the original duration and the export duration.

**Q: Can it convert in batch?**
Currently one file at a time; batch support is planned. A batch script built on `vgmstream-cli` already exists and will be merged in later.

**Q: The exe triggers a virus warning?**
Unsigned small tools are frequently flagged by antivirus software and browsers. The source is in `src/` — about 660 lines of C#. Read it and build it yourself.

---

## Building (developers)

### Requirements

- Windows 7 or later
- **.NET Framework 4.x** (bundled with Windows 10/11, includes `csc.exe`)
- No Visual Studio and no .NET SDK needed

### Steps

```powershell
git clone https://github.com/underwood5239/AWB-Converter.git
cd AWB-Converter
powershell -NoProfile -ExecutionPolicy Bypass -File src\build.ps1
```

The build script will:

1. Validate the 13 vgmstream files under `tools\`
2. Pack them into an embedded resource `src\awbtools.bin` (custom binary format, see below)
3. Compile `dist\AWB转换器.exe` with `csc.exe` (icon and manifest included)
4. Copy `app.config` → `dist\AWB转换器.exe.config`

> To upgrade vgmstream, drop the new `vgmstream-cli.exe` and matching DLLs into `tools\` and rebuild. The embedded resource carries a SHA-256 fingerprint, so upgraded tools automatically get a new cache directory and never collide with the old one.

### Project layout

```
AWB-Converter/
├── src/
│   ├── Program.cs        main program (C#, WinForms, ~660 lines)
│   ├── build.ps1         one-click build script
│   ├── app.manifest      DPI awareness + Win10 compatibility
│   ├── app.config        high-DPI support (copied to exe.config)
│   └── app.ico           program icon
├── tools/                vgmstream-cli and dependencies (shipped with the repo for reproducible builds)
│   ├── vgmstream-cli.exe
│   ├── *.dll
│   └── vgmstream-COPYING.txt
├── docs/
│   ├── README.en.md      this file
│   └── vgmstream-README.md
└── dist/                 build output (gitignored)
```

### Embedded resource format

To make a single exe carry every runtime library, `build.ps1` concatenates the files into `awbtools.bin` using the format below, then embeds it via `/resource:`. At startup the program parses it, validates it and extracts it to a cache directory:

| Offset | Type | Content |
|---|---|---|
| 0 | `uint32` | magic `0x31574241` (ASCII `ABW1`) |
| 4 | `int32` | file count N |
| 8 | … | repeated N times: `int32` name length, UTF-8 name, `int32` data length, data |

Extraction targets are tried in order: folder next to the exe → `%LOCALAPPDATA%\AWBConverter\tools_<fingerprint>` → `%TEMP%`. The fingerprint is the first 16 hex characters of the resource pack's SHA-256, so upgraded tools never reuse an old cache.

---

## Roadmap

- [ ] MP3 / FLAC output (requires an embedded encoder; download size grows noticeably)
- [ ] Batch export (select several files / recurse a folder)
- [ ] Selectable output sample rate and bit depth
- [ ] Customizable loop count and fade length
- [x] Dark UI
- [x] High-DPI support

## Credits

- **Decoding core**: [vgmstream](https://github.com/vgmstream/vgmstream) (r2117) does all format parsing and decoding; this repository merely wraps it in a Chinese UI plus a batch workflow.
  - vgmstream's own code is under a **permissive ISC-style license** (use, modify and redistribute freely, keep the copyright notice). Copyright belongs to the vgmstream project and its contributors; full text in `tools/vgmstream-COPYING.txt`.
  - The four DLLs `avcodec` / `avformat` / `avutil` / `swresample` come from **FFmpeg** and are licensed under **LGPL-2.1**; keep their license notice when redistributing.
  - Packaging and redistribution notes: `tools/vgmstream-LICENSE.txt`.
  - Thanks to the audio team of *Girls' Frontline 2: Exilium* (and to the odd packaging habit of stuffing two BGM tracks into one archive, which caused plenty of trouble — without it this tool would not exist).

## License

- **Source code in this repository** (`src/` and documentation): [MIT](../LICENSE)
- **vgmstream-related files under `tools/`**: vgmstream's own code is under a **permissive ISC-style license** (copyright the vgmstream project and contributors; full text in `tools/vgmstream-COPYING.txt`); the four `av*` / `swresample` DLLs come from FFmpeg under **LGPL-2.1**. These license files must be preserved when redistributing. See `tools/vgmstream-LICENSE.txt` for details.

This tool only performs format conversion. It bundles no game assets and provides no download sources. Please make sure your use case is lawful.

---

**中文**: [../README.md](../README.md)
