#Requires -Version 5.1

<#
  Compressarr.Config.psm1

  Owns the JSON config schema: defaults, load/merge/save, environment-variable
  expansion for path fields, and reading HandBrake's presets.json (preset
  name enumeration + per-preset FileFormat lookup, used by both the UI preset
  dropdowns and the Conversion module's output-extension logic).
#>

$script:DefaultConfigJson = @'
{
  "handbrake": {
    "cliPath": "%ProgramFiles%\\HandBrake\\HandBrakeCLI.exe",
    "presetsPath": "%appdata%\\HandBrake\\presets.json",
    "options": ""
  },
  "contentLanes": {
    "hdsd": {
      "enabled": true,
      "input": "",
      "output": "",
      "tvPreset": "VeryFastDDtoAAC",
      "moviePreset": "VeryFastDDtoAAC",
      "tvShowBasePath": "",
      "movieBasePath": ""
    },
    "uhd": {
      "enabled": true,
      "input": "",
      "output": "",
      "tvPreset": "",
      "moviePreset": "",
      "tvShowBasePath": "",
      "movieBasePath": ""
    }
  },
  "processing": {
    "vidTypes": ["mkv", "avi", "mp4", "mpg", "ts", "m4v"],
    "outSameAsIn": false,
    "deleteAfterConvert": "Maintain",
    "moveFiles": false,
    "limit": 999,
    "minSize": "0gb"
  },
  "logging": {
    "logFilePath": ".\\Logs",
    "retentionDays": 30
  },
  "postExec": {
    "cmd": "",
    "args": ""
  },
  "report": {
    "reportPath": ".\\Reports",
    "openAfterRun": "Always"
  },
  "repeat": {
    "count": 0,
    "monitor": true
  },
  "startup": {
    "countdownSeconds": 10
  },
  "arrs": {
    "sonarr": {
      "enabled": false,
      "url": "http://localhost:8989",
      "apiKey": ""
    },
    "radarr": {
      "enabled": false,
      "url": "http://localhost:7878",
      "apiKey": ""
    }
  }
}
'@

function Get-CompressarrDefaultConfig {
  <#
    Returns a fresh copy of the default config as a PSCustomObject.
    Always parse from the JSON string rather than caching an object instance,
    so callers can't mutate a shared default by reference.
  #>
  return $script:DefaultConfigJson | ConvertFrom-Json
}

function Merge-CompressarrConfigObject {
  <#
    Recursively overlays $Override's properties onto a clone of $Base.
    Both are expected to be PSCustomObjects (as produced by ConvertFrom-Json).
    Missing properties in $Override simply leave the $Base value in place,
    so a partial user config file is valid - only specify what differs.
  #>
  param(
    [Parameter(Mandatory)] $Base,
    [Parameter(Mandatory)] $Override
  )

  # Clone $Base via a JSON round-trip so we never mutate the caller's object
  $result = $Base | ConvertTo-Json -Depth 10 | ConvertFrom-Json

  foreach ($prop in $Override.PSObject.Properties) {
    $baseHasProp = $result.PSObject.Properties.Name -contains $prop.Name
    $baseValue = if ($baseHasProp) { $result.$($prop.Name) } else { $null }

    $isNestedObject = ($prop.Value -is [System.Management.Automation.PSCustomObject]) -and
                       ($baseValue -is [System.Management.Automation.PSCustomObject])

    if ($isNestedObject) {
      $merged = Merge-CompressarrConfigObject -Base $baseValue -Override $prop.Value
      if ($baseHasProp) { $result.$($prop.Name) = $merged }
      else { $result | Add-Member -MemberType NoteProperty -Name $prop.Name -Value $merged }
    }
    else {
      if ($baseHasProp) { $result.$($prop.Name) = $prop.Value }
      else { $result | Add-Member -MemberType NoteProperty -Name $prop.Name -Value $prop.Value }
    }
  }

  return $result
}

function Import-CompressarrConfig {
  <#
    Loads config from $Path, merged over the built-in defaults so a partial
    or missing file still yields a fully-populated config object. If $Path
    doesn't exist, returns the defaults unchanged (caller decides whether to
    write them out via Export-CompressarrConfig).
  #>
  param(
    [Parameter(Mandatory)] [string]$Path
  )

  $defaults = Get-CompressarrDefaultConfig

  if (-not (Test-Path $Path)) {
    return $defaults
  }

  try {
    $userConfig = Get-Content -Path $Path -Raw | ConvertFrom-Json
  }
  catch {
    throw "Compressarr: failed to parse config file '$Path' as JSON. $($_.Exception.Message)"
  }

  return Merge-CompressarrConfigObject -Base $defaults -Override $userConfig
}

function Export-CompressarrConfig {
  param(
    [Parameter(Mandatory)] $Config,
    [Parameter(Mandatory)] [string]$Path
  )

  $folder = Split-Path -Path $Path -Parent
  if ($folder -and -not (Test-Path $folder)) {
    New-Item -Path $folder -ItemType Directory -Force | Out-Null
  }

  $Config | ConvertTo-Json -Depth 10 | Set-Content -Path $Path -Encoding UTF8
}

function Expand-CompressarrPath {
  <#
    Expands %ENVVAR% tokens in a path field. Config files store paths with
    literal env-var tokens (e.g. %ProgramFiles%) so the same JSON works
    across machines; callers expand at the point of use.
  #>
  param(
    [Parameter(Mandatory)] [AllowEmptyString()] [string]$Value
  )

  if ([string]::IsNullOrWhiteSpace($Value)) { return $Value }
  return [Environment]::ExpandEnvironmentVariables($Value)
}

function Test-CompressarrPath {
  <# Mirrors Paul's chkPath - expands env vars first, then Test-Path. #>
  param(
    [Parameter(Mandatory)] [AllowEmptyString()] [string]$Value
  )

  if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
  return Test-Path (Expand-CompressarrPath $Value)
}

function Get-CompressarrLaneNames {
  <#
    Two content lanes: HD/SD and UHD. Unlike the original per-lane-per-type
    design, TV-vs-Movie is no longer a separate lane - each lane auto-detects
    content type per file (Paul's original checkIfTVfile approach, see
    Test-CompressarrIsTVFile in Compressarr.FileRouting.psm1) and picks the
    matching preset/destination from that lane's tvPreset/moviePreset and
    tvShowBasePath/movieBasePath.
  #>
  return @('hdsd', 'uhd')
}

function Get-CompressarrLaneDisplayName {
  param([Parameter(Mandatory)] [string]$LaneName)
  switch ($LaneName) {
    'hdsd' { return 'HD/SD' }
    'uhd'  { return 'UHD' }
    Default { return $LaneName }
  }
}

function Get-CompressarrPresetTree {
  <#
    Reads and caches presets.json's parsed object for a given path within
    this process, since the file can be large and is read once per preset
    lookup otherwise. Call Clear-CompressarrPresetCache if the underlying
    file changes during a run (e.g. user edits it while the GUI is open).
  #>
  param(
    [Parameter(Mandatory)] [string]$PresetsPath
  )

  $resolvedPath = Expand-CompressarrPath $PresetsPath

  if (-not $script:PresetTreeCache) { $script:PresetTreeCache = @{} }
  if ($script:PresetTreeCache.ContainsKey($resolvedPath)) {
    return $script:PresetTreeCache[$resolvedPath]
  }

  if (-not (Test-Path $resolvedPath)) {
    throw "Compressarr: HandBrake presets file not found at '$resolvedPath'."
  }

  $tree = Get-Content -Path $resolvedPath -Raw | ConvertFrom-Json
  $script:PresetTreeCache[$resolvedPath] = $tree
  return $tree
}

function Clear-CompressarrPresetCache {
  $script:PresetTreeCache = @{}
}

function Get-CompressarrPresetObjects {
  <#
    Recursively walks presets.json's PresetList/ChildrenArray tree
    (HandBrake nests presets under folder groupings) and returns every leaf
    preset object found, each of which carries at least PresetName and
    FileFormat.
  #>
  param(
    [Parameter(Mandatory)] [string]$PresetsPath
  )

  $tree = Get-CompressarrPresetTree -PresetsPath $PresetsPath
  $results = New-Object System.Collections.Generic.List[object]

  function Walk-Node($node) {
    if ($null -eq $node) { return }
    if ($node -is [System.Collections.IEnumerable] -and -not ($node -is [string]) -and -not ($node -is [System.Management.Automation.PSCustomObject])) {
      foreach ($child in $node) { Walk-Node $child }
      return
    }
    if ($node -is [System.Management.Automation.PSCustomObject]) {
      $propNames = $node.PSObject.Properties.Name
      if ($propNames -contains 'PresetName') {
        $results.Add($node)
      }
      if ($propNames -contains 'ChildrenArray') {
        Walk-Node $node.ChildrenArray
      }
    }
  }

  if ($tree.PSObject.Properties.Name -contains 'PresetList') {
    Walk-Node $tree.PresetList
  }

  # Comma operator required: PowerShell unrolls a returned collection onto
  # the pipeline, so an empty (or single-item) List[object] would otherwise
  # come back to the caller as $null (or a bare scalar) instead of a list.
  return ,$results
}

function Get-CompressarrPresetNames {
  param(
    [Parameter(Mandatory)] [string]$PresetsPath
  )

  $names = New-Object System.Collections.Generic.List[object]
  foreach ($n in (Get-CompressarrPresetObjects -PresetsPath $PresetsPath | ForEach-Object { $_.PresetName } | Sort-Object)) {
    $names.Add($n)
  }
  return ,$names
}

function Test-CompressarrPresetExists {
  param(
    [Parameter(Mandatory)] [string]$PresetName,
    [Parameter(Mandatory)] [string]$PresetsPath
  )

  if ([string]::IsNullOrWhiteSpace($PresetName)) { return $false }
  $names = Get-CompressarrPresetNames -PresetsPath $PresetsPath
  return $names -contains $PresetName
}

function Get-CompressarrPresetObject {
  <# Returns the raw preset object (with FileFormat, etc.) for a given name, or $null. #>
  param(
    [Parameter(Mandatory)] [string]$PresetName,
    [Parameter(Mandatory)] [string]$PresetsPath
  )

  $all = Get-CompressarrPresetObjects -PresetsPath $PresetsPath
  return ($all | Where-Object { $_.PresetName -eq $PresetName } | Select-Object -First 1)
}

Export-ModuleMember -Function `
  Get-CompressarrDefaultConfig, `
  Merge-CompressarrConfigObject, `
  Import-CompressarrConfig, `
  Export-CompressarrConfig, `
  Expand-CompressarrPath, `
  Test-CompressarrPath, `
  Get-CompressarrLaneNames, `
  Get-CompressarrLaneDisplayName, `
  Get-CompressarrPresetTree, `
  Clear-CompressarrPresetCache, `
  Get-CompressarrPresetObjects, `
  Get-CompressarrPresetNames, `
  Test-CompressarrPresetExists, `
  Get-CompressarrPresetObject
