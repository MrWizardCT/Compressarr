# Changelog

All notable changes to Compressarr are documented in this file. Pre-release/RC builds leading up
to v1.0.0 are omitted here - see [GitHub Releases](https://github.com/MrWizardCT/Compressarr/releases)
for that full history.

> [!NOTE]
> Compressarr contains no AI or machine learning at runtime. Movie/TV detection, file matching,
> and renaming all run on plain, inspectable regex pattern matching - the same deterministic
> logic every time, nothing generative involved.

## [2.1.4] - 2026-09-09

> [!TIP]
> **What's new in 2.1.4:** configuration validation with red-outlined fields and a consolidated
> toolbar status message across Settings, Lanes, and Notifications, numbered error-code badges on
> the HTML run report, a "Launch Monitor at Startup" option, and an important data-safety fix so a
> failed file move can no longer lose the source file. Destination-collision handling (Rename/Skip)
> now actually applies instead of silently behaving like Overwrite, and companion files follow the
> same setting as their video. Everything else below carries forward from 2.1.3 for context - new
> changes are in **bold**.

### Added
- **A "Launch Monitor at Startup" option (Settings > Monitoring): opens the Monitor page in your
  default browser as soon as Compressarr launches, independent of whether monitoring itself
  auto-starts.**
- **Configuration validation: required fields on Settings and Lanes (HandBrake/FileBot paths,
  video extensions, a lane's preset or Output folder) are checked on load and on save, with
  invalid fields outlined in red and a toolbar error message pointing you to them. Notifications
  validates its own required fields (e.g. a channel's webhook URL) the same way before you save.
  Saving still succeeds either way - this is a warning, not a gate - matching how Test Connection
  and other checks already behave in this app.**
- **Numbered error-code badges (101-110) on the HTML run report, with a hover tooltip explaining
  each one - covers missing HandBrake/presets, a misconfigured lane, and FileBot path problems, so
  a problem is identifiable from the report itself, not just the log.**
- **A "Keep Logs of successful HandBrake Encodes" setting (Settings > Maintenance, off by default),
  so successful-encode detail logs don't accumulate forever - failed-encode logs are always kept
  since the report links to them.**

### Changed
- **Every page's status/save messages now show in the toolbar, replacing each page's own scattered
  status element(s) - the same consistent place across Settings, Lanes, and Notifications.**
- **The toolbar shows elapsed time for the run currently in progress ("Monitoring is ON: Running
  (Time Elapsed: 2 hrs, 5 min 10 sec)"), ticking up live instead of only updating once per poll.**
- **Checking for updates now happens immediately when Compressarr starts, instead of only relying
  on a browser cache that could keep showing "update available" for up to a day after you'd
  already upgraded.**
- Donate page crypto address cards are more compact and show a truncated address (full address on
  hover, copy, and in the QR modal) so all six currencies fit in a single row.

### Fixed
- **A failed file move (offline network drive, permissions, etc.) no longer deletes the source
  file before the move is retried - the source is preserved until the move actually succeeds, and
  a failed move is retried automatically on the lane's next pass without re-encoding. A related
  bug this fix exposed - a rescan could mistake that pending retry for a fresh file and force a
  full re-encode instead of just retrying the move - is fixed alongside it.**
- **Sonarr/Radarr are no longer unmonitored for a file whose move to its destination failed - only
  once the move actually succeeds.**
- **The destination-collision setting (Rename/Skip) now actually applies - previously the staged
  output file was always given a fresh temporary name before the collision check ran, so Rename
  and Skip both behaved like Overwrite in practice.**
- **Companion files (subtitles, .nfo, artwork) now follow the same "On destination collision"
  setting as their video, and always take the video's own resulting filename (including any
  Rename-mode suffix), so a renamed video and its companions stay matched.**
- **The library scanner now skips reparse points (junctions/symlinks) and tracks visited folders,
  preventing runaway or duplicate scanning through a symlink loop.**
- **HandBrakeCLI's arguments are now passed individually instead of built into one manually-quoted
  string, removing a class of quoting problems from paths or preset names with spaces or special
  characters.**
- **Several cleanup steps (removing temp files, HandBrake detail logs, and trash-fallback
  warnings) that used to fail silently are now logged instead of swallowed.**
- **A source folder could be left behind, empty, after all its files successfully moved out.**
- **A monitor pass that keeps failing the same way (e.g. a lane with no usable preset) no longer
  writes a fresh log entry and report on every single pass.**
- **Installing or merging a new HandBrake preset didn't refresh the cached preset list, so it
  didn't show up in the Lanes page's preset dropdowns until a separate manual reload.**
- **The Lanes page's own save confirmation never turned green like it does on Settings and
  Notifications.**
- **Several other save/action confirmations across the app were missing their green success
  styling.**

## [2.1.3] - 2026-09-07

> [!TIP]
> **What's new in 2.1.3:** optional FileBot integration to rename/organize files before
> processing, daily/weekly digest notifications on top of per-run alerts, a live queue file count
> with a configurable Queue Completion display (date/time or countdown), a real trusted
> publisher signature in place of the old self-signed certificate, and the bundled installer
> option is back. Everything else below carries forward from 2.1.2 for context - new changes are
> in **bold**.

### Added
- **Compressarr is now signed with a real, trusted publisher certificate instead of the previous
  self-signed one, via Azure Trusted Signing.**
- **The bundled installer (`Compressarr-Setup-{version}-Full.exe`) is back as a second, permanent
  download option alongside the regular installer - it includes its own copy of the .NET runtime,
  so nothing else needs to be installed first, at the cost of a much larger download. It was
  dropped after 2.1.0 due to a Windows Defender reputation flag that a self-signed certificate
  contributed to; the real certificate above resolves that. Pick whichever fits: the regular,
  smaller installer if you already have (or don't mind installing) the .NET runtime it needs, or
  the Full one if you'd rather not deal with that at all.**
- **Optional FileBot pre-processing (Settings > File Name Processing): for users who don't run
  Sonarr/Radarr, Compressarr can shell out to the free [FileBot](https://www.filebot.net/) tool to
  rename and organize TV episodes and movies before scanning a lane's Input folder - separate
  enable toggles and Arguments for TV vs. Movies, a numbering-style picker (S01E01 vs. 1x01), a
  Fix Network button for FileBot's own common "Unable to establish loopback connection" issue, and
  an amber "Unmatched" badge on any queued file FileBot couldn't confidently rename.**
- **Daily and/or weekly digest notifications on every notification channel and the desktop toast,
  independent of the existing per-run trigger - a periodic summary ("Compressed N files, reducing
  original size from X GB to Y GB, saving Z% of original size") instead of, or alongside, a message
  after every single run. Each channel gets its own Daily/Weekly toggle, time, and day, plus
  Test/Save/Clear controls right on the row.**
- **Queue ETA on the Monitor page's state card ("Queue Completion"), estimated from real observed
  encode throughput - shows "Estimating" until enough data exists to calculate from. Configurable
  in Settings to display as an absolute date/time or a countdown duration (e.g. "2d 5h 36m").**
- **"Move to top" / "Move to bottom" in the queue item's 3-dot menu, for quickly repositioning a
  file in a long queue without a long drag.**
- **The Monitor page's "In Queue" heading now shows a live file count ("N Files In Queue").**
- **Configurable companion file extensions and unmatched-companion-file handling
  (Maintain/Delete/Recycle) in Settings, instead of a fixed built-in list.**
- **Clear Logs, Clear History, and Clear All buttons on Settings' Maintenance card.**
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
- **Settings saves now show a clear green confirmation instead of no feedback at all - both at the
  top of the page and directly on the row you just changed (e.g. a notification channel's own Save
  button).**
- **Page status messages now also mirror into the toolbar, so a confirmation like "Settings saved"
  stays visible even after scrolling down a long page.**
- Start/Stop Monitoring is now a single toggle button instead of two separate ones.
- Stop Monitoring now stops after the file currently converting finishes, rather than continuing
  to process every other file still queued behind it.
- Subtitle and other companion files now move to their destination immediately once their own
  video finishes converting, instead of waiting for every file in a shared folder to finish first.
- The queue's preset picker is a plain dropdown showing the preset actually in effect, instead of
  a custom popover that could show a stale or misleading placeholder.

### Fixed
- **FileBot's own console window no longer flashes on screen during an otherwise-background
  automated run.**
- **A file FileBot confirmed was already correctly named (it logs "already exists" for these) was
  incorrectly flagged "Unmatched" in the queue instead of being recognized as a real match.**
- **The Monitor page's "Renaming" state now appears as soon as FileBot actually starts, not after
  the fact - fixed a real blocking bug where starting monitoring could hang the page's very first
  status update for the full duration of FileBot's own work before anything appeared to happen.**
- **The post-execution command's own process launch could also flash a console window, the same
  root cause as FileBot's.**
- **A movie filename containing a raw resolution tag (e.g. an older DivX-era rip with "720x480" in
  the name) could be misdetected as a TV episode and routed into a nonsense "Season 720" folder.**
- **Log/report retention set to "0 days" deleted everything immediately instead of the intuitive
  "keep forever."**
- **The Monitor page's queue completion estimate could disappear once only a few files remained in
  the queue.**
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
- Upgrading from a self-contained install (v2.1.0, or the briefly-shipped self-contained 2.1.1
  build) left `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` behind in the install folder, since an
  in-place upgrade only overwrites files the new package ships - it never removes files that
  belonged only to the old one. .NET's host then treated the install folder itself as a
  self-contained runtime root and failed to find the real machine-wide runtime, showing "You must
  install or update .NET" even on a machine with the correct runtime properly installed. The
  installer now runs the previous version's own uninstaller before installing, guaranteeing a
  clean upgrade every time.

### Security
- Donation addresses on the Donate page are no longer stored as single literal strings in the
  compiled binary - a reasonable hardening measure.

## [2.1.2] - 2026-09-06

> [!TIP]
> **What's new in 2.1.2:** movies no longer get silently misrouted into an unrelated movie's own
> folder. Everything else below carries forward from 2.1.1 for context - new changes are in
> **bold**.

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
- **Movies could get silently misrouted into an unrelated movie's own destination folder instead
  of landing directly under the lane's Movie base path. `MoveMovieFile` tried to auto-detect a
  "bucket" folder (e.g. `01. Movies 1920-1979`, for libraries organized into year-range
  folders) by scanning the destination for any folder whose name merely contained the word
  "movie" - but an ordinary movie's own folder can just as easily match that (a title like
  `Scary Movie (2026)` contains "Movie"), and once that was the only such folder present, every
  subsequent movie got nested inside it instead of getting its own folder. Confirmed happening
  in production. Bucket/range folders were never actually used, so the auto-detection was
  removed entirely rather than made stricter - movies now always land directly under the
  configured Movie base path, each in its own folder, with no exceptions.**
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
- Upgrading from a self-contained install (v2.1.0, or the briefly-shipped self-contained 2.1.1
  build) left `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` behind in the install folder, since an
  in-place upgrade only overwrites files the new package ships - it never removes files that
  belonged only to the old one. .NET's host then treated the install folder itself as a
  self-contained runtime root and failed to find the real machine-wide runtime, showing "You must
  install or update .NET" even on a machine with the correct runtime properly installed. The
  installer now runs the previous version's own uninstaller before installing, guaranteeing a
  clean upgrade every time.

### Security
- Donation addresses on the Donate page are no longer stored as single literal strings in the
  compiled binary - a reasonable hardening measure.

## [2.1.1] - 2026-09-05

> [!TIP]
> **What's new in 2.1.1:** two Windows Defender false positives resolved
> (`Trojan:Win32/Wacatac.B!ml`, `Program:Win32/Contebrew.A!ml`), a cleaner installer upgrade
> path, and hardened donation-address storage. Everything else below carries forward from 2.1.0
> for context - new changes are in **bold**.

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
- **Resolved a false-positive `Trojan:Win32/Wacatac.B!ml` flag from one vendor on v2.1.0's
  installer. Root-caused through systematic isolation testing against VirusTotal to the
  installer's LZMA2 compression of the embedded application payload, not to any notification
  code, service, or architecture - confirmed by an A/B test where an identical build scanned
  clean the moment compression was disabled. The installer now ships uncompressed
  (`Compression=none`) as a result; every notification channel, including Discord and Slack,
  remains fully intact.**
- **Along the way, the notification providers (Discord, Slack, Telegram, Pushover, ntfy, Gotify,
  Notifiarr, IFTTT) were also rewritten onto a narrower, intentionally boring HTTP client
  interface (fixed JSON/form/text POST shapes, never a fully generic method+headers+content-type
  sender) instead of sharing one universal webhook-sending routine - a deliberate architecture
  improvement independent of the VirusTotal finding above. Generic Webhook keeps its own
  fully-flexible sender, since it's the one channel that genuinely needs arbitrary
  method/header/URL configurability.**
- **Resolved a second, unrelated false positive (`Program:Win32/Contebrew.A!ml`) that Windows
  Defender's live cloud/SmartScreen reputation classifier flagged on a real download of the
  self-contained installer, despite VirusTotal - including a same-day re-scan with Microsoft's own
  engine - showing it completely clean. That classifier weighs signals VirusTotal's static engine
  never sees: publisher trust (this project's cert is self-signed, so it starts with none) and how
  new/large/rarely-downloaded a file is. Rather than chase a live reputation heuristic, Compressarr
  now ships a single, much smaller framework-dependent installer instead of the self-contained
  build - removing the exposure rather than working around it. See Installation below for the
  runtime it now requires.**
- **Upgrading from a self-contained install (v2.1.0, or the briefly-shipped self-contained 2.1.1
  build) left `coreclr.dll`/`hostfxr.dll`/`hostpolicy.dll` behind in the install folder, since an
  in-place upgrade only overwrites files the new package ships - it never removes files that
  belonged only to the old one. .NET's host then treated the install folder itself as a
  self-contained runtime root and failed to find the real machine-wide runtime, showing "You must
  install or update .NET" even on a machine with the correct runtime properly installed. The
  installer now runs the previous version's own uninstaller before installing, guaranteeing a
  clean upgrade every time.**

### Security
- **Donation addresses on the Donate page are no longer stored as single literal strings in the
  compiled binary - a reasonable hardening measure.**

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
