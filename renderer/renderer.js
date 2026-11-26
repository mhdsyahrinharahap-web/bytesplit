(() => {
  const api = window.bytesplit;

  const state = {
    tasks: [],
    selectedId: null,
    filterType: 'status',
    filterValue: 'all',
    defaultOutputDir: '',
    speedLimitBps: 0,
    clipboardMonitor: true,
    theme: localStorage.getItem('bytesplit-theme') || 'light',
  };

  const el = {
    tbody: document.getElementById('downloadTbody'),
    emptyState: document.getElementById('emptyState'),
    btnAdd: document.getElementById('btnAdd'),
    btnPause: document.getElementById('btnPause'),
    btnResume: document.getElementById('btnResume'),
    btnRefreshLink: document.getElementById('btnRefreshLink'),
    btnRemove: document.getElementById('btnRemove'),
    chkClipboardMonitor: document.getElementById('chkClipboardMonitor'),
    selSpeedLimit: document.getElementById('selSpeedLimit'),
    btnBaseFolder: document.getElementById('btnBaseFolder'),
    btnTheme: document.getElementById('btnTheme'),
    btnAbout: document.getElementById('btnAbout'),
    statusSummary: document.getElementById('statusSummary'),
    statusFolder: document.getElementById('statusFolder'),
    detailTitle: document.getElementById('detailTitle'),
    detailMeta: document.getElementById('detailMeta'),
    segmentBar: document.getElementById('segmentBar'),
    detailUrl: document.getElementById('detailUrl'),
    contextMenu: document.getElementById('contextMenu'),
    // Add Modal
    addModal: document.getElementById('addModal'),
    addForm: document.getElementById('addForm'),
    clipboardBanner: document.getElementById('clipboardBanner'),
    inpUrl: document.getElementById('inpUrl'),
    inpFilename: document.getElementById('inpFilename'),
    inpOutputDir: document.getElementById('inpOutputDir'),
    selConnections: document.getElementById('selConnections'),
    chkAutoCategorize: document.getElementById('chkAutoCategorize'),
    btnPasteUrl: document.getElementById('btnPasteUrl'),
    btnChooseModalFolder: document.getElementById('btnChooseModalFolder'),
    addError: document.getElementById('addError'),
    // Refresh Link Modal
    refreshModal: document.getElementById('refreshModal'),
    refreshForm: document.getElementById('refreshForm'),
    refreshFileLabel: document.getElementById('refreshFileLabel'),
    inpRefreshUrl: document.getElementById('inpRefreshUrl'),
    btnPasteRefreshUrl: document.getElementById('btnPasteRefreshUrl'),
    refreshError: document.getElementById('refreshError'),
    // About Modal
    aboutModal: document.getElementById('aboutModal'),
    aboutDescription: document.getElementById('aboutDescription'),
    aboutVersion: document.getElementById('aboutVersion'),
    aboutDeveloper: document.getElementById('aboutDeveloper'),
  };

  function applyTheme(theme) {
    state.theme = theme === 'dark' ? 'dark' : 'light';
    document.documentElement.setAttribute('data-theme', state.theme);
    localStorage.setItem('bytesplit-theme', state.theme);
  }

  function formatBytes(bytes) {
    if (!Number.isFinite(bytes) || bytes <= 0) return '-';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let idx = 0;
    let val = bytes;
    while (val >= 1024 && idx < units.length - 1) {
      val /= 1024;
      idx += 1;
    }
    return `${val.toFixed(idx === 0 ? 0 : 2)} ${units[idx]}`;
  }

  function formatSpeed(bps) {
    if (!Number.isFinite(bps) || bps <= 0) return '0.00 MB/s';
    const mb = bps / (1024 * 1024);
    if (mb >= 1) return `${mb.toFixed(2)} MB/s`;
    return `${(bps / 1024).toFixed(1)} KB/s`;
  }

  function formatEta(sec) {
    if (!Number.isFinite(sec) || sec <= 0) return '';
    const s = Math.round(sec);
    if (s < 60) return `${s}d`;
    const m = Math.floor(s / 60);
    const remS = s % 60;
    if (m < 60) return `${m}m ${remS}d`;
    const h = Math.floor(m / 60);
    return `${h}j ${m % 60}m`;
  }

  function getTaskProgress(task) {
    if (task.status === 'completed') return 100;
    const total = Number(task.totalSize) || 0;
    const done = Number(task.downloaded) || 0;
    if (total <= 0) return 0;
    return Math.min(100, Math.max(0, Math.round((done / total) * 100)));
  }

  function getActiveConnCount(task) {
    if (Array.isArray(task.chunks) && task.chunks.length > 0) {
      if (task.status === 'downloading') {
        const active = task.chunks.filter((c) => c.status === 'downloading').length;
        return active > 0 ? `${active}/${task.chunks.length}` : String(task.chunks.length);
      }
      return String(task.chunks.length);
    }
    return String(task.connections || '-');
  }

  function statusLabel(status) {
    const map = {
      probing: 'Memeriksa...',
      queued: 'Antrean',
      downloading: 'Mengunduh',
      paused: 'Dijeda',
      completed: 'Selesai',
      error: 'Gagal',
      canceled: 'Dibatalkan',
    };
    return map[status] || status || '-';
  }

  function normalizeCategory(cat) {
    if (!cat || typeof cat !== 'string') return 'Other';
    const map = {
      compressed: 'Compressed',
      documents: 'Documents',
      music: 'Music',
      programs: 'Programs',
      video: 'Video',
      general: 'Other',
      other: 'Other',
    };
    return map[cat.trim().toLowerCase()] || 'Other';
  }

  function getFilteredTasks() {
    return state.tasks.filter((task) => {
      if (state.filterType === 'status') {
        if (state.filterValue === 'active') {
          return ['downloading', 'probing', 'queued'].includes(task.status);
        }
        if (state.filterValue === 'completed') {
          return task.status === 'completed';
        }
        return true;
      }
      if (state.filterType === 'category') {
        return normalizeCategory(task.category) === state.filterValue;
      }
      return true;
    });
  }

  function getSelectedTask() {
    return state.tasks.find((t) => t.id === state.selectedId) || null;
  }

  function updateSidebarCounts() {
    const counts = {
      all: state.tasks.length,
      active: 0,
      completed: 0,
      Compressed: 0,
      Documents: 0,
      Music: 0,
      Programs: 0,
      Video: 0,
      Other: 0,
    };

    for (const t of state.tasks) {
      if (['downloading', 'probing', 'queued'].includes(t.status)) counts.active += 1;
      if (t.status === 'completed') counts.completed += 1;
      const cat = normalizeCategory(t.category);
      counts[cat] += 1;
    }

    document.getElementById('countAll').textContent = String(counts.all);
    document.getElementById('countActive').textContent = String(counts.active);
    document.getElementById('countCompleted').textContent = String(counts.completed);
    document.getElementById('countCompressed').textContent = String(counts.Compressed);
    document.getElementById('countDocuments').textContent = String(counts.Documents);
    document.getElementById('countMusic').textContent = String(counts.Music);
    document.getElementById('countPrograms').textContent = String(counts.Programs);
    document.getElementById('countVideo').textContent = String(counts.Video);
    document.getElementById('countOther').textContent = String(counts.Other);
  }

  function updateToolbarButtons() {
    const task = getSelectedTask();
    if (!task) {
      el.btnPause.disabled = true;
      el.btnResume.disabled = true;
      el.btnRefreshLink.disabled = true;
      el.btnRemove.disabled = true;
      return;
    }

    const isRunning = ['downloading', 'probing', 'queued'].includes(task.status);
    const canResume = ['paused', 'error'].includes(task.status);

    el.btnPause.disabled = !isRunning;
    el.btnResume.disabled = !canResume;
    el.btnRefreshLink.disabled = !canResume;
    el.btnRemove.disabled = false;
  }

  function renderDetailPane() {
    const task = getSelectedTask();
    if (!task) {
      el.detailTitle.textContent = 'Pilih item unduhan untuk melihat detail segmen';
      el.detailMeta.textContent = '';
      el.segmentBar.replaceChildren();
      el.detailUrl.textContent = '';
      return;
    }

    el.detailTitle.textContent = task.fileName || task.url || 'Tanpa Nama';
    const pct = getTaskProgress(task);
    const etaText = task.status === 'downloading' && task.etaSec ? ` | Sisa: ${formatEta(task.etaSec)}` : '';
    const errorPart = task.status === 'error' && task.error ? ` | Error: ${task.error}` : '';
    el.detailMeta.textContent = `${statusLabel(task.status)} (${pct}%)${etaText}${errorPart}`;
    el.detailUrl.textContent = task.url || '';

    const chunks = Array.isArray(task.chunks) && task.chunks.length ? task.chunks : null;
    const frag = document.createDocumentFragment();

    if (chunks) {
      for (const c of chunks) {
        const size = Number(c.size) || 0;
        const done = Number(c.downloaded) || 0;
        const chunkPct =
          c.status === 'done' || c.status === 'completed'
            ? 100
            : size > 0
              ? Math.min(100, Math.max(0, Math.round((done / size) * 100)))
              : 0;

        const chunkEl = document.createElement('div');
        chunkEl.className = 'segment-chunk';
        const fillEl = document.createElement('div');
        fillEl.className = 'segment-chunk-fill';
        fillEl.style.width = `${chunkPct}%`;
        chunkEl.appendChild(fillEl);
        frag.appendChild(chunkEl);
      }
    } else {
      const chunkEl = document.createElement('div');
      chunkEl.className = 'segment-chunk';
      const fillEl = document.createElement('div');
      fillEl.className = 'segment-chunk-fill';
      fillEl.style.width = `${pct}%`;
      chunkEl.appendChild(fillEl);
      frag.appendChild(chunkEl);
    }

    el.segmentBar.replaceChildren(frag);
  }

  function renderTable() {
    const list = getFilteredTasks();
    el.emptyState.classList.toggle('hidden', list.length > 0);

    const frag = document.createDocumentFragment();
    for (const task of list) {
      const tr = document.createElement('tr');
      tr.dataset.id = task.id;
      if (task.id === state.selectedId) {
        tr.classList.add('selected');
      }

      const tdName = document.createElement('td');
      tdName.textContent = task.fileName || task.url || 'Menghubungi server...';
      tdName.title = task.fileName || task.url || '';

      const tdSize = document.createElement('td');
      tdSize.textContent = formatBytes(task.totalSize);

      const tdStatus = document.createElement('td');
      const statusSpan = document.createElement('span');
      statusSpan.className = `status-pill status-${task.status || 'queued'}`;
      statusSpan.textContent = statusLabel(task.status);
      if (task.error) statusSpan.title = task.error;
      tdStatus.appendChild(statusSpan);

      const tdProg = document.createElement('td');
      const progWrap = document.createElement('div');
      progWrap.className = 'progress-cell';
      const track = document.createElement('div');
      track.className = 'progress-track';
      const fill = document.createElement('div');
      fill.className = `progress-fill ${task.status === 'completed' ? 'completed' : ''} ${task.status === 'error' ? 'error' : ''}`;
      const pct = getTaskProgress(task);
      fill.style.width = `${pct}%`;
      track.appendChild(fill);
      const pctLabel = document.createElement('span');
      pctLabel.textContent = `${pct}%`;
      progWrap.append(track, pctLabel);
      tdProg.appendChild(progWrap);

      const tdSpeed = document.createElement('td');
      tdSpeed.textContent = task.status === 'downloading' ? formatSpeed(task.speedBps) : '-';

      const tdConn = document.createElement('td');
      tdConn.textContent = getActiveConnCount(task);

      const tdCat = document.createElement('td');
      tdCat.textContent = normalizeCategory(task.category);

      tr.append(tdName, tdSize, tdStatus, tdProg, tdSpeed, tdConn, tdCat);
      frag.appendChild(tr);
    }

    el.tbody.replaceChildren(frag);
    updateSidebarCounts();
    updateToolbarButtons();
    renderDetailPane();
    updateStatusBar();
  }

  function updateStatusBar() {
    let activeCount = 0;
    let totalSpeed = 0;
    for (const t of state.tasks) {
      if (['downloading', 'probing', 'queued'].includes(t.status)) {
        activeCount += 1;
        totalSpeed += Number(t.speedBps) || 0;
      }
    }
    const mbSpeed = (totalSpeed / (1024 * 1024)).toFixed(2);
    el.statusSummary.textContent = `${activeCount} aktif | ${mbSpeed} MB/s`;
    el.statusFolder.textContent = `Folder dasar: ${state.defaultOutputDir || '-'}`;
  }

  function upsertTask(updatedTask) {
    if (!updatedTask || !updatedTask.id) return;
    const idx = state.tasks.findIndex((t) => t.id === updatedTask.id);
    if (idx >= 0) {
      state.tasks[idx] = updatedTask;
    } else {
      state.tasks.unshift(updatedTask);
    }
    renderTable();
  }

  function openModal(modalEl) {
    modalEl.classList.remove('hidden');
  }

  function closeModal(modalEl) {
    modalEl.classList.add('hidden');
  }

  function openAddModal(prefillUrl = '', fromClipboard = false) {
    el.addError.classList.add('hidden');
    el.addError.textContent = '';
    el.inpUrl.value = prefillUrl;
    el.inpFilename.value = '';
    el.inpOutputDir.value = state.defaultOutputDir || '';
    el.clipboardBanner.classList.toggle('hidden', !fromClipboard);
    openModal(el.addModal);
    setTimeout(() => {
      if (prefillUrl) {
        document.getElementById('btnSubmitAdd').focus();
      } else {
        el.inpUrl.focus();
      }
    }, 30);
  }

  function openRefreshModal(task) {
    if (!task) return;
    el.refreshError.classList.add('hidden');
    el.refreshError.textContent = '';
    el.refreshFileLabel.textContent = `File: ${task.fileName || task.url}`;
    el.inpRefreshUrl.value = '';
    openModal(el.refreshModal);
    setTimeout(() => el.inpRefreshUrl.focus(), 30);
  }

  async function openAboutModal() {
    if (api?.getAppInfo) {
      try {
        const info = await api.getAppInfo();
        if (info?.about) el.aboutDescription.textContent = info.about;
        if (info?.version) el.aboutVersion.textContent = info.version;
        if (info?.developer) el.aboutDeveloper.textContent = info.developer;
      } catch {
        // Gunakan nilai default HTML bila IPC gagal
      }
    }
    openModal(el.aboutModal);
  }

  function hideContextMenu() {
    el.contextMenu.classList.add('hidden');
  }

  function showContextMenu(x, y, task) {
    state.selectedId = task.id;
    renderTable();

    const isCompleted = task.status === 'completed';
    const isRunning = ['downloading', 'probing', 'queued'].includes(task.status);
    const canResume = ['paused', 'error'].includes(task.status);

    el.contextMenu.querySelector('[data-action="open"]').disabled = !isCompleted;
    el.contextMenu.querySelector('[data-action="folder"]').disabled = !isCompleted;
    el.contextMenu.querySelector('[data-action="pause"]').disabled = !isRunning;
    el.contextMenu.querySelector('[data-action="resume"]').disabled = !canResume;
    el.contextMenu.querySelector('[data-action="refresh"]').disabled = !canResume;

    el.contextMenu.classList.remove('hidden');
    const rect = el.contextMenu.getBoundingClientRect();
    const left = Math.min(x, window.innerWidth - rect.width - 8);
    const top = Math.min(y, window.innerHeight - rect.height - 8);
    el.contextMenu.style.left = `${Math.max(8, left)}px`;
    el.contextMenu.style.top = `${Math.max(8, top)}px`;
  }

  // Event Listeners
  el.btnAdd.addEventListener('click', () => openAddModal('', false));

  el.btnPause.addEventListener('click', async () => {
    const task = getSelectedTask();
    if (task && api?.pauseDownload) await api.pauseDownload(task.id);
  });

  el.btnResume.addEventListener('click', async () => {
    const task = getSelectedTask();
    if (task && api?.resumeDownload) await api.resumeDownload(task.id);
  });

  el.btnRefreshLink.addEventListener('click', () => {
    const task = getSelectedTask();
    if (task) openRefreshModal(task);
  });

  el.btnRemove.addEventListener('click', async () => {
    const task = getSelectedTask();
    if (!task || !api?.removeDownload) return;
    await api.removeDownload(task.id);
    state.tasks = state.tasks.filter((t) => t.id !== task.id);
    state.selectedId = null;
    renderTable();
  });

  el.chkClipboardMonitor.addEventListener('change', async () => {
    state.clipboardMonitor = el.chkClipboardMonitor.checked;
    if (api?.setClipboardMonitor) {
      await api.setClipboardMonitor(state.clipboardMonitor);
    }
  });

  el.selSpeedLimit.addEventListener('change', async () => {
    const bps = Number(el.selSpeedLimit.value) || 0;
    if (api?.setSpeedLimit) {
      const res = await api.setSpeedLimit(bps);
      if (res?.ok) state.speedLimitBps = res.speedLimitBps;
    }
  });

  el.btnBaseFolder.addEventListener('click', async () => {
    if (!api?.chooseFolder) return;
    const res = await api.chooseFolder(true);
    if (!res.canceled && res.path) {
      state.defaultOutputDir = res.path;
      updateStatusBar();
    }
  });

  el.btnTheme.addEventListener('click', () => {
    applyTheme(state.theme === 'light' ? 'dark' : 'light');
  });

  el.btnAbout.addEventListener('click', () => {
    openAboutModal();
  });

  document.querySelectorAll('.nav-item').forEach((btn) => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.nav-item').forEach((b) => b.classList.remove('active'));
      btn.classList.add('active');
      state.filterType = btn.dataset.filterType;
      state.filterValue = btn.dataset.filterValue;
      renderTable();
    });
  });

  el.tbody.addEventListener('click', (e) => {
    const row = e.target.closest('tr[data-id]');
    if (!row) return;
    state.selectedId = row.dataset.id;
    renderTable();
  });

  el.tbody.addEventListener('dblclick', async (e) => {
    const row = e.target.closest('tr[data-id]');
    if (!row) return;
    const task = state.tasks.find((t) => t.id === row.dataset.id);
    if (task?.status === 'completed' && api?.openFile) {
      await api.openFile(task.id);
    }
  });

  el.tbody.addEventListener('contextmenu', (e) => {
    const row = e.target.closest('tr[data-id]');
    if (!row) return;
    e.preventDefault();
    const task = state.tasks.find((t) => t.id === row.dataset.id);
    if (task) showContextMenu(e.clientX, e.clientY, task);
  });

  el.contextMenu.addEventListener('click', async (e) => {
    const item = e.target.closest('.ctx-item[data-action]');
    if (!item || item.disabled) return;
    const action = item.dataset.action;
    const task = getSelectedTask();
    hideContextMenu();
    if (!task) return;

    if (action === 'open' && api?.openFile) await api.openFile(task.id);
    if (action === 'folder' && api?.showInFolder) await api.showInFolder(task.id);
    if (action === 'pause' && api?.pauseDownload) await api.pauseDownload(task.id);
    if (action === 'resume' && api?.resumeDownload) await api.resumeDownload(task.id);
    if (action === 'refresh') openRefreshModal(task);
    if (action === 'copy' && task.url && navigator.clipboard) {
      await navigator.clipboard.writeText(task.url).catch(() => {});
    }
    if (action === 'remove' && api?.removeDownload) {
      await api.removeDownload(task.id);
      state.tasks = state.tasks.filter((t) => t.id !== task.id);
      state.selectedId = null;
      renderTable();
    }
  });

  document.addEventListener('click', (e) => {
    if (!e.target.closest('#contextMenu')) hideContextMenu();
    const closeId = e.target.dataset?.closeModal;
    if (closeId) {
      const targetModal = document.getElementById(closeId);
      if (targetModal) closeModal(targetModal);
    }
  });

  el.btnPasteUrl.addEventListener('click', async () => {
    if (!api?.readClipboard) return;
    const text = await api.readClipboard();
    if (text) el.inpUrl.value = text.trim();
  });

  el.btnChooseModalFolder.addEventListener('click', async () => {
    if (!api?.chooseFolder) return;
    const res = await api.chooseFolder(false);
    if (!res.canceled && res.path) {
      el.inpOutputDir.value = res.path;
    }
  });

  el.addForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!api?.addDownload) return;
    el.addError.classList.add('hidden');

    const res = await api.addDownload({
      url: el.inpUrl.value.trim(),
      filename: el.inpFilename.value.trim(),
      outputDir: el.inpOutputDir.value.trim() || state.defaultOutputDir,
      connections: Number(el.selConnections.value) || 8,
      autoCategorize: el.chkAutoCategorize.checked,
    });

    if (!res?.ok) {
      el.addError.textContent = res?.error || 'Gagal memulai unduhan.';
      el.addError.classList.remove('hidden');
      return;
    }

    if (res.task) {
      state.selectedId = res.task.id;
      upsertTask(res.task);
    }
    closeModal(el.addModal);
  });

  el.btnPasteRefreshUrl.addEventListener('click', async () => {
    if (!api?.readClipboard) return;
    const text = await api.readClipboard();
    if (text) el.inpRefreshUrl.value = text.trim();
  });

  el.refreshForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const task = getSelectedTask();
    if (!task) return;
    el.refreshError.classList.add('hidden');

    if (!api?.refreshLink) {
      el.refreshError.textContent = 'Handler refreshLink belum tersambung di backend.';
      el.refreshError.classList.remove('hidden');
      return;
    }

    const res = await api.refreshLink(task.id, el.inpRefreshUrl.value.trim());
    if (!res?.ok) {
      el.refreshError.textContent = res?.error || 'Gagal memperbarui link unduhan.';
      el.refreshError.classList.remove('hidden');
      return;
    }

    if (res.task) upsertTask(res.task);
    closeModal(el.refreshModal);
  });

  document.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'n') {
      e.preventDefault();
      openAddModal('', false);
    }
    if (e.key === 'Escape') {
      hideContextMenu();
      closeModal(el.addModal);
      closeModal(el.refreshModal);
      closeModal(el.aboutModal);
    }
  });

  async function init() {
    applyTheme(state.theme);
    if (!api) return;

    const initial = await api.getInitialState();
    state.tasks = Array.isArray(initial?.tasks) ? initial.tasks : [];
    state.defaultOutputDir = initial?.defaultOutputDir || '';
    state.speedLimitBps = Number(initial?.speedLimitBps) || 0;
    state.clipboardMonitor = initial?.clipboardMonitor !== false;

    el.chkClipboardMonitor.checked = state.clipboardMonitor;
    el.selSpeedLimit.value = String(state.speedLimitBps);

    api.onUpdate?.((task) => upsertTask(task));
    api.onList?.((tasks) => {
      state.tasks = Array.isArray(tasks) ? tasks : [];
      renderTable();
    });
    api.onClipboardLink?.((url) => {
      if (url && el.addModal.classList.contains('hidden')) {
        openAddModal(url, true);
      }
    });
    api.onClipboardMonitorState?.((enabled) => {
      state.clipboardMonitor = Boolean(enabled);
      el.chkClipboardMonitor.checked = state.clipboardMonitor;
    });

    renderTable();
  }

  init();
})();