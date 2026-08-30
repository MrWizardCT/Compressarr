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

function renderNav(activePage) {
  const links = [
    { href: '/monitor.html', label: 'Monitor', page: 'monitor' },
    { href: '/index.html', label: 'Settings', page: 'settings' },
    { href: '/lanes.html', label: 'Lanes', page: 'lanes' },
    { href: '/history.html', label: 'History', page: 'history' }
  ];

  const header = document.createElement('header');
  const currentTheme = getPreferredTheme();
  header.innerHTML = `
    <img src="/assets/logo.png" alt="Compressarr" />
    <h1>Compressarr</h1>
    <div class="theme-toggle">
      <span>&#9728;</span>
      <label class="theme-switch">
        <input type="checkbox" id="themeToggleInput" ${currentTheme === 'dark' ? 'checked' : ''} />
        <span class="slider"></span>
      </label>
      <span>&#127769;</span>
    </div>
  `;

  const nav = document.createElement('nav');
  nav.innerHTML = links
    .map(l => `<a href="${l.href}"${l.page === activePage ? ' class="active"' : ''}>${l.label}</a>`)
    .join('') +
    `<a href="/about.html" class="nav-right${activePage === 'about' ? ' active' : ''}">About</a>`;

  document.body.prepend(nav);
  document.body.prepend(header);

  document.getElementById('themeToggleInput').addEventListener('change', e => {
    setTheme(e.target.checked ? 'dark' : 'light');
  });
}

// Applied immediately (before renderNav runs) so the page never flashes the wrong theme.
applyTheme(getPreferredTheme());
