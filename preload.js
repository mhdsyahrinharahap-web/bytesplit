const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('bytesplit', {
  getInitialState: () => ipcRenderer.invoke('state:get'),
  addDownload: (payload) => ipcRenderer.invoke('download:add', payload),
  pauseDownload: (id) => ipcRenderer.invoke('download:pause', id),
  resumeDownload: (id) => ipcRenderer.invoke('download:resume', id),
  cancelDownload: (id) => ipcRenderer.invoke('download:cancel', id),
  removeDownload: (id) => ipcRenderer.invoke('download:remove', id),
  setSpeedLimit: (bytesPerSec) => ipcRenderer.invoke('speed:set', bytesPerSec),
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
});