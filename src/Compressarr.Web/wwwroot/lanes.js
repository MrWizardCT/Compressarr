renderNav('lanes');

const lanesContainer = document.getElementById('lanes');
const template = document.getElementById('lane-template');

// Populated by populatePresetList() before any lane card is built - a <select> needs its
// <option>s to already exist before setting .value, unlike the old <input list> combo.
let presetNamesByEngine = { HandBrake: [], FFmpeg: [] };

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
  moviePreset: '.f-moviePreset',
  engine: '.f-engine'
};

// Highlights just this one card's fields - the aggregate page-level status message (if any) is
// decided by the caller, since a single lane's issues shouldn't overwrite/hide another card's.
function applyLaneValidation(node, issues) {
  showEngineNote(node, node.querySelector('.f-engine').value, issues);
  return applyValidationIssues(node, issues, LANE_FIELD_MAP, '');
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

function fillPresetSelect(select, currentValue, engine) {
  const presetNames = presetNamesByEngine[engine || 'HandBrake'] || [];
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
  const engine = dto.engine || 'HandBrake';
  node.querySelector('.f-engine').value = engine;
  fillPresetSelect(node.querySelector('.f-tvPreset'), dto.tvPreset, engine);
  fillPresetSelect(node.querySelector('.f-moviePreset'), dto.moviePreset, engine);
  showEngineNote(node, engine, dto.validationIssues);
  // Switching encoder swaps the profile lists (each tool has its own); a chosen name that isn't in the
  // new list stays selectable and gets flagged on save, rather than being blanked behind the user's back.
  node.querySelector('.f-engine').addEventListener('change', e => {
    const next = e.target.value;
    fillPresetSelect(node.querySelector('.f-tvPreset'), node.querySelector('.f-tvPreset').value, next);
    fillPresetSelect(node.querySelector('.f-moviePreset'), node.querySelector('.f-moviePreset').value, next);
    showEngineNote(node, next, []);
  });
  node.querySelector('.f-tvShowBasePath').value = dto.tvShowBasePath;
  node.querySelector('.f-movieBasePath').value = dto.movieBasePath;

  node.querySelector('.save-lane-btn').addEventListener('click', () => saveLane(node));
  node.querySelector('.remove-lane-btn').addEventListener('click', () => removeLane(node));

  for (const btn of node.querySelectorAll('.browse-btn')) {
    btn.addEventListener('click', () => {
      const targetField = node.querySelector(`.${btn.dataset.target}`);
      openFolderBrowser(targetField.value, chosenPath => {
        targetField.value = chosenPath;
        targetField.dispatchEvent(new Event('input', { bubbles: true }));
      });
    });
  }

  // A card that is showing red fields re-checks itself as it is edited, so a field turns normal the moment
  // it holds something valid instead of staying red until the next Save.
  for (const eventName of ['input', 'change']) {
    node.addEventListener(eventName, () => scheduleRevalidation(node));
  }

  return node;
}

let revalidationSeq = 0;

function scheduleRevalidation(node) {
  if (!node.querySelector('.field-invalid')) return; // nothing red to clear - validation stays a Save-time thing
  clearTimeout(node._revalidateTimer);
  node._revalidateTimer = setTimeout(async () => {
    const seq = ++revalidationSeq;
    node._revalidateSeq = seq;
    try {
      const res = await fetch('/api/lanes/validate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(readLaneCard(node))
      });
      if (!res.ok || node._revalidateSeq !== seq) return; // failed, or a newer check is already on its way
      applyLaneValidation(node, await res.json());
      if (!lanesContainer.querySelector('.field-invalid') && /configuration issue/.test(toolbarStatusEl?.textContent || '')) {
        setStatusMessage('', '');
      }
    } catch { /* leave the highlight as it was */ }
  }, 400);
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
    engine: node.querySelector('.f-engine').value,
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
  // Files in other lanes may have been told to land in this lane's library. Say so before it goes: they are
  // not misfiled anywhere else - each finished file waits safely in Output until a new destination is chosen.
  let redirectedNote = '';
  try {
    const res = await fetch(`/api/lanes/${encodeURIComponent(dto.id)}/redirected-files`);
    const { waitingFiles } = await res.json();
    if (waitingFiles > 0) {
      redirectedNote = `\n\n${waitingFiles} queued file${waitingFiles === 1 ? ' is' : 's are'} set to land in this lane. ${waitingFiles === 1 ? 'It' : 'They'} will be held in Output (never filed in another library) until you choose a new destination on the Monitor page.`;
    }
  } catch { /* best-effort - the plain confirmation below still works */ }

  const confirmed = confirm(`Remove lane "${dto.displayName}"?\n\nThis only removes it from Compressarr's configuration - no files are touched.${redirectedNote}`);
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
  const [hb, ff] = await Promise.all([fetch('/api/presets'), fetch('/api/presets?engine=ffmpeg')]);
  presetNamesByEngine = { HandBrake: await hb.json(), FFmpeg: await ff.json() };
}

// Under the Encoder select: why this lane's encoder can't run yet (ffmpeg missing, ...), or a reminder that it is experimental.
function showEngineNote(node, engine, issues) {
  const note = node.querySelector('.f-engineNote');
  const problem = (issues || []).find(i => i.field === 'engine');
  if (problem) {
    note.textContent = problem.message;
    note.classList.remove('hidden');
  } else if (engine === 'FFmpeg') {
    note.textContent = "Experimental. Files with Dolby Vision or HDR10+ are handed to HandBrake, using the ffmpeg profile's fallback profile.";
    note.classList.remove('hidden');
  } else {
    note.classList.add('hidden');
  }
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
