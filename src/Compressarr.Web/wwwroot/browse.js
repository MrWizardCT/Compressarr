// Shared server-side folder browser modal. Call openFolderBrowser(startPath, onSelect) to open
// it - onSelect(chosenPath) is called once the user clicks "Select This Folder".
let _browseModal = null;
let _browseOnSelect = null;
let _browseCurrentPath = null;

function ensureBrowseModal() {
  if (_browseModal) return _browseModal;

  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay hidden';
  overlay.innerHTML = `
    <div class="modal">
      <h3>Choose a folder</h3>
      <div class="modal-path" id="browseModalPath">-</div>
      <div class="modal-list" id="browseModalList"></div>
      <div class="modal-actions">
        <button id="browseModalCancel">Cancel</button>
        <button id="browseModalSelect" class="primary">Select This Folder</button>
      </div>
    </div>
  `;
  document.body.appendChild(overlay);

  overlay.addEventListener('click', e => {
    if (e.target === overlay) closeBrowseModal();
  });
  document.getElementById('browseModalCancel').addEventListener('click', closeBrowseModal);
  document.getElementById('browseModalSelect').addEventListener('click', () => {
    if (_browseOnSelect && _browseCurrentPath) _browseOnSelect(_browseCurrentPath);
    closeBrowseModal();
  });

  _browseModal = overlay;
  return overlay;
}

function closeBrowseModal() {
  if (_browseModal) _browseModal.classList.add('hidden');
}

async function loadBrowsePath(path) {
  const res = await fetch(`/api/browse?path=${encodeURIComponent(path || '')}`);
  const result = await res.json();

  _browseCurrentPath = result.currentPath;
  document.getElementById('browseModalPath').textContent = result.currentPath || 'Select a drive/root to begin';
  document.getElementById('browseModalSelect').disabled = !result.currentPath;

  const list = document.getElementById('browseModalList');
  list.innerHTML = '';

  if (result.parentPath !== null && result.parentPath !== undefined) {
    const up = document.createElement('div');
    up.className = 'modal-list-item up';
    up.textContent = '.. (up)';
    up.addEventListener('click', () => loadBrowsePath(result.parentPath));
    list.appendChild(up);
  }

  if (result.directories.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'modal-list-empty';
    empty.textContent = 'No subfolders here.';
    list.appendChild(empty);
  } else {
    for (const dir of result.directories) {
      const item = document.createElement('div');
      item.className = 'modal-list-item';
      item.textContent = dir.name;
      item.addEventListener('click', () => loadBrowsePath(dir.fullPath));
      list.appendChild(item);
    }
  }
}

function openFolderBrowser(startPath, onSelect) {
  ensureBrowseModal();
  _browseOnSelect = onSelect;
  _browseModal.classList.remove('hidden');
  loadBrowsePath(startPath || '');
}
