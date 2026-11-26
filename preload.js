const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('bytesplit', {
  getInitialState: () => ipcRenderer.invoke('state:get'),
  getAppInfo: () => ipcRenderer.invoke('app:getInfo'),
  addDownload: (payload) => ipcRenderer.invoke('download:add', payload),
  pauseDownload: (id) => ipcRenderer.invoke('download:pause', id),
  resumeDownload: (id) => ipcRenderer.invoke('download:resume', id),
  cancelDownload: (id) => ipcRenderer.invoke('download:cancel', id),
  removeDownload: (id) => ipcRenderer.invoke('download:remove', id),
  refreshLink: (id, newUrl) => ipcRenderer.invoke('download:refreshLink', { id, newUrl }),
  setSpeedLimit: (bytesPerSec) => ipcRenderer.invoke('speed:set', bytesPerSec),
  setClipboardMonitor: (enabled) => ipcRenderer.invoke('clipboard:setMonitor', enabled),
  chooseFolder: (updateDefault = false) => ipcRenderer.invoke('dialog:chooseFolder', updateDefault),
  openFile: (id) => ipcRenderer.invoke('file:open', id),
  showInFolder: (id) => ipcRenderer.invoke('file:showInFolder', id),
  readClipboard: () => ipcRenderer.invoke('clipboard:read'),

  onUpdate: (callback) => {
    const listener = (_event, task) => callback(task);
    ipcRenderer.on('download:update', listener);
    return () => ipcRenderer.removeListener('download:update', listener);
  },

  onList: (callback) => {
    const listener = (_event, tasks) => callback(tasks);
    ipcRenderer.on('download:list', listener);
    return () => ipcRenderer.removeListener('download:list', listener);
  },

  onClipboardLink: (callback) => {
    const listener = (_event, url) => callback(url);
    ipcRenderer.on('clipboard:link', listener);
    return () => ipcRenderer.removeListener('clipboard:link', listener);
  },

  onClipboardMonitorState: (callback) => {
    const listener = (_event, enabled) => callback(enabled);
    ipcRenderer.on('clipboard:monitorState', listener);
    return () => ipcRenderer.removeListener('clipboard:monitorState', listener);
  },
});