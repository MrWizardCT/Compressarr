#Requires -Version 5.1

<#
  Compressarr.UI.psm1

  WinForms GUI, laid out with TableLayoutPanel + Dock/Anchor so the window
  resizes cleanly - unlike Paul's displayForm, which positions every control
  with a hardcoded System.Drawing.Point. One "General" tab for settings that
  aren't per-lane, plus one tab per content lane (HD Movies / HD TV /
  UHD Movies / UHD TV), each with just Input, Output Base Path, and a Preset
  dropdown. No SMTP/email fields anywhere - post-run output is the
  Reporting module's HTML report, not an emailed log.
#>

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
    if ($Browse -eq 'Folder') {
      $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
      if (Test-Path $box.Text) { $dlg.SelectedPath = $box.Text }
      if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $box.Text = $dlg.SelectedPath }
    }
    else {
      $dlg = New-Object System.Windows.Forms.OpenFileDialog
      if (Test-Path $box.Text) {
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
    [Parameter(Mandatory)] [string]$ConfigPath
  )

  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing
  [System.Windows.Forms.Application]::EnableVisualStyles()

  $formResult = @{ Action = 'Exit'; Config = $Config }

  $form = New-Object System.Windows.Forms.Form
  $form.Text = 'Compressarr'
  $form.MinimumSize = New-Object System.Drawing.Size(880, 640)
  $form.Size = New-Object System.Drawing.Size(1000, 760)
  $form.StartPosition = 'CenterScreen'

  $root = New-Object System.Windows.Forms.TableLayoutPanel
  $root.Dock = 'Fill'
  $root.RowCount = 2
  $root.ColumnCount = 1
  [void]$root.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100)))
  [void]$root.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, 60)))
  $form.Controls.Add($root)

  $tabs = New-Object System.Windows.Forms.TabControl
  $tabs.Dock = 'Fill'
  $root.Controls.Add($tabs, 0, 0)

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

  $pathFields['handbrake.cliPath'] = $hbCliBox
  $pathFields['handbrake.presetsPath'] = $hbPresetsBox
  $pathFields['logging.logFilePath'] = $logPathBox
  $pathFields['report.reportPath'] = $reportPathBox

  # ---- Lane tabs ----
  $laneControls = @{}
  foreach ($laneName in (Get-CompressarrLaneNames)) {
    $laneTab = New-Object System.Windows.Forms.TabPage
    $laneTab.Text = Get-CompressarrLaneDisplayName -LaneName $laneName
    $tabs.TabPages.Add($laneTab)
    $lanePanel = New-CompressarrFormPanel
    $laneTab.Controls.Add($lanePanel)

    $laneConfig = $Config.contentLanes.$laneName
    $lrow = 0
    $inputBox   = Add-CompressarrPathRow  -Panel $lanePanel -Row ([ref]$lrow) -Label 'Input folder' -Value $laneConfig.input -Browse Folder
    $outputBox  = Add-CompressarrPathRow  -Panel $lanePanel -Row ([ref]$lrow) -Label 'Output base folder' -Value $laneConfig.outputBase -Browse Folder
    $presetCombo = Add-CompressarrComboRow -Panel $lanePanel -Row ([ref]$lrow) -Label 'HandBrake preset' -Items @() -Value $laneConfig.preset -Editable

    $pathFields["contentLanes.$laneName.input"] = $inputBox
    $pathFields["contentLanes.$laneName.outputBase"] = $outputBox
    $presetFields[$laneName] = $presetCombo

    $laneControls[$laneName] = [PSCustomObject]@{ Input = $inputBox; Output = $outputBox; Preset = $presetCombo }
  }

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
      $newConfig.contentLanes.$laneName.outputBase = $lc.Output.Text
      $newConfig.contentLanes.$laneName.preset = $lc.Preset.Text
    }

    return $newConfig
  }.GetNewClosure()

  # ---- Bottom button bar ----
  $buttonPanel = New-Object System.Windows.Forms.FlowLayoutPanel
  $buttonPanel.Dock = 'Fill'
  $buttonPanel.FlowDirection = [System.Windows.Forms.FlowDirection]::RightToLeft
  $buttonPanel.Padding = New-Object System.Windows.Forms.Padding(10)
  $root.Controls.Add($buttonPanel, 0, 1)

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

Export-ModuleMember -Function Show-CompressarrMainForm
