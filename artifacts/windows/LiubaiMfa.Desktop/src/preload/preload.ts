import { contextBridge, ipcRenderer } from 'electron';
contextBridge.exposeInMainWorld('mfaUpdates', Object.freeze({
  version: () => ipcRenderer.invoke('update:request', 'version'),
  channel: () => ipcRenderer.invoke('update:request', 'channel'),
  check: () => ipcRenderer.invoke('update:request', 'check'),
  open: () => ipcRenderer.invoke('update:request', 'open'),
  download: () => ipcRenderer.invoke('update:request', 'download'),
  cancel: () => ipcRenderer.invoke('update:request', 'cancel'),
  install: () => ipcRenderer.invoke('update:request', 'install'),
  onProgress: (callback: (percent: number) => void) => {
    ipcRenderer.on('update:progress', (_event, percent: number) => callback(percent));
  },
}));
contextBridge.exposeInMainWorld('mfaCore',Object.freeze({
  exportPrepare:()=>ipcRenderer.invoke('core:request','exportPrepare'),
  exportSave:(token:string)=>ipcRenderer.invoke('core:request','exportSave',{token,confirmed:true}),
  importChoose:()=>ipcRenderer.invoke('core:request','importChoose'),
  importPreview:(recoveryKey:string)=>ipcRenderer.invoke('core:request','importPreview',{recoveryKey}),
  importCommit:(token:string)=>ipcRenderer.invoke('core:request','importCommit',{token}),
  backupCancel:()=>ipcRenderer.invoke('core:request','backupCancel'),
  qrRead:(source:string)=>ipcRenderer.invoke('core:request','qrRead',{source}),
  qrCommit:(fields:unknown)=>ipcRenderer.invoke('core:request','qrCommit',fields),
  qrCancel:()=>ipcRenderer.invoke('core:request','qrCancel'),
  connect:()=>ipcRenderer.invoke('core:request','connect'),
  status:()=>ipcRenderer.invoke('core:request','status'),
  accounts:()=>ipcRenderer.invoke('core:request','accounts'),
  addAccount:(fields:unknown)=>ipcRenderer.invoke('core:request','addAccount',fields),
  unlock:()=>ipcRenderer.invoke('core:request','unlock'),
  lock:()=>ipcRenderer.invoke('core:request','lock'),
  setProtection:(enabled:boolean)=>ipcRenderer.invoke('core:request','setProtection',{enabled}),
  setAutoLock:(seconds:number)=>ipcRenderer.invoke('core:request','setAutoLock',{seconds}),
  editAccount:(fields:unknown)=>ipcRenderer.invoke('core:request','editAccount',fields),
  renameGroup:(group:string,name:string)=>ipcRenderer.invoke('core:request','renameGroup',{group,name}),
  createGroup:(name:string)=>ipcRenderer.invoke('core:request','createGroup',{name}),
  deleteGroup:(group:string)=>ipcRenderer.invoke('core:request','deleteGroup',{group}),
  moveAccount:(id:string,group:string)=>ipcRenderer.invoke('core:request','moveAccount',{id,group}),
  deleteAccount:(id:string)=>ipcRenderer.invoke('core:request','deleteAccount',{id}),
  copyCode:(id:string)=>ipcRenderer.invoke('core:request','copyCode',{id}),
  onDisconnected:(callback:()=>void)=>{const handler=()=>callback();ipcRenderer.on('core:disconnected',handler);return ()=>ipcRenderer.removeListener('core:disconnected',handler)},
  onLocked:(callback:()=>void)=>{const handler=()=>callback();ipcRenderer.on('core:locked',handler);return ()=>ipcRenderer.removeListener('core:locked',handler)}
}));
