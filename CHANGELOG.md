# Changelog

All notable changes to Compressarr are documented in this file. Pre-release/RC builds leading up
to v1.0.0 are omitted here - see [GitHub Releases](https://github.com/MrWizardCT/Compressarr/releases)
for that full history.

## [2.1.1] - 2026-09-05

### Added
- Per-queue-item controls on the Monitor page: drag a file to reorder it within its lane, skip it
  (stays visible, dimmed, excluded from processing until un-skipped), remove it from the queue
  entirely, or override its preset for that one file only from a dropdown of installed presets - a
  "Use Lane Preset" option resets an override back to the lane default.
- Error queue entries are now shown on Monitor (red badge) with a Remove action, instead of being
  invisible until the next report.
- Failed file *moves* (encode succeeded, but couldn't be filed into the library - an offline
  network drive, etc.) are now retried automatically on the lane's next pass, without re-encoding.
- Configurable behavior when a destination file already exists - Overwrite (previous behavior,
  still the default), Skip, or Rename - instead of always silently overwriting.
- Automated backups (Settings > Backups): scheduled zip backups of your full setup (settings,
  lanes, resume state, run counter, history) to a local or network folder, plus a "Backup Now"
  button and a list of existing backups you can restore from with one click - including on a
  brand-new install, before you've configured anything else.
- Export/import your full configuration as a single file from Settings, for backing up or moving
  to a new machine.
- Test Connection button next to the Sonarr/Radarr integration settings, so a bad URL or API key
  shows up immediately instead of only at unmonitor-time during a real run.
- A warning before leaving Settings or Lanes with unsaved changes, plus a Clear Changes button to
  discard edits in place.
- Pause/Resume for the file currently being converted.
- KB/MB/GB unit dropdown next to Settings' Minimum size field (previously bytes only).
- Per-file conversion duration on the HTML report, and a running total time on the History page.
- The Monitor page's status now shows which preset the current file is using.
- A completely redesigned web UI: a left sidebar for navigation (in place of the old top tab bar),
  a persistent toolbar showing monitoring status and CPU usage on every page, and a consistent
  card-based layout across Settings, Lanes, History, and About.
- A Donate page with QR codes and one-click copy for several cryptocurrency addresses.
- A small indicator appears in the toolbar when a newer version of Compressarr is available.
- Notification channels (Notifications page): get a message when a run completes via Discord,
  Slack, Telegram, Pushover, ntfy, Gotify, Notifiarr, IFTTT, or a custom webhook (which also
  covers Zapier, Make, n8n, Node-RED, and Home Assistant) - configure as many channels as you
  want, each with its own trigger (always / only on error or warning / never) and a Test button.
  A separate toggle controls the existing Windows toast notification, now off by default. Every
  field has a help bubble explaining what it needs and where to find it.
- True cross-lane queue priority: dragging a file in the Monitor page's queue can now move it
  ahead of files in a *different* Lane, not just within its own Lane - the order shown is exactly
  the order files will be processed in, regardless of which Lane each one belongs to.
- A [detailed GitHub Wiki](https://github.com/MrWizardCT/Compressarr/wiki) with a full walkthrough
  of every page, written for people new to Compressarr.

### Changed
- Start/Stop Monitoring is now a single toggle button instead of two separate ones.
- Stop Monitoring now stops after the file currently converting finishes, rather than continuing
  to process every other file still queued behind it.
- Subtitle and other companion files now move to their destination immediately once their own
  video finishes converting, instead of waiting for every file in a shared folder to finish first.
- The queue's preset picker is a plain dropdown showing the preset actually in effect, instead of
  a custom popover that could show a stale or misleading placeholder.

### Fixed
- A queue edit (reorder, skip, or preset override) made while a file was actively converting could
  be silently discarded once that file finished, and the wrong file could be processed next.
- Removing a file from the queue didn't stick - it could reappear within seconds.
- The queue's preset dropdown or its right-click-style menu could be yanked shut mid-interaction
  by the page's own periodic refresh.
- A queue edit could reorder the whole queue as a side effect, or make untouched files incorrectly
  show as "Resumed" instead of "New."
- The In Queue list could freeze while its own lane's pass was actively running.
- A stale Error entry whose source file was already gone (deleted by hand, or handled elsewhere)
  never cleared itself the way a stale queued entry already did, and could permanently inflate the
  "resuming previous run" count on every single pass.
- Reordering, skipping, removing, or overriding the preset for a file that lives in a subfolder
  under a Lane's Input folder (rather than directly in it - e.g. one folder per movie) silently
  did nothing, with no error shown.
- Resolved a false-positive `Trojan:Win32/Wacatac.B!ml` flag from one vendor on v2.1.0's
  installer. Root-caused through systematic isolation testing against VirusTotal to the
  installer's LZMA2 compression of the embedded application payload, not to any notification
  code, service, or architecture - confirmed by an A/B test where an identical build scanned
  clean the moment compression was disabled. The installer now ships uncompressed
  (`Compression=none`) as a result; every notification channel, including Discord and Slack,
  remains fully intact.
- Along the way, the notification providers (Discord, Slack, Telegram, Pushover, ntfy, Gotify,
  Notifiarr, IFTTT) were also rewritten onto a narrower, intentionally boring HTTP client
  interface (fixed JSON/form/text POST shapes, never a fully generic method+headers+content-type
  sender) instead of sharing one universal webhook-sending routine - a deliberate architecture
  improvement independent of the VirusTotal finding above. Generic Webhook keeps its own
  fully-flexible sender, since it's the one channel that genuinely needs arbitrary
  method/header/URL configurability.
- Resolved a second, unrelated false positive (`Program:Win32/Contebrew.A!ml`) that Windows
  Defender's live cloud/SmartScreen reputation classifier flagged on a real download of the
  self-contained installer, despite VirusTotal - including a same-day re-scan with Microsoft's own
  engine - showing it completely clean. That classifier weighs signals VirusTotal's static engine
  never sees: publisher trust (this project's cert is self-signed, so it starts with none) and how
  new/large/rarely-downloaded a file is. Rather than chase a live reputation heuristic, Compressarr
  now ships a single, much smaller framework-dependent installer instead of the self-contained
  build - removing the exposure rather than working around it. See Installation below for the
  runtime it now requires.

### Security
- Donation addresses on the Donate page are no longer stored as single literal strings in the
  compiled binary - unrelated to the Wacatac finding above, but a reasonable hardening measure
  found during the same investigation.

## [2.1.0] - 2026-09-04

### Added
- Per-queue-item controls on the Monitor page: drag a file to reorder it within its lane, skip it
  (stays visible, dimmed, excluded from processing until un-skipped), remove it from the queue
  entirely, or override its preset for that one file only from a dropdown of installed presets - a
  "Use Lane Preset" option resets an override back to the lane default.
- Error queue entries are now shown on Monitor (red badge) with a Remove action, instead of being
  invisible until the next report.
- Failed file *moves* (encode succeeded, but couldn't be filed into the library - an offline
  network drive, etc.) are now retried automatically on the lane's next pass, without re-encoding.
- Configurable behavior when a destination file already exists - Overwrite (previous behavior,
  still the default), Skip, or Rename - instead of always silently overwriting.
- Automated backups (Settings > Backups): scheduled zip backups of your full setup (settings,
  lanes, resume state, run counter, history) to a local or network folder, plus a "Backup Now"
  button and a list of existing backups you can restore from with one click - including on a
  brand-new install, before you've configured anything else.
- Export/import your full configuration as a single file from Settings, for backing up or moving
  to a new machine.
- Test Connection button next to the Sonarr/Radarr integration settings, so a bad URL or API key
  shows up immediately instead of only at unmonitor-time during a real run.
- A warning before leaving Settings or Lanes with unsaved changes, plus a Clear Changes button to
  discard edits in place.
- Pause/Resume for the file currently being converted.
- KB/MB/GB unit dropdown next to Settings' Minimum size field (previously bytes only).
- Per-file conversion duration on the HTML report, and a running total time on the History page.
- The Monitor page's status now shows which preset the current file is using.
- A completely redesigned web UI: a left sidebar for navigation (in place of the old top tab bar),
  a persistent toolbar showing monitoring status and CPU usage on every page, and a consistent
  card-based layout across Settings, Lanes, History, and About.
- A Donate page with QR codes and one-click copy for several cryptocurrency addresses.
- A small indicator appears in the toolbar when a newer version of Compressarr is available.
- Notification channels (Notifications page): get a message when a run completes via Discord,
  Slack, Telegram, Pushover, ntfy, Gotify, Notifiarr, IFTTT, or a custom webhook (which also
  covers Zapier, Make, n8n, Node-RED, and Home Assistant) - configure as many channels as you
  want, each with its own trigger (always / only on error or warning / never) and a Test button.
  A separate toggle controls the existing Windows toast notification, now off by default. Every
  field has a help bubble explaining what it needs and where to find it.
- True cross-lane queue priority: dragging a file in the Monitor page's queue can now move it
  ahead of files in a *different* Lane, not just within its own Lane - the order shown is exactly
  the order files will be processed in, regardless of which Lane each one belongs to.
- A [detailed GitHub Wiki](https://github.com/MrWizardCT/Compressarr/wiki) with a full walkthrough
  of every page, written for people new to Compressarr.

### Changed
- Start/Stop Monitoring is now a single toggle button instead of two separate ones.
- Stop Monitoring now stops after the file currently converting finishes, rather than continuing
  to process every other file still queued behind it.
- Subtitle and other companion files now move to their destination immediately once their own
  video finishes converting, instead of waiting for every file in a shared folder to finish first.
- The queue's preset picker is a plain dropdown showing the preset actually in effect, instead of
  a custom popover that could show a stale or misleading placeholder.

### Fixed
- A queue edit (reorder, skip, or preset override) made while a file was actively converting could
  be silently discarded once that file finished, and the wrong file could be processed next.
- Removing a file from the queue didn't stick - it could reappear within seconds.
- The queue's preset dropdown or its right-click-style menu could be yanked shut mid-interaction
  by the page's own periodic refresh.
- A queue edit could reorder the whole queue as a side effect, or make untouched files incorrectly
  show as "Resumed" instead of "New."
- The In Queue list could freeze while its own lane's pass was actively running.
- A stale Error entry whose source file was already gone (deleted by hand, or handled elsewhere)
  never cleared itself the way a stale queued entry already did, and could permanently inflate the
  "resuming previous run" count on every single pass.
- Reordering, skipping, removing, or overriding the preset for a file that lives in a subfolder
  under a Lane's Input folder (rather than directly in it - e.g. one folder per movie) silently
  did nothing, with no error shown.

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
