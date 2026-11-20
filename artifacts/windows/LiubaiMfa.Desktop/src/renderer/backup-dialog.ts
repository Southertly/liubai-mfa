namespace BackupDialog {
  const dialog=document.getElementById('backupDialog') as HTMLDialogElement;
  const output=document.getElementById('recoveryOutput') as HTMLTextAreaElement;
  const input=document.getElementById('recoveryInput') as HTMLInputElement;
  const confirmed=document.getElementById('recoveryConfirmed') as HTMLInputElement;
  const next=document.getElementById('backupNext') as HTMLButtonElement;
  let token='';let phase='';let generation=0;let saving=false;
  let timer:ReturnType<typeof setTimeout>|undefined;
  export let run:(operation:()=>Promise<Reply>)=>Promise<Reply>;
  export let restored:()=>void;
  function message(text:string):void{document.getElementById('backupError')!.textContent=text;}
  function section(id:string,show:boolean):void{document.getElementById(id)!.hidden=!show;}
  function hideRecoveryKey():void{
    output.value='';output.hidden=true;
    document.getElementById('backupDescription')!.textContent='恢复密钥已隐藏。如果已单独保存，请勾选确认并继续；否则取消后重新导出。';
  }
  export function reset():void {
    ++generation;clearTimeout(timer);token='';phase='';output.value='';input.value='';confirmed.checked=false;
    document.getElementById('backupPreview')!.replaceChildren();dialog.close();
  }
  function cancel():void{reset();void mfaCore.backupCancel();}
  function open(title:string,description:string):void{
    reset();document.getElementById('backupCancel')!.textContent='取消';
    document.getElementById('backupTitle')!.textContent=title;document.getElementById('backupDescription')!.textContent=description;
    for(const id of ['recoveryBox','importKeyBox','backupPreview'])section(id,false);
    message('');next.hidden=true;next.disabled=false;dialog.showModal();
  }
  export async function startExport():Promise<void>{
    open('导出加密备份','请在 Windows 系统窗口验证身份。');const ticket=generation;
    const response=await run(()=>mfaCore.exportPrepare());if(ticket!==generation)return;
    if(!response.ok||!response.result?.token||!response.result.recoveryKey){message(response.message||'未能生成备份。');return;}
    token=response.result.token;phase='export';output.hidden=false;output.value=response.result.recoveryKey;
    document.getElementById('backupDescription')!.textContent='先保存恢复密钥，再选择备份文件的保存位置。';
    section('recoveryBox',true);next.textContent='选择位置并保存';next.hidden=false;next.disabled=true;
    timer=setTimeout(hideRecoveryKey,30000);
  }
  export async function startImport():Promise<void>{
    open('从备份恢复','请选择加密备份文件。');const ticket=generation;
    const response=await run(()=>mfaCore.importChoose());if(ticket!==generation)return;
    if(response.result?.cancelled){cancel();return;}
    if(!response.ok){message(response.message||'无法选择文件。');return;}
    document.getElementById('backupDescription')!.textContent=`已选择：${response.result?.name||'加密备份'}。输入恢复密钥以预览。`;
    phase='key';section('importKeyBox',true);next.hidden=false;next.textContent='预览恢复内容';input.focus();
  }
  confirmed.onchange=()=>next.disabled=!confirmed.checked;
  next.onclick=async()=>{
    if(next.disabled)return;next.disabled=true;const ticket=generation;message('');
    try{
      if(phase==='export'){
        if(!confirmed.checked)return;
        clearTimeout(timer);output.value='';section('recoveryBox',false);saving=true;
        const response=await run(()=>mfaCore.exportSave(token));if(ticket!==generation)return;
        token='';phase='';
        if(response.ok&&response.result?.saved){document.getElementById('backupDescription')!.textContent='加密备份已保存。请将恢复密钥与备份文件分开保管。';next.hidden=true;document.getElementById('backupCancel')!.textContent='完成';}
        else{message(response.result?.cancelled?'保存已取消，未导出备份。':response.message||'备份未保存，请重新开始。');next.hidden=true;}
      }else if(phase==='key'){
        const key=input.value;input.value='';section('importKeyBox',false);
        const response=await run(()=>mfaCore.importPreview(key));if(ticket!==generation)return;
        if(!response.ok||!response.result?.token){message(response.message||'无法读取备份，请重新选择文件。');next.hidden=true;return;}
        token=response.result.token;phase='preview';
        document.getElementById('backupDescription')!.textContent=`将新增 ${response.result.added} 个账户，跳过 ${response.result.skipped} 个重复账户。确认后追加到本机。`;
        if(response.result.groupsAdded)document.getElementById('backupDescription')!.textContent+=`同时新增 ${response.result.groupsAdded} 个分组。`;
        const preview=document.getElementById('backupPreview')!;
        for(const account of response.result.accounts||[]){const row=document.createElement('p');row.textContent=`${account.issuer} · ${account.account}${account.group?' · '+account.group:''}`;preview.append(row);}
        section('backupPreview',true);next.textContent='确认恢复';next.disabled=false;document.getElementById('backupCancel')!.focus();
      }else if(phase==='preview'){
        const response=await run(()=>mfaCore.importCommit(token));if(ticket!==generation)return;
        token='';phase='';section('backupPreview',false);document.getElementById('backupPreview')!.replaceChildren();next.hidden=true;
        if(response.ok){document.getElementById('backupDescription')!.textContent=`恢复完成：新增 ${response.result?.added} 个账户，跳过 ${response.result?.skipped} 个，新增 ${response.result?.groupsAdded||0} 个分组。`;document.getElementById('backupCancel')!.textContent='完成';restored();}
        else message(response.message||'恢复未完成，请重新预览。');
      }
    }finally{saving=false;}
  };
  document.getElementById('backupCancel')!.onclick=cancel;
  dialog.addEventListener('cancel',event=>{event.preventDefault();cancel();});
  window.addEventListener('blur',()=>{if(phase==='export'&&!saving)hideRecoveryKey();if(phase==='key')input.value='';});
}
