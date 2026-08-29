function renderNav(activePage) {
  const links = [
    { href: '/index.html', label: 'Settings', page: 'settings' },
    { href: '/lanes.html', label: 'Lanes', page: 'lanes' },
    { href: '/monitor.html', label: 'Monitor', page: 'monitor' },
    { href: '/history.html', label: 'History', page: 'history' }
  ];

  const header = document.createElement('header');
  header.innerHTML = '<img src="/assets/logo.png" alt="Compressarr" /><h1>Compressarr</h1>';

  const nav = document.createElement('nav');
  nav.innerHTML = links
    .map(l => `<a href="${l.href}"${l.page === activePage ? ' class="active"' : ''}>${l.label}</a>`)
    .join('');

  document.body.prepend(nav);
  document.body.prepend(header);
}
