renderNav('lanes');

const lanesContainer = document.getElementById('lanes');
const template = document.getElementById('lane-template');

// Populated by populatePresetList() before any lane card is built - a <select> needs its
// <option>s to already exist before setting .value, unlike the old <input list> combo.
let presetNames = [];

// success truthy -> green (auto-clears after 4s), same convention as settings.js's setStatus -
// this used to just set plain text with no color at all, which is why Lanes' own save messages
// never turned green like Settings/Notifications did.
function setStatus(text, success) { setStatusMessage(text, success ? 'success' : ''); }

// Logical validation-issue field key -> the class each field lives on, resolved within one lane
// card's own node (not the whole document) - every card repeats the same classes, so scoping
// matters. `node.querySelector` (not `document.querySelector`) is what makes that scoping work.
const LANE_FIELD_MAP = {
  input: '.f-input',
  output: '.f-output',
  tvPreset: '.f-tvPreset',
  moviePreset: '.f-moviePreset'
};

// Highlights just this one card's fields - the aggregate page-level status message (if any) is
// decided by the caller, since a single lane's issues shouldn't overwrite/hide another card's.
function applyLaneValidation(node, issues) {
  return applyValidationIssues(node, issues, LANE_FIELD_MAP, '');
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

function fillPresetSelect(select, currentValue) {
  // If the lane's saved preset isn't in the current list (e.g. presets.json changed since this
  // lane was configured), keep it as a selectable option anyway rather than silently blanking
  // the field out from under the user.
  const names = (currentValue && !presetNames.includes(currentValue))
    ? [currentValue, ...presetNames]
    : presetNames;

  select.innerHTML = '<option value=""></option>' + names.map(n => `<option value="${escapeHtml(n)}">${escapeHtml(n)}</option>`).join('');
  select.value = currentValue || '';
}

function laneCardFromDto(dto) {
  const node = template.content.firstElementChild.cloneNode(true);
  node.dataset.id = dto.id;
  node.querySelector('.f-enabled').checked = dto.enabled;
  node.querySelector('.f-displayName').value = dto.displayName;
  node.querySelector('.f-input').value = dto.input;
  node.querySelector('.f-output').value = dto.output;
  fillPresetSelect(node.querySelector('.f-tvPreset'), dto.tvPreset);
  fillPresetSelect(node.querySelector('.f-moviePreset'), dto.moviePreset);
  node.querySelector('.f-tvShowBasePath').value = dto.tvShowBasePath;
  node.querySelector('.f-movieBasePath').value = dto.movieBasePath;

  node.querySelector('.save-lane-btn').addEventListener('click', () => saveLane(node));
  node.querySelector('.remove-lane-btn').addEventListener('click', () => removeLane(node));

  for (const btn of node.querySelectorAll('.browse-btn')) {
    btn.addEventListener('click', () => {
      const targetField = node.querySelector(`.${btn.dataset.target}`);
      openFolderBrowser(targetField.value, chosenPath => { targetField.value = chosenPath; });
    });
  }

  return node;
}

function readLaneCard(node) {
  return {
    id: node.dataset.id,
    displayName: node.querySelector('.f-displayName').value,
    enabled: node.querySelector('.f-enabled').checked,
    input: node.querySelector('.f-input').value,
    output: node.querySelector('.f-output').value,
    tvPreset: node.querySelector('.f-tvPreset').value,
    moviePreset: node.querySelector('.f-moviePreset').value,
    tvShowBasePath: node.querySelector('.f-tvShowBasePath').value,
    movieBasePath: node.querySelector('.f-movieBasePath').value
  };
}

async function putLane(dto) {
  const res = await fetch(`/api/lanes/${dto.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto)
  });
  return { ok: res.ok, dto: res.ok ? await res.json() : null };
}

async function saveLane(node) {
  const dto = readLaneCard(node);
  setStatus(`Saving lane "${dto.displayName}"...`);
  const result = await putLane(dto);
  if (!result.ok) {
    setStatus('Failed to save lane.');
    return;
  }

  // A single lane's Save clears the global dirty flag even if another card still has unsaved
  // edits of its own - a known simplification (no per-card tracking), same trade-off as Settings.
  lanesDirty = false;
  if (applyLaneValidation(node, result.dto.validationIssues)) {
    setStatusMessage(`Lane "${dto.displayName}" saved, but has a configuration issue - check the fields below.`, 'error');
  } else {
    setStatus(`Lane "${dto.displayName}" saved.`, true);
  }
}

async function saveAllLanes() {
  const cards = Array.from(lanesContainer.querySelectorAll('.lane-card'));
  if (cards.length === 0) {
    setStatus('No lanes to save.');
    return;
  }

  setStatus(`Saving ${cards.length} lane(s)...`);
  const results = await Promise.all(cards.map(node => putLane(readLaneCard(node))));
  const failedCount = results.filter(r => !r.ok).length;

  let anyIssues = false;
  results.forEach((result, i) => {
    if (result.ok && applyLaneValidation(cards[i], result.dto.validationIssues)) anyIssues = true;
  });

  if (failedCount > 0) {
    setStatus(`Saved ${cards.length - failedCount} of ${cards.length} lane(s) - ${failedCount} failed.`);
  } else if (anyIssues) {
    setStatusMessage(`All ${cards.length} lane(s) saved, but one or more has a configuration issue - check the fields below.`, 'error');
  } else {
    setStatus(`All ${cards.length} lane(s) saved.`, true);
  }
  if (failedCount === 0) lanesDirty = false;
}

async function removeLane(node) {
  const dto = readLaneCard(node);
  const confirmed = confirm(`Remove lane "${dto.displayName}"?\n\nThis only removes it from Compressarr's configuration - no files are touched.`);
  if (!confirmed) return;

  const res = await fetch(`/api/lanes/${dto.id}`, { method: 'DELETE' });
  if (res.ok) {
    node.remove();
    setStatus(`Lane "${dto.displayName}" removed.`, true);
  } else {
    setStatus('Failed to remove lane.');
  }
}

async function populatePresetList() {
  const settingsRes = await fetch('/api/settings');
  const settings = await settingsRes.json();
  if (!settings.presetsPath) return;

  const presetsRes = await fetch(`/api/presets?path=${encodeURIComponent(settings.presetsPath)}`);
  presetNames = await presetsRes.json();
}

async function loadLanes() {
  const res = await fetch('/api/lanes');
  const lanes = await res.json();
  lanesContainer.innerHTML = '';
  let anyIssues = false;
  for (const dto of lanes) {
    const node = laneCardFromDto(dto);
    lanesContainer.appendChild(node);
    if (applyLaneValidation(node, dto.validationIssues)) anyIssues = true;
  }
  lanesDirty = false;
  if (anyIssues) {
    setStatusMessage('One or more lanes has a configuration issue - check the fields below.', 'error');
  }
}

document.getElementById('addLaneBtn').addEventListener('click', async () => {
  const res = await fetch('/api/lanes', { method: 'POST' });
  const dto = await res.json();
  const node = laneCardFromDto(dto);
  lanesContainer.appendChild(node);
  applyLaneValidation(node, dto.validationIssues);
  setStatus(`Lane "${dto.displayName}" added.`, true);
});

document.getElementById('saveAllLanesBtn').addEventListener('click', saveAllLanes);

document.getElementById('clearChangesBtn').addEventListener('click', async () => {
  if (lanesDirty && !confirm('Discard unsaved changes and reload the last saved lanes?')) return;
  await loadLanes();
  setStatus('Changes cleared.', true);
});

// Warn before leaving with unsaved field edits inside a lane card - Add/Remove/Save all persist
// immediately on click, so they're never what this is protecting; only in-progress edits to a
// card's own fields (typed but not yet Saved) are. Sidebar nav links are plain <a href>
// navigation, so beforeunload fires for those the same as tab close/reload - no separate
// in-app-click handling needed.
let lanesDirty = false;
for (const eventName of ['input', 'change']) {
  lanesContainer.addEventListener(eventName, () => { lanesDirty = true; });
}
window.addEventListener('beforeunload', e => {
  if (!lanesDirty) return;
  e.preventDefault();
  e.returnValue = '';
});

// loadLanes() (and Add Lane's own card-building) needs presetNames already populated - a
// <select>'s .value only "sticks" once a matching <option> exists.
populatePresetList().then(loadLanes);
