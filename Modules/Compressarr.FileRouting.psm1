#Requires -Version 5.1

<#
  Compressarr.FileRouting.psm1

  Two content lanes (hdsd, uhd), each with a single shared input folder -
  TV vs Movie is auto-detected per file, the same way Paul's VidMonHB does
  it (checkIfTVfile: a file "is TV" if a season/episode marker like S01E01
  is found in the name). See Test-CompressarrIsTVFile below. That
  auto-detected type then picks which preset (tvPreset/moviePreset) and
  which destination (tvShowBasePath/movieBasePath) apply to that file.
#>

function Get-CompressarrEpisodeInfo {
  <#
    Parses season/episode/show-name out of a TV episode filename.

    Paul's original pattern capped both digit groups at \d{1,2}, e.g.
    'S*(\d{1,2})(x|E)(\d{1,2})'. That silently mis-parses any filename
    where the season or episode number runs 3+ digits (S123E45, S01E123 -
    the latter shows up with absolute-numbering schemes), because -split
    only ever hands back the first 1-2 digits it matched. Using \d+ instead
    removes the cap entirely: since the digits are unambiguously delimited
    by the literal 'x'/'E' separator, there's no reason to bound their
    length - all leading digits belong to the season, all trailing digits
    belong to the episode, however many there are.
  #>
  param(
    [Parameter(Mandatory)] [string]$FileName
  )

  $pattern = 'S*(\d+)(x|E)(\d+)'

  $splitFull = [regex]::Split($FileName, $pattern)
  $season  = if ($splitFull.Count -gt 1) { $splitFull[1] } else { $null }
  $episode = if ($splitFull.Count -gt 3) { $splitFull[3] } else { $null }

  $epiName = Split-Path -Path $FileName -Leaf
  $splitName = [regex]::Split($epiName, $pattern)
  $showName = $splitName[0]
  if ($showName.Length -gt 0) {
    # Drop the single separator character (usually '.', ' ', or '-') that
    # sat directly in front of the season marker.
    $showName = $showName.Substring(0, $showName.Length - 1).Trim()
  }
  $showName = $showName.TrimEnd('-').Trim()

  $hasSeasonAndEpisode = (-not [string]::IsNullOrEmpty($season)) -and (-not [string]::IsNullOrEmpty($episode))

  return [PSCustomObject]@{
    HasSeasonAndEpisode = $hasSeasonAndEpisode
    Season              = $season
    Episode             = $episode
    ShowName            = $showName
    EpisodeFileName     = $epiName
  }
}

function Test-CompressarrIsTVFile {
  <#
    Paul's checkIfTVfile logic: a file is treated as a TV episode if a
    season/episode marker is found in its name, otherwise it's a Movie.
    This is the single auto-detection point that picks which preset
    (tvPreset vs moviePreset) and which destination (tvShowBasePath vs
    movieBasePath) apply to a given file within a lane.
  #>
  param(
    [Parameter(Mandatory)] [string]$FileName
  )

  return (Get-CompressarrEpisodeInfo -FileName $FileName).HasSeasonAndEpisode
}

function Get-CompressarrMovieFolderName {
  <#
    The per-movie subfolder name a converted movie is filed under: the
    base filename up through and including its "(YYYY)" year tag, with
    anything after that discarded - "Caddyshack (1980) {edition-Director's
    Cut}.mkv" becomes "Caddyshack (1980)", same as plain
    "Caddyshack (1980).mkv" would. Filenames with no year tag at all fall
    back to the full base filename unchanged.
  #>
  param(
    [Parameter(Mandatory)] [string]$FileName
  )

  $baseName = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
  if ($baseName -match '^(.*?\(\d{4}\))') {
    return $Matches[1].Trim()
  }
  return $baseName.Trim()
}

function Move-CompressarrMovieFile {
  <#
    Ported from Paul's moveMovieFile: buckets a movie into a year-range
    subfolder under $OutputBase (e.g. "01. Movies 1920-1979") when the
    filename carries a "(YYYY)" year tag and a matching folder exists;
    falls back to the single existing "*movie*" folder, then to
    $OutputBase itself. Within whichever of those bucket folders is
    chosen, the movie now gets its own per-title subfolder (same idea as
    TV's Show Name\Season NN\) instead of sitting loose.
  #>
  param(
    [Parameter(Mandatory)] [string]$FileName,
    [Parameter(Mandatory)] [AllowEmptyString()] [string]$OutputBase
  )

  if ([string]::IsNullOrWhiteSpace($OutputBase)) {
    throw "Compressarr: cannot move '$FileName' - Movie base path is not configured for this lane."
  }

  if (-not (Test-Path $OutputBase)) {
    New-Item -Path $OutputBase -ItemType Directory -Force | Out-Null
  }

  $leaf = Split-Path -Path $FileName -Leaf
  $movieFolderName = Get-CompressarrMovieFolderName -FileName $leaf
  $movieYear = ($FileName -split '\(([^\)]+)\)')[1]
  $movieFolders = Get-ChildItem -Path $OutputBase -Recurse -Directory -Include '*movie*' -ErrorAction SilentlyContinue | Sort-Object

  function Move-IntoBucket ($bucketFolder) {
    $movieDestFolder = Join-Path -Path $bucketFolder -ChildPath $movieFolderName
    if (-not (Test-Path $movieDestFolder)) {
      New-Item -Path $movieDestFolder -ItemType Directory -Force | Out-Null
    }
    $destPath = Join-Path -Path $movieDestFolder -ChildPath $leaf
    Move-Item -Path $FileName -Destination $destPath -Force
    return $destPath
  }

  if (($movieFolders | Measure-Object).Count -eq 1) {
    return Move-IntoBucket $movieFolders.FullName
  }

  if ($null -ne $movieYear) {
    foreach ($movieFolder in $movieFolders) {
      $yearParts = [regex]::Split($movieFolder.Name, '(\d{4})( ?- ?)?(\d{4})?')
      $minYear = if ($yearParts.Count -gt 1) { $yearParts[1] } else { $null }
      $maxYear = if ($yearParts.Count -gt 3) { $yearParts[3] } else { $null }

      $isMatch = ($movieFolder.Name -eq $movieYear) -or
                 ($movieYear -eq $minYear) -or
                 (($minYear -and $maxYear) -and ($movieYear -ge $minYear) -and ($movieYear -le $maxYear))

      if ($isMatch) {
        return Move-IntoBucket $movieFolder.FullName
      }
    }
  }

  return Move-IntoBucket $OutputBase
}

function Move-CompressarrTVFile {
  <#
    Ported from Paul's moveTVFile, using the shared, bug-fixed
    Get-CompressarrEpisodeInfo parser instead of its own inline copy of the
    regex (Paul's script duplicated the pattern between checkIfTVfile and
    moveTVFile - one shared helper means the fix only has to exist once).
  #>
  param(
    [Parameter(Mandatory)] [string]$FileName,
    [Parameter(Mandatory)] [AllowEmptyString()] [string]$OutputBase
  )

  if ([string]::IsNullOrWhiteSpace($OutputBase)) {
    throw "Compressarr: cannot move '$FileName' - TV Show base path is not configured for this lane."
  }

  $info = Get-CompressarrEpisodeInfo -FileName $FileName

  if (-not $info.HasSeasonAndEpisode) {
    Write-Warning "Compressarr: cannot move '$FileName' - filename is missing season/episode info."
    return $null
  }

  $destFolder = Join-Path -Path (Join-Path -Path $OutputBase -ChildPath $info.ShowName) -ChildPath ("Season " + $info.Season)
  $destPath = Join-Path -Path $destFolder -ChildPath $info.EpisodeFileName

  if (-not (Test-Path $destFolder)) {
    New-Item -Path $destFolder -ItemType Directory -Force | Out-Null
  }

  Move-Item -Path $FileName -Destination $destPath -Force
  return $destPath
}

function Move-CompressarrRoutedFile {
  <#
    Dispatches on the file's auto-detected content type (see
    Test-CompressarrIsTVFile) to the matching base path - a lane's
    tvShowBasePath for TV episodes, movieBasePath for everything else.
  #>
  param(
    [Parameter(Mandatory)] [string]$FileName,
    [Parameter(Mandatory)] [bool]$IsTV,
    [Parameter(Mandatory)] [AllowEmptyString()] [string]$TVShowBasePath,
    [Parameter(Mandatory)] [AllowEmptyString()] [string]$MovieBasePath,
    [Parameter(Mandatory)] [bool]$MoveFiles
  )

  if (-not $MoveFiles) { return $null }

  if ($IsTV) { return Move-CompressarrTVFile -FileName $FileName -OutputBase $TVShowBasePath }
  return Move-CompressarrMovieFile -FileName $FileName -OutputBase $MovieBasePath
}

function Move-CompressarrCompanionFiles {
  <#
    After a file has been routed into its destination folder (a movie's
    per-title subfolder, or a TV episode's Season folder), handles
    whatever else was sitting alongside it in its original source folder -
    subtitles, .nfo files, artwork, etc:
      - Delete/Recycle mode: siblings are MOVED into the destination
        folder, then anything still left in the source folder is cleared
        out and the now-empty source folder itself is removed.
      - Maintain mode: siblings are COPIED into the destination folder;
        the source folder and everything in it (including the original
        media file) is left untouched.
    Applies the same way for both Movies and TV - it only cares about the
    file's own source folder and the folder it ended up in, not which
    content type it was.

    Safety guard: only acts if $OriginalFileFullName was the ONLY file
    matching $VidTypes in its source folder. If other, not-yet-processed
    video files share that folder (a flat/shared folder rather than one
    dedicated to this item), nothing here is touched - only the file that
    was actually converted gets handled, everywhere else in the module.

    In Delete/Recycle mode, once the source folder itself is removed, its
    ancestors are also checked and removed as long as each is now completely
    empty - e.g. "MASH\Season 01\" being removed after its last episode is
    processed should also remove "MASH\" if that was Season 01's only
    remaining content. This walks upward one folder at a time and stops the
    instant it hits a non-empty folder, or $InputRoot itself (the lane's
    configured watch folder is never removed, even if briefly empty, since
    it has to still exist for the next run to find new files in).
  #>
  param(
    [Parameter(Mandatory)] [string]$OriginalFileFullName,
    [Parameter(Mandatory)] [string]$OriginalFileDirectory,
    [Parameter(Mandatory)] [string]$DestinationFolder,
    [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]]$VidTypes,
    [Parameter(Mandatory)] [ValidateSet('Maintain', 'Delete', 'Recycle')] [string]$DeleteAfterConvert,
    [Parameter(Mandatory)] [string]$InputRoot
  )

  if (-not (Test-Path $OriginalFileDirectory)) { return }

  # -Path must end in \* for -Include to actually filter anything here:
  # Get-ChildItem -Path $folder -Include *.ext (no -Recurse, no wildcard in
  # -Path) is a well-known PowerShell trap that silently returns nothing at
  # all, even when matching files exist - which would have made this safety
  # guard never see any "other videos" and always proceed to sweep/delete.
  $vidIncludes = $VidTypes | Where-Object { $_ } | ForEach-Object { "*.$($_.Trim())" }
  $otherVideos = @(Get-ChildItem -Path "$OriginalFileDirectory\*" -File -Include $vidIncludes -ErrorAction SilentlyContinue |
    Where-Object { -not [string]::Equals($_.FullName, $OriginalFileFullName, [System.StringComparison]::OrdinalIgnoreCase) })

  if ($otherVideos.Count -gt 0) {
    # Shared/flat folder - leave everything alone except the file that was
    # actually converted (already handled elsewhere).
    return
  }

  $siblings = @(Get-ChildItem -Path $OriginalFileDirectory -File -Force -ErrorAction SilentlyContinue |
    Where-Object { -not [string]::Equals($_.FullName, $OriginalFileFullName, [System.StringComparison]::OrdinalIgnoreCase) })

  foreach ($sibling in $siblings) {
    $destPath = Join-Path -Path $DestinationFolder -ChildPath $sibling.Name
    if ($DeleteAfterConvert -eq 'Maintain') {
      Copy-Item -Path $sibling.FullName -Destination $destPath -Force
    }
    else {
      Move-Item -Path $sibling.FullName -Destination $destPath -Force
    }
  }

  if ($DeleteAfterConvert -eq 'Maintain') { return }

  # Clear out anything still left (e.g. a subfolder, or an item a move
  # above couldn't complete), then remove the now-empty source folder
  # itself, leaving a clean workspace for the next run.
  $remaining = Get-ChildItem -Path $OriginalFileDirectory -Force -ErrorAction SilentlyContinue
  foreach ($item in $remaining) {
    if ($item.PSIsContainer) {
      Remove-Item -Path $item.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    else {
      Remove-CompressarrItem -Path $item.FullName -Mode $DeleteAfterConvert
    }
  }
  Remove-CompressarrFolder -Path $OriginalFileDirectory -Mode $DeleteAfterConvert

  # Cascade upward: check each parent folder in turn, removing it too as
  # long as it's now completely empty, stopping the moment we reach a
  # non-empty folder or the lane's Input root.
  $inputRootFull = (Resolve-Path -Path $InputRoot -ErrorAction SilentlyContinue).Path
  if ($inputRootFull) {
    $current = Split-Path -Path $OriginalFileDirectory -Parent
    while ($current -and (Test-Path $current) -and
           -not [string]::Equals($current, $inputRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
      $currentContents = @(Get-ChildItem -Path $current -Force -ErrorAction SilentlyContinue)
      if ($currentContents.Count -gt 0) { break }
      Remove-CompressarrFolder -Path $current -Mode $DeleteAfterConvert
      $current = Split-Path -Path $current -Parent
    }
  }
}

Export-ModuleMember -Function `
  Get-CompressarrEpisodeInfo, `
  Test-CompressarrIsTVFile, `
  Get-CompressarrMovieFolderName, `
  Move-CompressarrMovieFile, `
  Move-CompressarrTVFile, `
  Move-CompressarrRoutedFile, `
  Move-CompressarrCompanionFiles
