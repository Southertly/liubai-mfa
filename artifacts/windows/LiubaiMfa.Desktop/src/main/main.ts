import { app, BrowserWindow, ipcMain, protocol, powerMonitor, dialog, shell, type IpcMainInvokeEvent } from 'electron';
import path from 'node:path';
import { readFile } from 'node:fs/promises';
import { CoreBridge } from './core-bridge';
import { readQr } from './qr-import';
import { AutoLockTimer, loadAutoLockSeconds, saveAutoLockSeconds } from './auto-lock';
import { checkWindowsRelease } from './windows-updates';

const root = path.resolve(__dirname, '../..');
let bridge: CoreBridge;
let window: BrowserWindow | undefined;
let busy = false;
let authenticating = false;
let protectionEnabled = false;
let mustLock = false;
let revision = 0;
const autoLock=new AutoLockTimer(()=>{void lock().catch(()=>{});});
let connecting = false;
let importPath: string | undefined;
async function chooseFile<T>(operation:Promise<T>):Promise<T> {
  let timer:NodeJS.Timeout|undefined;
  try{return await Promise.race([operation,new Promise<never>((_resolve,reject)=>{timer=setTimeout(()=>reject(new Error('文件选择已超时，请重试。')),120000)})]);}
  finally{clearTimeout(timer);}
}
protocol.registerSchemesAsPrivileged([{scheme:'mfa', privileges:{standard:true,secure:true,supportFetchAPI:true}}]);
function scheduleAutoLock(): void {
  if(window)autoLock.schedule({enabled:protectionEnabled&&!mustLock,authenticating,focused:window.isFocused()});
}
async function lock(): Promise<Record<string,unknown>> {
  importPath=undefined;
  ++revision; mustLock = true; autoLock.cancel();
  if (window && !window.webContents.isDestroyed()) window.webContents.send('core:locked');
  try { return await bridge.request('lock'); }
  catch { bridge.stop(); throw new Error('连接已关闭，请重新连接后验证身份。'); }
}
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', () => { window?.show(); window?.focus(); });
  let closing = false;
  app.on('before-quit', event => {
    if (closing || !bridge) return;
    event.preventDefault(); closing = true; autoLock.cancel();
    void bridge.close().finally(() => app.quit());
  });
  app.on('window-all-closed', () => app.quit());
  void app.whenReady().then(async () => {
    const localData = process.env.LOCALAPPDATA;
    if (!localData || !path.isAbsolute(localData)) throw new Error('无法确定本地数据目录。');
    const dataDirectory = app.isPackaged
      ? path.join(app.getPath('userData'), 'accounts')
      : path.join(localData, 'Liubai MFA Electron');
    // 界面偏好独立保存，不改动账户密文、设备密钥和身份保护配置。
    const autoLockPath=path.join(app.getPath('userData'),'auto-lock.v1.json');
    autoLock.setSeconds(loadAutoLockSeconds(autoLockPath));
    const storageArgs = ['--data-directory', dataDirectory];
    bridge = app.isPackaged
      ? new CoreBridge(path.join(process.resourcesPath, 'core', 'LiubaiMfa.CoreHost.exe'), storageArgs)
      : new CoreBridge('dotnet', [path.resolve(root, '../LiubaiMfa.CoreHost/bin/Release/net8.0-windows10.0.19041.0/LiubaiMfa.CoreHost.dll'), ...storageArgs]);
    protocol.handle('mfa', async request => {
      const url = new URL(request.url);
      const allowed = new Set(['index.html','styles.css','ui.js','renderer.js','accounts-view.js','account-dialog.js','backup-dialog.js','update-view.js']);
      const file = url.pathname.slice(1);
      const isIcon = /^icons\/[a-z]+\.(?:svg|png)$/.test(file);
      if (url.hostname !== 'app' || (!allowed.has(file) && !isIcon) || request.method !== 'GET') return new Response('', {status:404});
      const mimeTypes: Record<string, string> = {
        '.html': 'text/html; charset=utf-8',
        '.css': 'text/css; charset=utf-8',
        '.js': 'text/javascript; charset=utf-8',
        '.svg': 'image/svg+xml',
        '.png': 'image/png',
      };
      const data = await readFile(path.join(root, 'dist/renderer', file)).catch(() => undefined);
      if (!data) return new Response('', {status:404});
      return new Response(Uint8Array.from(data), {headers:{
        'Content-Type': mimeTypes[path.extname(file)],
        'X-Content-Type-Options': 'nosniff',
      }});
    });
    window = new BrowserWindow({
      width:880,height:640,minWidth:480,minHeight:520,title:'留白 MFA · 预览版',
      icon:path.join(root, 'dist/renderer/app-icon.png'),backgroundColor:'#f2f2f5',show:false,
      webPreferences:{preload:path.join(root,'dist/preload/preload.js'),contextIsolation:true,nodeIntegration:false,sandbox:true,devTools:!app.isPackaged},
    });
    const contents = window.webContents;
    const verifyApplicationFrame = (event: IpcMainInvokeEvent): void => {
      if (event.sender !== contents || event.senderFrame !== contents.mainFrame || event.senderFrame.url !== 'mfa://app/index.html') {
        throw new Error('请求来源无效。');
      }
    };
    let updateCheck: ReturnType<typeof checkWindowsRelease> | undefined;
    let releaseUrl: string | undefined;
    ipcMain.handle('update:request', async (event, method: unknown, ...args: unknown[]) => {
      verifyApplicationFrame(event);
      if (args.length) throw new Error('输入内容无效。');
      if (method === 'version') return app.getVersion();
      if (method === 'check') {
        releaseUrl = undefined;
        updateCheck ??= checkWindowsRelease(app.getVersion()).finally(() => { updateCheck = undefined; });
        const result = await updateCheck;
        if (result.status === 'available') {
          releaseUrl = result.releaseUrl;
          return {status: result.status, version: result.version};
        }
        return result;
      }
      if (method === 'open' && releaseUrl && !mustLock) {
        await shell.openExternal(releaseUrl);
        return true;
      }
      return false;
    });
    window.removeMenu();
    window.on('blur', scheduleAutoLock);
    window.on('focus', () => autoLock.cancel());
    powerMonitor.on('lock-screen', () => { void lock().catch(() => {}); });
    powerMonitor.on('suspend', () => { void lock().catch(() => {}); });
    contents.session.setPermissionRequestHandler((_wc,_permission,callback) => callback(false));
    contents.session.setPermissionCheckHandler(() => false);
    contents.session.webRequest.onBeforeRequest((details,callback) => callback({cancel:!details.url.startsWith('mfa://app/')}));
    contents.setWindowOpenHandler(() => ({action:'deny'}));
    contents.on('will-navigate', event => event.preventDefault());
    contents.on('render-process-gone', () => bridge.stop());
    bridge.on('locked', () => {
      importPath=undefined;
      ++revision;mustLock=true;
      if(!contents.isDestroyed())contents.send('core:locked');
    });
    bridge.on('disconnected', () => {
      importPath=undefined;
      if (connecting) return;
      ++revision; mustLock = true;
      if (!contents.isDestroyed()) contents.send('core:disconnected');
    });
    ipcMain.handle('core:request', async (event, method: unknown, payload: unknown = {}) => {
      verifyApplicationFrame(event);
      const permitted = ['connect','status','accounts','addAccount','editAccount','createGroup','deleteGroup','renameGroup','moveAccount','deleteAccount','copyCode','unlock','lock','setProtection','setAutoLock','exportPrepare','exportSave','importChoose','importPreview','importCommit','backupCancel','qrRead','qrCommit','qrCancel'];
      if (typeof method !== 'string' || !permitted.includes(method)) return {ok:false,message:'不支持此操作。'};
      if (!payload || typeof payload !== 'object' || Array.isArray(payload) || JSON.stringify(payload).length > 4096) return {ok:false,message:'输入内容无效。'};
      const fields = payload as Record<string,unknown>;
      const keys = method==='addAccount' ? ['issuer','account','group','color','secret'] : method==='editAccount' ? ['id','issuer','account','group','color'] : method==='createGroup'?['name']:method==='deleteGroup'?['group']:method==='renameGroup'?['group','name']:method==='moveAccount'?['id','group']:method==='qrCommit'?['token','issuer','account','group','color']:method==='qrRead'?['source']:method==='deleteAccount'||method==='copyCode' ? ['id'] : method==='setProtection'?['enabled']:method==='setAutoLock'?['seconds']:method==='exportSave'?['token','confirmed']:method==='importCommit'?['token']:method==='importPreview'?['recoveryKey']:[];
      if (Object.keys(fields).length !== keys.length || keys.some(key => typeof fields[key] !== (key==='seconds'?'number':key==='enabled'||key==='confirmed'?'boolean':'string'))) return {ok:false,message:'输入内容无效。'};
      if(method==='backupCancel'){++revision;importPath=undefined;try{return {ok:true,result:await bridge.request('backupCancel')}}catch{return {ok:false,message:'本次备份操作已取消。'}}}
      if(method==='qrCancel'){++revision;try{return {ok:true,result:await bridge.request('qrCancel')}}catch{return {ok:false,message:'二维码导入已取消。'}}}
      // 锁定不排在身份验证后面；代次检查阻止旧结果恢复页面。
      if (method==='lock') {
        try { return {ok:true,result:await lock()}; } catch { return {ok:false,message:'已关闭连接，请重新验证身份。'}; }
      }
      if (busy) return {ok:false,message:'正在处理，请稍后重试。'};
      busy = true;
      const ticket = revision;
      authenticating = ['unlock','setProtection','exportPrepare','exportSave','importChoose','qrRead'].includes(method);
      if (authenticating)autoLock.cancel();
      try {
        let result:Record<string,unknown>;
        if(method==='setAutoLock'){
          const state=await bridge.request('status');
          if(ticket!==revision||state.locked)throw new Error('请先解锁账户。');
          saveAutoLockSeconds(autoLockPath,fields.seconds);
          autoLock.setSeconds(fields.seconds);
          result={autoLockSeconds:autoLock.seconds};
        }else if(method==='qrRead'){
          if(fields.source!=='image'&&fields.source!=='screen')return {ok:false,message:'不支持此识别方式。'};
          result=await readQr(fields.source,window!,bridge,()=>ticket===revision,chooseFile);
        }else if(method==='importChoose'){
          importPath=undefined;
          const state=await bridge.request('status');if(state.locked)throw new Error('请先解锁账户。');
          const selected=await chooseFile(dialog.showOpenDialog(window!,{title:'选择加密备份',properties:['openFile'],filters:[{name:'留白 MFA 加密备份',extensions:['json']}]}));
          if(ticket!==revision)throw new Error('操作已失效。');
          if(selected.canceled)return {ok:true,result:{cancelled:true}};
          importPath=selected.filePaths[0];result={name:path.basename(importPath)};
        } else if(method==='exportSave'){
          if(fields.confirmed!==true)throw new Error('请先确认已保存恢复密钥。');
          const selected=await chooseFile(dialog.showSaveDialog(window!,{title:'保存加密备份（请选择新文件名）',defaultPath:`留白MFA备份-${new Date().toISOString().slice(0,10)}.json`,filters:[{name:'加密备份',extensions:['json']}]}));
          if(ticket!==revision)throw new Error('操作已失效。');
          if(selected.canceled||!selected.filePath){await bridge.request('backupCancel');return {ok:true,result:{cancelled:true}};}
          result=await bridge.request('exportSave',{...fields,path:selected.filePath});
        } else if(method==='importPreview'){
          if(!importPath)throw new Error('请先选择备份文件。');
          const chosen=importPath;importPath=undefined;
          result=await bridge.request('importPreview',{path:chosen,recoveryKey:fields.recoveryKey});
        } else if (method === 'connect') {
          const reconnect = bridge.connected;
          connecting = true;
          try {
            await bridge.start();
            const handle = window!.getNativeWindowHandle();
            result = await bridge.request('handshake',{hwnd:(handle.length===8?handle.readBigUInt64LE():BigInt(handle.readUInt32LE())).toString()});
            if (mustLock || reconnect) result = await bridge.request('lock');
          } finally { connecting = false; }
        } else {
          result = await bridge.request(method as 'accounts', fields);
        }
        if (ticket!==revision) return {ok:false,message:'操作已失效，请重新验证身份。'};
        if(['connect','status','unlock','setProtection'].includes(method)){
          if (typeof result.protectionEnabled==='boolean') protectionEnabled=result.protectionEnabled;
          if (typeof result.locked==='boolean') mustLock=result.locked;
          result={...result,autoLockSeconds:autoLock.seconds};
        }
        if (method==='copyCode' && result.copied!==true) return {ok:false,message:'复制失败，请重试。'};
        return {ok:true,result};
      } catch (error) { return {ok:false,message:error instanceof Error ? error.message : '操作失败，请重试。'}; }
      finally { busy = false; authenticating = false; scheduleAutoLock(); }
    });
    window.once('ready-to-show', () => window?.show());
    await window.loadURL('mfa://app/index.html');
  }).catch(() => { bridge?.stop(); app.exit(1); });
}
