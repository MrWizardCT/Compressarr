const THEME_STORAGE_KEY = 'compressarr.theme';

function getPreferredTheme() {
  const saved = localStorage.getItem(THEME_STORAGE_KEY);
  if (saved === 'light' || saved === 'dark') return saved;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function applyTheme(theme) {
  document.documentElement.setAttribute('data-theme', theme);
}

function setTheme(theme) {
  localStorage.setItem(THEME_STORAGE_KEY, theme);
  applyTheme(theme);
}

// Line-style icons matching the approved sidebar mockup - kept as plain shape primitives
// (circles/rects/polylines), not hand-authored illustration paths.
const NAV_ICONS = {
  monitor: '<polygon points="5 3 19 12 5 21 5 3"></polygon>',
  lanes: '<rect x="3" y="4" width="7" height="16" rx="1"></rect><rect x="14" y="4" width="7" height="10" rx="1"></rect>',
  settings: '<circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"></path>',
  notifications: '<path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9"></path><path d="M13.73 21a2 2 0 0 1-3.46 0"></path>',
  history: '<circle cx="12" cy="12" r="9"></circle><polyline points="12 7 12 12 15.5 14"></polyline>',
  about: '<circle cx="12" cy="12" r="9"></circle><line x1="12" y1="16" x2="12" y2="11.5"></line><circle cx="12" cy="8" r="0.6" fill="currentColor" stroke="none"></circle>',
  // Solid and always red (not currentColor) - unlike every other nav icon, this one shouldn't
  // fade/recolor with hover or active state, the same way a "Sponsor"/donate heart never does.
  donate: '<path d="M12 21s-6.72-4.35-9.34-8.02C.9 10.49 1.02 7.49 3.42 5.6c1.88-1.5 4.46-1.2 6 .5L12 9l2.58-2.9c1.54-1.7 4.12-2 6-.5 2.4 1.89 2.52 4.89.72 7.38C18.72 16.65 12 21 12 21z" fill="#ef4444" stroke="none"></path>'
};

function navIcon(name) {
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${NAV_ICONS[name]}</svg>`;
}

const CPU_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="6" y="6" width="12" height="12" rx="1"></rect><line x1="6" y1="10" x2="3" y2="10"></line><line x1="6" y1="14" x2="3" y2="14"></line><line x1="18" y1="10" x2="21" y2="10"></line><line x1="18" y1="14" x2="21" y2="14"></line><line x1="10" y1="6" x2="10" y2="3"></line><line x1="14" y1="6" x2="14" y2="3"></line><line x1="10" y1="18" x2="10" y2="21"></line><line x1="14" y1="18" x2="14" y2="21"></line></svg>';
const UPDATE_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"></circle><polyline points="16 12 12 8 8 12"></polyline><line x1="12" y1="16" x2="12" y2="8"></line></svg>';

function renderNav(activePage) {
  const links = [
    { href: '/monitor.html', label: 'Monitor', page: 'monitor', icon: 'monitor' },
    { href: '/lanes.html', label: 'Lanes', page: 'lanes', icon: 'lanes' },
    { href: '/index.html', label: 'Settings', page: 'settings', icon: 'settings' },
    { href: '/notifications.html', label: 'Notifications', page: 'notifications', icon: 'notifications' },
    { href: '/history.html', label: 'History', page: 'history', icon: 'history' },
    { href: '/about.html', label: 'About', page: 'about', icon: 'about' }
  ];

  // Rendered as a second, separate <nav> pinned to the bottom of the sidebar (see
  // .sidebar-nav-bottom's margin-top: auto) rather than appended to the main list above - a
  // Donate link belongs visually apart from the app's actual pages, not mixed into a growing list
  // of them.
  const bottomLinks = [
    { href: '/donate.html', label: 'Donate', page: 'donate', icon: 'donate' }
  ];

  // The page's real content is already sitting in <main> - moved into the new layout below
  // rather than rebuilt, so none of the per-page HTML/JS needs to change for this.
  const existingMain = document.querySelector('body > main');

  const appShell = document.createElement('div');
  appShell.className = 'app-shell';

  const sidebar = document.createElement('aside');
  sidebar.className = 'sidebar';
  sidebar.innerHTML = `
    <div class="sidebar-brand">
      <img src="/assets/logo.png" alt="Compressarr" />
      <span class="name">Compressarr</span>
    </div>
    <nav class="sidebar-nav">
      ${links.map(l => `<a href="${l.href}"${l.page === activePage ? ' class="active"' : ''}>${navIcon(l.icon)}<span>${l.label}</span>${l.page === 'history' ? '<span class="sidebar-badges" id="historyBadges"></span>' : ''}</a>`).join('')}
    </nav>
    <nav class="sidebar-nav sidebar-nav-bottom">
      ${bottomLinks.map(l => `<a href="${l.href}"${l.page === activePage ? ' class="active"' : ''}>${navIcon(l.icon)}<span>${l.label}</span></a>`).join('')}
    </nav>
  `;

  const mainCol = document.createElement('div');
  mainCol.className = 'main-col';

  // A page's own <h2 class="page-title"> (if it has one) becomes the toolbar title and is
  // removed from the content below it, so it isn't shown twice - falls back to this page's own
  // nav label for pages that never had a title heading of their own (Settings, About).
  const titleEl = existingMain ? existingMain.querySelector('.page-title') : null;
  const titleText = titleEl ? titleEl.textContent : (links.find(l => l.page === activePage)?.label ?? '');
  if (titleEl) titleEl.remove();

  // A page's own primary action buttons (if marked) move into the toolbar too, right next to
  // the title - pages without one (About) just get a title with no actions.
  const actionsEl = existingMain ? existingMain.querySelector('.page-actions') : null;

  const currentTheme = getPreferredTheme();
  const toolbar = document.createElement('div');
  toolbar.className = 'toolbar';
  toolbar.innerHTML = `
    <h1>${titleText}</h1>
    <span class="toolbar-spacer"></span>
    <div class="toolbar-global-status">
      <a id="updateLink" class="toolbar-update hidden" target="_blank" rel="noopener" title="Update available">${UPDATE_ICON}</a>
      <span class="status-dot" id="statusDot"></span>
      <span id="monitoringState"></span>
      <span id="countdown" class="countdown"></span>
      <span class="toolbar-cpu" title="CPU Usage">${CPU_ICON}<span id="cpuValue">-</span></span>
    </div>
    <label class="theme-toggle">
      <span>&#9728;</span>
      <span class="theme-switch">
        <input type="checkbox" id="themeToggleInput" ${currentTheme === 'dark' ? 'checked' : ''} />
        <span class="slider"></span>
      </span>
      <span>&#127769;</span>
    </label>
  `;
  // The spacer takes the room between the title and the global status cluster - a page's own
  // action buttons (if any) sit inside it, left-aligned, same as the title. Pages with no
  // actions (About) just leave it empty.
  if (actionsEl) toolbar.querySelector('.toolbar-spacer').appendChild(actionsEl);

  // Wraps the page's remaining content in .content-inner (see styles.css) so <main> itself can
  // span the full column width for scrolling while the actual content still visually caps/centers
  // at 1080px, same as before.
  if (existingMain) {
    const contentInner = document.createElement('div');
    contentInner.className = 'content-inner';
    while (existingMain.firstChild) contentInner.appendChild(existingMain.firstChild);
    existingMain.appendChild(contentInner);
  }

  mainCol.appendChild(toolbar);
  if (existingMain) mainCol.appendChild(existingMain);

  appShell.appendChild(sidebar);
  appShell.appendChild(mainCol);
  document.body.prepend(appShell);

  document.getElementById('themeToggleInput').addEventListener('change', e => {
    setTheme(e.target.checked ? 'dark' : 'light');
  });

  renderHistoryBadges();
  startGlobalStatusPoll();
  checkForUpdate();
}

// Update indicator in the toolbar - visible on every page, same reasoning as the monitoring
// status cluster above. Checks GitHub at most once a day (cached in localStorage, keyed by when
// it was last checked) rather than on every page load; once shown, it keeps showing on every
// subsequent page load without waiting for the next check, and only goes away once a later daily
// check finds the installed version has caught up - there's no separate "dismiss" for it.
const UPDATE_CHECK_STORAGE_KEY = 'compressarr.updateCheck';
const UPDATE_CHECK_INTERVAL_MS = 24 * 60 * 60 * 1000;

function renderUpdateIndicator(cached) {
  const link = document.getElementById('updateLink');
  if (!link) return; // toolbar not built yet, or this raced a navigation

  if (cached && cached.hasUpdate && cached.releaseUrl) {
    link.href = cached.releaseUrl;
    link.classList.remove('hidden');
  } else {
    link.classList.add('hidden');
  }
}

function checkForUpdate() {
  let cached = null;
  try { cached = JSON.parse(localStorage.getItem(UPDATE_CHECK_STORAGE_KEY) || 'null'); } catch { /* corrupt/old value - treat as absent */ }

  // Render from cache immediately - don't make every page load wait on a network round-trip
  // just to show an indicator that, most days, won't even need re-checking.
  renderUpdateIndicator(cached);

  const dueForRecheck = !cached || (Date.now() - cached.checkedAt) > UPDATE_CHECK_INTERVAL_MS;
  if (!dueForRecheck) return;

  fetch('/api/about/check-update')
    .then(res => res.json())
    .then(body => {
      const fresh = {
        checkedAt: Date.now(),
        hasUpdate: !!body.hasUpdate,
        releaseUrl: body.releaseUrl || '',
        latestVersion: body.latestVersion || ''
      };
      localStorage.setItem(UPDATE_CHECK_STORAGE_KEY, JSON.stringify(fresh));
      renderUpdateIndicator(fresh);
    })
    .catch(() => {}); // best-effort - GitHub unreachable today just means keep showing whatever was cached before
}

// Counts runs (within the History page's own retention window) that had at least one error or
// at least one post-process warning - same data /api/history/reports already exposes, just
// aggregated here since every page (not only History) shows the sidebar. Best-effort: if this
// fails, the badges just don't appear rather than breaking the rest of the nav.
function renderHistoryBadges() {
  const holder = document.getElementById('historyBadges');
  if (!holder) return;

  fetch('/api/history/reports')
    .then(res => res.json())
    .then(entries => {
      const errorRuns = entries.filter(e => e.errorCount > 0).length;
      const warningRuns = entries.filter(e => e.warningCount > 0).length;
      const parts = [];
      if (errorRuns > 0) parts.push(`<span class="sidebar-badge err" title="${errorRuns} run(s) with errors">${errorRuns}</span>`);
      if (warningRuns > 0) parts.push(`<span class="sidebar-badge warn" title="${warningRuns} run(s) with warnings">${warningRuns}</span>`);
      holder.innerHTML = parts.join('');
    })
    .catch(() => {});
}

// Monitoring status, countdown, and CPU are shown in the toolbar on every page (not just
// Monitor), so this owns its own independent poll of /api/run/status rather than relying on
// monitor.js, which only loads on monitor.html. On Monitor itself, monitor.js still owns the
// queue/log/current-file detail and button enable/disable from its own poll of the same
// endpoint - two lightweight polls of the same cheap endpoint, not worth sharing one across
// scripts for.
let globalNextRunAtMs = null;
const GLOBAL_STOPPING_MESSAGE = 'Stopping monitor after current task completes';

function renderGlobalCountdown() {
  const el = document.getElementById('countdown');
  if (!el) return;
  if (globalNextRunAtMs === null) {
    el.textContent = '';
    return;
  }
  const secondsLeft = Math.max(0, Math.ceil((globalNextRunAtMs - Date.now()) / 1000));
  el.textContent = `Next Pass in: ${secondsLeft} Seconds`;
}

async function pollGlobalStatus() {
  const dot = document.getElementById('statusDot');
  const stateEl = document.getElementById('monitoringState');
  const cpuEl = document.getElementById('cpuValue');
  if (!dot || !stateEl || !cpuEl) return; // toolbar not built yet, or this tick raced a navigation

  let s;
  try {
    const res = await fetch('/api/run/status');
    s = await res.json();
  } catch {
    return; // best-effort - leave the last-known values showing rather than blank them out
  }

  const isActive = s.isMonitoring && !s.isStopping;
  const isPaused = isActive && s.isPaused;
  dot.classList.toggle('on', isActive && !isPaused);
  dot.classList.toggle('off', !isActive);
  dot.classList.toggle('paused', isPaused);

  stateEl.textContent = s.isStopping
    ? GLOBAL_STOPPING_MESSAGE
    : (s.isMonitoring ? `Monitoring is ON${isPaused ? ': Paused' : (s.isRunning ? ': Running' : '')}` : 'Monitoring is OFF');

  globalNextRunAtMs = (s.isMonitoring && s.secondsUntilNextRun !== null && s.secondsUntilNextRun !== undefined)
    ? Date.now() + s.secondsUntilNextRun * 1000
    : null;
  renderGlobalCountdown();

  cpuEl.textContent = (s.cpuUsagePercent === null || s.cpuUsagePercent === undefined) ? 'unavailable' : `${Math.round(s.cpuUsagePercent)}%`;
}

function startGlobalStatusPoll() {
  pollGlobalStatus();
  setInterval(pollGlobalStatus, 1500);
  setInterval(renderGlobalCountdown, 1000);
}

// Applied immediately (before renderNav runs) so the page never flashes the wrong theme.
applyTheme(getPreferredTheme());
