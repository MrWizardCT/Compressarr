renderNav('profiles');

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

const LOCK_ICON = '<svg class="prof-lock-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="11" width="16" height="10" rx="2"></rect><path d="M8 11V7a4 4 0 0 1 8 0v4"></path></svg>';

function profileRow(p) {
  const lock = p.builtIn ? `<span class="prof-lock" title="Built-in - ships with Compressarr and can't be changed or deleted">${LOCK_ICON}built-in</span>` : '';
  const engine = p.engine === 'ffmpeg' ? 'ffmpeg' : 'HandBrake';
  const sub = [p.container, p.builtIn ? '' : 'saved in Compressarr\'s own file'].filter(Boolean).join(' · ');
  return `<tr>
    <td><div class="prof-name">${escapeHtml(p.name)} ${lock}</div>${sub ? `<div class="prof-sub">${escapeHtml(sub)}</div>` : ''}${p.description ? `<div class="prof-sub" title="${escapeHtml(p.description)}">${escapeHtml(p.description.length > 110 ? p.description.slice(0, 107) + '...' : p.description)}</div>` : ''}</td>
    <td><span class="prof-chip">${engine}</span></td>
    <td>${escapeHtml(p.video)}</td>
    <td>${escapeHtml(p.audio)}</td>
    <td>${p.usedBy.length ? p.usedBy.map(escapeHtml).join(', ') : '-'}</td>
  </tr>`;
}

async function loadProfiles() {
  const res = await fetch('/api/profiles');
  const dto = await res.json();

  document.getElementById('profileRows').innerHTML = dto.profiles.length
    ? dto.profiles.map(profileRow).join('')
    : '<tr><td colspan="5" class="prof-sub">No profiles.</td></tr>';
  document.getElementById('userFilePath').textContent = dto.userFilePath;

  const errorBox = document.getElementById('profileError');
  if (dto.userFileError) {
    errorBox.textContent = `Your own profile file could not be read, so only the built-ins are shown: ${dto.userFileError}`;
    errorBox.classList.remove('hidden');
  } else {
    errorBox.classList.add('hidden');
  }
}

loadProfiles();
