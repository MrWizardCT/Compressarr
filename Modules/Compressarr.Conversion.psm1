#Requires -Version 5.1

<#
  Compressarr.Conversion.psm1

  Sequential (no parallelism) per-file HandBrakeCLI conversion, one lane at
  a time. Depends on functions from Compressarr.Config.psm1,
  Compressarr.Logging.psm1, and Compressarr.FileRouting.psm1 being imported
  into the same session first (Compressarr.ps1 does this).
#>

$script:ClearMetaFlag = $false

function ConvertTo-CompressarrByteSize {
  <#
    Parses size strings like "500mb", "2gb", "0" into a byte count.
    Paul's original relied on PowerShell implicitly coercing a string like
    "0gb" during a -gt comparison, which only works because of how the
    comparison operator converts operands - fragile and easy to get wrong
    for other units. This parses explicitly instead.
  #>
  param(
    [Parameter(Mandatory)] [AllowEmptyString()] [string]$Value
  )

  if ([string]::IsNullOrWhiteSpace($Value)) { return 0 }
  if ($Value.Trim() -notmatch '^(?<num>[\d\.]+)\s*(?<unit>[a-zA-Z]*)$') { return 0 }

  $num = [double]$Matches['num']
  switch ($Matches['unit'].ToLower()) {
    'kb' { return [long]($num * 1KB) }
    'mb' { return [long]($num * 1MB) }
    'gb' { return [long]($num * 1GB) }
    'tb' { return [long]($num * 1TB) }
    default { return [long]$num }
  }
}

function Find-CompressarrVideoFiles {
  param(
    [Parameter(Mandatory)] [string]$InputPath,
    [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]]$VidTypes,
    [string]$MinSize = '0gb',
    [int]$Limit = 999
  )

  $includePatterns = $VidTypes | Where-Object { $_ } | ForEach-Object { "*.$($_.Trim())" }
  $minBytes = ConvertTo-CompressarrByteSize -Value $MinSize

  return Get-ChildItem -Path $InputPath -Recurse -Include $includePatterns -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Length -gt $minBytes } |
    Select-Object -First $Limit
}

function Get-CompressarrPresetExtension {
  <#
    Reads the selected preset's FileFormat out of presets.json and maps it
    to an output extension, replacing Paul's hardcoded ".mp4". Tolerates
    both the modern "av_mp4"/"av_mkv" values and the plain "mp4"/"mkv"
    values seen in older HandBrake preset exports - both contain the
    container name as a substring, so a simple substring match covers both
    without needing two parallel switch tables.
  #>
  param(
    [Parameter(Mandatory)] [string]$PresetName,
    [Parameter(Mandatory)] [string]$PresetsPath
  )

  $presetObj = Get-CompressarrPresetObject -PresetName $PresetName -PresetsPath $PresetsPath
  if (-not $presetObj) {
    Write-Warning "Compressarr: preset '$PresetName' not found in presets.json - defaulting output extension to .mp4"
    return '.mp4'
  }

  $format = [string]$presetObj.FileFormat
  if ($format -match 'mkv') { return '.mkv' }
  if ($format -match 'mp4') { return '.mp4' }

  Write-Warning "Compressarr: preset '$PresetName' has unrecognized FileFormat '$format' - defaulting output extension to .mp4"
  return '.mp4'
}

function Enable-CompressarrMetadataClearing {
  <# Optional soft dependency, same as Paul's taglib-sharp.dll check. #>
  param(
    [Parameter(Mandatory)] [string]$TagLibPath
  )

  if (Test-Path $TagLibPath) {
    Import-Module $TagLibPath -ErrorAction Stop
    $script:ClearMetaFlag = $true
  }
  else {
    $script:ClearMetaFlag = $false
  }
  return $script:ClearMetaFlag
}

function Clear-CompressarrTitleMetadata {
  param(
    [Parameter(Mandatory)] [string]$FilePath
  )

  if (-not $script:ClearMetaFlag) { return }
  if (-not (Test-Path $FilePath)) { return }

  try {
    $mediaFile = [TagLib.File]::Create((Get-Item $FilePath).FullName)
    $customTag = $mediaFile.GetTag([TagLib.TagTypes]::Apple, 1)
    $customTag.Title = ''
    $mediaFile.Save()
  }
  catch {
    Write-Warning "Compressarr: failed to clear title metadata on '$FilePath'. $($_.Exception.Message)"
  }
}

function Import-CompressarrResumeState {
  param(
    [Parameter(Mandatory)] [string]$Path
  )

  $list = New-Object System.Collections.Generic.List[object]
  if (Test-Path $Path) {
    $raw = Get-Content -Path $Path -Raw | ConvertFrom-Json
    foreach ($entry in @($raw)) { $list.Add($entry) }
  }
  # The unary comma is required here: PowerShell unrolls a returned
  # collection onto the pipeline, so an empty List[object] would otherwise
  # come back to the caller as $null instead of an empty list.
  return ,$list
}

function Export-CompressarrResumeState {
  param(
    [Parameter(Mandatory)] $State,
    [Parameter(Mandatory)] [string]$Path
  )

  $State | ConvertTo-Json -Depth 5 | Set-Content -Path $Path -Encoding UTF8
}

function Invoke-CompressarrLaneConversion {
  <#
    Processes every pending file for one lane, strictly sequentially: build
    args, Start-Process -Wait, then immediately run that file's
    post-processing (metadata clear, delete/recycle original, move, log)
    before moving on to the next file. No concurrent HandBrakeCLI processes,
    no job-list polling - each file is fully finished before the next one
    starts, which also means the resume file is always accurate up to the
    file currently in flight.
  #>
  param(
    [Parameter(Mandatory)] [string]$LaneName,
    [Parameter(Mandatory)] $LaneConfig,
    [Parameter(Mandatory)] $Config,
    [Parameter(Mandatory)] [string]$LogFilePath,
    [Parameter(Mandatory)] [string]$Timestamp,
    [Parameter(Mandatory)] $ResumeState,
    [Parameter(Mandatory)] [string]$ResumeFilePath
  )

  $results = New-Object System.Collections.Generic.List[object]

  $inputPath = Expand-CompressarrPath $LaneConfig.input
  $outputBase = Expand-CompressarrPath $LaneConfig.output
  $tvShowBasePath = Expand-CompressarrPath $LaneConfig.tvShowBasePath
  $movieBasePath = Expand-CompressarrPath $LaneConfig.movieBasePath
  if ([string]::IsNullOrWhiteSpace($inputPath) -or -not (Test-Path $inputPath)) {
    return ,$results
  }
  if ([string]::IsNullOrWhiteSpace($outputBase) -and -not $Config.processing.outSameAsIn) {
    Write-CompressarrLog "Lane '$LaneName' has no Output folder configured and 'write output to same folder as input' is off - skipping." -Severity 'E'
    return ,$results
  }

  $hbloc = Expand-CompressarrPath $Config.handbrake.cliPath
  $presetsPath = Expand-CompressarrPath $Config.handbrake.presetsPath

  $pending = @($ResumeState | Where-Object { $_.lane -eq $LaneName -and $_.status -eq 'Pending' })
  if ($pending.Count -gt 0) {
    $videoFiles = $pending | ForEach-Object { Get-Item -Path $_.fullName -ErrorAction SilentlyContinue } | Where-Object { $_ }
  }
  else {
    $videoFiles = Find-CompressarrVideoFiles -InputPath $inputPath -VidTypes $Config.processing.vidTypes -MinSize $Config.processing.minSize -Limit $Config.processing.limit
    foreach ($f in $videoFiles) {
      $ResumeState.Add([PSCustomObject]@{ lane = $LaneName; fullName = $f.FullName; status = 'Pending' })
    }
    Export-CompressarrResumeState -State $ResumeState -Path $ResumeFilePath
  }

  $fileCount = ($videoFiles | Measure-Object).Count
  if ($fileCount -eq 0) { return ,$results }

  $padSize = ([string]$fileCount).Length
  $laneDisplayName = Get-CompressarrLaneDisplayName -LaneName $LaneName

  $i = 0
  foreach ($file in $videoFiles) {
    $i++

    $isTV = Test-CompressarrIsTVFile -FileName $file.Name
    $contentType = if ($isTV) { 'TV Show' } else { 'Movie' }
    $presetName = if ($isTV) { $LaneConfig.tvPreset } else { $LaneConfig.moviePreset }

    $beginSizeGB = [math]::Round($file.Length / 1GB, 3)
    $startTime = Get-Date

    Write-CompressarrFileStart -LaneDisplayName $laneDisplayName -Index $i -Total $fileCount `
      -FileName $file.Name -SizeGB $beginSizeGB -ContentType $contentType -Preset $presetName

    $resumeEntry = $ResumeState | Where-Object { $_.lane -eq $LaneName -and $_.fullName -eq $file.FullName } | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($presetName)) {
      Write-CompressarrLog "  No $contentType preset configured for this lane - skipping." -Severity 'E'
      if ($resumeEntry) { $resumeEntry.status = 'Error' }
      Export-CompressarrResumeState -State $ResumeState -Path $ResumeFilePath
      $results.Add([PSCustomObject]@{
        LaneName = $LaneName; FileName = $file.Name; FullName = $file.FullName; NewFileName = $null
        ContentType = $contentType; PresetName = $presetName
        BeginSizeGB = $beginSizeGB; EndSizeGB = 0; Success = $false; DetailLogFile = $null
        StartTime = $startTime; EndTime = (Get-Date)
      })
      continue
    }

    $extension = Get-CompressarrPresetExtension -PresetName $presetName -PresetsPath $presetsPath

    Clear-CompressarrTitleMetadata -FilePath $file.FullName

    $destFolder = $outputBase
    if ($Config.processing.outSameAsIn) { $destFolder = $file.DirectoryName }
    if (-not (Test-Path $destFolder)) {
      New-Item -Path $destFolder -ItemType Directory -Force | Out-Null
    }
    $newFileName = Join-Path -Path $destFolder -ChildPath ($file.BaseName + $extension)

    # HandBrake must NEVER be told to write (-o) to the same path it's
    # reading (-i) - opening the output truncates it immediately, which
    # then corrupts the source out from under HandBrake's own read and
    # produces a near-instant, empty "conversion". This is easy to hit
    # once source and destination folders match and the preset's output
    # extension happens to match the source's (e.g. mp4 -> mp4 in place -
    # both increasingly common now that mp4 is a default scanned type).
    # Always stage to a uniquely-named temp file in the destination folder
    # and only rename it into place after a verified-successful encode, so
    # this can never happen regardless of in/out path configuration, and a
    # failed encode never leaves a corrupt file sitting at the final name.
    $tempFileName = Join-Path -Path $destFolder -ChildPath ($file.BaseName + '.compressarr-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + $extension)

    $cmdArgs = '-i "' + $file.FullName + '" -t 1 -o "' + $tempFileName + '" --preset-import-file "' + $presetsPath + '" --preset "' + $presetName + '"'
    if ($Config.handbrake.options) { $cmdArgs += ' ' + $Config.handbrake.options }
    Write-CompressarrLog "HB Command: $hbloc $cmdArgs" -LogType 'L'

    $logName = "$($file.BaseName)_${Timestamp}_$(([string]$i).PadLeft($padSize,'0'))_HBdetails.txt"
    $dtlLogFile = Join-Path -Path $LogFilePath -ChildPath $logName
    Remove-Item -Path $dtlLogFile -ErrorAction SilentlyContinue

    Start-Process -FilePath $hbloc -ArgumentList $cmdArgs -RedirectStandardError $dtlLogFile -Wait -NoNewWindow

    $endTime = Get-Date
    $success = $false
    if (Test-Path $tempFileName) {
      $finishedCount = @(Get-Content -Path $dtlLogFile -ErrorAction SilentlyContinue | Where-Object { $_ -like '*Finished work at*' }).Count
      # Also require a non-empty file - belt and braces against any other
      # failure mode that still manages to print "Finished work at" while
      # leaving a truncated/empty file behind.
      $success = ($finishedCount -ge 1) -and ((Get-Item $tempFileName).Length -gt 0)
    }

    $endSizeGB = 0

    if ($success) {
      Clear-CompressarrTitleMetadata -FilePath $tempFileName
      Move-Item -Path $tempFileName -Destination $newFileName -Force
      $endSizeGB = [math]::Round((Get-Item $newFileName).Length / 1GB, 3)

      # If the source and final destination are the same path (in-place
      # conversion), the rename above already replaced the original with
      # the converted result - there is nothing left to separately delete.
      $sameAsSource = [string]::Equals((Get-Item $file.FullName -ErrorAction SilentlyContinue).FullName, $newFileName, [System.StringComparison]::OrdinalIgnoreCase)
      if (-not $sameAsSource -and $Config.processing.deleteAfterConvert -ne 'Maintain') {
        $chkFile = Get-Item -Path $file.FullName -ErrorAction SilentlyContinue
        if ($chkFile -and ($chkFile.Attributes -band [System.IO.FileAttributes]::ReadOnly)) {
          $chkFile.Attributes = $chkFile.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
        }
        Remove-CompressarrItem -Path $file.FullName -Mode $Config.processing.deleteAfterConvert
      }

      try {
        Move-CompressarrRoutedFile -FileName $newFileName -IsTV $isTV -TVShowBasePath $tvShowBasePath `
          -MovieBasePath $movieBasePath -MoveFiles $Config.processing.moveFiles | Out-Null
      }
      catch {
        # The conversion itself already succeeded - a bad/blank base path
        # shouldn't take down the whole run, just leave this file where
        # HandBrake wrote it and note why the move step was skipped.
        Write-CompressarrLog "  Move skipped: $($_.Exception.Message)" -Severity 'E'
      }

      if ($resumeEntry) { $resumeEntry.status = 'Completed' }
    }
    else {
      Remove-Item -Path $tempFileName -Force -ErrorAction SilentlyContinue
      if ($resumeEntry) { $resumeEntry.status = 'Error' }
    }

    $timeDiff = Get-CompressarrTimeDiff -BeginTime $startTime -EndTime $endTime
    Write-CompressarrFileComplete -FileName $newFileName -BeginSizeGB $beginSizeGB -EndSizeGB $endSizeGB `
      -Duration $timeDiff -Success $success -DetailLogFile $dtlLogFile

    Export-CompressarrResumeState -State $ResumeState -Path $ResumeFilePath

    $results.Add([PSCustomObject]@{
      LaneName      = $LaneName
      FileName      = $file.Name
      FullName      = $file.FullName
      NewFileName   = $newFileName
      ContentType   = $contentType
      PresetName    = $presetName
      BeginSizeGB   = $beginSizeGB
      EndSizeGB     = $endSizeGB
      Success       = $success
      DetailLogFile = $dtlLogFile
      StartTime     = $startTime
      EndTime       = $endTime
    })
  }

  return ,$results
}

Export-ModuleMember -Function `
  ConvertTo-CompressarrByteSize, `
  Find-CompressarrVideoFiles, `
  Get-CompressarrPresetExtension, `
  Enable-CompressarrMetadataClearing, `
  Clear-CompressarrTitleMetadata, `
  Import-CompressarrResumeState, `
  Export-CompressarrResumeState, `
  Invoke-CompressarrLaneConversion
