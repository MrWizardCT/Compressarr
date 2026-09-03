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

function renderQueue(items) {
  const list = document.getElementById('queueList');
  if (!items || items.length === 0) {
    list.innerHTML = '<div class="queue-empty">Nothing queued.</div>';
    return;
  }

  list.innerHTML = items
    .map(item => `<div class="queue-item">
      <span class="queue-badge ${item.isResumed ? 'resumed' : 'new'}">${item.isResumed ? 'Resumed' : 'New'}</span>
      <div class="queue-lane">${escapeHtml(item.laneDisplayName)}</div>
      <div class="queue-file">${escapeHtml(item.fileName)}</div>
      <div class="queue-meta">${item.sizeGb.toFixed(2)} GB &middot; ${escapeHtml(item.preset || '-')}</div>
    </div>`)
    .join('');
}

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
