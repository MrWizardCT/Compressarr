renderNav('settings');

const statusEl = document.getElementById('status');
const presetStatusEl = document.getElementById('presetStatus');
let presetStatusClearTimer = null;

function setStatus(text) {
  statusEl.textContent = text;
}

function setPresetStatus(text, success) {
  clearTimeout(presetStatusClearTimer);
  presetStatusEl.textContent = text;
  presetStatusEl.classList.toggle('success', !!success);

  // Success messages are easy to miss if they just sit there indefinitely next to a button the
  // user might click again - fade them out after a few seconds instead of leaving stale
  // "reloaded"/"installed" text up forever. Errors stay up so they're not missed.
  if (success) {
    presetStatusClearTimer = setTimeout(() => {
      presetStatusEl.textContent = '';
      presetStatusEl.classList.remove('success');
    }, 4000);
  }
}

const MIN_SIZE_UNIT_MULTIPLIERS = { KB: 1024, MB: 1024 * 1024, GB: 1024 * 1024 * 1024 };

// Tracks whatever unit the dropdown last resolved to, so the change handler below can convert
// the displayed number from that unit to the newly-selected one instead of reinterpreting the
// same digits in a different unit (which would silently change the underlying byte value).
let minSizeUnit = 'MB';

function minSizeBytesFromForm() {
  const raw = parseFloat(document.getElementById('minSizeBytes').value) || 0;
  return Math.round(raw * MIN_SIZE_UNIT_MULTIPLIERS[minSizeUnit]);
}

document.getElementById('minSizeUnit').addEventListener('change', e => {
  const bytes = minSizeBytesFromForm();
  minSizeUnit = e.target.value;
  document.getElementById('minSizeBytes').value = bytes / MIN_SIZE_UNIT_MULTIPLIERS[minSizeUnit];
});

function fillForm(dto) {
  document.getElementById('hbCliPath').value = dto.handBrakeCliPath;
  document.getElementById('presetsPath').value = dto.presetsPath;
  document.getElementById('hbOptions').value = dto.handBrakeOptions;
  document.getElementById('vidTypes').value = dto.vidTypes.join(', ');
  minSizeUnit = 'MB';
  document.getElementById('minSizeUnit').value = minSizeUnit;
  document.getElementById('minSizeBytes').value = (dto.minSizeBytes || 0) / MIN_SIZE_UNIT_MULTIPLIERS[minSizeUnit];
  document.getElementById('limit').value = dto.limit;
  document.getElementById('outSameAsIn').checked = dto.outSameAsIn;
  document.getElementById('moveFiles').checked = dto.moveFiles;
  document.getElementById('clearTitleMetadata').checked = dto.clearTitleMetadata;
  document.getElementById('deleteAfterConvert').value = dto.deleteAfterConvert;
  document.getElementById('logFilePath').value = dto.logFilePath;
  document.getElementById('reportPath').value = dto.reportPath;
  document.getElementById('retentionDays').value = dto.retentionDays;
  document.getElementById('openAfterRun').value = dto.openAfterRun;
  document.getElementById('repeatMonitor').checked = dto.repeatMonitor;
  document.getElementById('pollIntervalSeconds').value = dto.pollIntervalSeconds;
  document.getElementById('runAtLogin').checked = dto.runAtLogin;
  document.getElementById('postExecCmd').value = dto.postExecCmd;
  document.getElementById('postExecArgs').value = dto.postExecArgs;
  document.getElementById('sonarrEnabled').checked = dto.sonarr.enabled;
  document.getElementById('sonarrUrl').value = dto.sonarr.url;
  document.getElementById('sonarrApiKey').value = dto.sonarr.apiKey;
  document.getElementById('radarrEnabled').checked = dto.radarr.enabled;
  document.getElementById('radarrUrl').value = dto.radarr.url;
  document.getElementById('radarrApiKey').value = dto.radarr.apiKey;
  document.getElementById('webPort').value = dto.webPort;
}

function readForm() {
  return {
    handBrakeCliPath: document.getElementById('hbCliPath').value,
    presetsPath: document.getElementById('presetsPath').value,
    handBrakeOptions: document.getElementById('hbOptions').value,
    vidTypes: document.getElementById('vidTypes').value.split(',').map(s => s.trim()).filter(Boolean),
    outSameAsIn: document.getElementById('outSameAsIn').checked,
    deleteAfterConvert: document.getElementById('deleteAfterConvert').value,
    moveFiles: document.getElementById('moveFiles').checked,
    clearTitleMetadata: document.getElementById('clearTitleMetadata').checked,
    limit: parseInt(document.getElementById('limit').value, 10) || 0,
    minSizeBytes: minSizeBytesFromForm(),
    logFilePath: document.getElementById('logFilePath').value,
    retentionDays: parseInt(document.getElementById('retentionDays').value, 10) || 0,
    postExecCmd: document.getElementById('postExecCmd').value,
    postExecArgs: document.getElementById('postExecArgs').value,
    reportPath: document.getElementById('reportPath').value,
    openAfterRun: document.getElementById('openAfterRun').value,
    repeatCount: 0,
    repeatMonitor: document.getElementById('repeatMonitor').checked,
    pollIntervalSeconds: parseInt(document.getElementById('pollIntervalSeconds').value, 10) || 60,
    sonarr: {
      enabled: document.getElementById('sonarrEnabled').checked,
      url: document.getElementById('sonarrUrl').value,
      apiKey: document.getElementById('sonarrApiKey').value
    },
    radarr: {
      enabled: document.getElementById('radarrEnabled').checked,
      url: document.getElementById('radarrUrl').value,
      apiKey: document.getElementById('radarrApiKey').value
    },
    webPort: parseInt(document.getElementById('webPort').value, 10) || 1212,
    runAtLogin: document.getElementById('runAtLogin').checked
  };
}

async function loadSettings() {
  const res = await fetch('/api/settings');
  const dto = await res.json();
  fillForm(dto);
  settingsDirty = false;
}

document.getElementById('saveBtn').addEventListener('click', async () => {
  setStatus('Saving...');
  const res = await fetch('/api/settings', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(readForm())
  });
  if (res.ok) {
    setStatus('Settings saved.');
    settingsDirty = false;
  } else {
    setStatus('Failed to save settings.');
  }
});

document.getElementById('clearChangesBtn').addEventListener('click', async () => {
  if (settingsDirty && !confirm('Discard unsaved changes and reload the last saved settings?')) return;
  await loadSettings();
  setStatus('Changes cleared.');
});

// Warn before leaving with unsaved edits - covers tab close/reload and sidebar nav clicks alike,
// since the sidebar's links are plain <a href> navigation (no client-side router intercepting
// them), so both are a real page unload beforeunload actually fires for. importConfigFile is
// excluded - picking a file to import isn't itself an unsaved settings change, and Import already
// has its own confirm() before it does anything.
let settingsDirty = false;
const settingsMain = document.querySelector('main');
for (const eventName of ['input', 'change']) {
  settingsMain.addEventListener(eventName, e => {
    if (e.target.id === 'importConfigFile') return;
    settingsDirty = true;
  });
}
window.addEventListener('beforeunload', e => {
  if (!settingsDirty) return;
  e.preventDefault();
  e.returnValue = '';
});

document.getElementById('runOnceBtn').addEventListener('click', async () => {
  setStatus('Running...');
  const res = await fetch('/api/run/once', { method: 'POST' });
  const body = await res.json();
  if (res.ok) {
    setStatus(`Done: ${body.totalFiles} file(s) processed.`);
  } else {
    setStatus(body.message || 'Run failed.');
  }
});

document.getElementById('checkHandBrakeBtn').addEventListener('click', async () => {
  setStatus('Checking HandBrakeCLI...');
  const statusRes = await fetch('/api/handbrake/status');
  const statusBody = await statusRes.json();
  if (statusBody.exists) {
    setStatus('HandBrakeCLI already found at the configured path.');
    return;
  }

  const releaseRes = await fetch('/api/handbrake/latest-release');
  const release = await releaseRes.json();
  if (!release.available) {
    setStatus('No downloadable HandBrakeCLI build for this platform - on Linux, install it via your package manager or Flatpak.');
    return;
  }

  const confirmed = confirm(
    `Download and install HandBrakeCLI ${release.version}?\n\nFile: ${release.assetName}\nSize: ${release.sizeMb} MB\n\nInstalls into Compressarr's own folder - won't touch any existing HandBrake install.`
  );
  if (!confirmed) return;

  setStatus('Downloading and installing HandBrakeCLI...');
  const installRes = await fetch('/api/handbrake/install', { method: 'POST' });
  const installBody = await installRes.json();
  if (installRes.ok) {
    document.getElementById('hbCliPath').value = installBody.installedPath;
    setStatus(`HandBrakeCLI ${installBody.version} installed.`);
  } else {
    setStatus('HandBrakeCLI install failed.');
  }
});

document.getElementById('installPresetsBtn').addEventListener('click', async () => {
  const statusRes = await fetch('/api/presets/status');
  const statusBody = await statusRes.json();

  let mode = 'fresh';
  if (statusBody.needsMergePrompt) {
    const confirmed = confirm(
      'A presets.json already exists at this path.\n\nMerge Compressarr\'s presets ("Compressarr SD-HD" and "Compressarr UHD AV1") into it? Every other preset already in that file is left untouched.'
    );
    if (!confirmed) return;
    mode = 'merge';
  }

  setPresetStatus('Installing presets...', false);
  const res = await fetch('/api/presets/install', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ mode })
  });
  setPresetStatus(res.ok ? 'Presets installed.' : 'Failed to install presets.', res.ok);
});

document.getElementById('reloadPresetsBtn').addEventListener('click', async () => {
  setPresetStatus('Reloading presets...', false);
  const res = await fetch('/api/presets/reload', { method: 'POST' });
  setPresetStatus(res.ok ? 'Presets reloaded.' : 'Failed to reload presets.', res.ok);
});

async function testArrConnection(service, statusElId) {
  const statusEl = document.getElementById(statusElId);
  statusEl.textContent = 'Testing...';
  statusEl.classList.remove('success');

  const res = await fetch('/api/arr/test', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      enabled: true,
      url: document.getElementById(`${service}Url`).value,
      apiKey: document.getElementById(`${service}ApiKey`).value
    })
  });
  const body = await res.json();
  statusEl.textContent = body.message;
  statusEl.classList.toggle('success', !!body.success);
}

document.getElementById('sonarrTestBtn').addEventListener('click', () => testArrConnection('sonarr', 'sonarrTestStatus'));
document.getElementById('radarrTestBtn').addEventListener('click', () => testArrConnection('radarr', 'radarrTestStatus'));

document.getElementById('exportConfigBtn').addEventListener('click', () => {
  // Content-Disposition: attachment (set by Results.File's fileDownloadName on the server) makes
  // the browser download this instead of navigating to it, so a plain location change is enough
  // - no anchor/blob juggling needed.
  window.location.href = '/api/settings/export';
});

document.getElementById('importConfigBtn').addEventListener('click', () => {
  const fileInput = document.getElementById('importConfigFile');
  const file = fileInput.files[0];
  const backupStatusEl = document.getElementById('backupStatus');
  if (!file) {
    backupStatusEl.textContent = 'Choose a file to import first.';
    backupStatusEl.classList.remove('success');
    return;
  }

  if (!confirm('Import this file? It will replace all current settings and lanes.')) return;

  backupStatusEl.textContent = 'Importing...';
  backupStatusEl.classList.remove('success');

  const reader = new FileReader();
  reader.onload = async () => {
    const res = await fetch('/api/settings/import', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: reader.result
    });
    if (res.ok) {
      backupStatusEl.textContent = 'Config imported.';
      backupStatusEl.classList.add('success');
      fileInput.value = '';
      loadSettings();
    } else {
      const body = await res.json().catch(() => ({}));
      backupStatusEl.textContent = body.message || 'Failed to import config.';
      backupStatusEl.classList.remove('success');
    }
  };
  reader.readAsText(file);
});

async function runMaintenanceAction(url, confirmMessage, successMessage) {
  if (!confirm(confirmMessage)) return;

  const statusEl = document.getElementById('maintenanceStatus');
  statusEl.textContent = 'Working...';
  statusEl.classList.remove('success');

  const res = await fetch(url, { method: 'POST' });
  statusEl.textContent = res.ok ? successMessage : 'Failed - see the recent log for details.';
  statusEl.classList.toggle('success', res.ok);

  // Every maintenance action only ever writes a file - reloading the form (which re-fetches
  // /api/settings) is how the effect actually shows up immediately, no app restart needed. A
  // harmless no-op for Reset Resume File/Clean Up Now, essential for Clear Configuration.
  if (res.ok) loadSettings();
}

document.getElementById('resetResumeBtn').addEventListener('click', () => runMaintenanceAction(
  '/api/maintenance/reset-resume',
  'Clear every tracked resume-state entry?\n\nThe next pass will do a completely fresh scan of every lane\'s Input folder instead of resuming or skipping anything.',
  'Resume file cleared.'
));

document.getElementById('cleanupNowBtn').addEventListener('click', () => runMaintenanceAction(
  '/api/maintenance/cleanup-now',
  'Clean up old logs and reports now, using the retention setting above?\n\nRemoved files go to the Recycle Bin.',
  'Old logs and reports cleaned up.'
));

document.getElementById('resetLanesBtn').addEventListener('click', () => runMaintenanceAction(
  '/api/maintenance/reset-lanes',
  'Delete ALL configured lanes and replace them with a single new, blank lane?\n\nThis cannot be undone unless you\'ve exported a backup first.',
  'Lanes reset to a single new lane.'
));

document.getElementById('clearConfigBtn').addEventListener('click', () => runMaintenanceAction(
  '/api/maintenance/clear-config',
  'Reset ALL settings and lanes back to defaults?\n\nThis cannot be undone unless you\'ve exported a backup first.',
  'Configuration reset to defaults.'
));

loadSettings();
