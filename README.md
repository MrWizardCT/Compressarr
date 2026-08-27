# Compressarr

A Windows PowerShell + [HandBrakeCLI](https://handbrake.fr/downloads2.php) batch
video converter. A from-scratch rewrite of
[VidMonHB](https://github.com/mrpaulwasserman/VidMonHB), keeping the same
core idea - scan a folder, transcode matching video files, file the results
into Show/Movie folders - while adding independent HD/UHD content lanes and
a modular codebase.

## What it does

Compressarr watches up to **four independent content lanes**, each with its
own input folder, output base path, and HandBrake preset:

| Lane | Purpose |
|---|---|
| HD Movies | Standard/HD movie sources |
| HD TV Shows | Standard/HD TV episode sources |
| UHD Movies | 4K/UHD movie sources |
| UHD TV Shows | 4K/UHD TV episode sources |

Because each lane has its own input folder, there's no auto-detection
needed to tell HD from UHD - you decide by which folder you drop a file
into. Within the two TV lanes, filenames are parsed for `S##E##`-style
season/episode markers to build a `Show Name\Season NN\` destination
folder when "move files" is enabled. Within the two Movie lanes, files are
bucketed into year-range folders (e.g. `03. Movies 2000-2019`) the same way.

Processing is **sequential** - one file at a time, no parallel HandBrakeCLI
jobs. If a run is interrupted, relaunching resumes from the unprocessed
files (tracked in `compressarr.resume.json`).

At the end of a run, Compressarr writes a **standalone HTML report** to the
`Reports\` folder (no email/SMTP involved) covering per-lane results, disk
savings, any errors, and daily/monthly/yearly history rollups. The
`report.openAfterRun` setting (`Always`/`Error`/`Never`) controls whether it
opens automatically.

## Setup

1. Install [HandBrakeCLI](https://handbrake.fr/downloads2.php).
2. Have a HandBrake `presets.json` available (exported from the HandBrake
   GUI, or the default one under `%appdata%\HandBrake\presets.json`).
3. Copy `Config\compressarr.settings.json` if you want a starting point, or
   just launch `Compressarr.ps1` once - it will write out a default config
   next to itself if none exists yet at the path you point it at.
4. Optional: drop `taglib-sharp.dll` next to `Compressarr.ps1` to enable
   clearing the Title tag on converted files (skipped silently if absent).
5. First-time PowerShell execution policy, same as the original project:
   ```powershell
   Set-ExecutionPolicy Unrestricted -Scope CurrentUser -Force
   ```

## Running it

```powershell
# Interactive - opens the configuration GUI, then runs on Execute
.\Compressarr.ps1

# Headless - runs immediately using the config file on disk
.\Compressarr.ps1 -NoGui

# Use a specific config file
.\Compressarr.ps1 -ConfigPath "D:\MyConfigs\uhd-only.json"

# Force exactly one run, ignoring repeat count / monitor mode
.\Compressarr.ps1 -Once
```

## Configuration

Config is JSON (`Config\compressarr.settings.json` by default). Paths may
contain `%ENVVAR%` tokens (e.g. `%ProgramFiles%`), expanded at the point of
use so the same file works across machines.

```json
{
  "handbrake": {
    "cliPath": "%ProgramFiles%\\HandBrake\\HandBrakeCLI.exe",
    "presetsPath": "%appdata%\\HandBrake\\presets.json",
    "options": ""
  },
  "contentLanes": {
    "hdMovies":  { "input": "", "outputBase": "", "preset": "VeryFastDDtoAAC" },
    "hdTV":      { "input": "", "outputBase": "", "preset": "VeryFastDDtoAAC" },
    "uhdMovies": { "input": "", "outputBase": "", "preset": "" },
    "uhdTV":     { "input": "", "outputBase": "", "preset": "" }
  },
  "processing": {
    "vidTypes": ["mkv", "avi"],
    "outSameAsIn": false,
    "deleteAfterConvert": "Maintain",
    "moveFiles": false,
    "limit": 999,
    "minSize": "0gb"
  },
  "logging": { "logFilePath": ".\\Logs", "retentionDays": 30 },
  "postExec": { "cmd": "", "args": "" },
  "report": { "reportPath": ".\\Reports", "openAfterRun": "Always" },
  "repeat": { "count": 0, "monitor": false }
}
```

The output file extension is derived from each lane's selected preset
(its `FileFormat` value in `presets.json`, mapping `av_mp4`/`mp4` to
`.mp4` and `av_mkv`/`mkv` to `.mkv`) rather than being hardcoded.

## Project layout

```
Compressarr.ps1                  Entry point
Modules/
  Compressarr.Config.psm1        JSON config load/save, env-var expansion, presets.json helpers
  Compressarr.Conversion.psm1    HandBrakeCLI invocation, resume state, extension derivation
  Compressarr.FileRouting.psm1   TV/Movie move-to-folder logic
  Compressarr.Logging.psm1       Log file writer, cleanup, history CSV
  Compressarr.Reporting.psm1     Standalone HTML report generator
  Compressarr.UI.psm1            WinForms GUI
Config/compressarr.settings.json Sample/default config
Logs/                            Per-run log files (gitignored)
Reports/                         Per-run HTML reports (gitignored)
```

## Differences from VidMonHB

- Four independent HD/UHD x Movie/TV lanes instead of one shared input folder.
- JSON config instead of `.ps-properties`.
- Modular `.psm1` files instead of one 3,000-line script.
- No email/SMTP notifications - a standalone HTML report instead.
- No parallel processing - strictly sequential, one file at a time.
- Output extension is derived from the preset's `FileFormat`, not hardcoded to `.mp4`.
- Fixed a season/episode parsing bug: filenames with 3+ digit season or
  episode numbers (`S123E45`, `S01E123`) now parse correctly instead of
  being truncated to 1-2 digits.
