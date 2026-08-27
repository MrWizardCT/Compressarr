<#
.SYNOPSIS
  Compressarr - Windows PowerShell HandBrake batch converter.

.DESCRIPTION
  A from-scratch rewrite of Paul Wasserman's VidMonHB, keeping the same
  core behavior (scan a folder, transcode matching video files through
  HandBrakeCLI, optionally file the results into Show/Movie folders) while
  adding four independent content lanes - HD Movies, HD TV Shows,
  UHD Movies, and UHD TV Shows - each with its own input folder, output
  base path, and HandBrake preset.

  Configuration lives in JSON (see Config\compressarr.settings.json) rather
  than Paul's flat .ps-properties file. Processing is single-threaded
  (sequential, one file at a time - no parallel job tracking). There is no
  email/SMTP notification: each run produces a standalone HTML report in
  the Reports folder instead.

.PARAMETER ConfigPath
  Path to the JSON config file. Defaults to Config\compressarr.settings.json
  next to this script.

.PARAMETER NoGui
  Skip the WinForms GUI and run immediately with the config as loaded from
  disk. Useful for scheduled/headless runs.

.PARAMETER Once
  When set, ignore the config's repeat count / monitor mode and perform
  exactly one run. Useful for testing.
#>

[CmdletBinding()]
param(
  [string]$ConfigPath,
  [switch]$NoGui,
  [switch]$Once
)

$script:CompressarrVersion = '1.0.0-beta.3'

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

if (-not $ConfigPath) {
  $ConfigPath = Join-Path -Path $scriptRoot -ChildPath 'Config\compressarr.settings.json'
}

$moduleRoot = Join-Path -Path $scriptRoot -ChildPath 'Modules'
Import-Module (Join-Path -Path $moduleRoot -ChildPath 'Compressarr.Config.psm1') -Force
Import-Module (Join-Path -Path $moduleRoot -ChildPath 'Compressarr.Logging.psm1') -Force
Import-Module (Join-Path -Path $moduleRoot -ChildPath 'Compressarr.FileRouting.psm1') -Force
Import-Module (Join-Path -Path $moduleRoot -ChildPath 'Compressarr.Conversion.psm1') -Force
Import-Module (Join-Path -Path $moduleRoot -ChildPath 'Compressarr.Reporting.psm1') -Force
if (-not $NoGui) {
  Import-Module (Join-Path -Path $moduleRoot -ChildPath 'Compressarr.UI.psm1') -Force
}

$config = Import-CompressarrConfig -Path $ConfigPath
if (-not (Test-Path $ConfigPath)) {
  Export-CompressarrConfig -Config $config -Path $ConfigPath
}

# Optional soft dependency, same as Paul's taglib-sharp.dll check.
$tagLibPath = Join-Path -Path $scriptRoot -ChildPath 'taglib-sharp.dll'
Enable-CompressarrMetadataClearing -TagLibPath $tagLibPath | Out-Null

function Invoke-CompressarrRun {
  param(
    [Parameter(Mandatory)] $Config
  )

  $timestamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
  $beginTime = Get-Date

  $logFilePath = Expand-CompressarrPath $Config.logging.logFilePath
  $reportPath = Expand-CompressarrPath $Config.report.reportPath
  $summaryLogFile = Initialize-CompressarrLogging -LogFilePath $logFilePath -Timestamp $timestamp

  Write-CompressarrLog "Compressarr v$script:CompressarrVersion - run started $timestamp"
  Write-CompressarrLog ('-' * 80)

  $hbloc = Expand-CompressarrPath $Config.handbrake.cliPath
  if (-not (Test-Path $hbloc)) {
    Write-CompressarrLog "HandBrakeCLI.exe not found at $hbloc. Download it from https://handbrake.fr/downloads2.php" -Severity 'E'
    return $null
  }

  $presetsPath = Expand-CompressarrPath $Config.handbrake.presetsPath
  if (-not (Test-Path $presetsPath)) {
    Write-CompressarrLog "HandBrake presets file not found at $presetsPath" -Severity 'E'
    return $null
  }

  $resumeFilePath = Join-Path -Path $scriptRoot -ChildPath 'compressarr.resume.json'
  $resumeState = Import-CompressarrResumeState -Path $resumeFilePath
  if ($resumeState.Count -gt 0) {
    Write-CompressarrLog "Resuming previous incomplete run ($($resumeState.Count) file(s) tracked)."
  }

  $laneResults = @{}
  foreach ($laneName in (Get-CompressarrLaneNames)) {
    $laneConfig = $Config.contentLanes.$laneName
    $laneDisplayName = Get-CompressarrLaneDisplayName -LaneName $laneName

    if ([string]::IsNullOrWhiteSpace($laneConfig.input)) { continue }

    if ([string]::IsNullOrWhiteSpace($laneConfig.preset)) {
      Write-CompressarrLog "Skipping lane [$laneDisplayName] - no preset configured." -Severity 'E'
      continue
    }
    if (-not (Test-CompressarrPresetExists -PresetName $laneConfig.preset -PresetsPath $presetsPath)) {
      Write-CompressarrLog "Skipping lane [$laneDisplayName] - preset '$($laneConfig.preset)' not found in presets.json." -Severity 'E'
      continue
    }

    Write-CompressarrLog "`nScanning lane [$laneDisplayName] - $(Expand-CompressarrPath $laneConfig.input)"
    $results = Invoke-CompressarrLaneConversion -LaneName $laneName -LaneConfig $laneConfig -Config $Config `
      -LogFilePath $logFilePath -Timestamp $timestamp -ResumeState $resumeState -ResumeFilePath $resumeFilePath
    $laneResults[$laneName] = $results
  }

  $stillOutstanding = @($resumeState | Where-Object { $_.status -ne 'Completed' }).Count -gt 0
  if (-not $stillOutstanding) {
    Remove-Item -Path $resumeFilePath -ErrorAction SilentlyContinue
  }

  Remove-CompressarrOldLogs -LogFilePath $logFilePath -RetentionDays $Config.logging.retentionDays -Mode 'Recycle'

  $endTime = Get-Date
  $runTime = Get-CompressarrTimeDiff -BeginTime $beginTime -EndTime $endTime

  $allResults = @($laneResults.Values | ForEach-Object { $_ })
  $totalFiles = $allResults.Count
  if ($totalFiles -gt 0) {
    $totalBeg = ($allResults | Measure-Object -Property BeginSizeGB -Sum).Sum
    $totalEnd = ($allResults | Measure-Object -Property EndSizeGB -Sum).Sum
    Add-CompressarrHistoryRecord -LogFilePath $logFilePath -BeginSizeGB $totalBeg -EndSizeGB $totalEnd -FileCount $totalFiles -RunTime $runTime | Out-Null
  }

  $postExecCmd = Expand-CompressarrPath $Config.postExec.cmd
  if ($postExecCmd -and (Test-Path $postExecCmd)) {
    Write-CompressarrLog "`nRunning post-execution command: $postExecCmd $($Config.postExec.args)"
    Start-Process -FilePath $postExecCmd -ArgumentList $Config.postExec.args -Wait
  }

  Write-CompressarrLog "`nCompressarr run completed. $totalFiles file(s) processed in $($runTime.Hours)h $($runTime.Minutes)m $($runTime.Seconds)s."

  $report = New-CompressarrReport -ReportPath $reportPath -Timestamp $timestamp -LaneResults $laneResults `
    -RunTime $runTime -LogFilePath $logFilePath -SummaryLogFile $summaryLogFile
  Show-CompressarrReport -ReportFile $report.Path -OpenAfterRun $Config.report.openAfterRun -ErrorCount $report.ErrorCount

  return $report
}

# ---------------------------------------------------------------------------

try {
  if ($NoGui) {
    Invoke-CompressarrRun -Config $config | Out-Null
  }
  else {
    $formResult = Show-CompressarrMainForm -Config $config -ConfigPath $ConfigPath -Version $script:CompressarrVersion
    $config = $formResult.Config

    if ($formResult.Action -eq 'Execute') {
      Invoke-CompressarrRun -Config $config | Out-Null

      $remainingRepeats = [int]$config.repeat.count
      while ($remainingRepeats -gt 0 -and -not $Once) {
        Invoke-CompressarrRun -Config $config | Out-Null
        $remainingRepeats--
      }

      if ($config.repeat.monitor -and -not $Once) {
        Write-Host "`nMonitor mode enabled - watching lane input folders every 60 seconds. Press Ctrl+C to stop."
        while ($true) {
          Start-Sleep -Seconds 60
          $foundAny = $false
          foreach ($laneName in (Get-CompressarrLaneNames)) {
            $laneConfig = $config.contentLanes.$laneName
            if ([string]::IsNullOrWhiteSpace($laneConfig.input)) { continue }
            $inputPath = Expand-CompressarrPath $laneConfig.input
            if (-not (Test-Path $inputPath)) { continue }
            $found = Find-CompressarrVideoFiles -InputPath $inputPath -VidTypes $config.processing.vidTypes -MinSize $config.processing.minSize -Limit 1
            if (($found | Measure-Object).Count -gt 0) { $foundAny = $true; break }
          }
          if ($foundAny) {
            Invoke-CompressarrRun -Config $config | Out-Null
          }
        }
      }
    }
  }
}
catch {
  Write-Error "Compressarr encountered a fatal error: $($_.Exception.Message)"
  throw
}
