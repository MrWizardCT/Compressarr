#Requires -Version 5.1

<#
  Compressarr.UI.psm1

  WinForms GUI, laid out with TableLayoutPanel + Dock/Anchor so the window
  resizes cleanly - unlike Paul's displayForm, which positions every control
  with a hardcoded System.Drawing.Point. One "General" tab for settings that
  aren't per-lane, plus one "Paths" tab holding both lanes (HD/SD, UHD) on
  the same page, separated by a section header + rule. Each lane has Input,
  Output, TV Preset, Movie Preset, TV Show Base Path, and Movie Base Path -
  TV-vs-Movie is auto-detected per file (Paul's original approach), not a
  separate lane. No SMTP/email fields anywhere - post-run output is the
  Reporting module's HTML report, not an emailed log.
#>

function Get-CompressarrAssetsPath {
  return (Join-Path -Path $PSScriptRoot -ChildPath '..\Assets')
}

function New-CompressarrFormPanel {
  $panel = New-Object System.Windows.Forms.TableLayoutPanel
  $panel.Dock = 'Fill'
  $panel.AutoScroll = $true
  $panel.ColumnCount = 3
  [void]$panel.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Absolute, 230)))
  [void]$panel.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 100)))
  [void]$panel.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Absolute, 90)))
  $panel.RowCount = 0
  $panel.Padding = New-Object System.Windows.Forms.Padding(14)
  return $panel
}

function Add-CompressarrFillerRow {
  <#
    A trailing zero-content row with Percent(100) sizing, so any leftover
    vertical space in the panel is absorbed here instead of stretching the
    last real row's controls (a TableLayoutPanel quirk when total Absolute
    row heights add up to less than the panel's actual height).
  #>
  param($Panel, [ref]$Row)
  $Panel.RowCount = $Row.Value + 1
  [void]$Panel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100)))
  $Row.Value++
}

function Add-CompressarrSectionHeader {
  <# A bold section title spanning the full row width, e.g. "HD/SD" / "UHD". #>
  param($Panel, [ref]$Row, [string]$Text)
  $Panel.RowCount = $Row.Value + 1
  [void]$Panel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 34)))
  $label = New-Object System.Windows.Forms.Label
  $label.Text = $Text
  $label.Dock = 'Fill'
  $label.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
  $label.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
  $Panel.Controls.Add($label, 0, $Row.Value)
  $Panel.SetColumnSpan($label, 3)
  $Row.Value++
}

function Add-CompressarrSeparator {
  <# A thin horizontal rule spanning the full row width, between sections. #>
  param($Panel, [ref]$Row)
  $Panel.RowCount = $Row.Value + 1
  [void]$Panel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 20)))
  $line = New-Object System.Windows.Forms.Panel
  $line.Height = 2
  $line.Dock = 'Top'
  $line.Margin = New-Object System.Windows.Forms.Padding(0, 9, 0, 9)
  $line.BackColor = [System.Drawing.Color]::Gainsboro
  $Panel.Controls.Add($line, 0, $Row.Value)
  $Panel.SetColumnSpan($line, 3)
  $Row.Value++
}

function Add-CompressarrRowLabel {
  param($Panel, [ref]$Row, [string]$LabelText)
  $Panel.RowCount = $Row.Value + 1
  [void]$Panel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 36)))
  $label = New-Object System.Windows.Forms.Label
  $label.Text = $LabelText
  $label.Dock = 'Fill'
  $label.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
  $Panel.Controls.Add($label, 0, $Row.Value)
}

function Add-CompressarrTextRow {
  param($Panel, [ref]$Row, [string]$Label, $Value)
  Add-CompressarrRowLabel -Panel $Panel -Row $Row -LabelText $Label
  $box = New-Object System.Windows.Forms.TextBox
  $box.Text = [string]$Value
  $box.Dock = 'Fill'
  $box.Anchor = [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
  $Panel.Controls.Add($box, 1, $Row.Value)
  $Panel.SetColumnSpan($box, 2)
  $Row.Value++
  return $box
}

function Add-CompressarrCheckRow {
  param($Panel, [ref]$Row, [string]$Label, [bool]$Value)
  Add-CompressarrRowLabel -Panel $Panel -Row $Row -LabelText $Label
  $check = New-Object System.Windows.Forms.CheckBox
  $check.Checked = $Value
  $check.Dock = 'Fill'
  $Panel.Controls.Add($check, 1, $Row.Value)
  $Panel.SetColumnSpan($check, 2)
  $Row.Value++
  return $check
}

function Add-CompressarrComboRow {
  param($Panel, [ref]$Row, [string]$Label, [string[]]$Items = @(), $Value, [switch]$Editable)
  Add-CompressarrRowLabel -Panel $Panel -Row $Row -LabelText $Label
  $combo = New-Object System.Windows.Forms.ComboBox
  $combo.Dock = 'Fill'
  $combo.Anchor = [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
  $combo.DropDownStyle = if ($Editable) { [System.Windows.Forms.ComboBoxStyle]::DropDown } else { [System.Windows.Forms.ComboBoxStyle]::DropDownList }
  if ($Items -and $Items.Count -gt 0) { [void]$combo.Items.AddRange($Items) }
  $combo.Text = [string]$Value
  $Panel.Controls.Add($combo, 1, $Row.Value)
  $Panel.SetColumnSpan($combo, 2)
  $Row.Value++
  return $combo
}

function Add-CompressarrPathRow {
  param($Panel, [ref]$Row, [string]$Label, $Value, [ValidateSet('File', 'Folder')] [string]$Browse = 'Folder')
  Add-CompressarrRowLabel -Panel $Panel -Row $Row -LabelText $Label
  $box = New-Object System.Windows.Forms.TextBox
  $box.Text = [string]$Value
  $box.Dock = 'Fill'
  $box.Anchor = [System.Windows.Forms.AnchorStyles]::Left -bor [System.Windows.Forms.AnchorStyles]::Right
  $Panel.Controls.Add($box, 1, $Row.Value)

  $browseBtn = New-Object System.Windows.Forms.Button
  $browseBtn.Text = '...'
  $browseBtn.Dock = 'Fill'
  $Panel.Controls.Add($browseBtn, 2, $Row.Value)

  $browseBtn.Add_Click({
    # Test-CompressarrPath (not the raw Test-Path cmdlet) because Test-Path's
    # -Path parameter is mandatory and throws on an empty string - and every
    # one of these fields starts out empty until the user fills it in.
    if ($Browse -eq 'Folder') {
      $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
      if (Test-CompressarrPath $box.Text) { $dlg.SelectedPath = $box.Text }
      if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $box.Text = $dlg.SelectedPath }
    }
    else {
      $dlg = New-Object System.Windows.Forms.OpenFileDialog
      if (Test-CompressarrPath $box.Text) {
        $dlg.InitialDirectory = Split-Path -Path $box.Text -Parent
        $dlg.FileName = Split-Path -Path $box.Text -Leaf
      }
      if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $box.Text = $dlg.FileName }
    }
  }.GetNewClosure())

  $Row.Value++
  return $box
}

function Show-CompressarrMainForm {
  <#
    Displays the main Compressarr window. Returns a hashtable:
      @{ Action = 'Execute' | 'Exit'; Config = <updated config object> }
    'Execute' means the user wants to run a conversion pass with the config
    as currently shown (whether or not they also clicked Save first).
  #>
  param(
    [Parameter(Mandatory)] $Config,
    [Parameter(Mandatory)] [string]$ConfigPath,
    [string]$Version
  )

  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing
  [System.Windows.Forms.Application]::EnableVisualStyles()

  $formResult = @{ Action = 'Exit'; Config = $Config }

  $assetsPath = Get-CompressarrAssetsPath
  $iconPath = Join-Path -Path $assetsPath -ChildPath 'compressarr.ico'
  $logoPath = Join-Path -Path $assetsPath -ChildPath 'compressarr-logo.png'

  $form = New-Object System.Windows.Forms.Form
  $form.Text = if ($Version) { "Compressarr v$Version" } else { 'Compressarr' }
  $form.MinimumSize = New-Object System.Drawing.Size(880, 680)
  $form.Size = New-Object System.Drawing.Size(1000, 800)
  $form.StartPosition = 'CenterScreen'
  if (Test-Path $iconPath) { $form.Icon = New-Object System.Drawing.Icon($iconPath) }

  $root = New-Object System.Windows.Forms.TableLayoutPanel
  $root.Dock = 'Fill'
  $root.RowCount = 3
  $root.ColumnCount = 1
  [void]$root.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 64)))
  [void]$root.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100)))
  [void]$root.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 60)))
  $form.Controls.Add($root)

  # ---- Header: logo + title ----
  $headerPanel = New-Object System.Windows.Forms.FlowLayoutPanel
  $headerPanel.Dock = 'Fill'
  $headerPanel.FlowDirection = [System.Windows.Forms.FlowDirection]::LeftToRight
  $headerPanel.Padding = New-Object System.Windows.Forms.Padding(10, 8, 0, 0)
  $root.Controls.Add($headerPanel, 0, 0)

  if (Test-Path $logoPath) {
    $logoBox = New-Object System.Windows.Forms.PictureBox
    $logoBox.Image = [System.Drawing.Image]::FromFile($logoPath)
    $logoBox.Size = New-Object System.Drawing.Size(48, 48)
    $logoBox.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $logoBox.Margin = New-Object System.Windows.Forms.Padding(0, 0, 10, 0)
    $headerPanel.Controls.Add($logoBox)
  }

  $titleLabel = New-Object System.Windows.Forms.Label
  $titleLabel.Text = if ($Version) { "Compressarr  v$Version" } else { 'Compressarr' }
  $titleLabel.AutoSize = $true
  $titleLabel.Font = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Bold)
  $titleLabel.Margin = New-Object System.Windows.Forms.Padding(0, 8, 0, 0)
  $headerPanel.Controls.Add($titleLabel)

  $tabs = New-Object System.Windows.Forms.TabControl
  $tabs.Dock = 'Fill'
  $root.Controls.Add($tabs, 0, 1)

  $pathFields = @{}
  $presetFields = @{}

  # ---- General tab ----
  $generalTab = New-Object System.Windows.Forms.TabPage
  $generalTab.Text = 'General'
  $tabs.TabPages.Add($generalTab)
  $generalPanel = New-CompressarrFormPanel
  $generalTab.Controls.Add($generalPanel)

  $row = 0
  $hbCliBox         = Add-CompressarrPathRow  -Panel $generalPanel -Row ([ref]$row) -Label 'HandBrakeCLI.exe location'          -Value $Config.handbrake.cliPath -Browse File
  $hbPresetsBox     = Add-CompressarrPathRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Presets file (presets.json)'        -Value $Config.handbrake.presetsPath -Browse File
  $hbOptsBox        = Add-CompressarrTextRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Extra HandBrake options'            -Value $Config.handbrake.options
  $logPathBox       = Add-CompressarrPathRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Log folder'                        -Value $Config.logging.logFilePath -Browse Folder
  $retentionBox     = Add-CompressarrTextRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Log retention (days)'               -Value $Config.logging.retentionDays
  $reportPathBox    = Add-CompressarrPathRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Report folder'                     -Value $Config.report.reportPath -Browse Folder
  $openAfterRunCombo = Add-CompressarrComboRow -Panel $generalPanel -Row ([ref]$row) -Label 'Open report after run' -Items @('Always', 'Error', 'Never') -Value $Config.report.openAfterRun
  $vidTypesBox      = Add-CompressarrTextRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Video file types (comma-separated)' -Value ($Config.processing.vidTypes -join ',')
  $limitBox         = Add-CompressarrTextRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Max files per run'                  -Value $Config.processing.limit
  $minSizeBox       = Add-CompressarrTextRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Minimum file size (e.g. 100mb)'      -Value $Config.processing.minSize
  $outSameAsInCheck = Add-CompressarrCheckRow -Panel $generalPanel -Row ([ref]$row) -Label 'Write output to same folder as input' -Value ([bool]$Config.processing.outSameAsIn)
  $moveFilesCheck   = Add-CompressarrCheckRow -Panel $generalPanel -Row ([ref]$row) -Label 'Move converted files into show/movie folders' -Value ([bool]$Config.processing.moveFiles)
  $deleteCombo      = Add-CompressarrComboRow -Panel $generalPanel -Row ([ref]$row) -Label 'Original file after conversion' -Items @('Maintain', 'Delete', 'Recycle') -Value $Config.processing.deleteAfterConvert
  $postExecCmdBox   = Add-CompressarrPathRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Post-execution command (optional)'  -Value $Config.postExec.cmd -Browse File
  $postExecArgsBox  = Add-CompressarrTextRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Post-execution arguments'           -Value $Config.postExec.args
  $repeatCountBox   = Add-CompressarrTextRow  -Panel $generalPanel -Row ([ref]$row) -Label 'Repeat run count'                   -Value $Config.repeat.count
  $monitorCheck     = Add-CompressarrCheckRow -Panel $generalPanel -Row ([ref]$row) -Label 'Monitor mode (keep watching for new files)' -Value ([bool]$Config.repeat.monitor)
  Add-CompressarrFillerRow -Panel $generalPanel -Row ([ref]$row)

  $pathFields['handbrake.cliPath'] = $hbCliBox
  $pathFields['handbrake.presetsPath'] = $hbPresetsBox
  $pathFields['logging.logFilePath'] = $logPathBox
  $pathFields['report.reportPath'] = $reportPathBox

  # ---- Paths tab (both lanes, one page, separated by a rule) ----
  # Each lane auto-detects TV vs Movie per file (Test-CompressarrIsTVFile),
  # so every lane needs its own TV and Movie preset, plus separate
  # destination base paths used when "move files" relocates a converted
  # file into a Show/Movie folder structure.
  $pathsTab = New-Object System.Windows.Forms.TabPage
  $pathsTab.Text = 'Paths'
  $tabs.TabPages.Add($pathsTab)
  $pathsPanel = New-CompressarrFormPanel
  $pathsTab.Controls.Add($pathsPanel)

  $laneControls = @{}
  $prow = 0
  $laneNames = Get-CompressarrLaneNames
  for ($laneIndex = 0; $laneIndex -lt $laneNames.Count; $laneIndex++) {
    $laneName = $laneNames[$laneIndex]
    if ($laneIndex -gt 0) { Add-CompressarrSeparator -Panel $pathsPanel -Row ([ref]$prow) }
    Add-CompressarrSectionHeader -Panel $pathsPanel -Row ([ref]$prow) -Text (Get-CompressarrLaneDisplayName -LaneName $laneName)

    $laneConfig = $Config.contentLanes.$laneName
    $inputBox          = Add-CompressarrPathRow  -Panel $pathsPanel -Row ([ref]$prow) -Label 'Input folder' -Value $laneConfig.input -Browse Folder
    $outputBox         = Add-CompressarrPathRow  -Panel $pathsPanel -Row ([ref]$prow) -Label 'Output folder' -Value $laneConfig.output -Browse Folder
    $tvPresetCombo     = Add-CompressarrComboRow -Panel $pathsPanel -Row ([ref]$prow) -Label 'TV Show preset' -Items @() -Value $laneConfig.tvPreset -Editable
    $moviePresetCombo  = Add-CompressarrComboRow -Panel $pathsPanel -Row ([ref]$prow) -Label 'Movie preset' -Items @() -Value $laneConfig.moviePreset -Editable
    $tvShowBasePathBox = Add-CompressarrPathRow  -Panel $pathsPanel -Row ([ref]$prow) -Label 'TV Show base path (move to)' -Value $laneConfig.tvShowBasePath -Browse Folder
    $movieBasePathBox  = Add-CompressarrPathRow  -Panel $pathsPanel -Row ([ref]$prow) -Label 'Movie base path (move to)' -Value $laneConfig.movieBasePath -Browse Folder

    $pathFields["contentLanes.$laneName.input"] = $inputBox
    $pathFields["contentLanes.$laneName.output"] = $outputBox
    $pathFields["contentLanes.$laneName.tvShowBasePath"] = $tvShowBasePathBox
    $pathFields["contentLanes.$laneName.movieBasePath"] = $movieBasePathBox
    $presetFields["$laneName.tv"] = $tvPresetCombo
    $presetFields["$laneName.movie"] = $moviePresetCombo

    $laneControls[$laneName] = [PSCustomObject]@{
      Input          = $inputBox
      Output         = $outputBox
      TVPreset       = $tvPresetCombo
      MoviePreset    = $moviePresetCombo
      TVShowBasePath = $tvShowBasePathBox
      MovieBasePath  = $movieBasePathBox
    }
  }
  Add-CompressarrFillerRow -Panel $pathsPanel -Row ([ref]$prow)

  # ---- Preset dropdown population ----
  $refreshPresets = {
    try {
      Clear-CompressarrPresetCache
      $names = @(Get-CompressarrPresetNames -PresetsPath $hbPresetsBox.Text)
      foreach ($combo in $presetFields.Values) {
        $current = $combo.Text
        $combo.Items.Clear()
        if ($names.Count -gt 0) { [void]$combo.Items.AddRange($names) }
        $combo.Text = $current
      }
    }
    catch { }
  }.GetNewClosure()
  & $refreshPresets

  # ---- Validation ----
  $statusLabel = New-Object System.Windows.Forms.Label
  $statusLabel.AutoSize = $true
  $statusLabel.ForeColor = [System.Drawing.Color]::Firebrick
  $statusLabel.Text = ''
  $statusLabel.Anchor = [System.Windows.Forms.AnchorStyles]::Left

  $validateAll = {
    $allValid = $true
    foreach ($tb in $pathFields.Values) {
      if (Test-CompressarrPath $tb.Text) {
        $tb.BackColor = [System.Drawing.Color]::White
        $tb.ForeColor = [System.Drawing.Color]::Black
      }
      else {
        $tb.BackColor = [System.Drawing.Color]::LightYellow
        $tb.ForeColor = [System.Drawing.Color]::Firebrick
        $allValid = $false
      }
    }
    foreach ($combo in $presetFields.Values) {
      $presetOk = $false
      try { $presetOk = Test-CompressarrPresetExists -PresetName $combo.Text -PresetsPath $hbPresetsBox.Text } catch { $presetOk = $false }
      if ($presetOk) {
        $combo.BackColor = [System.Drawing.Color]::White
        $combo.ForeColor = [System.Drawing.Color]::Black
      }
      else {
        $combo.BackColor = [System.Drawing.Color]::LightYellow
        $combo.ForeColor = [System.Drawing.Color]::Firebrick
        $allValid = $false
      }
    }
    $statusLabel.Text = if ($allValid) { '' } else { 'Some fields need attention (highlighted).' }
    return $allValid
  }.GetNewClosure()

  foreach ($tb in $pathFields.Values) { $tb.Add_Leave({ & $validateAll | Out-Null }.GetNewClosure()) }
  $hbPresetsBox.Add_Leave({ & $refreshPresets; & $validateAll | Out-Null }.GetNewClosure())

  # ---- Build a config object from current form state ----
  $buildConfigFromForm = {
    $newConfig = $Config | ConvertTo-Json -Depth 10 | ConvertFrom-Json

    $newConfig.handbrake.cliPath = $hbCliBox.Text
    $newConfig.handbrake.presetsPath = $hbPresetsBox.Text
    $newConfig.handbrake.options = $hbOptsBox.Text

    $newConfig.logging.logFilePath = $logPathBox.Text
    $newConfig.logging.retentionDays = [int]($retentionBox.Text)

    $newConfig.report.reportPath = $reportPathBox.Text
    $newConfig.report.openAfterRun = $openAfterRunCombo.Text

    $newConfig.processing.vidTypes = @($vidTypesBox.Text -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $newConfig.processing.limit = [int]($limitBox.Text)
    $newConfig.processing.minSize = $minSizeBox.Text
    $newConfig.processing.outSameAsIn = $outSameAsInCheck.Checked
    $newConfig.processing.moveFiles = $moveFilesCheck.Checked
    $newConfig.processing.deleteAfterConvert = $deleteCombo.Text

    $newConfig.postExec.cmd = $postExecCmdBox.Text
    $newConfig.postExec.args = $postExecArgsBox.Text

    $newConfig.repeat.count = [int]($repeatCountBox.Text)
    $newConfig.repeat.monitor = $monitorCheck.Checked

    foreach ($laneName in (Get-CompressarrLaneNames)) {
      $lc = $laneControls[$laneName]
      $newConfig.contentLanes.$laneName.input = $lc.Input.Text
      $newConfig.contentLanes.$laneName.output = $lc.Output.Text
      $newConfig.contentLanes.$laneName.tvPreset = $lc.TVPreset.Text
      $newConfig.contentLanes.$laneName.moviePreset = $lc.MoviePreset.Text
      $newConfig.contentLanes.$laneName.tvShowBasePath = $lc.TVShowBasePath.Text
      $newConfig.contentLanes.$laneName.movieBasePath = $lc.MovieBasePath.Text
    }

    return $newConfig
  }.GetNewClosure()

  # ---- Bottom button bar ----
  $buttonPanel = New-Object System.Windows.Forms.FlowLayoutPanel
  $buttonPanel.Dock = 'Fill'
  $buttonPanel.FlowDirection = [System.Windows.Forms.FlowDirection]::RightToLeft
  $buttonPanel.Padding = New-Object System.Windows.Forms.Padding(10)
  $root.Controls.Add($buttonPanel, 0, 2)

  $exitBtn = New-Object System.Windows.Forms.Button
  $exitBtn.Text = 'Exit'
  $exitBtn.AutoSize = $true
  $exitBtn.Add_Click({ $formResult.Action = 'Exit'; $form.Close() }.GetNewClosure())

  $executeBtn = New-Object System.Windows.Forms.Button
  $executeBtn.Text = 'Execute'
  $executeBtn.AutoSize = $true
  $executeBtn.Add_Click({
    if (-not (& $validateAll)) {
      [System.Windows.Forms.MessageBox]::Show('Please correct the highlighted fields before executing.', 'Compressarr', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
      return
    }
    $formResult.Config = (& $buildConfigFromForm)
    $formResult.Action = 'Execute'
    $form.Close()
  }.GetNewClosure())

  $saveBtn = New-Object System.Windows.Forms.Button
  $saveBtn.Text = 'Save Config'
  $saveBtn.AutoSize = $true
  $saveBtn.Add_Click({
    $cfg = (& $buildConfigFromForm)
    Export-CompressarrConfig -Config $cfg -Path $ConfigPath
    $statusLabel.Text = "Saved to $ConfigPath"
    $statusLabel.ForeColor = [System.Drawing.Color]::DarkGreen
  }.GetNewClosure())

  $buttonPanel.Controls.Add($exitBtn)
  $buttonPanel.Controls.Add($executeBtn)
  $buttonPanel.Controls.Add($saveBtn)
  $buttonPanel.Controls.Add($statusLabel)

  $form.AcceptButton = $executeBtn
  & $validateAll | Out-Null

  [void]$form.ShowDialog()
  $form.Dispose()

  return $formResult
}

Export-ModuleMember -Function Show-CompressarrMainForm, Get-CompressarrAssetsPath
