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
  $label.UseMnemonic = $false
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
  # UseMnemonic = $false so a literal "&" in label text (e.g. "Log &
  # Reports Retention (Days)") displays as-is, instead of WinForms
  # treating it as an accelerator-key prefix and swallowing it - the
  # character right after "&" gets underlined/hidden from display rather
  # than the "&" itself appearing, which is what "Log Reports Retention
  # (Days)" (missing its "&") turned out to be.
  $label.UseMnemonic = $false
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

function New-CompressarrCompactTextBox {
  param($Value, [int]$Width = 90)
  $box = New-Object System.Windows.Forms.TextBox
  $box.Text = [string]$Value
  $box.Width = $Width
  return $box
}

function New-CompressarrCompactComboBox {
  param([string[]]$Items = @(), $Value, [int]$Width = 130)
  $combo = New-Object System.Windows.Forms.ComboBox
  $combo.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
  if ($Items -and $Items.Count -gt 0) { [void]$combo.Items.AddRange($Items) }
  $combo.Text = [string]$Value
  $combo.Width = $Width
  return $combo
}

function Add-CompressarrDualRow {
  <#
    Packs two independent label+control pairs onto one row, each control
    kept at its own natural/explicit size rather than stretched - for
    compact fields (small numbers, short dropdowns) that don't need the
    full row width Add-CompressarrTextRow/ComboRow give a single field.
  #>
  param($Panel, [ref]$Row, [string]$Label1, $Control1, [string]$Label2, $Control2)

  $Panel.RowCount = $Row.Value + 1
  [void]$Panel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 36)))

  $inner = New-Object System.Windows.Forms.TableLayoutPanel
  $inner.Dock = 'Fill'
  $inner.ColumnCount = 5
  [void]$inner.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize)))
  [void]$inner.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize)))
  [void]$inner.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize)))
  [void]$inner.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize)))
  [void]$inner.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 100)))
  $inner.RowCount = 1
  [void]$inner.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100)))

  $lbl1 = New-Object System.Windows.Forms.Label
  $lbl1.Text = $Label1
  # See Add-CompressarrRowLabel for why UseMnemonic is disabled - a literal
  # "&" in label text (e.g. "Log & Reports Retention (Days)") would
  # otherwise be swallowed as an accelerator-key prefix instead of shown.
  $lbl1.UseMnemonic = $false
  $lbl1.AutoSize = $true
  $lbl1.Anchor = [System.Windows.Forms.AnchorStyles]::Left
  $lbl1.Margin = New-Object System.Windows.Forms.Padding(0, 7, 8, 0)
  $inner.Controls.Add($lbl1, 0, 0)

  $Control1.Margin = New-Object System.Windows.Forms.Padding(0, 3, 28, 3)
  $Control1.Anchor = [System.Windows.Forms.AnchorStyles]::Left
  $inner.Controls.Add($Control1, 1, 0)

  $lbl2 = New-Object System.Windows.Forms.Label
  $lbl2.Text = $Label2
  $lbl2.UseMnemonic = $false
  $lbl2.AutoSize = $true
  $lbl2.Anchor = [System.Windows.Forms.AnchorStyles]::Left
  $lbl2.Margin = New-Object System.Windows.Forms.Padding(0, 7, 8, 0)
  $inner.Controls.Add($lbl2, 2, 0)

  $Control2.Margin = New-Object System.Windows.Forms.Padding(0, 3, 0, 3)
  $Control2.Anchor = [System.Windows.Forms.AnchorStyles]::Left
  $inner.Controls.Add($Control2, 3, 0)

  $Panel.Controls.Add($inner, 0, $Row.Value)
  $Panel.SetColumnSpan($inner, 3)
  $Row.Value++
}

function Add-CompressarrCheckboxRow {
  <#
    Multiple checkboxes on a single row, each using the checkbox's own
    .Text (no separate label column) - far more compact than one full row
    per checkbox. Returns the CheckBox controls in the same order passed
    in, via $Items = @(@{Label='...'; Value=$true}, ...).
  #>
  param($Panel, [ref]$Row, [array]$Items)

  $Panel.RowCount = $Row.Value + 1
  [void]$Panel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 30)))

  $flow = New-Object System.Windows.Forms.FlowLayoutPanel
  $flow.Dock = 'Fill'
  $flow.FlowDirection = [System.Windows.Forms.FlowDirection]::LeftToRight
  $flow.WrapContents = $true

  $checkBoxes = New-Object System.Collections.Generic.List[object]
  foreach ($item in $Items) {
    $cb = New-Object System.Windows.Forms.CheckBox
    $cb.Text = $item.Label
    $cb.Checked = [bool]$item.Value
    $cb.AutoSize = $true
    $cb.Margin = New-Object System.Windows.Forms.Padding(0, 5, 26, 5)
    $flow.Controls.Add($cb)
    $checkBoxes.Add($cb)
  }

  $Panel.Controls.Add($flow, 0, $Row.Value)
  $Panel.SetColumnSpan($flow, 3)
  $Row.Value++
  return ,$checkBoxes
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
  $hbCliBox      = Add-CompressarrPathRow -Panel $generalPanel -Row ([ref]$row) -Label 'HandBrakeCLI.exe location'   -Value $Config.handbrake.cliPath -Browse File
  $hbPresetsBox  = Add-CompressarrPathRow -Panel $generalPanel -Row ([ref]$row) -Label 'Presets file (presets.json)' -Value $Config.handbrake.presetsPath -Browse File
  $hbOptsBox     = Add-CompressarrTextRow -Panel $generalPanel -Row ([ref]$row) -Label 'Extra HandBrake options'     -Value $Config.handbrake.options

  $logPathBox    = Add-CompressarrPathRow -Panel $generalPanel -Row ([ref]$row) -Label 'Log folder'    -Value $Config.logging.logFilePath -Browse Folder
  $reportPathBox = Add-CompressarrPathRow -Panel $generalPanel -Row ([ref]$row) -Label 'Report folder' -Value $Config.report.reportPath -Browse Folder

  $retentionBox = New-CompressarrCompactTextBox -Value $Config.logging.retentionDays -Width 60
  $openAfterRunCombo = New-CompressarrCompactComboBox -Items @('Always', 'Error', 'Never') -Value $Config.report.openAfterRun -Width 110
  Add-CompressarrDualRow -Panel $generalPanel -Row ([ref]$row) -Label1 'Log & Reports Retention (Days)' -Control1 $retentionBox -Label2 'Open report after run' -Control2 $openAfterRunCombo

  $vidTypesBox = Add-CompressarrTextRow -Panel $generalPanel -Row ([ref]$row) -Label 'Video file types (comma-separated)' -Value ($Config.processing.vidTypes -join ',')

  $limitBox = New-CompressarrCompactTextBox -Value $Config.processing.limit -Width 70
  $minSizeBox = New-CompressarrCompactTextBox -Value $Config.processing.minSize -Width 90
  Add-CompressarrDualRow -Panel $generalPanel -Row ([ref]$row) -Label1 'Max files per run' -Control1 $limitBox -Label2 'Minimum file size (e.g. 100mb)' -Control2 $minSizeBox

  $generalChecks = Add-CompressarrCheckboxRow -Panel $generalPanel -Row ([ref]$row) -Items @(
    @{ Label = 'Write output to same folder as input'; Value = [bool]$Config.processing.outSameAsIn },
    @{ Label = 'Move converted files into show/movie folders'; Value = [bool]$Config.processing.moveFiles },
    @{ Label = 'Monitor mode (keep watching for new files)'; Value = [bool]$Config.repeat.monitor }
  )
  $outSameAsInCheck = $generalChecks[0]
  $moveFilesCheck = $generalChecks[1]
  $monitorCheck = $generalChecks[2]

  $deleteCombo = Add-CompressarrComboRow -Panel $generalPanel -Row ([ref]$row) -Label 'Original file after conversion' -Items @('Maintain', 'Delete', 'Recycle') -Value $Config.processing.deleteAfterConvert

  $repeatCountBox = New-CompressarrCompactTextBox -Value $Config.repeat.count -Width 60
  $countdownBox = New-CompressarrCompactTextBox -Value $Config.startup.countdownSeconds -Width 60
  Add-CompressarrDualRow -Panel $generalPanel -Row ([ref]$row) -Label1 'Repeat run count' -Control1 $repeatCountBox -Label2 'Change Settings countdown (seconds)' -Control2 $countdownBox

  $postExecCmdBox = Add-CompressarrPathRow -Panel $generalPanel -Row ([ref]$row) -Label 'Post-execution command (optional)' -Value $Config.postExec.cmd -Browse File
  $postExecArgsBox = Add-CompressarrTextRow -Panel $generalPanel -Row ([ref]$row) -Label 'Post-execution arguments' -Value $Config.postExec.args

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
      # NOT @(...) here - Get-CompressarrPresetNames already returns a
      # single preserved List[object] (comma-operator return, so it
      # doesn't unroll to $null when empty - see Compressarr.Config.psm1).
      # Wrapping that in @() again produces a 1-element array whose only
      # element IS the list, so AddRange added one "item" to the combo
      # box whose displayed text was every preset name space-joined onto
      # a single line. .ToArray() gives AddRange the real string[] it needs.
      $names = (Get-CompressarrPresetNames -PresetsPath $hbPresetsBox.Text).ToArray()
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

    $newConfig.startup.countdownSeconds = [int]($countdownBox.Text)

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

function Show-CompressarrCountdownForm {
  <#
    Shown at startup instead of the full config screen once Compressarr
    has run before (run count > 0) - a small splash with the logo and a
    countdown; "Change Settings" opens the real config screen, and if the
    countdown reaches zero with no action, the caller proceeds to execute
    using the config already on disk. Returns @{ Action = 'ChangeSettings' | 'Proceed' }.
  #>
  param(
    [Parameter(Mandatory)] $Config,
    [string]$Version
  )

  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing
  [System.Windows.Forms.Application]::EnableVisualStyles()

  $assetsPath = Get-CompressarrAssetsPath
  $iconPath = Join-Path -Path $assetsPath -ChildPath 'compressarr.ico'
  $logoPath = Join-Path -Path $assetsPath -ChildPath 'compressarr-logo.png'

  $seconds = 10
  if ($Config.startup -and $Config.startup.countdownSeconds) { $seconds = [int]$Config.startup.countdownSeconds }
  if ($seconds -le 0) { $seconds = 10 }

  # A hashtable, not a bare int variable: Timer.Tick fires the same
  # scriptblock instance repeatedly, and GetNewClosure() gives each
  # invocation a fresh snapshot of any plain captured variable - a bare
  # `$remaining--` would silently reset to the original value on every
  # tick instead of counting down. Mutating a field on a shared reference
  # object (this hashtable) persists correctly across ticks instead.
  $state = @{ Remaining = $seconds; Action = 'Proceed' }

  $form = New-Object System.Windows.Forms.Form
  $form.Text = if ($Version) { "Compressarr v$Version" } else { 'Compressarr' }
  $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
  $form.MaximizeBox = $false
  $form.MinimizeBox = $false
  $form.StartPosition = 'CenterScreen'
  $form.ClientSize = New-Object System.Drawing.Size(400, 190)
  if (Test-Path $iconPath) { $form.Icon = New-Object System.Drawing.Icon($iconPath) }

  $layout = New-Object System.Windows.Forms.TableLayoutPanel
  $layout.Dock = 'Fill'
  $layout.Padding = New-Object System.Windows.Forms.Padding(20)
  $layout.ColumnCount = 1
  $layout.RowCount = 4
  [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 56)))
  [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 26)))
  [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100)))
  [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 40)))
  $form.Controls.Add($layout)

  $headerFlow = New-Object System.Windows.Forms.FlowLayoutPanel
  $headerFlow.Dock = 'Fill'
  $headerFlow.FlowDirection = [System.Windows.Forms.FlowDirection]::LeftToRight
  $layout.Controls.Add($headerFlow, 0, 0)

  if (Test-Path $logoPath) {
    $logoBox = New-Object System.Windows.Forms.PictureBox
    $logoBox.Image = [System.Drawing.Image]::FromFile($logoPath)
    $logoBox.Size = New-Object System.Drawing.Size(48, 48)
    $logoBox.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $logoBox.Margin = New-Object System.Windows.Forms.Padding(0, 0, 12, 0)
    $headerFlow.Controls.Add($logoBox)
  }

  $titleLabel = New-Object System.Windows.Forms.Label
  $titleLabel.Text = 'Compressarr'
  $titleLabel.AutoSize = $true
  $titleLabel.Font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
  $titleLabel.Margin = New-Object System.Windows.Forms.Padding(0, 8, 0, 0)
  $headerFlow.Controls.Add($titleLabel)

  $countdownLabel = New-Object System.Windows.Forms.Label
  $countdownLabel.Text = "Starting in $($state.Remaining) second(s)..."
  $countdownLabel.AutoSize = $true
  $countdownLabel.Font = New-Object System.Drawing.Font('Segoe UI', 10)
  $layout.Controls.Add($countdownLabel, 0, 1)

  $bodyLabel = New-Object System.Windows.Forms.Label
  $bodyLabel.Text = "Compressarr will run automatically with the current settings.`nClick Change Settings to review or edit them first."
  $bodyLabel.AutoSize = $true
  $bodyLabel.ForeColor = [System.Drawing.Color]::DimGray
  $layout.Controls.Add($bodyLabel, 0, 2)

  $buttonPanel = New-Object System.Windows.Forms.FlowLayoutPanel
  $buttonPanel.Dock = 'Fill'
  $buttonPanel.FlowDirection = [System.Windows.Forms.FlowDirection]::RightToLeft
  $layout.Controls.Add($buttonPanel, 0, 3)

  $changeBtn = New-Object System.Windows.Forms.Button
  $changeBtn.Text = 'Change Settings'
  $changeBtn.AutoSize = $true
  $buttonPanel.Controls.Add($changeBtn)

  $timer = New-Object System.Windows.Forms.Timer
  $timer.Interval = 1000

  $changeBtn.Add_Click({
    $state.Action = 'ChangeSettings'
    $timer.Stop()
    $form.Close()
  }.GetNewClosure())

  $timer.Add_Tick({
    $state.Remaining--
    if ($state.Remaining -le 0) {
      $state.Action = 'Proceed'
      $timer.Stop()
      $form.Close()
    }
    else {
      $countdownLabel.Text = "Starting in $($state.Remaining) second(s)..."
    }
  }.GetNewClosure())

  $form.Add_Shown({ $timer.Start() }.GetNewClosure())
  $form.Add_FormClosing({ $timer.Stop() }.GetNewClosure())

  [void]$form.ShowDialog()
  $timer.Dispose()
  $form.Dispose()

  return @{ Action = $state.Action }
}

function Show-CompressarrResumePromptForm {
  <#
    Shown once at startup, right before the first run of the session,
    when compressarr.resume.json still has files tracked from a previous
    run (killed mid-run, or a file that errored out - see
    Import-CompressarrResumeState). Same look as Show-CompressarrCountdownForm
    (logo, title, countdown), but the message is a red warning and the two
    buttons decide whether to keep those tracked files (pick up where the
    previous run left off) or discard the resume file and let the next scan
    start fresh. Auto-proceeds as "Finish Processing Files" if left
    untouched, so an unattended repeat/monitor-mode launch never blocks here.
    Returns @{ Action = 'ClearCache' | 'Finish' }.
  #>
  param(
    [Parameter(Mandatory)] $Config,
    [string]$Version,
    [Parameter(Mandatory)] [int]$PendingCount
  )

  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing
  [System.Windows.Forms.Application]::EnableVisualStyles()

  $assetsPath = Get-CompressarrAssetsPath
  $iconPath = Join-Path -Path $assetsPath -ChildPath 'compressarr.ico'
  $logoPath = Join-Path -Path $assetsPath -ChildPath 'compressarr-logo.png'

  $seconds = 10
  if ($Config.startup -and $Config.startup.countdownSeconds) { $seconds = [int]$Config.startup.countdownSeconds }
  if ($seconds -le 0) { $seconds = 10 }

  # Same mutable-hashtable pattern as Show-CompressarrCountdownForm - a
  # bare captured scalar resets on every separate Timer.Tick invocation
  # instead of counting down.
  $state = @{ Remaining = $seconds; Action = 'Finish' }
  $fileWord = if ($PendingCount -eq 1) { 'file' } else { 'files' }

  $form = New-Object System.Windows.Forms.Form
  $form.Text = if ($Version) { "Compressarr v$Version" } else { 'Compressarr' }
  $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
  $form.MaximizeBox = $false
  $form.MinimizeBox = $false
  $form.StartPosition = 'CenterScreen'
  # Wider/taller than Show-CompressarrCountdownForm's 400x190: this box
  # carries a longer warning message, an explanatory line, and two buttons
  # side by side (vs. that form's single short line and one button), so it
  # needs the extra room - the labels below wrap within it rather than
  # running off the edge of a narrower fixed-size window.
  $form.ClientSize = New-Object System.Drawing.Size(480, 230)
  if (Test-Path $iconPath) { $form.Icon = New-Object System.Drawing.Icon($iconPath) }

  $layout = New-Object System.Windows.Forms.TableLayoutPanel
  $layout.Dock = 'Fill'
  $layout.Padding = New-Object System.Windows.Forms.Padding(20)
  $layout.ColumnCount = 1
  $layout.RowCount = 4
  [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 56)))
  [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 40)))
  [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100)))
  [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 40)))
  $form.Controls.Add($layout)

  $headerFlow = New-Object System.Windows.Forms.FlowLayoutPanel
  $headerFlow.Dock = 'Fill'
  $headerFlow.FlowDirection = [System.Windows.Forms.FlowDirection]::LeftToRight
  $layout.Controls.Add($headerFlow, 0, 0)

  if (Test-Path $logoPath) {
    $logoBox = New-Object System.Windows.Forms.PictureBox
    $logoBox.Image = [System.Drawing.Image]::FromFile($logoPath)
    $logoBox.Size = New-Object System.Drawing.Size(48, 48)
    $logoBox.SizeMode = [System.Windows.Forms.PictureBoxSizeMode]::Zoom
    $logoBox.Margin = New-Object System.Windows.Forms.Padding(0, 0, 12, 0)
    $headerFlow.Controls.Add($logoBox)
  }

  $titleLabel = New-Object System.Windows.Forms.Label
  $titleLabel.Text = 'Compressarr'
  $titleLabel.AutoSize = $true
  $titleLabel.Font = New-Object System.Drawing.Font('Segoe UI', 14, [System.Drawing.FontStyle]::Bold)
  $titleLabel.Margin = New-Object System.Windows.Forms.Padding(0, 8, 0, 0)
  $headerFlow.Controls.Add($titleLabel)

  # MaximumSize with a 0 height (plus AutoSize) makes a Label wrap within
  # that width and grow vertically as needed, instead of running off the
  # right edge of the fixed-width window.
  $labelMaxSize = New-Object System.Drawing.Size(430, 0)

  $warnLabel = New-Object System.Windows.Forms.Label
  $warnLabel.Text = "$PendingCount $fileWord pending from a previous run - continuing in $($state.Remaining) second(s)..."
  $warnLabel.AutoSize = $true
  $warnLabel.MaximumSize = $labelMaxSize
  $warnLabel.UseMnemonic = $false
  $warnLabel.ForeColor = [System.Drawing.Color]::Red
  $warnLabel.Font = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
  $layout.Controls.Add($warnLabel, 0, 1)

  $bodyLabel = New-Object System.Windows.Forms.Label
  $bodyLabel.Text = "Clear Resume Cache to discard them and start fresh, or Finish Processing Files to pick up where the previous run left off."
  $bodyLabel.AutoSize = $true
  $bodyLabel.MaximumSize = $labelMaxSize
  $bodyLabel.UseMnemonic = $false
  $bodyLabel.ForeColor = [System.Drawing.Color]::DimGray
  $layout.Controls.Add($bodyLabel, 0, 2)

  $buttonPanel = New-Object System.Windows.Forms.FlowLayoutPanel
  $buttonPanel.Dock = 'Fill'
  $buttonPanel.FlowDirection = [System.Windows.Forms.FlowDirection]::RightToLeft
  $layout.Controls.Add($buttonPanel, 0, 3)

  # RightToLeft flow: the first control added lands rightmost (the
  # conventional "primary/default" slot) - Finish Processing Files is the
  # auto-proceed default, so it goes first/rightmost.
  $finishBtn = New-Object System.Windows.Forms.Button
  $finishBtn.Text = 'Finish Processing Files'
  $finishBtn.AutoSize = $true
  $buttonPanel.Controls.Add($finishBtn)

  $clearBtn = New-Object System.Windows.Forms.Button
  $clearBtn.Text = 'Clear Resume Cache'
  $clearBtn.AutoSize = $true
  $buttonPanel.Controls.Add($clearBtn)

  $form.AcceptButton = $finishBtn

  $timer = New-Object System.Windows.Forms.Timer
  $timer.Interval = 1000

  $clearBtn.Add_Click({
    $state.Action = 'ClearCache'
    $timer.Stop()
    $form.Close()
  }.GetNewClosure())

  $finishBtn.Add_Click({
    $state.Action = 'Finish'
    $timer.Stop()
    $form.Close()
  }.GetNewClosure())

  $timer.Add_Tick({
    $state.Remaining--
    if ($state.Remaining -le 0) {
      $state.Action = 'Finish'
      $timer.Stop()
      $form.Close()
    }
    else {
      $warnLabel.Text = "$PendingCount $fileWord pending from a previous run - continuing in $($state.Remaining) second(s)..."
    }
  }.GetNewClosure())

  $form.Add_Shown({ $timer.Start() }.GetNewClosure())
  $form.Add_FormClosing({ $timer.Stop() }.GetNewClosure())

  [void]$form.ShowDialog()
  $timer.Dispose()
  $form.Dispose()

  return @{ Action = $state.Action }
}

Export-ModuleMember -Function Show-CompressarrMainForm, Show-CompressarrCountdownForm, Show-CompressarrResumePromptForm, Get-CompressarrAssetsPath
