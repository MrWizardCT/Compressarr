# Compressarr

A Windows PowerShell + [HandBrakeCLI](https://handbrake.fr/downloads2.php) batch
video converter. A from-scratch rewrite of
[VidMonHB](https://github.com/mrpaulwasserman/VidMonHB), keeping the same
core idea - scan a folder, transcode matching video files, file the results
into Show/Movie folders - while adding an independent UHD content lane and
a modular codebase.

## What it does

Compressarr watches **two independent content lanes**, each with its own
input folder:

| Lane | Purpose |
|---|---|
| HD/SD | Standard/HD video sources |
| UHD | 4K/UHD video sources |

Within each lane, TV Shows vs Movies is **auto-detected per file**, the same
way Paul's VidMonHB does it: a filename carrying a season/episode marker
(`S01E01`, `1x01`, etc.) is treated as a TV episode, everything else as a
Movie. That detected type picks which preset applies (a lane's `tvPreset` or
`moviePreset`) and, if "move files" is enabled, which destination it's filed
into afterward - TV episodes go to `Show Name\Season NN\` under the lane's
`tvShowBasePath`, Movies get bucketed into year-range folders (e.g.
`03. Movies 2000-2019`) under `movieBasePath`.

Processing is **sequential** - one file at a time, no parallel HandBrakeCLI
jobs. If a run is interrupted, relaunching resumes from the unprocessed
files (tracked in `compressarr.resume.json`). Progress is logged as a neat
multi-line block per file (name, original size, detected type, and preset
in use), not a single cramped banner line.

At the end of a run, Compressarr writes a **standalone HTML report** to the
`Reports\` folder (no email/SMTP involved) covering per-lane results (with
each file's type and preset), disk savings, any errors, and daily/monthly/
yearly history rollups. The `report.openAfterRun` setting
(`Always`/`Error`/`Never`) controls whether it opens automatically.

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
    "hdsd": {
      "input": "",
      "output": "",
      "tvPreset": "VeryFastDDtoAAC",
      "moviePreset": "VeryFastDDtoAAC",
      "tvShowBasePath": "",
      "movieBasePath": ""
    },
    "uhd": {
      "input": "",
      "output": "",
      "tvPreset": "",
      "moviePreset": "",
      "tvShowBasePath": "",
      "movieBasePath": ""
    }
  },
  "processing": {
    "vidTypes": ["mkv", "avi", "mp4", "mpg", "ts", "m4v"],
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

Each lane's `output` is where HandBrake initially writes the converted
file; `tvShowBasePath`/`movieBasePath` are only used if `moveFiles` is on,
as the final destination once a file's type has been detected.

The output file extension is derived from whichever preset was selected
(its `FileFormat` value in `presets.json`, mapping `av_mp4`/`mp4` to `.mp4`
and `av_mkv`/`mkv` to `.mkv`) rather than being hardcoded.

## Project layout

```
Compressarr.ps1                  Entry point
Modules/
  Compressarr.Config.psm1        JSON config load/save, env-var expansion, presets.json helpers
  Compressarr.Conversion.psm1    HandBrakeCLI invocation, resume state, extension derivation
  Compressarr.FileRouting.psm1   TV/Movie auto-detection + move-to-folder logic
  Compressarr.Logging.psm1       Log file writer, cleanup, history CSV
  Compressarr.Reporting.psm1     Standalone HTML report generator
  Compressarr.UI.psm1            WinForms GUI
Assets/                          Logo + icon used by the GUI and the HTML report
Config/compressarr.settings.json Sample/default config
Logs/                            Per-run log files (gitignored)
Reports/                         Per-run HTML reports (gitignored)
```

## Differences from VidMonHB

- Two independent HD/SD and UHD lanes (Paul's version has one shared input
  folder); TV-vs-Movie detection within each lane uses Paul's original
  filename-based logic.
- JSON config instead of `.ps-properties`.
- Modular `.psm1` files instead of one 3,000-line script.
- No email/SMTP notifications - a standalone HTML report instead.
- No parallel processing - strictly sequential, one file at a time.
- Output extension is derived from the preset's `FileFormat`, not hardcoded to `.mp4`.
- Fixed a season/episode parsing bug: filenames with 3+ digit season or
  episode numbers (`S123E45`, `S01E123`) now parse correctly instead of
  being truncated to 1-2 digits.
- Per-file progress is a neat multi-line block (name, size, type, preset)
  instead of a single cramped banner line.
