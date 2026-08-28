#Requires -Version 5.1

<#
  Compressarr.ArrIntegration.psm1

  Optional post-conversion hook: after a file finishes converting
  successfully, tell Sonarr/Radarr to stop monitoring that specific
  episode/movie, so it isn't re-grabbed later. Matching is done entirely
  through each app's own /api/v3/parse endpoint - feed it the original
  filename and it returns whichever library item its own parser matches
  it to, the same lookup those apps use internally for manual imports.
  Compressarr deliberately does not attempt any fallback fuzzy matching of
  its own: a miss just means nothing changes (safe), but a wrong guess
  would mean unmonitoring the wrong show/movie (not safe), so a failed
  match is always treated as "skip", never "guess".
#>

function Invoke-CompressarrArrRequest {
  <# Thin wrapper around Invoke-RestMethod with the X-Api-Key header, so
     callers don't repeat that everywhere. #>
  param(
    [Parameter(Mandatory)] [string]$Method,
    [Parameter(Mandatory)] [string]$Uri,
    [Parameter(Mandatory)] [string]$ApiKey,
    $Body
  )

  $params = @{
    Method     = $Method
    Uri        = $Uri
    Headers    = @{ 'X-Api-Key' = $ApiKey }
    TimeoutSec = 15
  }
  if ($null -ne $Body) {
    $params.Body = ($Body | ConvertTo-Json -Depth 10)
    $params.ContentType = 'application/json'
  }
  return Invoke-RestMethod @params
}

function Invoke-CompressarrSonarrUnmonitor {
  <#
    Parses $FileName via Sonarr's /api/v3/parse; if it matches a series
    and one or more episodes Sonarr already knows about, sets each
    matched episode's monitored flag to $false, then triggers a
    RescanSeries command. The rescan matters even when nothing needed
    unmonitoring: Compressarr has already moved the file out of wherever
    Sonarr originally scanned it from, so without a rescan Sonarr keeps
    showing the episode as downloaded even though the file is gone from
    that path - the rescan is what actually clears that stale state.

    Returns [PSCustomObject]@{ Matched; Changed }. Matched is $true if
    Sonarr's own parser matched this filename to a real episode (this is
    also what gates the rescan). Changed is $true only if the monitored
    flag actually had to flip - an episode matched but already
    unmonitored still gets Matched=$true so the caller knows a rescan
    happened. Throws on request failures (bad URL, bad API key, Sonarr
    unreachable) - callers decide how to log that.
  #>
  param(
    [Parameter(Mandatory)] [string]$BaseUrl,
    [Parameter(Mandatory)] [string]$ApiKey,
    [Parameter(Mandatory)] [string]$FileName
  )

  $base = $BaseUrl.TrimEnd('/')
  $parseUri = "$base/api/v3/parse?title=" + [uri]::EscapeDataString($FileName)
  $parsed = Invoke-CompressarrArrRequest -Method 'Get' -Uri $parseUri -ApiKey $ApiKey

  if (-not $parsed -or -not $parsed.series -or -not $parsed.episodes -or $parsed.episodes.Count -eq 0) {
    return [PSCustomObject]@{ Matched = $false; Changed = $false }
  }

  $changedAny = $false
  foreach ($episode in $parsed.episodes) {
    if ($episode.monitored -eq $false) { continue }
    $episode.monitored = $false
    $episodeUri = "$base/api/v3/episode/$($episode.id)"
    Invoke-CompressarrArrRequest -Method 'Put' -Uri $episodeUri -ApiKey $ApiKey -Body $episode | Out-Null
    $changedAny = $true
  }

  $commandUri = "$base/api/v3/command"
  Invoke-CompressarrArrRequest -Method 'Post' -Uri $commandUri -ApiKey $ApiKey `
    -Body ([PSCustomObject]@{ name = 'RescanSeries'; seriesId = $parsed.series.id }) | Out-Null

  return [PSCustomObject]@{ Matched = $true; Changed = $changedAny }
}

function Invoke-CompressarrRadarrUnmonitor {
  <#
    Same idea as Invoke-CompressarrSonarrUnmonitor, for Radarr's single
    matched movie instead of a series/episode list - unmonitors if
    needed, then always triggers a RescanMovie command on any match so
    Radarr's "has file" state gets cleared once Compressarr has moved the
    file elsewhere.
  #>
  param(
    [Parameter(Mandatory)] [string]$BaseUrl,
    [Parameter(Mandatory)] [string]$ApiKey,
    [Parameter(Mandatory)] [string]$FileName
  )

  $base = $BaseUrl.TrimEnd('/')
  $parseUri = "$base/api/v3/parse?title=" + [uri]::EscapeDataString($FileName)
  $parsed = Invoke-CompressarrArrRequest -Method 'Get' -Uri $parseUri -ApiKey $ApiKey

  if (-not $parsed -or -not $parsed.movie -or -not $parsed.movie.id) {
    return [PSCustomObject]@{ Matched = $false; Changed = $false }
  }

  $movie = $parsed.movie
  $changed = $false
  if ($movie.monitored -ne $false) {
    $movie.monitored = $false
    $movieUri = "$base/api/v3/movie/$($movie.id)"
    Invoke-CompressarrArrRequest -Method 'Put' -Uri $movieUri -ApiKey $ApiKey -Body $movie | Out-Null
    $changed = $true
  }

  $commandUri = "$base/api/v3/command"
  Invoke-CompressarrArrRequest -Method 'Post' -Uri $commandUri -ApiKey $ApiKey `
    -Body ([PSCustomObject]@{ name = 'RescanMovie'; movieId = $movie.id }) | Out-Null

  return [PSCustomObject]@{ Matched = $true; Changed = $changed }
}

function Invoke-CompressarrArrUnmonitor {
  <#
    Dispatches to Sonarr (TV) or Radarr (Movie) based on $IsTV, but only
    if that service is enabled in config. Returns $null if the matching
    service isn't enabled (nothing to do - not an error). Returns a short
    status string describing the outcome on success - unmonitored (+
    rescanned), already unmonitored (rescanned anyway, to clear a stale
    "has file" state), or no match found at all. Throws if the service is
    enabled but not configured (blank URL/API key), or if the request
    itself fails - callers wrap this in their own try/catch, matching how
    Move-CompressarrRoutedFile and Move-CompressarrCompanionFiles are
    already called.
  #>
  param(
    [Parameter(Mandatory)] $Config,
    [Parameter(Mandatory)] [string]$FileName,
    [Parameter(Mandatory)] [bool]$IsTV
  )

  if ($IsTV) { $svc = $Config.arrs.sonarr; $serviceName = 'Sonarr'; $itemWord = 'episode' }
  else { $svc = $Config.arrs.radarr; $serviceName = 'Radarr'; $itemWord = 'movie' }

  if (-not $svc -or -not $svc.enabled) { return $null }

  if ([string]::IsNullOrWhiteSpace($svc.url) -or [string]::IsNullOrWhiteSpace($svc.apiKey)) {
    throw "$serviceName is enabled but its URL or API key is not configured."
  }

  $result = $null
  if ($IsTV) {
    $result = Invoke-CompressarrSonarrUnmonitor -BaseUrl $svc.url -ApiKey $svc.apiKey -FileName $FileName
  }
  else {
    $result = Invoke-CompressarrRadarrUnmonitor -BaseUrl $svc.url -ApiKey $svc.apiKey -FileName $FileName
  }

  if (-not $result.Matched) {
    return "$serviceName`: no matching monitored $itemWord found for '$FileName' - left unchanged."
  }
  if ($result.Changed) {
    return "$serviceName`: unmonitored the matching $itemWord and rescanned the library."
  }
  return "$serviceName`: already unmonitored - rescanned the library to clear its stale downloaded status."
}

Export-ModuleMember -Function `
  Invoke-CompressarrSonarrUnmonitor, `
  Invoke-CompressarrRadarrUnmonitor, `
  Invoke-CompressarrArrUnmonitor
