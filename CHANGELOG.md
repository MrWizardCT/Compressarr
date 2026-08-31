# Changelog

All notable changes to Compressarr are documented in this file. Pre-release/RC builds leading up
to v1.0.0 are omitted here - see [GitHub Releases](https://github.com/MrWizardCT/Compressarr/releases)
for that full history.

## [2.0.4] - 2026-08-31

### Changed
- Rebranded to the new "Squeeze" logo/icon mark throughout the app - report and toast logo, both
  favicons, the exe/tray/installer icon, and the web UI's nav-bar and About-page logos.
- The web UI's nav-bar logo is now a clickable link to compressarr.tv (opens in a new tab).
- Refreshed every README screenshot (Settings, Lanes, Monitor, History, sample report) against a
  real run on the current setup - real lane names/paths, the new branding, the Clear title
  metadata toggle, and a genuine completed run (4 files, 27.79GB -> 4.66GB, 83.22% saved).

### Fixed
- The HTML report header's logo and title weren't vertically aligned - Segoe UI's font metrics
  meant `line-height: 1` alone wasn't enough. Dialed in against the real report render.

## [2.0.3] - 2026-08-30

### Fixed
- The installer could hang trying to close a running Compressarr instance before updating it,
  leaving the app running but unresponsive to its own tray Exit command and requiring a manual
  End Task. Caused by Windows Restart Manager's graceful close handshake, which is unreliable
  against a tray-only app that's never had a window shown or interacted with. The installer now
  force-closes any running instance directly before touching files, sidestepping that handshake
  entirely - safe since Compressarr saves settings to disk immediately rather than holding
  anything unsaved in memory.

## [2.0.2] - 2026-08-30

### Fixed
- **Clear title metadata** now actually works. The TagLib-Sharp-based title-stripping feature was
  fully wired up but had no setting driving it, so it silently never ran. It's now a real toggle
  on the Settings page (on by default).

## [2.0.1] - 2026-08-30

### Added
- Enable Monitoring at Startup and Start with Windows settings
- Live countdown to the next pass, Run Now (skips the wait), and Abort (kills the in-flight
  HandBrakeCLI process)
- Live per-file progress (percent, fps, ETA) parsed from HandBrakeCLI's own output
- About page: installed version, credits, GitHub link, and Check for Updates against this repo
  and against HandBrakeCLI's own releases
- Help tooltips on every Settings/Lanes field

### Changed
- HTML report rewritten to match v1.1's layout (light theme, per-lane summaries, rolling history)
- README rewritten to mirror v1.1's structure, with real screenshots and a Custom presets section

### Fixed
- Log panel jumping back to the bottom while scrolled up reading it
- App icon not showing on the desktop shortcut, Start Menu, or the Control Panel uninstall entry
- Lanes page layout bug where Browse buttons could overflow past the card
- "Original v1.1" link now correctly points at the `1.x` branch

## [2.0.0] - 2026-08-30

A complete rewrite of Compressarr as a web-first app. Instead of a PowerShell script, this ships
as a signed Windows installer with a system-tray-only background process - all configuration,
monitoring, and history live in the browser.

### Added
- Web UI for Settings, Lanes, Monitor, History, and About - reachable from any device on the LAN
- Continuous monitoring with a live countdown, Run Now, and Abort
- HandBrakeCLI detection, install, and update checks from inside the app
- Live per-file progress (%, fps, ETA) during conversion
- Self-contained HTML run reports and a rolling history view
- Optional "Start with Windows"

v1.1 (PowerShell) is preserved on the [`1.x`](https://github.com/MrWizardCT/Compressarr/tree/1.x) branch.

## [1.1.0] - 2026-08-29

### Added
- Per-lane **Enable Lane** checkbox (HD/SD and UHD) to suspend a lane entirely, in both a regular
  run and monitor mode, without clearing its configured paths or presets
- New `contentLanes.<lane>.enabled` config field, defaulting to `true` so existing config files
  keep processing both lanes exactly as before

## [1.0.0] - 2026-08-28

The first stable release. A complete, end-to-end Windows batch video conversion workflow, from
watching a folder for new downloads through to a completion notification - a from-scratch,
modular rewrite of [VidMonHB](https://github.com/mrpaulwasserman/VidMonHB).

### Added
- Two independent content lanes (HD/SD and UHD), each with its own input folder, HandBrake
  presets, and destination paths
- Auto-detection of TV episodes vs. Movies per file from the filename
- Conversion through HandBrakeCLI, followed by filing into an organized `Show Name\Season NN\` or
  `Movie Title\` library structure, moving companion files (subtitles, `.nfo`, artwork) alongside
- Source folder cleanup once empty, guarded against shared folders that still hold other
  unconverted videos
- Optional Sonarr/Radarr integration: unmonitor the matching episode/movie after a successful
  conversion and trigger a library rescan
- Continuous Monitor mode, watching for new files, enabled by default
- Standalone HTML report and a desktop toast notification at the end of each run
