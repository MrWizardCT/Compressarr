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
    matched episode's monitored flag to $false. Returns $true if at least
    one episode was changed, $false if nothing matched or everything
    matched was already unmonitored. Throws on request failures (bad URL,
    bad API key, Sonarr unreachable) - callers decide how to log that.
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
    return $false
  }

  $changedAny = $false
  foreach ($episode in $parsed.episodes) {
    if ($episode.monitored -eq $false) { continue }
    $episode.monitored = $false
    $episodeUri = "$base/api/v3/episode/$($episode.id)"
    Invoke-CompressarrArrRequest -Method 'Put' -Uri $episodeUri -ApiKey $ApiKey -Body $episode | Out-Null
    $changedAny = $true
  }
  return $changedAny
}

function Invoke-CompressarrRadarrUnmonitor {
  <#
    Same idea as Invoke-CompressarrSonarrUnmonitor, for Radarr's single
    matched movie instead of a series/episode list.
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
    return $false
  }

  $movie = $parsed.movie
  if ($movie.monitored -eq $false) { return $false }

  $movie.monitored = $false
  $movieUri = "$base/api/v3/movie/$($movie.id)"
  Invoke-CompressarrArrRequest -Method 'Put' -Uri $movieUri -ApiKey $ApiKey -Body $movie | Out-Null
  return $true
}

function Invoke-CompressarrArrUnmonitor {
  <#
    Dispatches to Sonarr (TV) or Radarr (Movie) based on $IsTV, but only
    if that service is enabled in config. Returns $null if the matching
    service isn't enabled (nothing to do - not an error). Returns a short
    status string describing the outcome (changed vs. no match) on
    success. Throws if the service is enabled but not configured (blank
    URL/API key), or if the request itself fails - callers wrap this in
    their own try/catch, matching how Move-CompressarrRoutedFile and
    Move-CompressarrCompanionFiles are already called.
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

  $changed = $false
  if ($IsTV) {
    $changed = Invoke-CompressarrSonarrUnmonitor -BaseUrl $svc.url -ApiKey $svc.apiKey -FileName $FileName
  }
  else {
    $changed = Invoke-CompressarrRadarrUnmonitor -BaseUrl $svc.url -ApiKey $svc.apiKey -FileName $FileName
  }

  if ($changed) { return "$serviceName`: unmonitored the matching $itemWord." }
  return "$serviceName`: no matching monitored $itemWord found for '$FileName' - left unchanged."
}

Export-ModuleMember -Function `
  Invoke-CompressarrSonarrUnmonitor, `
  Invoke-CompressarrRadarrUnmonitor, `
  Invoke-CompressarrArrUnmonitor
