#Requires -Version 5.1

<#
  Compressarr.Logging.psm1

  Dual-target logging (summary log file + console), file-lock/delete helpers,
  age-based log cleanup, and the running history CSV that the Reporting
  module rolls up into daily/monthly/yearly totals.
#>

Add-Type -AssemblyName Microsoft.VisualBasic

$script:SummaryLogFile = $null

function Initialize-CompressarrLogging {
  param(
    [Parameter(Mandatory)] [string]$LogFilePath,
    [Parameter(Mandatory)] [string]$Timestamp
  )

  if ([string]::IsNullOrWhiteSpace($LogFilePath)) {
    throw 'Compressarr: Log folder is not configured.'
  }
  if (-not (Test-Path $LogFilePath)) {
    New-Item -Path $LogFilePath -ItemType Directory -Force | Out-Null
  }

  $logName = "Compressarr_${Timestamp}_Summary.txt"
  $script:SummaryLogFile = Join-Path -Path $LogFilePath -ChildPath $logName
  Remove-Item -Path $script:SummaryLogFile -ErrorAction SilentlyContinue

  return $script:SummaryLogFile
}

function Get-CompressarrSummaryLogFile {
  return $script:SummaryLogFile
}

function Write-CompressarrLog {
  <#
    Mirrors Paul's writeLog: -LogType controls whether the line goes to the
    summary log file only ('L'), the console only ('S'), or both (default).
    -Severity 'E' flips console coloring to white-on-red, matching the
    original error highlighting.
  #>
  param(
    [Parameter(Position = 0)] [string]$Message = '',
    [ValidateSet('L', 'S', 'Both')] [string]$LogType = 'Both',
    [ValidateSet('I', 'E')] [string]$Severity = 'I',
    [string]$BackgroundColor
  )

  $fgColor = 'Yellow'
  $bgColor = try { [System.Console]::BackgroundColor } catch { 'Black' }
  if ($BackgroundColor) { $bgColor = $BackgroundColor }
  if ($Severity -eq 'E') { $fgColor = 'White'; $bgColor = 'Red' }

  if ($LogType -in @('L', 'Both') -and $script:SummaryLogFile) {
    Write-Output $Message | Out-File -FilePath $script:SummaryLogFile -Append -Encoding UTF8
  }
  if ($LogType -in @('S', 'Both')) {
    Write-Host $Message -ForegroundColor $fgColor -BackgroundColor $bgColor
  }
}

function Write-CompressarrFileStart {
  <#
    Neat multi-line "about to process this file" block - replaces the old
    single-line "**** START [HD Movies] 1 of 3 - Caddyshack (1980).mkv ****"
    banner with the file name, its original size, whether it was
    auto-detected as a Movie or TV Show, and which preset will be used.
  #>
  param(
    [Parameter(Mandatory)] [string]$LaneDisplayName,
    [Parameter(Mandatory)] [int]$Index,
    [Parameter(Mandatory)] [int]$Total,
    [Parameter(Mandatory)] [string]$FileName,
    [Parameter(Mandatory)] [double]$SizeGB,
    [Parameter(Mandatory)] [string]$ContentType,
    [Parameter(Mandatory)] [string]$Preset
  )

  $rule = '-' * 80
  Write-CompressarrLog ''
  Write-CompressarrLog $rule
  Write-CompressarrLog "[$LaneDisplayName] File $Index of $Total"
  Write-CompressarrLog ('  Name   : ' + $FileName)
  Write-CompressarrLog ('  Size   : ' + ('{0:n3}' -f $SizeGB) + ' GB')
  Write-CompressarrLog ('  Type   : ' + $ContentType)
  Write-CompressarrLog ('  Preset : ' + $Preset)
  Write-CompressarrLog $rule
}

function Write-CompressarrFileComplete {
  <# Matching multi-line completion block - success or failure. #>
  param(
    [Parameter(Mandatory)] [string]$FileName,
    [Parameter(Mandatory)] [double]$BeginSizeGB,
    [Parameter(Mandatory)] [double]$EndSizeGB,
    [Parameter(Mandatory)] [TimeSpan]$Duration,
    [Parameter(Mandatory)] [bool]$Success,
    [string]$DetailLogFile
  )

  if ($Success) {
    $savings = [math]::Round($BeginSizeGB - $EndSizeGB, 3)
    $pct = 0
    if ($BeginSizeGB -gt 0) { $pct = [math]::Round(100 - ($EndSizeGB / $BeginSizeGB) * 100, 1) }
    Write-CompressarrLog ('  Completed : ' + $FileName)
    Write-CompressarrLog ('  End size  : ' + ('{0:n3}' -f $EndSizeGB) + ' GB    Saved: ' + ('{0:n3}' -f $savings) + " GB ($pct%)")
    Write-CompressarrLog ('  Duration  : ' + $Duration.Hours + 'h ' + $Duration.Minutes + 'm ' + $Duration.Seconds + 's')
  }
  else {
    Write-CompressarrLog ('  FAILED    : ' + $FileName) -Severity 'E'
    Write-CompressarrLog ('  Detail log: ' + $DetailLogFile) -Severity 'E'
  }
  Write-CompressarrLog ('-' * 80)
  Write-CompressarrLog ''
}

function Get-CompressarrTimeDiff {
  param(
    [Parameter(Mandatory)] [datetime]$BeginTime,
    [Parameter(Mandatory)] [datetime]$EndTime
  )

  $diff = New-TimeSpan -Start $BeginTime -End $EndTime
  if ($diff.Ticks -lt 0) { $diff = New-TimeSpan -Start $EndTime -End $BeginTime }
  return $diff
}

function Test-CompressarrFileLocked {
  param(
    [Parameter(Mandatory)] [string]$FilePath
  )

  $fileInfo = New-Object System.IO.FileInfo $FilePath
  try {
    $stream = $fileInfo.Open([System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $stream.Close()
    return $false
  }
  catch {
    return $true
  }
}

function Remove-CompressarrItem {
  <#
    Deletes or recycles a file depending on -Mode. 'Maintain' is a no-op
    guard so callers can pass the configured deleteAfterConvert value
    straight through without an extra branch at the call site.
  #>
  param(
    [Parameter(Mandatory)] [string]$Path,
    [ValidateSet('Delete', 'Recycle', 'Maintain')] [string]$Mode = 'Delete'
  )

  if ($Mode -eq 'Maintain') { return }

  if ($Mode -eq 'Recycle') {
    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($Path, 'OnlyErrorDialogs', 'SendToRecycleBin')
  }
  else {
    Remove-Item -Path $Path -Force -ErrorAction SilentlyContinue
  }
}

function Remove-CompressarrOldLogs {
  param(
    [Parameter(Mandatory)] [string]$LogFilePath,
    [int]$RetentionDays = 30,
    [ValidateSet('Delete', 'Recycle')] [string]$Mode = 'Recycle'
  )

  $oldLogs = Get-ChildItem -Path $LogFilePath -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in @('.log', '.txt') -and $_.CreationTime -lt (Get-Date).AddDays(-$RetentionDays) }

  $count = ($oldLogs | Measure-Object).Count
  if ($count -eq 0) {
    Write-CompressarrLog "`nNo logs to clean up"
    return
  }

  Write-CompressarrLog "`nNow cleaning up $count old log file(s)"
  foreach ($log in $oldLogs) {
    Remove-CompressarrItem -Path $log.FullName -Mode $Mode
  }
  Write-CompressarrLog "Cleaned up $count log file(s) that were > $RetentionDays days old"
}

function Add-CompressarrHistoryRecord {
  param(
    [Parameter(Mandatory)] [string]$LogFilePath,
    [Parameter(Mandatory)] [double]$BeginSizeGB,
    [Parameter(Mandatory)] [double]$EndSizeGB,
    [Parameter(Mandatory)] [int]$FileCount,
    [Parameter(Mandatory)] [TimeSpan]$RunTime
  )

  $record = [PSCustomObject]@{
    yyyy           = (Get-Date -Format yyyy)
    mm             = (Get-Date -Format MM)
    dd             = (Get-Date -Format dd)
    BegSize        = $BeginSizeGB
    EndSize        = $EndSizeGB
    FileCount      = $FileCount
    ProcessHours   = $RunTime.Hours
    ProcessMinutes = $RunTime.Minutes
    ProcessSeconds = $RunTime.Seconds
  }

  $historyFile = Join-Path -Path $LogFilePath -ChildPath 'Compressarr_History.csv'
  $record | Export-Csv -Path $historyFile -NoTypeInformation -Append
  return $historyFile
}

function Get-CompressarrHistory {
  param(
    [Parameter(Mandatory)] [string]$LogFilePath
  )

  # Comma operator required throughout: PowerShell unrolls a returned
  # collection onto the pipeline, so an empty result here would otherwise
  # come back to the caller as $null instead of an empty collection.
  $historyFile = Join-Path -Path $LogFilePath -ChildPath 'Compressarr_History.csv'
  if (-not (Test-Path $historyFile)) { return ,@() }
  return ,@(Import-Csv -Path $historyFile)
}

Export-ModuleMember -Function `
  Initialize-CompressarrLogging, `
  Get-CompressarrSummaryLogFile, `
  Write-CompressarrLog, `
  Write-CompressarrFileStart, `
  Write-CompressarrFileComplete, `
  Get-CompressarrTimeDiff, `
  Test-CompressarrFileLocked, `
  Remove-CompressarrItem, `
  Remove-CompressarrOldLogs, `
  Add-CompressarrHistoryRecord, `
  Get-CompressarrHistory
