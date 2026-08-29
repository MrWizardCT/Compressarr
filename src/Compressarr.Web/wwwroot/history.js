renderNav('history');

const historySection = document.getElementById('historySection');
const toggleBtn = document.getElementById('toggleHistoryBtn');

function applyVisibility() {
  const hidden = localStorage.getItem('compressarr.historyHidden') === 'true';
  historySection.style.display = hidden ? 'none' : '';
  toggleBtn.textContent = hidden ? 'Show History' : 'Hide History';
}

toggleBtn.addEventListener('click', () => {
  const hidden = localStorage.getItem('compressarr.historyHidden') === 'true';
  localStorage.setItem('compressarr.historyHidden', (!hidden).toString());
  applyVisibility();
});

function row(label, bucket) {
  const saved = bucket.beforeGb > 0 ? Math.round((1 - bucket.afterGb / bucket.beforeGb) * 1000) / 10 : 0;
  return `<tr><td>${label}</td><td>${bucket.fileCount}</td><td>${bucket.beforeGb.toFixed(2)} GB</td><td>${bucket.afterGb.toFixed(2)} GB</td><td>${saved}%</td></tr>`;
}

async function loadHistory() {
  const res = await fetch('/api/history');
  const h = await res.json();

  document.getElementById('totalRunCount').textContent = h.totalRunCount;
  document.getElementById('historyRows').innerHTML = [
    row('Today', h.today),
    row('Last 7 Days', h.last7Days),
    row('Last 30 Days', h.last30Days),
    row('Last Year', h.lastYear),
    row('All Time', h.allTime)
  ].join('');
}

function reportRow(entry) {
  const saved = entry.beforeGb > 0 ? Math.round((1 - entry.afterGb / entry.beforeGb) * 1000) / 10 : 0;
  const date = new Date(entry.date).toLocaleDateString();
  const url = `/api/reports/${encodeURIComponent(entry.reportFileName)}`;
  return `<tr>
    <td>${entry.runNumber}</td>
    <td><a href="${url}" target="_blank" rel="noopener">${date} report</a></td>
    <td>${entry.fileCount}</td>
    <td>${entry.beforeGb.toFixed(2)} GB</td>
    <td>${entry.afterGb.toFixed(2)} GB</td>
    <td>${saved}%</td>
  </tr>`;
}

async function loadReports() {
  const res = await fetch('/api/history/reports');
  const entries = await res.json();

  const reportsStatus = document.getElementById('reportsStatus');
  if (entries.length === 0) {
    document.getElementById('reportsRows').innerHTML = '';
    reportsStatus.textContent = 'No reports within the current retention period.';
    return;
  }

  reportsStatus.textContent = '';
  document.getElementById('reportsRows').innerHTML = entries.map(reportRow).join('');
}

applyVisibility();
loadHistory();
loadReports();
