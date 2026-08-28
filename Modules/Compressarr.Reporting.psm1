#Requires -Version 5.1

<#
  Compressarr.Reporting.psm1

  Builds a standalone HTML report at the end of a run - this replaces
  Paul's SMTP/email notification block entirely. Nothing in Compressarr
  sends anything anywhere; the report is a local file the user opens when
  they want to check in on a run, same information Paul used to email
  himself plus the daily/monthly/yearly history rollups his displayHistory
  function only ever printed to the console.
#>

function Get-CompressarrHistoryRollups {
  param(
    [Parameter(Mandatory)] [string]$LogFilePath
  )

  $history = Get-CompressarrHistory -LogFilePath $LogFilePath
  $yyyy = Get-Date -Format yyyy
  $mm = Get-Date -Format MM
  $dd = Get-Date -Format dd

  function Get-Rollup {
    param($Rows)
    $rowArray = @($Rows)
    $beg = ($rowArray | ForEach-Object { [double]$_.BegSize } | Measure-Object -Sum).Sum
    $end = ($rowArray | ForEach-Object { [double]$_.EndSize } | Measure-Object -Sum).Sum
    $count = ($rowArray | ForEach-Object { [int]$_.FileCount } | Measure-Object -Sum).Sum
    if (-not $beg) { $beg = 0 }
    if (-not $end) { $end = 0 }
    if (-not $count) { $count = 0 }
    $pct = if ($beg -gt 0) { [math]::Round(100 - ($end / $beg) * 100, 2) } else { 0 }
    return [PSCustomObject]@{
      BeginSizeGB = [math]::Round($beg, 3)
      EndSizeGB   = [math]::Round($end, 3)
      FileCount   = $count
      SavingsPct  = $pct
    }
  }

  return [PSCustomObject]@{
    Daily   = Get-Rollup ($history | Where-Object { $_.yyyy -eq $yyyy -and $_.mm -eq $mm -and $_.dd -eq $dd })
    Monthly = Get-Rollup ($history | Where-Object { $_.yyyy -eq $yyyy -and $_.mm -eq $mm })
    Yearly  = Get-Rollup ($history | Where-Object { $_.yyyy -eq $yyyy })
  }
}

function ConvertTo-CompressarrHtmlEncoded {
  param([string]$Text)
  if ($null -eq $Text) { return '' }
  return [System.Net.WebUtility]::HtmlEncode($Text)
}

function Get-CompressarrAssetsPath {
  return (Join-Path -Path $PSScriptRoot -ChildPath '..\Assets')
}

function Get-CompressarrBase64Asset {
  <# Reads a file and returns it as a base64 string, or $null if missing. #>
  param([Parameter(Mandatory)] [string]$Path)
  if (-not (Test-Path $Path)) { return $null }
  return [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($Path))
}

function Get-CompressarrLaneReportSection {
  param(
    [Parameter(Mandatory)] [string]$LaneDisplayName,
    [Parameter(Mandatory)] [AllowEmptyCollection()] [System.Collections.Generic.List[object]]$Results
  )

  if ($Results.Count -eq 0) {
    return "<h3>$LaneDisplayName</h3><p class='muted'>No files processed.</p>"
  }

  $rows = foreach ($r in $Results) {
    $statusClass = if ($r.Success) { 'ok' } else { 'err' }
    $statusText = if ($r.Success) { 'OK' } else { 'ERROR' }
    $savings = [math]::Round($r.BeginSizeGB - $r.EndSizeGB, 3)
    $contentType = if ($r.ContentType) { $r.ContentType } else { '' }
    $presetName = if ($r.PresetName) { $r.PresetName } else { '' }
    # Blank whenever Sonarr/Radarr integration wasn't attempted for this
    # file - either the conversion itself failed, or neither service is
    # enabled for this content type - not just when it succeeded/failed.
    # [char]0x2014 (em dash), not a literal non-ASCII character in source -
    # a literal here risks silent mojibake if this file is ever read back
    # with a different encoding than it was saved with.
    $arrStatus = if ($r.ArrStatus) { $r.ArrStatus } else { [string][char]0x2014 }
    @"
      <tr class="$statusClass">
        <td>$(ConvertTo-CompressarrHtmlEncoded $r.FileName)</td>
        <td>$(ConvertTo-CompressarrHtmlEncoded $contentType)</td>
        <td>$(ConvertTo-CompressarrHtmlEncoded $presetName)</td>
        <td>$($r.BeginSizeGB) GB</td>
        <td>$($r.EndSizeGB) GB</td>
        <td>$savings GB</td>
        <td>$statusText</td>
        <td>$(ConvertTo-CompressarrHtmlEncoded $arrStatus)</td>
      </tr>
"@
  }

  $beg = [math]::Round((($Results | Measure-Object -Property BeginSizeGB -Sum).Sum), 3)
  $end = [math]::Round((($Results | Measure-Object -Property EndSizeGB -Sum).Sum), 3)

  return @"
    <h3>$LaneDisplayName <span class="muted">($($Results.Count) file(s), $beg GB &rarr; $end GB)</span></h3>
    <div class="table-wrap">
    <table>
      <thead><tr><th>File</th><th>Type</th><th>Preset</th><th>Before</th><th>After</th><th>Savings</th><th>Status</th><th>Sonarr/Radarr</th></tr></thead>
      <tbody>
        $($rows -join "`n")
      </tbody>
    </table>
    </div>
"@
}

function Get-CompressarrRollupRow {
  param([string]$Label, $Rollup)
  return "<tr><td>$Label</td><td>$($Rollup.BeginSizeGB) GB</td><td>$($Rollup.EndSizeGB) GB</td><td>$($Rollup.SavingsPct)%</td><td>$($Rollup.FileCount)</td></tr>"
}

function New-CompressarrReport {
  <#
    Builds one self-contained HTML report covering every lane processed
    this run, plus the daily/monthly/yearly history rollups. Returns the
    report's file path and the run's error count.
  #>
  param(
    [Parameter(Mandatory)] [string]$ReportPath,
    [Parameter(Mandatory)] [string]$Timestamp,
    [Parameter(Mandatory)] [hashtable]$LaneResults,
    [Parameter(Mandatory)] [TimeSpan]$RunTime,
    [Parameter(Mandatory)] [string]$LogFilePath,
    [string]$SummaryLogFile,
    # Only a run that actually processed files gets a number (see
    # Get-CompressarrRunCount in Compressarr.Logging.psm1) - 0/omitted
    # means this pass found nothing to do, so the report shows a plain
    # "Run:" line instead of a "Run #N:" badge for a run that wasn't counted.
    [int]$RunNumber = 0
  )

  if (-not (Test-Path $ReportPath)) {
    New-Item -Path $ReportPath -ItemType Directory -Force | Out-Null
  }

  $allResults = @($LaneResults.Values | ForEach-Object { $_ })
  $totalFiles = $allResults.Count
  $totalBeg = [math]::Round((($allResults | Measure-Object -Property BeginSizeGB -Sum).Sum), 3)
  $totalEnd = [math]::Round((($allResults | Measure-Object -Property EndSizeGB -Sum).Sum), 3)
  $totalSavings = [math]::Round($totalBeg - $totalEnd, 3)
  $savingsPct = if ($totalBeg -gt 0) { [math]::Round(100 - ($totalEnd / $totalBeg) * 100, 2) } else { 0 }
  $errorCount = @($allResults | Where-Object { -not $_.Success }).Count

  $rollups = Get-CompressarrHistoryRollups -LogFilePath $LogFilePath

  $laneSections = foreach ($laneName in (Get-CompressarrLaneNames)) {
    $displayName = Get-CompressarrLaneDisplayName -LaneName $laneName

    # Deliberately NOT `$results = if (...) {...} else {...}` - unlike a plain
    # assignment, using an if/else *as an expression* to produce the value
    # goes through the same pipeline-unrolling PowerShell applies to
    # `return`: a lane that WAS processed but found zero files stores an
    # empty (non-null) List[object] in $LaneResults, and reading it back out
    # through that expression form collapses it to $null. Plain imperative
    # assignment does not have this problem.
    $results = New-Object System.Collections.Generic.List[object]
    if ($LaneResults.ContainsKey($laneName)) { $results = $LaneResults[$laneName] }

    Get-CompressarrLaneReportSection -LaneDisplayName $displayName -Results $results
  }

  $statusBanner = if ($errorCount -gt 0) {
    "<div class='banner err'>$errorCount error(s) occurred - see the per-lane tables below and the detail logs.</div>"
  } else {
    "<div class='banner ok'>Run completed with no errors.</div>"
  }

  $assetsPath = Get-CompressarrAssetsPath
  $faviconB64 = Get-CompressarrBase64Asset -Path (Join-Path -Path $assetsPath -ChildPath 'compressarr.ico')
  $logoB64 = Get-CompressarrBase64Asset -Path (Join-Path -Path $assetsPath -ChildPath 'compressarr-logo.png')
  $faviconTag = if ($faviconB64) { "<link rel=`"icon`" type=`"image/x-icon`" href=`"data:image/x-icon;base64,$faviconB64`">" } else { '' }
  $logoTag = if ($logoB64) { "<img src=`"data:image/png;base64,$logoB64`" alt=`"Compressarr`" class=`"logo`">" } else { '' }
  $runLabel = if ($RunNumber -gt 0) { "Run #${RunNumber}:" } else { 'Run:' }

  $html = @"
<title>Compressarr Report - $Timestamp</title>
$faviconTag
<style>
  body { font-family: Segoe UI, Verdana, sans-serif; margin: 2rem; color: #1c1c1c; background: #fafafa; }
  h1 { margin-bottom: 0; }
  .header { display: flex; align-items: center; gap: 0.75rem; }
  .logo { width: 48px; height: 48px; }
  .muted { color: #666; font-weight: normal; font-size: 0.85em; }
  .banner { padding: 0.75rem 1rem; border-radius: 6px; margin: 1rem 0; font-weight: 600; }
  .banner.ok { background: #e3f7e8; color: #16693a; }
  .banner.err { background: #fdeaea; color: #a1231e; }
  .table-wrap { overflow-x: auto; margin: 0.5rem 0 1.5rem 0; }
  table { border-collapse: collapse; width: 100%; min-width: 640px; margin: 0; background: #fff; }
  th, td { border: 1px solid #ddd; padding: 6px 10px; text-align: left; font-size: 0.9em; }
  th { background: #2c3e50; color: #fff; }
  tr.err { background: #fdeaea; }
  .summary-grid { display: flex; gap: 2rem; flex-wrap: wrap; margin: 1rem 0; }
  .stat { background: #fff; border: 1px solid #ddd; border-radius: 6px; padding: 0.75rem 1.25rem; min-width: 140px; }
  .stat .label { font-size: 0.8em; color: #666; }
  .stat .value { font-size: 1.4em; font-weight: 700; }
</style>
<div class="header">
  $logoTag
  <h1>Compressarr Report</h1>
</div>
<p class="muted">$runLabel $Timestamp &nbsp;|&nbsp; Duration: $($RunTime.Hours)h $($RunTime.Minutes)m $($RunTime.Seconds)s</p>
$statusBanner
<div class="summary-grid">
  <div class="stat"><div class="label">Files processed</div><div class="value">$totalFiles</div></div>
  <div class="stat"><div class="label">Before</div><div class="value">$totalBeg GB</div></div>
  <div class="stat"><div class="label">After</div><div class="value">$totalEnd GB</div></div>
  <div class="stat"><div class="label">Saved</div><div class="value">$totalSavings GB ($savingsPct%)</div></div>
  <div class="stat"><div class="label">Errors</div><div class="value">$errorCount</div></div>
</div>
<h2>By lane</h2>
$($laneSections -join "`n")
<h2>History</h2>
<div class="table-wrap">
<table>
  <thead><tr><th>Period</th><th>Before</th><th>After</th><th>Savings</th><th>Files</th></tr></thead>
  <tbody>
    $(Get-CompressarrRollupRow -Label 'Today' -Rollup $rollups.Daily)
    $(Get-CompressarrRollupRow -Label 'This month' -Rollup $rollups.Monthly)
    $(Get-CompressarrRollupRow -Label 'This year' -Rollup $rollups.Yearly)
  </tbody>
</table>
</div>
"@

  if ($SummaryLogFile -and (Test-Path $SummaryLogFile)) {
    $html += "<p class='muted'>Full run log: $(ConvertTo-CompressarrHtmlEncoded $SummaryLogFile)</p>"
  }

  $reportFile = Join-Path -Path $ReportPath -ChildPath "Compressarr_${Timestamp}_Report.html"
  Set-Content -Path $reportFile -Value $html -Encoding UTF8

  return [PSCustomObject]@{
    Path       = $reportFile
    ErrorCount = $errorCount
  }
}

function Show-CompressarrReport {
  param(
    [Parameter(Mandatory)] [string]$ReportFile,
    [Parameter(Mandatory)] [ValidateSet('Always', 'Error', 'Never')] [string]$OpenAfterRun,
    [Parameter(Mandatory)] [int]$ErrorCount
  )

  switch ($OpenAfterRun) {
    'Never' { return }
    'Error' { if ($ErrorCount -gt 0) { Invoke-Item $ReportFile } }
    'Always' { Invoke-Item $ReportFile }
  }
}

function Show-CompressarrToastNotification {
  <#
    Shows a Windows toast (lower-right, Action Center) summarizing a
    completed run - independent of report.openAfterRun, since that
    setting can be 'Never' or 'Error' (no errors), in which case the
    report never opens on its own and this is the only completion signal
    the user gets. Clicking the toast opens the report in the default
    browser.

    Uses the native Windows.UI.Notifications WinRT API directly (no
    BurntToast or any other external module - keeps Compressarr free of
    PowerShell Gallery dependencies, consistent with the rest of the app).
    The toast is registered under PowerShell's own well-known
    AppUserModelID rather than a custom one Compressarr would have to
    register via a Start Menu shortcut - the tradeoff is that Action
    Center's summary/grouping UI shows "Windows PowerShell" as the
    sending app, even though the toast's own content (logo, text) is
    Compressarr's.

    Click-to-open is offered two ways, both handled by the OS shell
    rather than this process (there is no in-process fallback for this -
    Windows PowerShell 5.1's Register-ObjectEvent cannot subscribe to
    WinRT events at all, so a live click handler here isn't possible):
    - The toast body itself is clickable (`launch`/activationType=
      "protocol" on the <toast> element).
    - An explicit "Click To View Full Report" button (an <actions>
      <action> with its own activationType="protocol"/arguments), for
      discoverability and as a second, independent attempt at the same
      protocol activation in case body-click behaves differently.
    Reliability of either depends on OS/session specifics for a
    non-packaged, borrowed-AUMID sender like this one.

    Never throws on the caller's behalf for anything toast-related failing
    (unsupported OS, no notification support, etc.) - callers should still
    wrap this in their own try/catch per the same soft-fail pattern used
    for the Sonarr/Radarr integration, since a completed conversion run
    should never be treated as failed just because the notification step
    couldn't display something.
  #>
  param(
    [Parameter(Mandatory)] [string]$ReportFile,
    [Parameter(Mandatory)] [int]$TotalFiles,
    [Parameter(Mandatory)] [double]$BeginSizeGB,
    [Parameter(Mandatory)] [double]$EndSizeGB,
    [Parameter(Mandatory)] [TimeSpan]$RunTime
  )

  [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
  [Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
  [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

  $savingsGB = [math]::Round($BeginSizeGB - $EndSizeGB, 3)
  $savingsPct = if ($BeginSizeGB -gt 0) { [math]::Round(100 - ($EndSizeGB / $BeginSizeGB) * 100, 2) } else { 0 }
  $fileWord = if ($TotalFiles -eq 1) { 'file' } else { 'files' }
  # [char]0x2192 (right arrow), not a literal non-ASCII character in
  # source - see the same reasoning in Get-CompressarrLaneReportSection's
  # em dash: a literal here risks silent mojibake if this file is ever
  # read back with a different encoding than it was saved with.
  $arrow = [string][char]0x2192

  $line1 = "$TotalFiles $fileWord processed"
  $line2 = "$BeginSizeGB GB $arrow $EndSizeGB GB (saved $savingsGB GB, $savingsPct%)"
  $line3 = "Duration: $($RunTime.Hours)h $($RunTime.Minutes)m $($RunTime.Seconds)s"

  $reportUri = ([uri]$ReportFile).AbsoluteUri
  $reportUriEscaped = [System.Security.SecurityElement]::Escape($reportUri)
  $logoPath = Join-Path -Path (Get-CompressarrAssetsPath) -ChildPath 'compressarr-logo.png'
  $imageXml = ''
  if (Test-Path $logoPath) {
    $logoUri = ([uri]$logoPath).AbsoluteUri
    $imageXml = "<image placement=`"appLogoOverride`" hint-crop=`"circle`" src=`"$([System.Security.SecurityElement]::Escape($logoUri))`"/>"
  }

  $toastXml = @"
<toast activationType="protocol" launch="$reportUriEscaped">
  <visual>
    <binding template="ToastGeneric">
      $imageXml
      <text>Compressarr - Run Complete</text>
      <text>$([System.Security.SecurityElement]::Escape($line1))</text>
      <text>$([System.Security.SecurityElement]::Escape($line2))</text>
      <text>$([System.Security.SecurityElement]::Escape($line3))</text>
    </binding>
  </visual>
  <actions>
    <action activationType="protocol" arguments="$reportUriEscaped" content="Click To View Full Report"/>
  </actions>
</toast>
"@

  $xmlDoc = New-Object Windows.Data.Xml.Dom.XmlDocument
  $xmlDoc.LoadXml($toastXml)
  $toast = New-Object Windows.UI.Notifications.ToastNotification($xmlDoc)

  # PowerShell's own registered AUMID - see the docstring above for why
  # this is used instead of a Compressarr-specific one.
  $aumid = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
  [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid).Show($toast)
}

Export-ModuleMember -Function `
  Get-CompressarrHistoryRollups, `
  Get-CompressarrAssetsPath, `
  Get-CompressarrBase64Asset, `
  New-CompressarrReport, `
  Show-CompressarrReport, `
  Show-CompressarrToastNotification
