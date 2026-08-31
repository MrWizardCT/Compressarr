# Changelog

All notable changes to Compressarr are documented in this file. Pre-release/RC builds leading up
to v1.0.0 are omitted here - see [GitHub Releases](https://github.com/MrWizardCT/Compressarr/releases)
for that full history.

## [2.0.6] - 2026-08-31

### Fixed
- A lane whose only tracked resume jobs pointed at since-deleted source files (e.g. removed by
  hand between runs) would silently process nothing forever - it never fell back to scanning
  Input for genuinely new files, because a non-empty (but entirely dead) pending queue took
  priority over scanning. Dead pending entries are now dropped from the resume file as soon as
  they're detected, so the lane falls back to a fresh scan once nothing resumable is left.
- A file that reappeared in Input after already completing once (e.g. re-added for another test)
  got a second, duplicate resume entry instead of reusing its existing one - resume.json could
  accumulate multiple rows for the same path. Scanning now reuses an existing entry for a path
  it already knows about instead of always adding a new one.

## [2.0.5] - 2026-08-31

### Added
- In Queue section on the Monitor page - lists every file still waiting across enabled lanes, with
  its lane, size, and preset, styled to match the Current status cards.
- Reload button next to Install/Merge Presets on Settings - reloads presets.json without going
  through the merge-prompt flow, with a visible green confirmation message.

### Changed
- Stop Monitoring now reflects the click immediately on both the web page and the tray icon,
  regardless of which surface it was requested from - previously each surface only knew about its
  own click, so stopping from one left the other showing stale state until the in-flight file
  actually finished converting.
- Settings are now re-read after every file's HandBrakeCLI pass finishes, not just once at the
  start of a run or monitoring loop - a change made mid-run now takes effect on the very next
  file instead of requiring a restart.
- Lanes page's TV/Movie preset fields are now real dropdowns instead of a text field with
  autocomplete suggestions - the old control only showed suggestions matching whatever text was
  already typed, so a field already holding a valid preset name would only ever "suggest" itself.

### Fixed
- The preset list included HandBrake's own category headers ("General", "Web", "Devices",
  "Matroska", etc.) as if they were real, selectable presets, because the parser never checked
  HandBrake's own "Folder" flag - on a full HandBrake install this polluted the list with ~15-20
  bogus entries.
- Launching a second Compressarr instance no longer runs two processes against the same lanes -
  it now shows a small "Compressarr is already running" window (with the logo, styled like a
  native Windows dialog) and exits instead.
- A file that encoded successfully but couldn't be moved into the library (e.g. an
  offline/unreachable network drive as the TV or Movie base path) used to still report "OK" -
  now it's flagged as an error, since it isn't actually where it's supposed to be. The file was
  never lost either way - it stays exactly where HandBrake wrote it, in the lane's Output folder.
- Running out of disk space mid-encode was reported as a successful conversion - confirmed live
  against a genuinely full disk that HandBrakeCLI still writes its "Finished work at" completion
  banner even when the encode fails (exit code 4, "No space left on device"), and Compressarr
  wasn't checking the exit code. Left unfixed, this would have moved the truncated/corrupt file
  into place and, depending on Delete-after-convert, deleted or recycled the real source out from
  under it. Success now also requires the process to have exited 0.
- Monitoring now stops itself automatically when a disk-full failure is detected (encode or
  move), instead of retrying the same doomed encode again every poll interval - a clear log
  message explains why.
- The report's Status column now shows the specific reason a file failed ("Output drive full,
  monitoring stopped", "Base folder path unavailable, move skipped", or "No TV/Movie preset
  configured for this lane") instead of a generic "ERROR" for the failure conditions Compressarr
  can actually diagnose - other failures still show "ERROR", rather than guessing. A failed file
  with a detail log also gets a "Full Details" link straight to it.

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
