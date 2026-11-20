const searchInput=document.getElementById('search') as HTMLInputElement;
const addButton=document.getElementById('add') as HTMLButtonElement;
const retryButton=document.getElementById('retry') as HTMLButtonElement;
const settingsButton=document.getElementById('settings') as HTMLButtonElement;
const protectionButton=document.getElementById('protection') as HTMLButtonElement;
const autoLockSelect=document.getElementById('autoLockSeconds') as HTMLSelectElement;
const statusLabel=document.getElementById('status')!;
let accounts:AccountDto[]=[];
let groupNames:string[]=[];
const newGroupButton=document.getElementById('newGroup') as HTMLButtonElement;
AccountDialog.availableGroups=()=>groupNames;
let epoch=0;
let working=false;
let protectionEnabled=false;
let autoLockSeconds=60;
let toastTimer:ReturnType<typeof setTimeout>;
const actions={copy,edit:AccountDialog.open,remove:AccountDialog.confirmDelete,renameGroup:AccountDialog.renameGroup,deleteGroup:AccountDialog.confirmDeleteGroup,move:moveAccount,canMove:()=>!working&&document.body.dataset.state==='connected'&&document.body.dataset.page==='home',add:()=>AccountDialog.open(),clearSearch:()=>{searchInput.value='';render();searchInput.focus()}};
function render():void {AccountsView.render(accounts,searchInput.value,actions,groupNames);document.getElementById('accountCount')!.textContent=`${accounts.length} 个账户`;}
function toast(message:string):void {const box=document.getElementById('toast')!;box.textContent=message;box.hidden=false;clearTimeout(toastTimer);toastTimer=setTimeout(()=>box.hidden=true,2200)}
function wipe():void {
  AccountsView.cancelDrag();
  BackupDialog.reset();
  accounts=[];groupNames=[];newGroupButton.disabled=true;addButton.disabled=true;settingsButton.disabled=true;searchInput.value='';searchInput.disabled=true;
  autoLockSelect.disabled=true;
  AccountDialog.reset();Ui.show('home');AccountsView.collapsed.clear();document.getElementById('accountCount')!.textContent='';
  document.getElementById('toast')!.hidden=true;document.getElementById('settingsError')!.textContent='';
}
function clear(message:string):void {
  wipe();document.body.dataset.state='disconnected';retryButton.hidden=false;statusLabel.textContent=message;
  AccountsView.empty('服务暂不可用',message,()=>void connect(),'重新连接');
}
function locked(message='验证身份后可查看账户和验证码。'):void {
  wipe();document.body.dataset.state='locked';retryButton.hidden=true;statusLabel.textContent='已锁定 · 本机加密保存';
  AccountsView.empty('账户已锁定',message,()=>void authenticate(),'验证身份并解锁');
}
async function showSession(response:Reply,ticket:number):Promise<void> {
  if(ticket!==epoch)return;
  if(!response.ok||typeof response.result?.locked!=='boolean')throw new Error(response.message||'无法读取安全状态。');
  protectionEnabled=response.result.protectionEnabled===true;
  protectionButton.setAttribute('aria-checked',String(protectionEnabled));
  autoLockSeconds=response.result.autoLockSeconds??60;
  autoLockSelect.value=String(autoLockSeconds);
  document.getElementById('autoLockHint')!.textContent=protectionEnabled?'切换窗口或最小化后开始计时':'开启上方身份保护后生效';
  if(response.result.locked){locked();return}
  const result=await mfaCore.accounts();if(ticket!==epoch)return;
  if(!result.ok||!Array.isArray(result.result?.accounts))throw new Error(result.message);
  accounts=result.result.accounts;groupNames=result.result.groups||[];document.body.dataset.state='connected';newGroupButton.disabled=false;addButton.disabled=false;settingsButton.disabled=false;searchInput.disabled=false;
  autoLockSelect.disabled=false;
  retryButton.hidden=true;
  statusLabel.textContent=protectionEnabled?'本机加密保存 · 启动保护已开启':'本机加密保存 · 启动保护未开启';render();
}
async function connect():Promise<void> {
  if(working)return;working=true;const ticket=++epoch;wipe();
  document.body.dataset.state='connecting';retryButton.disabled=true;
  AccountsView.empty('正在读取账户','正在连接本地加密账户库。');
  try {await showSession(await mfaCore.connect(),ticket)}
  catch(error){if(ticket===epoch)clear(error instanceof Error?error.message:'连接失败，请重试。')}
  finally{working=false;retryButton.disabled=false}
}
async function authenticate(enabled?:boolean):Promise<void> {
  if(working)return;working=true;const ticket=++epoch;wipe();retryButton.disabled=true;
  document.body.dataset.state='authenticating';statusLabel.textContent='请在 Windows 系统窗口完成验证';
  AccountsView.empty('正在验证身份','请在系统窗口完成验证，也可以取消本次操作。');
  try {
    const response=await (enabled===undefined?mfaCore.unlock():mfaCore.setProtection(enabled));
    if(ticket!==epoch)return;
    await showSession(await mfaCore.status(),ticket);
    if(ticket===epoch&&!response.ok)toast(response.message||'未完成验证，请重试。');
  }catch(error){if(ticket===epoch)clear(error instanceof Error?error.message:'无法验证身份。')}
  finally{working=false;retryButton.disabled=false}
}
async function mutate(operation:()=>Promise<Reply>,message:string,onError=AccountDialog.error):Promise<boolean> {
  if(working||document.body.dataset.state!=='connected')return false;
  working=true;const ticket=epoch;
  try {
    const response=await operation();if(ticket!==epoch)return false;
    if(!response.ok||!response.result?.accounts){onError(response.message||'保存失败，请重新填写密钥后重试。');return false}
    accounts=response.result.accounts;groupNames=response.result.groups||[];render();toast(message);return true;
  }catch{if(ticket===epoch)onError('操作失败，请重试。');return false}
  finally{working=false}
}
AccountDialog.save=(fields,id)=>mutate(()=>id?mfaCore.editAccount({...fields,id}):mfaCore.addAccount(fields),id?'账户已更新':'账户已加密保存');
AccountDialog.remove=id=>mutate(()=>mfaCore.deleteAccount(id),'账户已删除');
AccountDialog.create=name=>mutate(()=>mfaCore.createGroup(name),'分组已创建');
AccountDialog.removeGroup=async group=>{const saved=await mutate(()=>mfaCore.deleteGroup(group),'空分组已删除');if(saved)AccountsView.collapsed.delete(group);return saved;};
newGroupButton.onclick=AccountDialog.createGroup;
async function moveAccount(id:string,group:string):Promise<boolean> {
  const saved=await mutate(()=>mfaCore.moveAccount(id,group),`已移到“${group||'未分组'}”`,toast);
  if(saved){AccountsView.collapsed.delete(group);render();Array.from(document.querySelectorAll<HTMLElement>('.group-head')).find(header=>header.dataset.group===group)?.scrollIntoView({block:'nearest'});}
  return saved;
}
AccountDialog.rename=async(group,name)=>{const saved=await mutate(()=>mfaCore.renameGroup(group,name),'分组名称已更新');if(saved&&AccountsView.collapsed.delete(group)){AccountsView.collapsed.add(name);render();}return saved;};
AccountDialog.readQr=async source=>{
  if(working||document.body.dataset.state!=='connected')return {ok:false,message:'正在处理其他操作，请稍后重试。'};
  working=true;const ticket=epoch;
  try{const reply=await mfaCore.qrRead(source);return ticket===epoch?reply:{ok:false,message:'本次识别已失效。'};}
  catch{return {ok:false,message:'二维码识别失败，请重试。'};}
  finally{working=false;}
};
AccountDialog.saveQr=fields=>mutate(()=>mfaCore.qrCommit(fields),'二维码账户已加密保存');
async function copy(account:AccountDto):Promise<void> {
  if(working||document.body.dataset.state!=='connected')return;
  working=true;const ticket=epoch;
  try{const result=await mfaCore.copyCode(account.id);if(ticket===epoch)toast(result.ok&&result.result?.copied?'已复制，30 秒后自动清理':'复制失败，请重试。')}
  catch{if(ticket===epoch)toast('复制失败，请重试。')}finally{working=false}
}
searchInput.addEventListener('input',()=>{if(document.body.dataset.state==='connected')render()});
addButton.onclick=()=>AccountDialog.open();retryButton.onclick=()=>void connect();
settingsButton.onclick=()=>{Ui.show('settings');document.querySelector('#settingsDialog .scroll')!.scrollTop=0;document.getElementById('closeSettings')!.focus();};
BackupDialog.run=async operation=>{
  if(working||document.body.dataset.state!=='connected')return {ok:false,message:'正在处理其他操作，请稍后重试。'};
  working=true;const ticket=epoch;try{const reply=await operation();return ticket===epoch?reply:{ok:false,message:'操作已失效。'};}catch{return {ok:false,message:'操作失败，请重试。'};}finally{working=false;}
};
BackupDialog.restored=()=>{const ticket=epoch;void mfaCore.status().then(reply=>showSession(reply,ticket)).catch(()=>{if(ticket===epoch)clear('请重新连接以核对恢复后的账户。');});};
document.getElementById('exportBackup')!.onclick=()=>void BackupDialog.startExport();
document.getElementById('importBackup')!.onclick=()=>void BackupDialog.startImport();
document.getElementById('closeSettings')!.onclick=()=>{Ui.show('home');settingsButton.focus();};
protectionButton.onclick=()=>void authenticate(!protectionEnabled);
autoLockSelect.onchange=async()=>{
  if(working||document.body.dataset.state!=='connected'){autoLockSelect.value=String(autoLockSeconds);return;}
  working=true;autoLockSelect.disabled=true;const ticket=epoch;
  document.getElementById('settingsError')!.textContent='';
  try {
    const reply=await mfaCore.setAutoLock(Number(autoLockSelect.value));
    if(ticket!==epoch)return;
    if(!reply.ok||typeof reply.result?.autoLockSeconds!=='number')throw new Error(reply.message||'自动锁定时间保存失败，请重试。');
    autoLockSeconds=reply.result.autoLockSeconds;toast('自动锁定时间已保存');
  }catch(error){if(ticket===epoch)document.getElementById('settingsError')!.textContent=error instanceof Error?error.message:'自动锁定时间保存失败，请重试。';}
  finally{working=false;autoLockSelect.value=String(autoLockSeconds);autoLockSelect.disabled=document.body.dataset.state!=='connected';}
};
document.getElementById('lockNow')!.onclick=()=>{++epoch;locked();void mfaCore.lock().then(r=>{if(!r.ok)clear(r.message||'连接已关闭。')}).catch(()=>clear('连接已关闭。'))};
document.getElementById('theme')!.addEventListener('change',event=>{
  const theme=(event.target as HTMLSelectElement).value;
  if(theme==='system')delete document.documentElement.dataset.theme;else document.documentElement.dataset.theme=theme;
  document.querySelectorAll<HTMLButtonElement>('[data-theme-choice]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.themeChoice===theme)));
});
document.querySelectorAll<HTMLButtonElement>('[data-theme-choice]').forEach(button=>button.onclick=()=>{const theme=document.getElementById('theme') as HTMLSelectElement;theme.value=button.dataset.themeChoice!;theme.dispatchEvent(new Event('change'));});
document.addEventListener('keydown',event=>{
  if(document.querySelector('dialog[open]'))return;
  if(event.key==='Escape'){
    if(document.body.dataset.page==='editor')AccountDialog.close();
    else if(document.body.dataset.page==='settings'){Ui.show('home');settingsButton.focus();}
    else AccountsView.closeMenus();
  }
  if(event.ctrlKey&&event.key.toLowerCase()==='f'&&!searchInput.disabled&&document.body.dataset.page==='home'){event.preventDefault();searchInput.focus()}
  if(event.ctrlKey&&event.key.toLowerCase()==='n'&&!addButton.disabled){event.preventDefault();AccountDialog.open()}
});
document.addEventListener('click',event=>{if(!(event.target as Element).closest('.menu-wrap'))AccountsView.closeMenus()});
mfaCore.onDisconnected(()=>{++epoch;clear('连接已断开，请重新连接。')});
mfaCore.onLocked(()=>{++epoch;locked()});
setInterval(()=>{
  if(document.body.dataset.state!=='connected')return;
  const expired=AccountsView.tick()||accounts.some(account=>account.expiresAt<=Date.now());
  if(!expired||working||AccountsView.isDragging())return;
  working=true;const ticket=epoch;
  void mfaCore.accounts().then(response=>{
    if(ticket!==epoch)return;
    if(!response.ok||!response.result?.accounts){++epoch;clear(response.message||'无法刷新验证码，请重新连接。');return}
    accounts=response.result.accounts;groupNames=response.result.groups||[];render();
  }).catch(()=>{if(ticket===epoch){++epoch;clear('无法刷新验证码，请重新连接。')}}).finally(()=>working=false);
},250);
void connect();
