# Compressarr

A background video-compression tool for a media library: watch one or more folders ("lanes"),
re-encode new files with HandBrakeCLI on a schedule, route the results into your TV/Movie
library, and optionally tell Sonarr/Radarr to stop monitoring the originals. v2 is a full
rewrite of the original PowerShell tool as a Radarr/Sonarr-style app — a system-tray-only
background process with its entire UI (settings, lanes, monitoring, history, reports) in your
browser, reachable from any device on your LAN.

## Features

- **Lanes** — one or more independent input/output/preset configurations, each with its own TV
  and Movie HandBrake presets and destination folders.
- **Monitoring** — start it once and it watches your lanes on a configurable interval, or trigger
  a pass immediately with Run Now.
- **Web UI** — Settings, Lanes, Monitor, History, and About pages, all served locally; no client
  install beyond the tray app itself.
- **HandBrakeCLI management** — detect, install, and check for updates to HandBrakeCLI from
  inside the app.
- **Sonarr/Radarr integration** — automatically unmonitor a title in Sonarr/Radarr once
  Compressarr has converted it, so it isn't re-downloaded.
- **HTML run reports** — a self-contained report per run, plus a live rolling history view.
- **Windows toast notifications** and an optional "Start with Windows" setting.

## Installation

1. Download `Compressarr-Setup-x.x.x.exe` from the [Releases](https://github.com/MrWizardCT/Compressarr/releases) page.
2. Run it and follow the installer. It installs to Program Files, adds a Start Menu shortcut
   (and an optional desktop icon), and registers a normal Windows uninstaller.
3. Launch Compressarr from the Start Menu — it runs as a tray icon only, with no window of its
   own. Right-click the tray icon for **Open Web UI**, or just browse to
   `http://localhost:1212` (or whatever port you've configured).
4. Everything else — HandBrakeCLI setup, lanes, monitoring — is configured from the browser.

Compressarr is self-contained: it bundles its own .NET runtime, so nothing else needs to be
installed first.

### ⚠️ A note on Windows SmartScreen / Smart App Control

Windows may flag the installer or the app itself as coming from an "Unknown Publisher," or Smart
App Control may block it outright the first time you run it. **This is expected and not a sign
anything is wrong** — it's the same situation every independently-published Windows tool starts
in, including ones you may already trust and run daily (Sonarr, Radarr, and most other
self-hosted *arr-style apps included).

Here's why: Windows' reputation system (SmartScreen/Smart App Control) doesn't just check for a
signature — it checks how many other machines have already run the exact same file without it
turning out to be malicious. A freshly published release has zero of that history no matter how
it's signed, so it gets flagged until enough people have downloaded and run it. There's no way
around this for an independently-published app short of an Extended Validation code-signing
certificate, which requires a registered business entity and isn't practical for a project like
this one - so instead, this reputation simply builds up naturally as more people use the app,
the same way it did for every other tool in this space.

If you see a warning:

- **"Windows protected your PC" (SmartScreen)** — click **More info**, then **Run anyway**.
- **Smart App Control blocks it outright** — you have two options:
  - Wait; as more people download and run a given release, Windows typically stops flagging it
    within some weeks of that release being out.
  - Turn off Smart App Control (Windows Settings → Privacy & security → Windows Security → App &
    browser control → Smart App Control settings), which most people who regularly run
    non-Store software (including the rest of the *arr ecosystem) already have off. Note that
    Windows only allows turning this off, not back on, without a clean reinstall of Windows once
    it has decided to disable itself in "Evaluation" mode — turn it off deliberately here only if
    you're comfortable with that trade-off.

## Credits

- Created by Mark Wasserman
- v2.0.0 is a web-first rewrite of the original PowerShell tool, developed with
  [Claude Code](https://claude.com/claude-code)
- Original v1.1: [MrWizardCT/Compressarr](https://github.com/MrWizardCT/Compressarr)
- Based on work from Paul Wasserman, who developed the original
  [VidMonHB](https://github.com/mrpaulwasserman/VidMonHB)
- Built with .NET, ASP.NET Core, Avalonia UI, and [HandBrakeCLI](https://handbrake.fr)
