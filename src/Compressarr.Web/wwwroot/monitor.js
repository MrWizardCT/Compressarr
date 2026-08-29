renderNav('monitor');

const startBtn = document.getElementById('startBtn');
const stopBtn = document.getElementById('stopBtn');
const monitoringState = document.getElementById('monitoringState');
const logPanel = document.getElementById('log-panel');

startBtn.addEventListener('click', async () => {
  await fetch('/api/run/start', { method: 'POST' });
  poll();
});

stopBtn.addEventListener('click', async () => {
  await fetch('/api/run/stop', { method: 'POST' });
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

async function poll() {
  const res = await fetch('/api/run/status');
  const s = await res.json();

  monitoringState.textContent = s.isMonitoring ? 'Monitoring is ON' : 'Monitoring is OFF';
  startBtn.disabled = s.isMonitoring;
  stopBtn.disabled = !s.isMonitoring;

  document.getElementById('stateValue').textContent = s.isRunning ? 'Converting' : (s.isMonitoring ? 'Watching' : 'Idle');
  document.getElementById('laneValue').textContent = s.laneDisplayName || '-';
  document.getElementById('fileValue').textContent = s.fileName || '-';
  document.getElementById('progressValue').textContent = s.fileTotal ? `${s.fileIndex} of ${s.fileTotal}` : '-';
  document.getElementById('cpuValue').textContent = (s.cpuUsagePercent === null || s.cpuUsagePercent === undefined) ? 'unavailable' : `${s.cpuUsagePercent}%`;

  renderLog(s.recentLogLines);
}

poll();
setInterval(poll, 1500);
