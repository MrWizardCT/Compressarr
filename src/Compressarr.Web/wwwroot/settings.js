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

function fillForm(dto) {
  document.getElementById('hbCliPath').value = dto.handBrakeCliPath;
  document.getElementById('presetsPath').value = dto.presetsPath;
  document.getElementById('hbOptions').value = dto.handBrakeOptions;
  document.getElementById('vidTypes').value = dto.vidTypes.join(', ');
  document.getElementById('minSizeBytes').value = dto.minSizeBytes;
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
    minSizeBytes: parseInt(document.getElementById('minSizeBytes').value, 10) || 0,
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
  } else {
    setStatus('Failed to save settings.');
  }
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

loadSettings();
