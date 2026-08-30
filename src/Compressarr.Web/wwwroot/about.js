renderNav('about');

const versionText = document.getElementById('versionText');
const updateStatus = document.getElementById('updateStatus');
const checkUpdateBtn = document.getElementById('checkUpdateBtn');

const downloadUpdateBtn = document.getElementById('downloadUpdateBtn');

const hbVersionText = document.getElementById('hbVersionText');
const hbUpdateStatus = document.getElementById('hbUpdateStatus');
const hbCheckUpdateBtn = document.getElementById('hbCheckUpdateBtn');
const hbDownloadUpdateBtn = document.getElementById('hbDownloadUpdateBtn');

async function loadAbout() {
  const res = await fetch('/api/about');
  const dto = await res.json();
  versionText.textContent = `Version ${dto.version}`;
  document.getElementById('repoLink').href = dto.repoUrl;
}

async function loadHandBrakeVersion() {
  const res = await fetch('/api/handbrake/installed-version');
  const dto = await res.json();
  hbVersionText.textContent = dto.version || 'Not found';
}

hbCheckUpdateBtn.addEventListener('click', async () => {
  hbCheckUpdateBtn.disabled = true;
  hbUpdateStatus.textContent = 'Checking...';
  hbDownloadUpdateBtn.classList.add('hidden');

  try {
    const res = await fetch('/api/handbrake/latest-release');
    const dto = await res.json();

    if (!dto.available) {
      hbUpdateStatus.textContent = "Couldn't check for updates on this platform.";
    } else {
      const installedRes = await fetch('/api/handbrake/installed-version');
      const installedDto = await installedRes.json();
      const installed = installedDto.version;

      if (installed && installed === dto.version) {
        hbUpdateStatus.textContent = `You're up to date (latest: ${dto.version}).`;
      } else {
        hbUpdateStatus.textContent = installed
          ? `A newer version is available: ${dto.version} (installed: ${installed}).`
          : `Latest available: ${dto.version}.`;
        hbDownloadUpdateBtn.href = dto.releaseUrl;
        hbDownloadUpdateBtn.classList.remove('hidden');
      }
    }
  } catch (err) {
    hbUpdateStatus.textContent = `Couldn't check for updates: ${err.message}`;
  } finally {
    hbCheckUpdateBtn.disabled = false;
  }
});

checkUpdateBtn.addEventListener('click', async () => {
  checkUpdateBtn.disabled = true;
  updateStatus.textContent = 'Checking...';
  downloadUpdateBtn.classList.add('hidden');

  try {
    const res = await fetch('/api/about/check-update');
    const dto = await res.json();

    if (!dto.checkedOk) {
      updateStatus.textContent = `Couldn't check for updates: ${dto.error}`;
    } else if (dto.hasUpdate) {
      updateStatus.textContent = `A new version is available: ${dto.latestVersion}.`;
      downloadUpdateBtn.href = dto.releaseUrl;
      downloadUpdateBtn.classList.remove('hidden');
    } else {
      updateStatus.textContent = `You're up to date (latest release: ${dto.latestVersion}).`;
    }
  } catch (err) {
    updateStatus.textContent = `Couldn't check for updates: ${err.message}`;
  } finally {
    checkUpdateBtn.disabled = false;
  }
});

loadAbout();
loadHandBrakeVersion();
