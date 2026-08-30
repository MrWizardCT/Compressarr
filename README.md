<img src="Assets/CompressarrLogo.png" width="96" alt="Compressarr logo" align="left">

# Compressarr

<br clear="left">

A complete, end-to-end batch video conversion workflow - from the moment a file lands in a
watched folder to the moment you're notified it's done, with nothing manual in between.
Compressarr watches your folders, transcodes new video through
[HandBrakeCLI](https://handbrake.fr/downloads2.php), automatically files the result into an
organized TV Show/Movie library, cleans up after itself, optionally tells Sonarr/Radarr the
item no longer needs monitoring, and finishes with a standalone report plus a desktop
notification. v2 is a complete rewrite of the original PowerShell tool as a web-first,
Radarr/Sonarr-style app - a system-tray-only background process with its entire UI (settings,
lanes, monitoring, history, reports) in your browser, reachable from any device on your LAN.
Both the original PowerShell version and v2 trace back to
[VidMonHB](https://github.com/mrpaulwasserman/VidMonHB), Paul Wasserman's original take on this
same idea.

## The complete workflow

```
Watch folder → Detect TV/Movie → Convert (HandBrake) → File into library
   → Clean up source → Unmonitor in Sonarr/Radarr → Report + toast
```

1. **Watch** - each lane's Input folder is scanned for new video files, either once on demand
   (Run Once / Run Now) or continuously while monitoring is on.
2. **Detect** - every file is auto-classified as a TV episode or a Movie from its filename, no
   separate lanes needed for each.
3. **Convert** - HandBrakeCLI transcodes it using that content type's configured preset, with
   live progress (percent, fps, ETA) streamed into the Monitor page as it runs.
4. **File it** - the converted file, plus any companion files (subtitles, `.nfo`, artwork), is
   routed into an organized `Show Name\Season NN\` or `Movie Title\` folder.
5. **Clean up** - the original is deleted/recycled/kept per your setting, and now-empty source
   folders (including a TV show's own folder once its last season is gone) are removed.
6. **Notify Sonarr/Radarr** *(optional)* - the matching episode/movie is unmonitored so it isn't
   re-grabbed.
7. **Report** - a standalone HTML report is generated, and a Windows toast notification confirms
   the run is complete - click it to open the report, whether or not Compressarr is still
   running.

## What it does

Compressarr watches an **open-ended set of content lanes** you define yourself - add, remove,
rename, and enable/disable them from the Lanes page, each with its own Input/Output folders and
TV/Movie presets.

Within each lane, TV Shows vs Movies is **auto-detected per file**: a filename carrying a
season/episode marker (`S01E01`, `1x01`, etc.) is treated as a TV episode, everything else as a
Movie. That detected type picks which preset applies (a lane's TV preset or Movie preset) and,
if "Move converted files" is enabled, which destination it's filed into afterward - TV episodes
go to `Show Name\Season NN\` under the lane's TV base path; Movies get their own per-title
subfolder under the Movie base path.

If a converted file's source folder holds nothing else that still needs converting, whatever
else is sitting in there - subtitles, `.nfo` files, artwork - comes along too. If other
not-yet-processed video files still share that folder, none of this happens - only the file that
was actually converted is touched.

Processing is **sequential** - one file at a time, no parallel HandBrakeCLI jobs. If a run is
interrupted, relaunching resumes from the unprocessed files.

### Monitoring

The Monitor page is the control surface for continuous operation: **Start Monitoring** begins
watching every enabled lane on a configurable interval, with a live countdown to the next pass.
**Run Now** skips the rest of that countdown and starts immediately. **Abort** kills whatever
HandBrakeCLI process is currently running and stops monitoring outright - as opposed to **Stop
Monitoring**, which finishes the current pass before stopping. The recent-log panel and CPU
usage update live while a pass runs.

### Reports and notifications

At the end of a run, Compressarr writes a **standalone HTML report** (no email/SMTP involved)
covering per-lane results (each file's type, preset, and Sonarr/Radarr outcome), disk savings,
any errors, and rolling Today/This Month/This Year history. The Open report after run setting
(Always/On Error/Never) controls whether it opens automatically - independent of that setting, a
Windows toast notification also confirms completion and opens the report when clicked. Each
report is labeled with a running run number (`Run #237: ...`) - a persistent, cumulative count
of runs that actually processed at least one file. The History page also lists every report
still within your configured retention window, with columns for files, before/after size, and
percent saved.

---

## Installation

1. Download `Compressarr-Setup-x.x.x.exe` from the
   [Releases](https://github.com/MrWizardCT/Compressarr/releases) page.
2. Run it and follow the installer. It installs to Program Files, adds a Start Menu shortcut
   (and an optional desktop icon), and registers a normal Windows uninstaller.

   > **A note on Windows SmartScreen / Smart App Control**: Windows may flag the installer or
   > the app as coming from an "Unknown Publisher," or Smart App Control may block it outright
   > the first time you run it. This is expected, not a sign anything is wrong - it's the same
   > situation every independently-published Windows tool starts in, including ones you may
   > already trust and run daily (Sonarr, Radarr, and the rest of the *arr ecosystem included).
   > Windows' reputation system checks how many machines have already run the exact file you
   > downloaded, and a freshly published release starts at zero no matter how it's signed. If
   > SmartScreen shows "Windows protected your PC," click **More info**, then **Run anyway**. If
   > Smart App Control blocks it outright, either wait for the release's reputation to build (it
   > typically clears within some weeks of more people downloading it), or turn off Smart App
   > Control in Windows Security settings, same as most people running non-Store *arr-style
   > software already have.
3. Launch Compressarr from the Start Menu - it runs as a tray icon only, with no window of its
   own. Right-click the tray icon for **Open Web UI**, or just browse to
   `http://localhost:1212` (or whatever port you've configured).
4. On the Settings page, use **Check/Install** next to HandBrakeCLI path to detect an existing
   install or download one automatically, and **Install/Merge Presets** to add Compressarr's own
   HandBrake presets to your `presets.json` (merging into an existing file if you already have
   one, installing fresh if you don't).

Compressarr is self-contained: it bundles its own .NET runtime, so nothing else needs to be
installed first.

---

## Configuring Compressarr

Everything is configured from the browser - there's no desktop settings window. Every field on
these pages has a small **?** next to it with a tooltip explaining what it does.

### Settings page

| Field | What it's for |
|---|---|
| HandBrakeCLI path | Path to `HandBrakeCLI.exe` - Check/Install finds or downloads it |
| presets.json path | Path to HandBrake's presets.json - Install/Merge Presets adds Compressarr's own |
| Extra CLI options | Additional flags passed straight through to every HandBrakeCLI conversion |
| Video extensions | Comma-separated file extensions to scan for (default: `mkv, avi, mp4, mpg, ts, m4v`) |
| Minimum size (bytes) | Skip files smaller than this - useful for ignoring samples/junk |
| Max files per run | Caps how many files are picked up in one pass (0 = no limit) |
| Write output to same folder as input | Convert in place instead of using each lane's Output folder |
| Move converted files into show/movie folders | Turns on the TV/Movie filing step described above |
| Original file after convert | Maintain, Delete, or Recycle the source file once conversion succeeds |
| Log folder / Report folder | Where run logs, the history CSV, and HTML reports are written |
| Log/report retention (days) | Logs and reports older than this are cleaned up automatically |
| Open report after run | Always, On Error, or Never |
| Enable Monitoring at Startup | Start watching lanes automatically when Compressarr launches |
| Poll interval (seconds) | How often the monitor loop checks lanes while monitoring is on |
| Start with Windows (on login) | Registers Compressarr to launch automatically at login |
| Post-execution command/arguments | Optional command to run after each pass completes |
| Sonarr/Radarr Enabled, URL, API Key | See below |
| Port | Port the web UI listens on - changing this needs a restart |

**Sonarr/Radarr integration**: after a file finishes converting successfully, Compressarr can
tell whichever app tracks it to stop monitoring that specific episode or movie, so it isn't
re-grabbed later. Matching is done entirely through the app's own `/api/v3/parse` endpoint -
Compressarr hands it the original filename, and the app's own parser reports back which
series/episode or movie it matches, if any. A miss is always treated as "leave it alone," never
as a guess.

### Lanes page

Add, remove, rename, and enable/disable lanes freely - there's no fixed limit. Each lane has:

| Field | What it's for |
|---|---|
| Enabled | Turns this lane's processing on/off without clearing its configured paths |
| Input | Where Compressarr looks for source video files for this lane |
| Output | Where HandBrake writes the converted file initially |
| TV preset / Movie preset | HandBrake preset used for each detected content type - autocompletes from your presets.json |
| TV base path / Movie base path | Final destination once a file's type has been detected, if Move converted files is on |

Every path field has a **Browse...** button that opens a server-side folder picker, since a
browser's native file picker can't see the server's filesystem. **Save All Lanes** saves every
lane on the page in one click.

### Saving and running

- **Save Settings** / **Save** (per lane) write changes back to the config without running
  anything.
- **Run Once** (Settings page) runs a single pass immediately with whatever's currently saved.
- **Start Monitoring** / **Stop Monitoring** / **Run Now** / **Abort** (Monitor page) control
  continuous operation - see [Monitoring](#monitoring) above.

---

## Running it

Compressarr has no command-line interface - it's a tray-only background app. Right-click the
tray icon for:

- **Open Web UI** - opens the browser to Compressarr's own address
- **Start Monitoring** / **Stop Monitoring** - mirrors the Monitor page's buttons; either surface
  reflects the other's state
- **Exit** - shuts down the web server and closes Compressarr

If **Enable Monitoring at Startup** is on (Settings page), monitoring begins automatically as
soon as Compressarr launches - useful together with **Start with Windows** for a fully
hands-off setup.

## Configuration file reference

Config is JSON, stored per-user at `%AppData%\Compressarr\compressarr.settings.json` rather than
next to the install folder, so upgrades never overwrite your settings. Paths may contain
`%ENVVAR%` tokens (e.g. `%ProgramFiles%`, plus Compressarr's own `%CompressarrAppData%`),
expanded at the point of use. This shows the shipped defaults - lanes start empty; add your own
from the Lanes page.

```json
{
  "HandBrake": {
    "CliPath": "%ProgramFiles%\\HandBrake\\HandBrakeCLI.exe",
    "PresetsPath": "%appdata%\\HandBrake\\presets.json",
    "Options": ""
  },
  "Lanes": [],
  "Processing": {
    "VidTypes": ["mkv", "avi", "mp4", "mpg", "ts", "m4v"],
    "OutSameAsIn": false,
    "DeleteAfterConvert": "Recycle",
    "MoveFiles": true,
    "Limit": 0,
    "MinSizeBytes": 0
  },
  "Logging": { "LogFilePath": "%CompressarrAppData%\\Logs", "RetentionDays": 30 },
  "PostExec": { "Cmd": "", "Args": "" },
  "Report": { "ReportPath": "%CompressarrAppData%\\Reports", "OpenAfterRun": "OnError" },
  "Repeat": { "Count": 0, "Monitor": false, "PollIntervalSeconds": 60 },
  "Startup": { "CountdownSeconds": 10, "RunAtLogin": false },
  "Arrs": {
    "Sonarr": { "Enabled": false, "Url": "", "ApiKey": "" },
    "Radarr": { "Enabled": false, "Url": "", "ApiKey": "" }
  },
  "Web": { "Port": 1212 }
}
```

The output file extension is derived from whichever preset was selected, rather than being
hardcoded.

## Project layout

```
src/
  Compressarr.Core/       Conversion engine, config, reporting, HandBrake/Sonarr/Radarr clients -
                           platform-agnostic, no UI dependencies
  Compressarr.Web/         Minimal-API endpoints + wwwroot (the entire browser UI: vanilla JS/HTML/CSS)
  Compressarr.Desktop/     Tray-only host - Avalonia TrayIcon, Windows toast notifications, composition root
installer/
  Compressarr.iss           Inno Setup script that packages the self-contained publish output
tests/
  Compressarr.Core.Tests/   xUnit tests for Core
```

## Differences from v1.1

- Entire UI moved to the browser (Radarr/Sonarr-style) - no desktop settings window, reachable
  from any device on your LAN.
- Open-ended, user-managed lanes (add/remove/rename/enable) instead of two fixed HD/SD and UHD
  lanes.
- Continuous monitoring with a live countdown, Run Now, and Abort (kills the in-flight
  HandBrakeCLI process) - v1.1 only ever ran once per launch or in a simple repeat loop.
- Live per-file progress (percent, fps, ETA) parsed from HandBrakeCLI's own output and streamed
  into the Monitor page while a file converts.
- HandBrakeCLI and presets.json can be detected, installed, and checked for updates from inside
  the app - no manual download/unblock dance.
- Ships as a signed Windows installer (self-contained, bundles its own .NET runtime) instead of
  a PowerShell script requiring `Unblock-File` and an execution-policy change.
- Settings/lanes stored per-user under `%AppData%\Compressarr`, so upgrades never overwrite
  configuration.
- An About page with the installed version, one-click update checks (against this repo and
  against HandBrake's own releases), and credits.
- v1.1 (PowerShell) itself remains available on the [`1.x`](https://github.com/MrWizardCT/Compressarr/tree/1.x) branch.
