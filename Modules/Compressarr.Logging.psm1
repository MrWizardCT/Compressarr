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

  $historyFile = Join-Path -Path $LogFilePath -ChildPath 'Compressarr_History.csv'
  if (-not (Test-Path $historyFile)) { return @() }
  return Import-Csv -Path $historyFile
}

Export-ModuleMember -Function `
  Initialize-CompressarrLogging, `
  Get-CompressarrSummaryLogFile, `
  Write-CompressarrLog, `
  Get-CompressarrTimeDiff, `
  Test-CompressarrFileLocked, `
  Remove-CompressarrItem, `
  Remove-CompressarrOldLogs, `
  Add-CompressarrHistoryRecord, `
  Get-CompressarrHistory
