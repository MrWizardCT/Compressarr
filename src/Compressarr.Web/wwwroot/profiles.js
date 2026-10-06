renderNav('profiles');

function setStatus(text, success) { setStatusMessage(text, success ? 'success' : ''); }

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

const LOCK_ICON = '<svg class="prof-lock-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="11" width="16" height="10" rx="2"></rect><path d="M8 11V7a4 4 0 0 1 8 0v4"></path></svg>';

let profiles = [];

function profileRow(p, index) {
  const lock = p.builtIn ? `<span class="prof-lock" title="Built-in - ships with Compressarr and can't be changed or deleted">${LOCK_ICON}built-in</span>` : '';
  const engine = p.engine === 'ffmpeg' ? 'ffmpeg' : 'HandBrake';
  const sub = [p.container, p.builtIn ? '' : 'saved in Compressarr\'s own file'].filter(Boolean).join(' · ');
  const actions = p.builtIn
    ? `<button data-act="view" data-i="${index}">View</button><button data-act="dup" data-i="${index}">Duplicate</button>`
    : `<button data-act="edit" data-i="${index}">Edit</button><button data-act="dup" data-i="${index}">Duplicate</button><button data-act="del" data-i="${index}">Delete</button>`;
  return `<tr>
    <td><div class="prof-name">${escapeHtml(p.name)} ${lock}</div>${sub ? `<div class="prof-sub">${escapeHtml(sub)}</div>` : ''}${p.description ? `<div class="prof-sub" title="${escapeHtml(p.description)}">${escapeHtml(p.description.length > 110 ? p.description.slice(0, 107) + '...' : p.description)}</div>` : ''}</td>
    <td><span class="prof-chip">${engine}</span></td>
    <td>${escapeHtml(p.video)}</td>
    <td>${escapeHtml(p.audio)}</td>
    <td>${p.usedBy.length ? p.usedBy.map(escapeHtml).join(', ') : '-'}</td>
    <td><div class="prof-actions">${actions}</div></td>
  </tr>`;
}

async function loadProfiles() {
  const res = await fetch('/api/profiles');
  const dto = await res.json();
  profiles = dto.profiles;

  document.getElementById('profileRows').innerHTML = profiles.length
    ? profiles.map(profileRow).join('')
    : '<tr><td colspan="6" class="prof-sub">No profiles.</td></tr>';
  document.getElementById('userFilePath').textContent = dto.userFilePath;

  const errorBox = document.getElementById('profileError');
  if (dto.userFileError) {
    errorBox.textContent = `Your own profile file could not be read, so only the built-ins are shown: ${dto.userFileError}`;
    errorBox.classList.remove('hidden');
  } else {
    errorBox.classList.add('hidden');
  }
}

function editorUrl(name) { return `/profile-edit.html?name=${encodeURIComponent(name)}`; }

document.getElementById('profileRows').addEventListener('click', async e => {
  const btn = e.target.closest('button[data-act]');
  if (!btn) return;
  const p = profiles[Number(btn.dataset.i)];

  if (btn.dataset.act === 'edit' || btn.dataset.act === 'view') {
    location.href = editorUrl(p.name);
  } else if (btn.dataset.act === 'dup') {
    const res = await fetch(`/api/profiles/handbrake/${encodeURIComponent(p.name)}/duplicate`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}'
    });
    const body = await res.json().catch(() => ({}));
    if (res.ok) {
      setStatus(`Duplicated as "${body.name}".`, true);
      await loadProfiles();
    } else {
      setStatus(body.message || 'Could not duplicate the profile.');
    }
  } else if (btn.dataset.act === 'del') {
    if (!confirm(`Delete the profile "${p.name}"?\n\nThis can't be undone.`)) return;
    const res = await fetch(`/api/profiles/handbrake/${encodeURIComponent(p.name)}`, { method: 'DELETE' });
    if (res.ok) {
      setStatus(`Deleted "${p.name}".`, true);
      await loadProfiles();
    } else {
      const body = await res.json().catch(() => ({}));
      setStatus(body.message || 'Could not delete the profile.');
    }
  }
});

document.getElementById('newHbBtn').addEventListener('click', () => { location.href = '/profile-edit.html?new=1'; });

// ---- Import dialog -------------------------------------------------------------------------------

const overlay = document.getElementById('importOverlay');
let importPath = '';
let candidates = [];
const selected = new Set();

function openImport() {
  overlay.classList.remove('hidden');
  showTab('installed');
}

function closeImport() { overlay.classList.add('hidden'); }

function showTab(tab) {
  for (const b of document.querySelectorAll('#importTabs button')) b.classList.toggle('on', b.dataset.t === tab);
  document.getElementById('tabInstalled').classList.toggle('hidden', tab !== 'installed');
  document.getElementById('tabFile').classList.toggle('hidden', tab !== 'file');
  resetPicker();
  if (tab === 'installed') loadInstalled();
}

function resetPicker() {
  candidates = [];
  selected.clear();
  document.getElementById('importPicker').classList.add('hidden');
  updateCount();
}

async function loadInstalled() {
  const note = document.getElementById('installedNote');
  note.textContent = 'Looking for HandBrake\'s presets...';
  const res = await fetch('/api/profiles/import/installed');
  const dto = await res.json();
  if (!dto.found) {
    note.textContent = dto.error || 'No HandBrake presets file was found.';
    return;
  }
  note.innerHTML = `Found HandBrake's presets at <code>${escapeHtml(dto.path)}</code> (${dto.candidates.length} preset${dto.candidates.length === 1 ? '' : 's'}). Pick the ones to copy into Compressarr.`;
  showCandidates(dto);
}

async function readFile() {
  const path = document.getElementById('importFilePath').value;
  const res = await fetch('/api/profiles/import/read', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path })
  });
  const dto = await res.json();
  if (!dto.found) {
    resetPicker();
    setStatus(dto.error || 'Could not read that file.');
    return;
  }
  setStatus(`Read ${dto.candidates.length} preset${dto.candidates.length === 1 ? '' : 's'} from the file.`, true);
  showCandidates(dto);
}

function showCandidates(dto) {
  importPath = dto.path;
  candidates = dto.candidates;
  selected.clear();
  document.getElementById('importPicker').classList.remove('hidden');
  document.getElementById('importSearch').value = '';
  renderCandidates();
}

function renderCandidates() {
  const q = document.getElementById('importSearch').value.trim().toLowerCase();
  const rows = candidates
    .map((c, i) => ({ c, i }))
    .filter(({ c }) => !q || c.name.toLowerCase().includes(q) || (c.group || '').toLowerCase().includes(q));

  document.querySelector('#importTable tbody').innerHTML = rows.length ? rows.map(({ c, i }) => {
    const note = c.status === 'alreadyBuiltIn' ? '<span class="prof-chip muted">already built in</span>'
      : c.status === 'nameUsed' ? '<span class="prof-chip warn">name already used</span>' : '';
    return `<tr>
      <td style="width:28px"><input type="checkbox" data-i="${i}" ${c.status === 'alreadyBuiltIn' ? 'disabled' : ''} ${selected.has(i) ? 'checked' : ''} /></td>
      <td class="prof-name">${escapeHtml(c.name)}</td><td>${escapeHtml(c.group || '')}</td><td>${escapeHtml(c.video)}</td><td>${note}</td></tr>`;
  }).join('') : '<tr><td class="prof-sub">Nothing matches.</td></tr>';
  updateCount();
}

function updateCount() {
  const n = selected.size;
  document.getElementById('importCount').textContent = n ? `${n} selected - will be saved to handbrake-profiles.json` : '';
  const go = document.getElementById('importGo');
  go.disabled = n === 0;
  go.textContent = n ? `Import ${n} profile${n === 1 ? '' : 's'}` : 'Import';
}

document.getElementById('importBtn').addEventListener('click', openImport);
document.getElementById('importCancel').addEventListener('click', closeImport);
overlay.addEventListener('click', e => { if (e.target === overlay) closeImport(); });
document.getElementById('importTabs').addEventListener('click', e => {
  const b = e.target.closest('button[data-t]');
  if (b) showTab(b.dataset.t);
});
document.getElementById('readFileBtn').addEventListener('click', readFile);
document.getElementById('importFilePath').addEventListener('keydown', e => { if (e.key === 'Enter') readFile(); });
document.getElementById('importSearch').addEventListener('input', renderCandidates);
document.getElementById('importTable').addEventListener('change', e => {
  const i = Number(e.target.dataset.i);
  if (e.target.checked) selected.add(i); else selected.delete(i);
  updateCount();
});
document.getElementById('importSelectShown').addEventListener('click', () => {
  for (const cb of document.querySelectorAll('#importTable input[type=checkbox]:not(:disabled)')) selected.add(Number(cb.dataset.i));
  renderCandidates();
});
document.getElementById('importClear').addEventListener('click', () => { selected.clear(); renderCandidates(); });

document.getElementById('importGo').addEventListener('click', async () => {
  const onConflict = document.querySelector('input[name=onConflict]:checked').value;
  const names = [...selected].map(i => candidates[i].name);
  const res = await fetch('/api/profiles/import', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path: importPath, names, onConflict })
  });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) {
    setStatus(body.message || 'Import failed.');
    return;
  }
  closeImport();
  const parts = [];
  if (body.imported.length) parts.push(`${body.imported.length} imported`);
  if (body.replaced.length) parts.push(`${body.replaced.length} replaced`);
  if (body.skipped.length) parts.push(`${body.skipped.length} skipped`);
  setStatus(parts.join(', ') + '.', true);
  await loadProfiles();
});

loadProfiles();
