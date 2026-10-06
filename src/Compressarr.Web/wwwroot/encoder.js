renderNav('encoder');

function setStatus(text, success) { setStatusMessage(text, success ? 'success' : ''); }

// Logical validation-issue field key -> the actual input it lives on.
const ENCODER_FIELD_MAP = {
  handBrakeCliPath: '#hbCliPath',
  handBrakeOptions: '#hbOptions'
};

function applyEncoderValidation(issues) {
  return applyValidationIssues(document, issues, ENCODER_FIELD_MAP, 'Error in the encoder configuration, check fields below.');
}

let encoderDirty = false;
let lastDto = null;

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function plural(n, word) { return `${n} ${word}${n === 1 ? '' : 's'}`; }

function fillForm(dto) {
  lastDto = dto;
  document.getElementById('hbCliPath').value = dto.handBrakeCliPath;
  document.getElementById('hbOptions').value = dto.handBrakeOptions;
  renderGlance(dto);
}

function readForm() {
  // The read-only fields (profile counts, lanes) are ignored by the server on PUT; the record just needs values.
  return {
    handBrakeCliPath: document.getElementById('hbCliPath').value,
    handBrakeOptions: document.getElementById('hbOptions').value,
    builtInProfileCount: lastDto ? lastDto.builtInProfileCount : 0,
    userProfileCount: lastDto ? lastDto.userProfileCount : 0,
    handBrakeLanes: [],
    validationIssues: []
  };
}

async function renderGlance(dto) {
  const total = dto.builtInProfileCount + dto.userProfileCount;
  const lanes = dto.handBrakeLanes || [];
  document.getElementById('hbUsedBy').innerHTML = lanes.length
    ? `Used by: ${lanes.map(l => `<b>${escapeHtml(l)}</b>`).join(', ')}`
    : 'Not used by any enabled lane yet.';

  const dot = document.getElementById('hbDot');
  const line = document.getElementById('hbVersionLine');
  const found = !dto.validationIssues.some(i => i.field === 'handBrakeCliPath');
  dot.classList.toggle('ok', found);
  dot.classList.toggle('bad', !found);
  if (!found) {
    line.textContent = `HandBrakeCLI not found · ${plural(total, 'profile')}`;
    return;
  }

  line.textContent = plural(total, 'profile');
  try {
    const res = await fetch('/api/handbrake/installed-version');
    const body = await res.json();
    if (body.version) line.textContent = `HandBrakeCLI ${body.version} · ${plural(total, 'profile')}`;
  } catch { /* the version is a nicety - the profile count above already says enough */ }
}

async function loadEncoder() {
  const res = await fetch('/api/encoder');
  const dto = await res.json();
  fillForm(dto);
  encoderDirty = false;
  applyEncoderValidation(dto.validationIssues);
}

document.getElementById('saveBtn').addEventListener('click', async () => {
  setStatus('Saving...');
  const res = await fetch('/api/encoder', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(readForm())
  });
  if (res.ok) {
    const dto = await res.json();
    fillForm(dto);
    encoderDirty = false;
    if (!applyEncoderValidation(dto.validationIssues)) {
      setStatus('Encoder settings saved.', true);
    }
  } else {
    setStatus('Failed to save encoder settings.');
  }
});

document.getElementById('clearChangesBtn').addEventListener('click', async () => {
  if (encoderDirty && !confirm('Discard unsaved changes and reload the last saved settings?')) return;
  await loadEncoder();
  setStatus('Changes cleared.', true);
});

// Warn before leaving with unsaved edits, same as the Settings page.
const encoderMain = document.querySelector('main');
for (const eventName of ['input', 'change']) {
  encoderMain.addEventListener(eventName, () => { encoderDirty = true; });
}
window.addEventListener('beforeunload', e => {
  if (!encoderDirty) return;
  e.preventDefault();
  e.returnValue = '';
});

document.getElementById('checkHandBrakeBtn').addEventListener('click', async () => {
  setStatus('Checking HandBrakeCLI...');
  const statusRes = await fetch('/api/handbrake/status');
  const statusBody = await statusRes.json();
  if (statusBody.exists) {
    setStatus('HandBrakeCLI already found at the configured path.', true);
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
    // The server already saved the new path; reload so the glance card and validation match.
    await loadEncoder();
    setStatus(`HandBrakeCLI ${installBody.version} installed.`, true);
  } else {
    setStatus('HandBrakeCLI install failed.');
  }
});

loadEncoder();
