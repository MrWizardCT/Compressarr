// Shared server-side folder browser modal. Call openFolderBrowser(startPath, onSelect) to open
// it - onSelect(chosenPath) is called once the user clicks "Select This Folder". openFileBrowser(startPath,
// onSelect) is the same dialog also listing files (executables on Windows), for picking a tool such as
// HandBrakeCLI - onSelect gets the chosen file's full path.
let _browseModal = null;
let _browseOnSelect = null;
let _browseCurrentPath = null;
let _browseFileMode = false;
let _browseExtension = '';
let _browseSelectedFile = null;

function ensureBrowseModal() {
  if (_browseModal) return _browseModal;

  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay hidden';
  overlay.innerHTML = `
    <div class="modal">
      <h3 id="browseModalTitle">Choose a folder</h3>
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
    const chosen = _browseFileMode ? _browseSelectedFile : _browseCurrentPath;
    if (_browseOnSelect && chosen) _browseOnSelect(chosen);
    closeBrowseModal();
  });

  _browseModal = overlay;
  return overlay;
}

function closeBrowseModal() {
  if (_browseModal) _browseModal.classList.add('hidden');
}

async function loadBrowsePath(path) {
  const res = await fetch(`/api/browse?path=${encodeURIComponent(path || '')}${_browseFileMode ? '&files=true' + (_browseExtension ? '&ext=' + encodeURIComponent(_browseExtension) : '') : ''}`);
  const result = await res.json();

  _browseCurrentPath = result.currentPath;
  document.getElementById('browseModalPath').textContent = result.currentPath || 'Select a drive/root to begin';
  _browseSelectedFile = null;
  document.getElementById('browseModalSelect').disabled = _browseFileMode || !result.currentPath;

  const list = document.getElementById('browseModalList');
  list.innerHTML = '';

  if (result.parentPath !== null && result.parentPath !== undefined) {
    const up = document.createElement('div');
    up.className = 'modal-list-item up';
    up.textContent = '.. (up)';
    up.addEventListener('click', () => loadBrowsePath(result.parentPath));
    list.appendChild(up);
  }

  const files = _browseFileMode ? (result.files || []) : [];
  if (result.directories.length === 0 && files.length === 0) {
    const empty = document.createElement('div');
    empty.className = 'modal-list-empty';
    empty.textContent = _browseFileMode ? 'No subfolders or programs here.' : 'No subfolders here.';
    list.appendChild(empty);
  } else {
    for (const dir of result.directories) {
      const item = document.createElement('div');
      item.className = 'modal-list-item';
      item.textContent = dir.name;
      item.addEventListener('click', () => loadBrowsePath(dir.fullPath));
      list.appendChild(item);
    }
    for (const file of files) {
      const item = document.createElement('div');
      item.className = 'modal-list-item file';
      item.textContent = file.name;
      item.addEventListener('click', () => {
        for (const other of list.querySelectorAll('.file.selected')) other.classList.remove('selected');
        item.classList.add('selected');
        _browseSelectedFile = file.fullPath;
        document.getElementById('browseModalSelect').disabled = false;
      });
      item.addEventListener('dblclick', () => {
        if (_browseOnSelect) _browseOnSelect(file.fullPath);
        closeBrowseModal();
      });
      list.appendChild(item);
    }
  }
}

function openBrowser(fileMode, startPath, onSelect, extension) {
  ensureBrowseModal();
  _browseFileMode = fileMode;
  _browseExtension = extension || '';
  document.getElementById('browseModalTitle').textContent = fileMode ? 'Choose a file' : 'Choose a folder';
  document.getElementById('browseModalSelect').textContent = fileMode ? 'Select This File' : 'Select This Folder';
  _browseOnSelect = onSelect;
  _browseModal.classList.remove('hidden');
  loadBrowsePath(startPath || '');
}

function openFolderBrowser(startPath, onSelect) { openBrowser(false, startPath, onSelect); }
// extension (e.g. '.json') limits the list to that kind of file; leave it out to list programs.
function openFileBrowser(startPath, onSelect, extension) { openBrowser(true, startPath, onSelect, extension); }
