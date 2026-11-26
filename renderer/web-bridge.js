(() => {
  if (window.bytesplit) return;

  const updateListeners = new Set();
  const listListeners = new Set();

  const events = new EventSource('/api/events');
  events.addEventListener('update', (e) => {
    try {
      const task = JSON.parse(e.data);
      for (const fn of updateListeners) fn(task);
    } catch {
      // Abaikan frame yang tidak lengkap
    }
  });

  events.addEventListener('list', (e) => {
    try {
      const tasks = JSON.parse(e.data);
      for (const fn of listListeners) fn(tasks);
    } catch {
      // Abaikan frame yang tidak lengkap
    }
  });

  async function api(url, options = {}) {
    try {
      const res = await fetch(url, {
        headers: { 'Content-Type': 'application/json' },
        ...options,
      });
      return await res.json();
    } catch (err) {
      return { ok: false, error: err.message || 'Gagal terhubung ke server ByteSplit.' };
    }
  }

  window.bytesplit = {
    getInitialState: () => api('/api/state', { method: 'GET' }),

    addDownload: (payload) =>
      api('/api/downloads', {
        method: 'POST',
        body: JSON.stringify(payload),
      }),

    pauseDownload: (id) =>
      api(`/api/downloads/${encodeURIComponent(id)}/pause`, { method: 'POST' }),

    resumeDownload: (id) =>
      api(`/api/downloads/${encodeURIComponent(id)}/resume`, { method: 'POST' }),

    cancelDownload: (id) =>
      api(`/api/downloads/${encodeURIComponent(id)}/cancel`, { method: 'POST' }),

    removeDownload: (id) =>
      api(`/api/downloads/${encodeURIComponent(id)}`, { method: 'DELETE' }),

    setSpeedLimit: (bytesPerSec) =>
      api('/api/speed', {
        method: 'POST',
        body: JSON.stringify({ bytesPerSec }),
      }),

    chooseFolder: async (updateDefault = false) => {
      const folderInput = document.getElementById('input-folder');
      const defaultText = document.getElementById('default-folder-text');
      const current =
        (!updateDefault && folderInput && folderInput.value) ||
        (defaultText && defaultText.textContent !== '-' ? defaultText.textContent : '');

      const entered = window.prompt('Masukkan alamat folder tujuan penyimpanan:', current);
      if (!entered || !entered.trim()) {
        return { canceled: true };
      }

      const trimmed = entered.trim();
      if (updateDefault) {
        const res = await api('/api/settings/folder', {
          method: 'POST',
          body: JSON.stringify({ path: trimmed }),
        });
        if (!res.ok) {
          window.alert(res.error || 'Gagal menyimpan folder default.');
          return { canceled: true };
        }
        return { canceled: false, path: res.path };
      }

      return { canceled: false, path: trimmed };
    },

    openFile: (id) =>
      api(`/api/downloads/${encodeURIComponent(id)}/open`, { method: 'POST' }),

    showInFolder: (id) =>
      api(`/api/downloads/${encodeURIComponent(id)}/folder`, { method: 'POST' }),

    readClipboard: async () => {
      try {
        return await navigator.clipboard.readText();
      } catch {
        return '';
      }
    },

    onUpdate: (callback) => {
      updateListeners.add(callback);
      return () => updateListeners.delete(callback);
    },

    onList: (callback) => {
      listListeners.add(callback);
      return () => listListeners.delete(callback);
    },
  };
})();