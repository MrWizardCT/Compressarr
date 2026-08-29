renderNav('lanes');

const statusEl = document.getElementById('status');
const lanesContainer = document.getElementById('lanes');
const template = document.getElementById('lane-template');

function setStatus(text) {
  statusEl.textContent = text;
}

function laneCardFromDto(dto) {
  const node = template.content.firstElementChild.cloneNode(true);
  node.dataset.id = dto.id;
  node.querySelector('.f-enabled').checked = dto.enabled;
  node.querySelector('.f-displayName').value = dto.displayName;
  node.querySelector('.f-input').value = dto.input;
  node.querySelector('.f-output').value = dto.output;
  node.querySelector('.f-tvPreset').value = dto.tvPreset;
  node.querySelector('.f-moviePreset').value = dto.moviePreset;
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
  const names = await presetsRes.json();

  const datalist = document.getElementById('preset-list');
  datalist.innerHTML = names.map(n => `<option value="${n}"></option>`).join('');
}

async function loadLanes() {
  const res = await fetch('/api/lanes');
  const lanes = await res.json();
  lanesContainer.innerHTML = '';
  for (const dto of lanes) {
    lanesContainer.appendChild(laneCardFromDto(dto));
  }
}

document.getElementById('addLaneBtn').addEventListener('click', async () => {
  const res = await fetch('/api/lanes', { method: 'POST' });
  const dto = await res.json();
  lanesContainer.appendChild(laneCardFromDto(dto));
  setStatus(`Lane "${dto.displayName}" added.`);
});

document.getElementById('saveAllLanesBtn').addEventListener('click', saveAllLanes);

populatePresetList();
loadLanes();
