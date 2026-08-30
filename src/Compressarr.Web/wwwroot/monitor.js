renderNav('monitor');

const startBtn = document.getElementById('startBtn');
const stopBtn = document.getElementById('stopBtn');
const runNowBtn = document.getElementById('runNowBtn');
const abortBtn = document.getElementById('abortBtn');
const monitoringState = document.getElementById('monitoringState');
const countdownEl = document.getElementById('countdown');
const logPanel = document.getElementById('log-panel');

let nextRunAtMs = null;

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
  logPanel.innerHTML = lines
    .map(l => `<div class="log-line${l.severity === 'Error' ? ' error' : ''}">${escapeHtml(l.text)}</div>`)
    .join('');
  logPanel.scrollTop = logPanel.scrollHeight;
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

  monitoringState.textContent = s.isMonitoring ? 'Monitoring is ON' : 'Monitoring is OFF';
  startBtn.disabled = s.isMonitoring;
  stopBtn.disabled = !s.isMonitoring;
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

  let progressText = s.fileTotal ? `${s.fileIndex} of ${s.fileTotal}` : '-';
  if (s.progressPercent !== null && s.progressPercent !== undefined) {
    progressText += ` (${s.progressPercent.toFixed(1)}%`;
    if (s.progressFps) progressText += `, ${s.progressFps.toFixed(1)} fps`;
    if (s.progressEta) progressText += `, ETA ${s.progressEta}`;
    progressText += ')';
  }
  document.getElementById('progressValue').textContent = progressText;

  document.getElementById('cpuValue').textContent = (s.cpuUsagePercent === null || s.cpuUsagePercent === undefined) ? 'unavailable' : `${s.cpuUsagePercent}%`;

  renderLog(s.recentLogLines);
}

poll();
setInterval(poll, 1500);
setInterval(renderCountdown, 1000);
