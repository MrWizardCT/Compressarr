renderNav('notifications');

const channelsList = document.getElementById('channelsList');
const channelsStatus = document.getElementById('channelsStatus');
const addChannelType = document.getElementById('addChannelType');

let notifierTypes = []; // [{type, displayName, fields: [{key,label,inputType,required,secret,options}]}]

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// ---- Toast toggle - a single setting, saved immediately on change rather than needing its own
// Save button, same "act right away" feel Reload/Test buttons elsewhere in the app already have.
async function loadToastSetting() {
  const res = await fetch('/api/notifications/settings');
  const dto = await res.json();
  document.getElementById('toastEnabled').checked = dto.toastEnabled;
}

document.getElementById('toastEnabled').addEventListener('change', async e => {
  const statusEl = document.getElementById('toastStatus');
  await fetch('/api/notifications/settings', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ toastEnabled: e.target.checked })
  });
  statusEl.textContent = e.target.checked ? 'Toast notifications enabled.' : 'Toast notifications disabled.';
  statusEl.classList.add('success');
  setTimeout(() => { statusEl.textContent = ''; statusEl.classList.remove('success'); }, 4000);
});

// ---- Notification channels - a dynamic list, each channel's own field set driven entirely by
// notifierTypes (fetched once from the server) rather than any hardcoded per-type HTML, so a
// future channel type needs no frontend changes at all.

function fieldInputHtml(field, value) {
  const val = value || '';
  if (field.inputType === 'textarea') {
    return `<textarea class="f-setting" data-key="${field.key}" rows="3">${escapeHtml(val)}</textarea>`;
  }
  if (field.inputType === 'select') {
    const options = (field.options || []).map(o => `<option value="${escapeHtml(o)}" ${o === val ? 'selected' : ''}>${escapeHtml(o)}</option>`).join('');
    return `<select class="f-setting" data-key="${field.key}">${options}</select>`;
  }
  const inputType = field.inputType === 'password' || field.secret ? 'password' : 'text';
  return `<input type="${inputType}" class="f-setting" data-key="${field.key}" value="${escapeHtml(val)}" />`;
}

function channelCardFromDto(dto) {
  const typeInfo = notifierTypes.find(t => t.type === dto.type);
  const fields = typeInfo ? typeInfo.fields : [];
  const typeLabel = typeInfo ? typeInfo.displayName : dto.type;

  const node = document.createElement('div');
  node.className = 'lane-card';
  node.dataset.id = dto.id;
  node.dataset.type = dto.type;

  const fieldBlocks = fields.map(f => `
    <div class="field-block">
      <label>${escapeHtml(f.label)}</label>
      ${fieldInputHtml(f, dto.settings[f.key])}
    </div>
  `).join('');

  node.innerHTML = `
    <div class="lane-head">
      <select class="f-trigger" title="When this channel fires">
        <option value="Always">Always</option>
        <option value="OnError">On error or warning</option>
        <option value="Never">Never (disabled)</option>
      </select>
      <input type="text" class="f-displayName lane-name-input" placeholder="Channel name" />
      <span style="color: var(--text-dim); font-size: 11.5px; white-space: nowrap;">${escapeHtml(typeLabel)}</span>
      <div class="lane-head-spacer"></div>
      <div class="lane-head-actions">
        <button type="button" class="test-channel-btn icon-btn">Test</button>
        <button type="button" class="save-channel-btn icon-btn save">Save</button>
        <button type="button" class="remove-channel-btn icon-btn remove">Remove</button>
      </div>
    </div>
    <div class="lane-body">
      ${fieldBlocks}
      <div class="preset-status"></div>
    </div>
  `;

  node.querySelector('.f-displayName').value = dto.displayName;
  node.querySelector('.f-trigger').value = dto.trigger;

  node.querySelector('.save-channel-btn').addEventListener('click', () => saveChannel(node));
  node.querySelector('.remove-channel-btn').addEventListener('click', () => removeChannel(node));
  node.querySelector('.test-channel-btn').addEventListener('click', () => testChannel(node));

  return node;
}

function readChannelCard(node) {
  const settings = {};
  for (const el of node.querySelectorAll('.f-setting')) {
    settings[el.dataset.key] = el.value;
  }
  return {
    id: node.dataset.id,
    type: node.dataset.type,
    displayName: node.querySelector('.f-displayName').value,
    trigger: node.querySelector('.f-trigger').value,
    settings
  };
}

async function saveChannel(node) {
  const dto = readChannelCard(node);
  channelsStatus.textContent = `Saving "${dto.displayName}"...`;
  const res = await fetch(`/api/notifications/channels/${dto.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto)
  });
  channelsStatus.textContent = res.ok ? `"${dto.displayName}" saved.` : 'Failed to save channel.';
}

async function removeChannel(node) {
  const dto = readChannelCard(node);
  if (!confirm(`Remove "${dto.displayName}"?`)) return;

  const res = await fetch(`/api/notifications/channels/${dto.id}`, { method: 'DELETE' });
  if (res.ok) {
    node.remove();
    channelsStatus.textContent = `"${dto.displayName}" removed.`;
  } else {
    channelsStatus.textContent = 'Failed to remove channel.';
  }
}

async function testChannel(node) {
  const dto = readChannelCard(node);
  const statusEl = node.querySelector('.preset-status');
  statusEl.textContent = 'Testing...';
  statusEl.classList.remove('success');

  const res = await fetch('/api/notifications/test', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ type: dto.type, settings: dto.settings })
  });
  const body = await res.json();
  statusEl.textContent = body.message;
  statusEl.classList.toggle('success', !!body.success);
}

async function loadNotifierTypes() {
  const res = await fetch('/api/notifications/types');
  notifierTypes = await res.json();
  addChannelType.innerHTML = notifierTypes.map(t => `<option value="${escapeHtml(t.type)}">${escapeHtml(t.displayName)}</option>`).join('');
}

async function loadChannels() {
  const res = await fetch('/api/notifications/channels');
  const channels = await res.json();
  channelsList.innerHTML = '';
  for (const dto of channels) {
    channelsList.appendChild(channelCardFromDto(dto));
  }
}

document.getElementById('addChannelBtn').addEventListener('click', async () => {
  const type = addChannelType.value;
  if (!type) return;

  const res = await fetch('/api/notifications/channels', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ type })
  });
  const dto = await res.json();
  channelsList.appendChild(channelCardFromDto(dto));
  channelsStatus.textContent = `"${dto.displayName}" added.`;
});

loadToastSetting();
loadNotifierTypes().then(loadChannels);
