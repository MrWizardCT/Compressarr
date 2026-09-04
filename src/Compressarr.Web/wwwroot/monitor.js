renderNav('monitor');

const toggleMonitorBtn = document.getElementById('toggleMonitorBtn');
const runNowBtn = document.getElementById('runNowBtn');
const togglePauseBtn = document.getElementById('togglePauseBtn');
const abortBtn = document.getElementById('abortBtn');
const logPanel = document.getElementById('log-panel');

// Monitoring status text, the countdown, and CPU are global toolbar elements owned by nav.js
// (visible on every page, not just this one, hence GLOBAL_STOPPING_MESSAGE living there, not
// redeclared here) - this file only sets them optimistically, right at the moment of a click,
// for zero-latency feedback on the one surface that actually acted. nav.js's own poll (every
// 1.5s) is what keeps them accurate on an ongoing basis afterward.

const START_ICON = '<svg class="btn-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="6 3 20 12 6 21 6 3"></polygon></svg>';
const STOP_ICON = '<svg class="btn-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="6" y="6" width="12" height="12" rx="1"></rect></svg>';

// Single Start/Stop toggle - what it shows and what a click does both depend on the monitoring
// state as of the last poll (or an optimistic override set the instant this surface clicks it,
// same zero-latency idea the old two-button version used). Stays the same blue "primary" look in
// every state, on purpose - only the icon/label/action change, not the color.
let toggleIsMonitoring = false;
let toggleIsStopping = false;

function renderToggleButton() {
  if (toggleIsStopping) {
    toggleMonitorBtn.innerHTML = STOP_ICON + 'Stopping...';
    toggleMonitorBtn.disabled = true;
  } else if (toggleIsMonitoring) {
    toggleMonitorBtn.innerHTML = STOP_ICON + 'Stop Monitoring';
    toggleMonitorBtn.disabled = false;
  } else {
    toggleMonitorBtn.innerHTML = START_ICON + 'Start Monitoring';
    toggleMonitorBtn.disabled = false;
  }
}

toggleMonitorBtn.addEventListener('click', async () => {
  if (toggleIsMonitoring) {
    // Reflect the click immediately, zero-latency, on this surface - StopAsync doesn't resolve
    // until the in-flight file finishes converting, which can be minutes away. poll() below picks
    // up the server's own isStopping flag (shared with the tray icon) within 1.5s regardless, so a
    // stop requested from either surface shows up on both - this is just to not even wait that
    // long for the surface that actually clicked.
    toggleIsStopping = true;
    renderToggleButton();
    document.getElementById('monitoringState').textContent = 'Stopping monitor after current task completes';

    await fetch('/api/run/stop', { method: 'POST' });
  } else {
    // Reflect the click immediately - a real conversion pass can start on the server within
    // milliseconds, well before the next poll() would otherwise refresh this panel, so the log
    // window needs to say "starting" right now rather than sitting on stale content until then.
    toggleIsMonitoring = true;
    renderToggleButton();
    document.getElementById('monitoringState').textContent = 'Monitoring is ON';
    renderLog([{ text: 'Starting monitoring...', severity: 'Info' }]);

    await fetch('/api/run/start', { method: 'POST' });
  }
  poll();
});

const PAUSE_ICON = '<svg class="btn-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="6" y="4" width="4" height="16"></rect><rect x="14" y="4" width="4" height="16"></rect></svg>';
// Same play-triangle shape as Start Monitoring's icon - "resume" is the same action, visually.
const RESUME_ICON = START_ICON;

// Pause/Resume toggle - only meaningful while a file is actually converting (disabled otherwise,
// same reasoning as Abort). Sends HandBrakeCLI's own interactive "p"/"r" keystrokes via
// IActiveHandBrakeProcess, not a real OS-level process suspend - matches what pressing those keys
// already does when HandBrakeCLI runs directly in a console.
let togglePauseIsRunning = false;
let togglePauseIsPaused = false;

function renderPauseButton() {
  togglePauseBtn.disabled = !togglePauseIsRunning;
  if (togglePauseIsPaused) {
    togglePauseBtn.innerHTML = RESUME_ICON + 'Resume';
    togglePauseBtn.classList.add('primary');
  } else {
    togglePauseBtn.innerHTML = PAUSE_ICON + 'Pause';
    togglePauseBtn.classList.remove('primary');
  }
}

togglePauseBtn.addEventListener('click', async () => {
  if (togglePauseIsPaused) {
    togglePauseIsPaused = false;
    renderPauseButton();
    await fetch('/api/run/resume', { method: 'POST' });
  } else {
    togglePauseIsPaused = true;
    renderPauseButton();
    await fetch('/api/run/pause', { method: 'POST' });
  }
  poll();
});

runNowBtn.addEventListener('click', async () => {
  // Clear the countdown immediately in the UI - the server-side trigger races the remaining
  // delay, but there's no reason to keep showing a countdown the click just made moot.
  document.getElementById('countdown').textContent = '';
  runNowBtn.disabled = true;

  await fetch('/api/run/trigger-now', { method: 'POST' });
  poll();
});

abortBtn.addEventListener('click', async () => {
  if (!confirm('Abort the current conversion immediately and stop monitoring?')) return;
  await fetch('/api/run/abort', { method: 'POST' });
  poll();
});

function renderLog(lines) {
  // poll() calls this every 1.5s - unconditionally forcing scrollTop to the bottom every time
  // means scrolling up to read something gets yanked back down before you can read it. Only
  // auto-scroll if the user was already at (or very near) the bottom, same "stick to bottom"
  // behavior most live log/chat views use. Captured before the innerHTML replacement below,
  // since replacing it resets scrollTop.
  const wasAtBottom = logPanel.scrollHeight - logPanel.scrollTop - logPanel.clientHeight < 20;

  logPanel.innerHTML = lines
    .map(l => `<div class="log-line${l.severity === 'Error' ? ' error' : ''}">${escapeHtml(l.text)}</div>`)
    .join('');

  if (wasAtBottom) {
    logPanel.scrollTop = logPanel.scrollHeight;
  }
}

// --- In Queue: drag-to-reorder, skip/remove, per-file preset override -----------------------
//
// latestItems is always the most recent server snapshot (refreshed every poll). displayItems is
// what's actually rendered - normally kept in sync with latestItems, but frozen to a locally
// reordered copy while a drag is in progress so the periodic poll can't yank the list out from
// under the user's hand mid-drag. A composite laneId+fileName key stands in for a stable row id,
// since the server doesn't hand back one.

let latestItems = [];
let displayItems = [];
let draggingKey = null;
let openMenuKey = null;
let openPresetKey = null;
let ghostEl = null;
let grabOffsetY = 0;
let presetNames = [];

const QUEUE_ICON_GRIP = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="8" cy="6" r="1.6"></circle><circle cx="16" cy="6" r="1.6"></circle><circle cx="8" cy="12" r="1.6"></circle><circle cx="16" cy="12" r="1.6"></circle><circle cx="8" cy="18" r="1.6"></circle><circle cx="16" cy="18" r="1.6"></circle></svg>';
const QUEUE_ICON_DOTS = '<svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="5" r="1.8"></circle><circle cx="12" cy="12" r="1.8"></circle><circle cx="12" cy="19" r="1.8"></circle></svg>';
const QUEUE_ICON_CHEVRON = '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>';

async function loadQueuePresetNames() {
  try {
    const settingsRes = await fetch('/api/settings');
    const settings = await settingsRes.json();
    if (!settings.presetsPath) return;
    const presetsRes = await fetch(`/api/presets?path=${encodeURIComponent(settings.presetsPath)}`);
    presetNames = await presetsRes.json();
  } catch { /* best-effort - the preset-override dropdown just stays empty if this fails */ }
}

function queueKey(item) { return `${item.laneId}::${item.fileName}`; }

function queueBadgeClass(item) {
  if (item.isError) return 'error';
  return item.isResumed ? 'resumed' : 'new';
}

function queueBadgeLabel(item) {
  if (item.isError) return 'Error';
  return item.isResumed ? 'Resumed' : 'New';
}

function renderQueue(items) {
  latestItems = items || [];
  if (draggingKey === null) {
    displayItems = latestItems.map(i => ({ ...i }));
  }
  // A row whose item disappeared from the server snapshot (completed, removed elsewhere) shouldn't
  // leave a dangling open popover referencing it.
  const keys = new Set(displayItems.map(queueKey));
  if (openMenuKey !== null && !keys.has(openMenuKey)) openMenuKey = null;
  if (openPresetKey !== null && !keys.has(openPresetKey)) openPresetKey = null;

  renderQueueList();
}

function renderQueueList() {
  const list = document.getElementById('queueList');
  if (displayItems.length === 0) {
    list.innerHTML = '<div class="queue-empty">Nothing queued.</div>';
    return;
  }

  const prevRects = {};
  [...list.children].forEach(el => { if (el.dataset.key) prevRects[el.dataset.key] = el.getBoundingClientRect(); });

  list.innerHTML = '';

  for (const item of displayItems) {
    const key = queueKey(item);

    if (key === draggingKey) {
      const ph = document.createElement('div');
      ph.dataset.key = key;
      ph.className = 'queue-item-placeholder';
      ph.style.height = (ghostEl ? ghostEl.offsetHeight : 46) + 'px';
      list.appendChild(ph);
      continue;
    }

    const row = document.createElement('div');
    row.className = 'queue-item';
    row.dataset.key = key;
    row.dataset.laneId = item.laneId;
    if (item.isSkipped) row.classList.add('skipped');

    row.innerHTML = `
      ${item.isError ? '' : `<span class="queue-handle">${QUEUE_ICON_GRIP}</span>`}
      <span class="queue-badge ${queueBadgeClass(item)}">${item.isSkipped ? 'Skipped' : queueBadgeLabel(item)}</span>
      <div class="queue-lane">${escapeHtml(item.laneDisplayName)}</div>
      <div class="queue-file">${escapeHtml(item.fileName)}</div>
      <div class="queue-meta">${item.sizeGb.toFixed(2)} GB</div>
      <div class="queue-preset-wrap">
        ${item.isError
          ? `<span class="queue-preset-static">${escapeHtml(item.preset || '-')}</span>`
          : `<button type="button" class="queue-preset-btn${item.isCustomPreset ? ' custom' : ''}">${escapeHtml(item.preset || '-')}${QUEUE_ICON_CHEVRON}</button>`}
      </div>
      ${item.isError ? '' : `<div class="queue-menu-wrap"><button type="button" class="queue-dots-btn" aria-label="Row actions">${QUEUE_ICON_DOTS}</button></div>`}
    `;
    list.appendChild(row);

    if (item.isError) {
      // Error entries don't support skip/reorder/preset-override - they're excluded from
      // processing entirely until removed, and the backend's preset-override endpoint only
      // finds-or-creates a Pending entry, so wiring it up here would silently create a duplicate
      // Pending row alongside the untouched Error one instead of editing it.
      const removeBtn = document.createElement('button');
      removeBtn.textContent = 'Remove';
      removeBtn.addEventListener('click', () => removeErrorQueueEntry(item.laneId, item.fileName));
      row.appendChild(removeBtn);
    } else {
      row.querySelector('.queue-handle').addEventListener('pointerdown', e => startQueueDrag(e, item, row));
      row.querySelector('.queue-dots-btn').addEventListener('click', e => {
        e.stopPropagation();
        openMenuKey = openMenuKey === key ? null : key;
        openPresetKey = null;
        renderQueueList();
      });
      row.querySelector('.queue-preset-btn').addEventListener('click', e => {
        e.stopPropagation();
        openPresetKey = openPresetKey === key ? null : key;
        openMenuKey = null;
        renderQueueList();
      });
    }

    if (openPresetKey === key) {
      const pop = document.createElement('div');
      pop.className = 'queue-popover';
      const options = presetNames.length > 0 ? presetNames : (item.preset ? [item.preset] : []);
      pop.innerHTML = options.map(p =>
        `<div class="queue-popover-item${p === item.preset ? ' active' : ''}" data-preset="${escapeHtml(p)}">${escapeHtml(p)}</div>`
      ).join('') || '<div class="queue-popover-item disabled">No presets found</div>';
      if (item.isCustomPreset) {
        pop.innerHTML += `<div class="queue-popover-item queue-popover-reset" data-preset="">Use lane default</div>`;
      }
      row.querySelector('.queue-preset-wrap').appendChild(pop);
      pop.querySelectorAll('.queue-popover-item:not(.disabled)').forEach(opt => opt.addEventListener('click', async e => {
        e.stopPropagation();
        openPresetKey = null;
        renderQueueList();
        await fetch('/api/run/queue/preset-override', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ laneId: item.laneId, fileName: item.fileName, preset: opt.dataset.preset || null })
        });
        poll();
      }));
    }

    if (openMenuKey === key) {
      const pop = document.createElement('div');
      pop.className = 'queue-popover';
      pop.innerHTML = `
        <div class="queue-popover-item" data-act="skip">${item.isSkipped ? 'Unskip' : 'Skip this pass'}</div>
        <div class="queue-popover-item danger" data-act="remove">Remove from queue</div>
      `;
      row.querySelector('.queue-menu-wrap').appendChild(pop);
      pop.querySelectorAll('.queue-popover-item').forEach(opt => opt.addEventListener('click', async e => {
        e.stopPropagation();
        openMenuKey = null;
        const act = opt.dataset.act;
        if (act === 'skip') {
          await fetch('/api/run/queue/skip', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ laneId: item.laneId, fileName: item.fileName, skipped: !item.isSkipped })
          });
        } else if (act === 'remove') {
          await fetch('/api/run/queue/remove', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ laneId: item.laneId, fileName: item.fileName })
          });
        }
        poll();
      }));
    }
  }

  [...list.children].forEach(el => {
    const prev = prevRects[el.dataset.key];
    if (!prev) return;
    const now = el.getBoundingClientRect();
    const dy = prev.top - now.top;
    if (Math.abs(dy) > 0.5) {
      el.style.transition = 'none';
      el.style.transform = `translateY(${dy}px)`;
      requestAnimationFrame(() => {
        el.style.transition = 'transform 180ms cubic-bezier(.2,.8,.2,1)';
        el.style.transform = '';
      });
    }
  });
}

function startQueueDrag(e, item, row) {
  e.preventDefault();
  row.querySelectorAll('.queue-popover').forEach(p => p.remove());
  openMenuKey = null; openPresetKey = null;

  const rect = row.getBoundingClientRect();
  grabOffsetY = e.clientY - rect.top;
  draggingKey = queueKey(item);

  ghostEl = row.cloneNode(true);
  ghostEl.classList.add('queue-item-ghost');
  ghostEl.style.left = rect.left + 'px';
  ghostEl.style.top = rect.top + 'px';
  ghostEl.style.width = rect.width + 'px';
  document.body.appendChild(ghostEl);

  renderQueueList();

  document.addEventListener('pointermove', onQueueDragMove);
  document.addEventListener('pointerup', onQueueDragEnd);
}

function onQueueDragMove(e) {
  if (!ghostEl) return;
  ghostEl.style.top = (e.clientY - grabOffsetY) + 'px';

  const list = document.getElementById('queueList');
  const draggedIdx = displayItems.findIndex(i => queueKey(i) === draggingKey);
  const draggedLaneId = displayItems[draggedIdx].laneId;

  for (const el of [...list.children]) {
    if (el.dataset.key === draggingKey) continue;
    // Reordering only makes sense within the same lane - each lane's Order is independent, and
    // there's no "move this file to a different lane" operation for a drag to imply.
    if (el.dataset.laneId !== draggedLaneId) continue;

    const r = el.getBoundingClientRect();
    const mid = r.top + r.height / 2;
    const targetIdx = displayItems.findIndex(i => queueKey(i) === el.dataset.key);
    if ((e.clientY < mid && targetIdx < draggedIdx) || (e.clientY > mid && targetIdx > draggedIdx)) {
      const [moved] = displayItems.splice(draggedIdx, 1);
      displayItems.splice(targetIdx, 0, moved);
      renderQueueList();
      break;
    }
  }
}

async function onQueueDragEnd() {
  document.removeEventListener('pointermove', onQueueDragMove);
  document.removeEventListener('pointerup', onQueueDragEnd);

  const list = document.getElementById('queueList');
  const ph = [...list.children].find(el => el.dataset.key === draggingKey);
  if (ph && ghostEl) {
    const target = ph.getBoundingClientRect();
    ghostEl.style.transition = 'top 140ms cubic-bezier(.2,.8,.2,1)';
    ghostEl.style.top = target.top + 'px';
  }

  const draggedItem = displayItems.find(i => queueKey(i) === draggingKey);
  const laneId = draggedItem.laneId;
  const orderedFileNames = displayItems.filter(i => i.laneId === laneId).map(i => i.fileName);

  setTimeout(() => {
    if (ghostEl) { ghostEl.remove(); ghostEl = null; }
    draggingKey = null;
    renderQueueList();
  }, 150);

  await fetch('/api/run/queue/reorder', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ laneId, orderedFileNames })
  });
  poll();
}

async function removeErrorQueueEntry(laneId, fileName) {
  if (!confirm(`Remove '${fileName}' from the queue?\n\nThis only clears its tracked error status - the file itself is left untouched on disk, and a future scan can pick it back up as new.`)) return;

  await fetch('/api/run/queue/remove-error', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ laneId, fileName })
  });
  poll();
}

document.addEventListener('click', () => {
  if (openMenuKey !== null || openPresetKey !== null) {
    openMenuKey = null;
    openPresetKey = null;
    renderQueueList();
  }
});

loadQueuePresetNames();

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

async function poll() {
  const res = await fetch('/api/run/status');
  const s = await res.json();

  toggleIsMonitoring = s.isMonitoring;
  toggleIsStopping = s.isStopping;
  renderToggleButton();
  abortBtn.disabled = !s.isMonitoring && !s.isRunning;
  // Only meaningful while idle between passes - nothing to skip if not monitoring at all, or
  // if a pass is already running right now.
  runNowBtn.disabled = !s.isMonitoring || s.isRunning;
  togglePauseIsRunning = s.isRunning;
  togglePauseIsPaused = s.isPaused;
  renderPauseButton();

  const stateValueEl = document.getElementById('stateValue');
  stateValueEl.textContent = s.isRunning ? 'Running' : (s.isMonitoring ? 'Watching' : 'Idle');
  stateValueEl.classList.toggle('running', s.isRunning);
  document.getElementById('fileLabel').textContent = (s.isRunning && s.laneDisplayName)
    ? `Compressing File in Lane ${s.laneDisplayName}${s.presetName ? ` using preset ${s.presetName}` : ''}`
    : 'Waiting for files';
  document.getElementById('fileValue').textContent = s.isRunning ? (s.fileName || '-') : '-';

  const hasPercent = s.progressPercent !== null && s.progressPercent !== undefined;
  document.getElementById('progressFill').style.width = `${hasPercent ? s.progressPercent : 0}%`;

  const subParts = [];
  if (s.fileTotal) subParts.push(`${s.fileIndex} of ${s.fileTotal}`);
  if (hasPercent) subParts.push(`${s.progressPercent.toFixed(1)}%`);
  if (s.progressFps) subParts.push(`${s.progressFps.toFixed(1)} fps`);
  if (s.progressEta) subParts.push(`ETA ${s.progressEta}`);
  document.getElementById('progressSub').textContent = subParts.join(' · ');

  renderQueue(s.upNext);
  renderLog(s.recentLogLines);
}

poll();
setInterval(poll, 1500);
