const CATEGORY_RULES = [
  {
    id: 'compressed',
    label: 'Compressed',
    folderName: 'Compressed',
    exts: ['.zip', '.rar', '.7z', '.tar', '.gz', '.bz2', '.xz', '.tgz', '.zst', '.cab', '.arj', '.lzh', '.ace', '.z'],
  },
  {
    id: 'documents',
    label: 'Documents',
    folderName: 'Documents',
    exts: ['.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx', '.txt', '.rtf', '.odt', '.ods', '.odp', '.csv', '.md', '.epub', '.djvu', '.chm'],
  },
  {
    id: 'music',
    label: 'Music',
    folderName: 'Music',
    exts: ['.mp3', '.wav', '.flac', '.aac', '.ogg', '.m4a', '.wma', '.opus', '.aiff', '.mid', '.midi'],
  },
  {
    id: 'programs',
    label: 'Programs',
    folderName: 'Programs',
    exts: ['.exe', '.msi', '.msix', '.apk', '.deb', '.rpm', '.dmg', '.pkg', '.appimage', '.iso', '.img', '.bin', '.bat', '.cmd', '.ps1', '.sh'],
  },
  {
    id: 'video',
    label: 'Video',
    folderName: 'Video',
    exts: ['.mp4', '.mkv', '.avi', '.mov', '.wmv', '.flv', '.webm', '.m4v', '.mpeg', '.mpg', '.3gp', '.ts'],
  },
];

const CATEGORY_LABELS = {
  compressed: 'Compressed',
  documents: 'Documents',
  music: 'Music',
  programs: 'Programs',
  video: 'Video',
  general: 'Lainnya',
};

const state = {
  tasks: new Map(),
  view: 'all',
  selectedId: null,
  defaultOutputDir: '',
  rowRefs: new Map(),
  inspChunkBars: [],
  inspChunkCount: 0,
  lastTrigger: null,
};

const els = {
  workspace: document.getElementById('workspace'),
  btnToggleSidebar: document.getElementById('btn-toggle-sidebar'),
  btnAdd: document.getElementById('btn-add'),
  btnResume: document.getElementById('btn-resume'),
  btnPause: document.getElementById('btn-pause'),
  btnCancel: document.getElementById('btn-cancel'),
  btnRemove: document.getElementById('btn-remove'),
  btnOpenFolder: document.getElementById('btn-open-folder'),
  speedLimit: document.getElementById('speed-limit'),
  themeToggle: document.getElementById('theme-toggle'),
  toast: document.getElementById('toast'),

  tableBody: document.getElementById('table-body'),
  tableEmpty: document.getElementById('table-empty'),
  emptyTitle: document.getElementById('empty-title'),
  emptyDesc: document.getElementById('empty-desc'),
  btnEmptyAdd: document.getElementById('btn-empty-add'),

  inspEmpty: document.getElementById('inspector-empty'),
  inspContent: document.getElementById('inspector-content'),
  inspName: document.getElementById('insp-name'),
  inspCategory: document.getElementById('insp-category'),
  inspPath: document.getElementById('insp-path'),
  inspUrl: document.getElementById('insp-url'),
  inspError: document.getElementById('insp-error'),
  inspChunks: document.getElementById('insp-chunks'),
  inspChunkLegend: document.getElementById('insp-chunk-legend'),
  inspOpenFile: document.getElementById('insp-open-file'),
  inspOpenFolder: document.getElementById('insp-open-folder'),

  statusActive: document.getElementById('status-active'),
  statusSpeed: document.getElementById('status-speed'),
  btnDefaultFolder: document.getElementById('btn-default-folder'),
  defaultFolderText: document.getElementById('default-folder-text'),

  modalBackdrop: document.getElementById('modal-backdrop'),
  btnCloseModal: document.getElementById('btn-close-modal'),
  btnCancelModal: document.getElementById('btn-cancel-modal'),
  form: document.getElementById('new-download-form'),
  inputUrl: document.getElementById('input-url'),
  btnPasteUrl: document.getElementById('btn-paste-url'),
  inputAutoCat: document.getElementById('input-auto-cat'),
  autoDestPreview: document.getElementById('auto-dest-preview'),
  customFolderWrap: document.getElementById('custom-folder-wrap'),
  inputFolder: document.getElementById('input-folder'),
  btnBrowseFolder: document.getElementById('btn-browse-folder'),
  inputConnections: document.getElementById('input-connections'),
  formError: document.getElementById('form-error'),

  counts: {
    all: document.getElementById('cnt-all'),
    active: document.getElementById('cnt-active'),
    completed: document.getElementById('cnt-completed'),
    compressed: document.getElementById('cnt-compressed'),
    documents: document.getElementById('cnt-documents'),
    music: document.getElementById('cnt-music'),
    programs: document.getElementById('cnt-programs'),
    video: document.getElementById('cnt-video'),
    general: document.getElementById('cnt-general'),
  },
};

let toastTimer = null;
function showToast(msg) {
  els.toast.textContent = msg;
  els.toast.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    els.toast.hidden = true;
  }, 4500);
}

function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const idx = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${(bytes / 1024 ** idx).toFixed(idx === 0 ? 0 : 2)} ${units[idx]}`;
}

function formatSpeed(bps) {
  if (!Number.isFinite(bps) || bps <= 0) return '';
  if (bps < 1024 * 1024) return `${(bps / 1024).toFixed(1)} KB/s`;
  return `${(bps / (1024 * 1024)).toFixed(2)} MB/s`;
}

function formatTotalSpeed(bps) {
  if (!Number.isFinite(bps) || bps <= 0) return '0.00 MB/s';
  return formatSpeed(bps);
}

function formatEta(sec) {
  if (sec === null || sec === undefined || !Number.isFinite(sec)) return '';
  if (sec <= 0) return '0d';
  const h = Math.floor(sec / 3600);
  const m = Math.floor((sec % 3600) / 60);
  const s = Math.floor(sec % 60);
  if (h > 0) return `${h}j ${m}m`;
  if (m > 0) return `${m}m ${s}d`;
  return `${s}d`;
}

function statusTextLabel(status) {
  switch (status) {
    case 'queued':
      return 'Antrean';
    case 'probing':
      return 'Menghubungi...';
    case 'downloading':
      return 'Mengunduh';
    case 'paused':
      return 'Dijeda';
    case 'completed':
      return 'Selesai';
    case 'canceled':
      return 'Dibatalkan';
    case 'error':
      return 'Gagal';
    default:
      return status;
  }
}

function isActiveStatus(status) {
  return status === 'queued' || status === 'probing' || status === 'downloading' || status === 'paused';
}

function matchesView(task) {
  if (state.view === 'all') return true;
  if (state.view === 'active') return isActiveStatus(task.status);
  if (state.view === 'completed') return task.status === 'completed';
  if (state.view.startsWith('cat:')) {
    return (task.category || 'general') === state.view.slice(4);
  }
  return true;
}

function guessCategoryFromUrl(rawUrl) {
  try {
    const u = new URL(rawUrl);
    const segment = u.pathname.split('/').filter(Boolean).pop() || '';
    const dotIdx = segment.lastIndexOf('.');
    if (dotIdx <= 0) return null;
    const ext = segment.slice(dotIdx).toLowerCase();
    for (const rule of CATEGORY_RULES) {
      if (rule.exts.includes(ext)) return rule;
    }
  } catch {
    return null;
  }
  return null;
}

function updateAutoDestPreview() {
  const base = state.defaultOutputDir || '';
  const sep = base.includes('\\') ? '\\' : '/';
  const cleanBase = base.replace(/[\\/]+$/, '');
  const match = guessCategoryFromUrl(els.inputUrl.value.trim());

  if (match) {
    els.autoDestPreview.textContent = `Tujuan terdeteksi (${match.label}): ${cleanBase}${sep}${match.folderName}`;
  } else {
    els.autoDestPreview.textContent = `Tujuan otomatis mengikuti format file dari server (dasar: ${cleanBase})`;
  }
}

function getVisibleTasks() {
  return Array.from(state.tasks.values())
    .sort((a, b) => b.createdAt - a.createdAt)
    .filter(matchesView);
}

function createRow(task) {
  const tr = document.createElement('tr');
  tr.className = 'dl-row';
  tr.dataset.id = task.id;

  const tdName = document.createElement('td');
  tdName.className = 'cell-name';

  const tdSize = document.createElement('td');
  tdSize.className = 'mono';

  const tdStatus = document.createElement('td');
  const statusWrap = document.createElement('div');
  statusWrap.className = 'status-cell';
  const statusLine = document.createElement('div');
  statusLine.className = 'status-line';
  const statusLabelEl = document.createElement('span');
  statusLabelEl.className = 'status-text';
  const pctEl = document.createElement('span');
  pctEl.className = 'mono';
  statusLine.append(statusLabelEl, pctEl);

  const miniBar = document.createElement('div');
  miniBar.className = 'mini-bar';
  const miniFill = document.createElement('div');
  miniFill.className = 'mini-fill';
  miniBar.append(miniFill);
  statusWrap.append(statusLine, miniBar);
  tdStatus.append(statusWrap);

  const tdSpeed = document.createElement('td');
  tdSpeed.className = 'mono';

  const tdEta = document.createElement('td');
  tdEta.className = 'mono';

  const tdConn = document.createElement('td');
  tdConn.className = 'mono';

  tr.append(tdName, tdSize, tdStatus, tdSpeed, tdEta, tdConn);

  tr.addEventListener('click', () => selectTask(task.id));
  tr.addEventListener('dblclick', async () => {
    const current = state.tasks.get(task.id);
    if (current && current.status === 'completed') {
      const res = await window.bytesplit.openFile(task.id);
      if (!res.ok) showToast(res.error);
    }
  });

  const ref = { tr, tdName, tdSize, statusLabelEl, pctEl, miniFill, tdSpeed, tdEta, tdConn };
  updateRow(ref, task);
  return ref;
}

function updateRow(ref, task) {
  const pct =
    task.totalSize > 0
      ? Math.min(100, (task.downloaded / task.totalSize) * 100)
      : task.status === 'completed'
        ? 100
        : 0;

  ref.tr.classList.toggle('is-selected', task.id === state.selectedId);
  ref.tdName.textContent = task.fileName || task.url;
  ref.tdName.title = task.destPath || task.url;

  if (task.totalSize > 0) {
    ref.tdSize.textContent =
      task.status === 'completed'
        ? formatBytes(task.totalSize)
        : `${formatBytes(task.downloaded)} / ${formatBytes(task.totalSize)}`;
  } else {
    ref.tdSize.textContent = formatBytes(task.downloaded);
  }

  ref.statusLabelEl.textContent = statusTextLabel(task.status);
  ref.statusLabelEl.className = `status-text is-${task.status}`;
  ref.pctEl.textContent = task.totalSize > 0 || task.status === 'completed' ? `${pct.toFixed(1)}%` : '';

  ref.miniFill.style.width = `${pct.toFixed(1)}%`;
  ref.miniFill.className = `mini-fill is-${task.status}`;

  ref.tdSpeed.textContent = task.status === 'downloading' ? formatSpeed(task.speedBps) : '';
  ref.tdEta.textContent = task.status === 'downloading' ? formatEta(task.etaSec) : '';

  const chunkCount = Array.isArray(task.chunks) ? task.chunks.length : 0;
  ref.tdConn.textContent = chunkCount > 0 ? String(chunkCount) : '-';
}

function syncToolbarAndInspector() {
  const task = state.selectedId ? state.tasks.get(state.selectedId) : null;

  const canResume = !!task && (task.status === 'paused' || task.status === 'error' || task.status === 'canceled');
  const canPause = !!task && (task.status === 'downloading' || task.status === 'probing');
  const canCancel = !!task && (task.status === 'downloading' || task.status === 'probing' || task.status === 'paused');
  const canRemove = !!task;
  const canOpen = !!task && task.status === 'completed';

  els.btnResume.disabled = !canResume;
  els.btnPause.disabled = !canPause;
  els.btnCancel.disabled = !canCancel;
  els.btnRemove.disabled = !canRemove;
  els.btnOpenFolder.disabled = !canOpen;

  if (!task) {
    els.inspEmpty.hidden = false;
    els.inspContent.hidden = true;
    return;
  }

  els.inspEmpty.hidden = true;
  els.inspContent.hidden = false;

  els.inspName.textContent = task.fileName || task.url;
  els.inspCategory.textContent = CATEGORY_LABELS[task.category] || 'Lainnya';
  els.inspPath.textContent = task.destPath || task.outputDir || '-';
  els.inspPath.title = task.destPath || task.outputDir || '';
  els.inspUrl.textContent = task.url;
  els.inspUrl.title = task.url;

  els.inspOpenFile.disabled = !canOpen;
  els.inspOpenFolder.disabled = !canOpen;

  if (task.error) {
    els.inspError.textContent = `Error: ${task.error}`;
    els.inspError.hidden = false;
  } else {
    els.inspError.hidden = true;
  }

  const chunks = Array.isArray(task.chunks) ? task.chunks : [];
  const count = chunks.length;

  if (count === 0) {
    els.inspChunkLegend.textContent = 'Menunggu respons server';
    if (state.inspChunkCount !== 0) {
      els.inspChunks.replaceChildren();
      state.inspChunkBars = [];
      state.inspChunkCount = 0;
    }
    return;
  }

  const doneCount = chunks.filter((c) => c.status === 'done').length;
  els.inspChunkLegend.textContent = `${doneCount}/${count} segmen selesai`;

  if (state.inspChunkCount !== count) {
    els.inspChunks.replaceChildren();
    els.inspChunks.style.gridTemplateColumns = `repeat(${count}, minmax(0, 1fr))`;
    state.inspChunkBars = chunks.map((c) => {
      const slot = document.createElement('div');
      slot.className = 'chunk-slot';
      const bar = document.createElement('div');
      bar.className = 'chunk-bar';
      slot.append(bar);
      els.inspChunks.append(slot);
      return { slot, bar };
    });
    state.inspChunkCount = count;
  }

  chunks.forEach((c, i) => {
    const cp = c.size > 0 ? Math.min(100, (c.downloaded / c.size) * 100) : task.status === 'completed' ? 100 : 0;
    const item = state.inspChunkBars[i];
    if (item) {
      item.bar.style.width = `${cp.toFixed(1)}%`;
      item.bar.className = `chunk-bar is-${c.status}`;
      item.slot.title = `Segmen #${c.id + 1}: ${cp.toFixed(1)}% (${formatBytes(c.downloaded)})`;
    }
  });
}

function selectTask(id) {
  if (state.selectedId === id) return;
  const prevRef = state.selectedId ? state.rowRefs.get(state.selectedId) : null;
  if (prevRef) prevRef.tr.classList.remove('is-selected');

  state.selectedId = id;
  const nextRef = id ? state.rowRefs.get(id) : null;
  if (nextRef) nextRef.tr.classList.add('is-selected');

  syncToolbarAndInspector();
}

function updateCountsAndStatus() {
  const all = Array.from(state.tasks.values());
  const counters = {
    all: all.length,
    active: 0,
    completed: 0,
    compressed: 0,
    documents: 0,
    music: 0,
    programs: 0,
    video: 0,
    general: 0,
  };

  let totalSpeed = 0;

  for (const t of all) {
    if (isActiveStatus(t.status)) counters.active++;
    if (t.status === 'completed') counters.completed++;
    if (t.status === 'downloading') totalSpeed += t.speedBps || 0;

    const cat = t.category && counters[t.category] !== undefined ? t.category : 'general';
    counters[cat]++;
  }

  for (const [key, el] of Object.entries(els.counts)) {
    if (el) el.textContent = String(counters[key] || 0);
  }

  els.statusActive.textContent = `${counters.active} aktif`;
  els.statusSpeed.textContent = formatTotalSpeed(totalSpeed);
}

function renderTable() {
  const visible = getVisibleTasks();
  const visibleIds = new Set(visible.map((t) => t.id));

  for (const [id, ref] of state.rowRefs.entries()) {
    if (!visibleIds.has(id)) {
      ref.tr.remove();
      state.rowRefs.delete(id);
    }
  }

  if (state.selectedId && !visibleIds.has(state.selectedId)) {
    state.selectedId = visible.length > 0 ? visible[0].id : null;
  }

  const frag = document.createDocumentFragment();
  for (const task of visible) {
    let ref = state.rowRefs.get(task.id);
    if (!ref) {
      ref = createRow(task);
      state.rowRefs.set(task.id, ref);
    } else {
      updateRow(ref, task);
    }
    frag.append(ref.tr);
  }
  els.tableBody.append(frag);

  const isEmpty = visible.length === 0;
  els.tableEmpty.hidden = !isEmpty;
  if (isEmpty) {
    if (state.tasks.size === 0) {
      els.emptyTitle.textContent = 'Belum ada unduhan';
      els.emptyDesc.textContent =
        'Klik Tambah URL atau tekan Ctrl+N. File otomatis disimpan ke folder Compressed, Documents, Music, Programs, atau Video sesuai formatnya.';
      els.btnEmptyAdd.hidden = false;
    } else {
      els.emptyTitle.textContent = 'Kategori ini masih kosong';
      els.emptyDesc.textContent = 'Belum ada file unduhan yang masuk ke kategori ini.';
      els.btnEmptyAdd.hidden = true;
    }
  }

  updateCountsAndStatus();
  syncToolbarAndInspector();
}

function openModal(triggerEl) {
  state.lastTrigger = triggerEl || document.activeElement;
  els.inputUrl.value = '';
  els.inputAutoCat.checked = true;
  els.customFolderWrap.hidden = true;
  els.autoDestPreview.hidden = false;
  els.inputFolder.value = state.defaultOutputDir;
  els.formError.hidden = true;
  updateAutoDestPreview();
  els.modalBackdrop.hidden = false;
  els.inputUrl.focus();
}

function closeModal() {
  els.modalBackdrop.hidden = true;
  if (state.lastTrigger && typeof state.lastTrigger.focus === 'function') {
    state.lastTrigger.focus();
  }
}

function applyTheme(theme) {
  document.documentElement.setAttribute('data-theme', theme);
  localStorage.setItem('bytesplit-theme', theme);
  els.themeToggle.textContent = theme === 'dark' ? 'Mode Terang' : 'Mode Gelap';
}

function bindEvents() {
  els.btnToggleSidebar.addEventListener('click', () => {
    const collapsed = els.workspace.classList.toggle('is-collapsed');
    els.btnToggleSidebar.setAttribute('aria-expanded', String(!collapsed));
  });

  document.querySelectorAll('.nav-item').forEach((btn) => {
    btn.addEventListener('click', () => {
      state.view = btn.dataset.view;
      document.querySelectorAll('.nav-item').forEach((b) => {
        b.classList.toggle('is-active', b === btn);
      });
      renderTable();
    });
  });

  els.btnAdd.addEventListener('click', (e) => openModal(e.currentTarget));
  els.btnEmptyAdd.addEventListener('click', (e) => openModal(e.currentTarget));
  els.btnCloseModal.addEventListener('click', closeModal);
  els.btnCancelModal.addEventListener('click', closeModal);
  els.modalBackdrop.addEventListener('click', (e) => {
    if (e.target === els.modalBackdrop) closeModal();
  });

  els.btnResume.addEventListener('click', async () => {
    if (!state.selectedId) return;
    const res = await window.bytesplit.resumeDownload(state.selectedId);
    if (!res.ok) showToast(res.error);
  });

  els.btnPause.addEventListener('click', async () => {
    if (!state.selectedId) return;
    const res = await window.bytesplit.pauseDownload(state.selectedId);
    if (!res.ok) showToast(res.error);
  });

  els.btnCancel.addEventListener('click', async () => {
    if (!state.selectedId) return;
    const res = await window.bytesplit.cancelDownload(state.selectedId);
    if (!res.ok) showToast(res.error);
  });

  els.btnRemove.addEventListener('click', async () => {
    if (!state.selectedId) return;
    const res = await window.bytesplit.removeDownload(state.selectedId);
    if (!res.ok) showToast(res.error);
  });

  const openSelectedFolder = async () => {
    if (!state.selectedId) return;
    const res = await window.bytesplit.showInFolder(state.selectedId);
    if (!res.ok) showToast(res.error);
  };

  els.btnOpenFolder.addEventListener('click', openSelectedFolder);
  els.inspOpenFolder.addEventListener('click', openSelectedFolder);

  els.inspOpenFile.addEventListener('click', async () => {
    if (!state.selectedId) return;
    const res = await window.bytesplit.openFile(state.selectedId);
    if (!res.ok) showToast(res.error);
  });

  els.speedLimit.addEventListener('change', async () => {
    await window.bytesplit.setSpeedLimit(Number(els.speedLimit.value) || 0);
  });

  els.themeToggle.addEventListener('click', () => {
    const current = document.documentElement.getAttribute('data-theme') || 'light';
    applyTheme(current === 'dark' ? 'light' : 'dark');
  });

  els.btnDefaultFolder.addEventListener('click', async () => {
    const res = await window.bytesplit.chooseFolder(true);
    if (!res.canceled && res.path) {
      state.defaultOutputDir = res.path;
      els.defaultFolderText.textContent = res.path;
    }
  });

  els.inputUrl.addEventListener('input', updateAutoDestPreview);

  els.inputAutoCat.addEventListener('change', () => {
    const auto = els.inputAutoCat.checked;
    els.autoDestPreview.hidden = !auto;
    els.customFolderWrap.hidden = auto;
  });

  els.btnPasteUrl.addEventListener('click', async () => {
    const text = await window.bytesplit.readClipboard();
    if (text) {
      els.inputUrl.value = text.trim();
      updateAutoDestPreview();
      els.inputUrl.focus();
    }
  });

  els.btnBrowseFolder.addEventListener('click', async () => {
    const res = await window.bytesplit.chooseFolder(false);
    if (!res.canceled && res.path) {
      els.inputFolder.value = res.path;
    }
  });

  els.form.addEventListener('submit', async (e) => {
    e.preventDefault();
    els.formError.hidden = true;

    const autoCategorize = els.inputAutoCat.checked;
    const res = await window.bytesplit.addDownload({
      url: els.inputUrl.value.trim(),
      outputDir: autoCategorize ? state.defaultOutputDir : els.inputFolder.value.trim(),
      connections: Number(els.inputConnections.value) || 8,
      autoCategorize,
    });

    if (!res.ok) {
      els.formError.textContent = res.error;
      els.formError.hidden = false;
      return;
    }

    if (res.task && res.task.id) {
      state.selectedId = res.task.id;
    }
    closeModal();
  });

  document.addEventListener('keydown', (e) => {
    if (!els.modalBackdrop.hidden) {
      if (e.key === 'Escape') closeModal();
      return;
    }

    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'n') {
      e.preventDefault();
      openModal(els.btnAdd);
      return;
    }

    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      const visible = getVisibleTasks();
      if (visible.length === 0) return;
      e.preventDefault();
      const idx = visible.findIndex((t) => t.id === state.selectedId);
      const nextIdx =
        idx === -1
          ? 0
          : e.key === 'ArrowDown'
            ? Math.min(visible.length - 1, idx + 1)
            : Math.max(0, idx - 1);
      selectTask(visible[nextIdx].id);
    }
  });

  window.bytesplit.onList((tasks) => {
    state.tasks.clear();
    for (const t of tasks) state.tasks.set(t.id, t);
    renderTable();
  });

  window.bytesplit.onUpdate((task) => {
    const prev = state.tasks.get(task.id);
    state.tasks.set(task.id, task);

    const ref = state.rowRefs.get(task.id);
    const sameBucket =
      prev && prev.status === task.status && (prev.category || 'general') === (task.category || 'general');

    if (ref && matchesView(task) && sameBucket) {
      updateRow(ref, task);
      updateCountsAndStatus();
      if (task.id === state.selectedId) syncToolbarAndInspector();
    } else {
      renderTable();
    }
  });
}

async function init() {
  const savedTheme = localStorage.getItem('bytesplit-theme') || 'light';
  applyTheme(savedTheme);
  bindEvents();

  const initial = await window.bytesplit.getInitialState();
  state.defaultOutputDir = initial.defaultOutputDir;
  els.defaultFolderText.textContent = initial.defaultOutputDir;
  els.speedLimit.value = String(initial.speedLimitBps || 0);

  state.tasks.clear();
  for (const t of initial.tasks || []) {
    state.tasks.set(t.id, t);
  }
  renderTable();
}

init();