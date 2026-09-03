renderNav('monitor');

const startBtn = document.getElementById('startBtn');
const stopBtn = document.getElementById('stopBtn');
const runNowBtn = document.getElementById('runNowBtn');
const abortBtn = document.getElementById('abortBtn');
const monitoringState = document.getElementById('monitoringState');
const countdownEl = document.getElementById('countdown');
const logPanel = document.getElementById('log-panel');

let nextRunAtMs = null;
const STOPPING_MESSAGE = 'Stopping monitor after current task completes';

startBtn.addEventListener('click', async () => {
  // Reflect the click immediately - a real conversion pass can start on the server within
  // milliseconds, well before the next poll() would otherwise refresh this panel, so the log
  // window needs to say "starting" right now rather than sitting on stale content until then.
  monitoringState.textContent = 'Monitoring is ON';
  startBtn.disabled = true;
  stopBtn.disabled = false;
  renderLog([{ text: 'Starting monitoring...', severity: 'Info' }]);

  await fetch('/api/run/start', { method: 'POST' });
  poll();
});

stopBtn.addEventListener('click', async () => {
  // Reflect the click immediately, zero-latency, on this surface - StopAsync doesn't resolve
  // until the in-flight file finishes converting, which can be minutes away. poll() below picks
  // up the server's own isStopping flag (shared with the tray icon) within 1.5s regardless, so a
  // stop requested from either surface shows up on both - this is just to not even wait that long
  // for the surface that actually clicked.
  monitoringState.textContent = STOPPING_MESSAGE;
  stopBtn.disabled = true;

  await fetch('/api/run/stop', { method: 'POST' });
  poll();
});

runNowBtn.addEventListener('click', async () => {
  // Stop the countdown immediately in the UI - the server-side trigger races the remaining
  // delay, but there's no reason to keep ticking a countdown the click just made moot.
  nextRunAtMs = null;
  renderCountdown();
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

function renderCountdown() {
  if (nextRunAtMs === null) {
    countdownEl.textContent = '';
    return;
  }
  const secondsLeft = Math.max(0, Math.ceil((nextRunAtMs - Date.now()) / 1000));
  countdownEl.textContent = `Next Run in: ${secondsLeft} Seconds`;
}

async function poll() {
  const res = await fetch('/api/run/status');
  const s = await res.json();

  if (s.isStopping) {
    // Authoritative server state - true regardless of which surface (this page or the tray
    // icon) actually requested the stop.
    monitoringState.textContent = STOPPING_MESSAGE;
    startBtn.disabled = true;
    stopBtn.disabled = true;
  } else {
    monitoringState.textContent = s.isMonitoring ? 'Monitoring is ON' : 'Monitoring is OFF';
    startBtn.disabled = s.isMonitoring;
    stopBtn.disabled = !s.isMonitoring;
  }
  abortBtn.disabled = !s.isMonitoring && !s.isRunning;
  // Only meaningful while idle between passes - nothing to skip if not monitoring at all, or
  // if a pass is already running right now.
  runNowBtn.disabled = !s.isMonitoring || s.isRunning;

  nextRunAtMs = (s.isMonitoring && s.secondsUntilNextRun !== null && s.secondsUntilNextRun !== undefined)
    ? Date.now() + s.secondsUntilNextRun * 1000
    : null;
  renderCountdown();

  document.getElementById('stateValue').textContent = s.isRunning ? 'Converting' : (s.isMonitoring ? 'Watching' : 'Idle');
  document.getElementById('laneValue').textContent = s.laneDisplayName || '-';
  document.getElementById('fileValue').textContent = s.fileName || '-';

  const hasPercent = s.progressPercent !== null && s.progressPercent !== undefined;
  document.getElementById('progressFill').style.width = `${hasPercent ? s.progressPercent : 0}%`;

  const subParts = [];
  if (s.fileTotal) subParts.push(`${s.fileIndex} of ${s.fileTotal}`);
  if (hasPercent) subParts.push(`${s.progressPercent.toFixed(1)}%`);
  if (s.progressFps) subParts.push(`${s.progressFps.toFixed(1)} fps`);
  if (s.progressEta) subParts.push(`ETA ${s.progressEta}`);
  document.getElementById('progressSub').textContent = subParts.join(' · ');

  // Whole numbers only - a percent to one decimal place reads as false precision for a value
  // that's already sampled/smoothed server-side, and it isn't reproducible reading to reading.
  document.getElementById('cpuValue').textContent = (s.cpuUsagePercent === null || s.cpuUsagePercent === undefined) ? 'unavailable' : `${Math.round(s.cpuUsagePercent)}%`;

  renderQueue(s.upNext);
  renderLog(s.recentLogLines);
}

poll();
setInterval(poll, 1500);
setInterval(renderCountdown, 1000);
