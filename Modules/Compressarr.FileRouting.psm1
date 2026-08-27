#Requires -Version 5.1

<#
  Compressarr.FileRouting.psm1

  Each of the four content lanes (hdMovies, hdTV, uhdMovies, uhdTV) has its
  own input folder, so - unlike Paul's VidMonHB, which auto-detects TV vs
  Movie from a single shared input folder - the lane itself already tells us
  whether a file is a TV episode or a Movie. The season/episode regex is
  still needed, but only inside the TV lanes, to work out the "Show Name /
  Season NN" destination folder for the move step.
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

function Move-CompressarrMovieFile {
  <#
    Ported from Paul's moveMovieFile: buckets a movie into a year-range
    subfolder under $OutputBase (e.g. "01. Movies 1920-1979") when the
    filename carries a "(YYYY)" year tag and a matching folder exists;
    falls back to the single existing "*movie*" folder, then to
    $OutputBase itself.
  #>
  param(
    [Parameter(Mandatory)] [string]$FileName,
    [Parameter(Mandatory)] [string]$OutputBase
  )

  $leaf = Split-Path -Path $FileName -Leaf
  $movieYear = ($FileName -split '\(([^\)]+)\)')[1]
  $movieFolders = Get-ChildItem -Path $OutputBase -Recurse -Directory -Include '*movie*' -ErrorAction SilentlyContinue | Sort-Object

  if (($movieFolders | Measure-Object).Count -eq 1) {
    $destPath = Join-Path -Path $movieFolders.FullName -ChildPath $leaf
    Move-Item -Path $FileName -Destination $destPath -Force
    return $destPath
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
        $destPath = Join-Path -Path $movieFolder.FullName -ChildPath $leaf
        Move-Item -Path $FileName -Destination $destPath -Force
        return $destPath
      }
    }
  }

  $destPath = Join-Path -Path $OutputBase -ChildPath $leaf
  Move-Item -Path $FileName -Destination $destPath -Force
  return $destPath
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
    [Parameter(Mandatory)] [string]$OutputBase
  )

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
    Dispatches on the lane name (hdMovies/uhdMovies vs hdTV/uhdTV) rather
    than re-detecting content type from the filename - the lane already
    tells us that, since each lane has its own dedicated input folder.
  #>
  param(
    [Parameter(Mandatory)] [string]$FileName,
    [Parameter(Mandatory)] [string]$LaneName,
    [Parameter(Mandatory)] [string]$OutputBase,
    [Parameter(Mandatory)] [bool]$MoveFiles
  )

  if (-not $MoveFiles) { return $null }

  switch ($LaneName) {
    { $_ -in @('hdMovies', 'uhdMovies') } { return Move-CompressarrMovieFile -FileName $FileName -OutputBase $OutputBase }
    { $_ -in @('hdTV', 'uhdTV') }         { return Move-CompressarrTVFile -FileName $FileName -OutputBase $OutputBase }
    Default { throw "Compressarr: unknown lane '$LaneName' in Move-CompressarrRoutedFile." }
  }
}

Export-ModuleMember -Function `
  Get-CompressarrEpisodeInfo, `
  Move-CompressarrMovieFile, `
  Move-CompressarrTVFile, `
  Move-CompressarrRoutedFile
