renderNav('lanes');

const statusEl = document.getElementById('status');
const lanesContainer = document.getElementById('lanes');
const template = document.getElementById('lane-template');

// Populated by populatePresetList() before any lane card is built - a <select> needs its
// <option>s to already exist before setting .value, unlike the old <input list> combo.
let presetNames = [];

function setStatus(text) {
  statusEl.textContent = text;
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
  return res.ok;
}

async function saveLane(node) {
  const dto = readLaneCard(node);
  setStatus(`Saving lane "${dto.displayName}"...`);
  const ok = await putLane(dto);
  setStatus(ok ? `Lane "${dto.displayName}" saved.` : 'Failed to save lane.');
  // A single lane's Save clears the global dirty flag even if another card still has unsaved
  // edits of its own - a known simplification (no per-card tracking), same trade-off as Settings.
  if (ok) lanesDirty = false;
}

async function saveAllLanes() {
  const cards = lanesContainer.querySelectorAll('.lane-card');
  if (cards.length === 0) {
    setStatus('No lanes to save.');
    return;
  }

  setStatus(`Saving ${cards.length} lane(s)...`);
  const results = await Promise.all(Array.from(cards).map(node => putLane(readLaneCard(node))));
  const failedCount = results.filter(ok => !ok).length;

  setStatus(failedCount === 0
    ? `All ${cards.length} lane(s) saved.`
    : `Saved ${cards.length - failedCount} of ${cards.length} lane(s) - ${failedCount} failed.`);
  if (failedCount === 0) lanesDirty = false;
}

async function removeLane(node) {
  const dto = readLaneCard(node);
  const confirmed = confirm(`Remove lane "${dto.displayName}"?\n\nThis only removes it from Compressarr's configuration - no files are touched.`);
  if (!confirmed) return;

  const res = await fetch(`/api/lanes/${dto.id}`, { method: 'DELETE' });
  if (res.ok) {
    node.remove();
    setStatus(`Lane "${dto.displayName}" removed.`);
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
  for (const dto of lanes) {
    lanesContainer.appendChild(laneCardFromDto(dto));
  }
  lanesDirty = false;
}

document.getElementById('addLaneBtn').addEventListener('click', async () => {
  const res = await fetch('/api/lanes', { method: 'POST' });
  const dto = await res.json();
  lanesContainer.appendChild(laneCardFromDto(dto));
  setStatus(`Lane "${dto.displayName}" added.`);
});

document.getElementById('saveAllLanesBtn').addEventListener('click', saveAllLanes);

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
