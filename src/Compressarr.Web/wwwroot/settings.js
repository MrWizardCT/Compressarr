renderNav('settings');

const statusEl = document.getElementById('status');

function setStatus(text) {
  statusEl.textContent = text;
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

  setStatus('Installing presets...');
  const res = await fetch('/api/presets/install', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ mode })
  });
  setStatus(res.ok ? 'Compressarr presets installed.' : 'Failed to install presets.');
});

document.getElementById('reloadPresetsBtn').addEventListener('click', async () => {
  setStatus('Reloading presets...');
  const res = await fetch('/api/presets/reload', { method: 'POST' });
  setStatus(res.ok ? 'Presets reloaded.' : 'Failed to reload presets.');
});

loadSettings();
