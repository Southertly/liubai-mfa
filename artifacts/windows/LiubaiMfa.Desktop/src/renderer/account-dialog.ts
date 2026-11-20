namespace AccountDialog {
  const editor=document.getElementById('editor')!;
  const form=document.getElementById('accountForm') as HTMLFormElement;
  const deleteDialog=document.getElementById('deleteDialog') as HTMLDialogElement;
  const groupSelect=document.getElementById('groupSelect') as HTMLSelectElement;
  const groupInput=document.getElementById('group') as HTMLInputElement;
  const renameDialog=document.getElementById('renameGroupDialog') as HTMLDialogElement;
  const renameInput=document.getElementById('renameGroupName') as HTMLInputElement;
  let renamingGroup:string|undefined;
  let creatingGroup=false;
  let groupChoices:string[]=[];
  let currentId:string|undefined;
  let removalId:string|undefined;
  let removalGroup:string|undefined;
  let submitting=false;
  let qrToken:string|undefined;
  let qrActive=false;
  let operation=0;
  export let save:(fields:AccountFields,id?:string)=>Promise<boolean>;
  export let readQr:(source:'image'|'screen')=>Promise<Reply>;
  export let saveQr:(fields:Omit<AccountFields,'secret'> & {token:string})=>Promise<boolean>;
  export let remove:(id:string)=>Promise<boolean>;
  export let removeGroup:(group:string)=>Promise<boolean>;
  export let rename:(group:string,name:string)=>Promise<boolean>;
  export let create:(name:string)=>Promise<boolean>;
  export function createGroup():void {
    if(submitting||document.body.dataset.state!=='connected')return;
    creatingGroup=true;renamingGroup=undefined;renameInput.value='';
    document.getElementById('renameGroupTitle')!.textContent='新建分组';
    document.getElementById('renameGroupDescription')!.textContent='创建后可把账户拖入这个分组，空分组也会保留。';
    document.getElementById('renameGroupError')!.textContent='';renameDialog.showModal();renameInput.focus();
  }
  export function renameGroup(group:string,count:number):void {
    if(submitting||document.body.dataset.state!=='connected')return;
    creatingGroup=false;renamingGroup=group;renameInput.value=group;
    document.getElementById('renameGroupTitle')!.textContent='重命名分组';
    document.getElementById('renameGroupDescription')!.textContent=`将更新“${group}”分组内全部 ${count} 个账户的分组名称。`;
    document.getElementById('renameGroupError')!.textContent='';renameDialog.showModal();renameInput.focus();renameInput.select();
  }
  renameDialog.addEventListener('cancel',event=>{if(submitting)event.preventDefault()});
  renameDialog.addEventListener('close',()=>{renamingGroup=undefined;creatingGroup=false;renameInput.value='';document.getElementById('renameGroupDescription')!.textContent='';document.getElementById('renameGroupError')!.textContent='';});
  document.getElementById('cancelGroupRename')!.onclick=()=>renameDialog.close();
  document.getElementById('renameGroupForm')!.addEventListener('submit',async event=>{
    event.preventDefault();if(submitting||(!creatingGroup&&renamingGroup===undefined))return;
    const group=renamingGroup,name=renameInput.value.trim();
    if(!name){error('请输入分组名称。');renameInput.focus();return;}
    disabled(true);
    try{if(await (creatingGroup?create(name):rename(group!,name))){renameDialog.close();const focus=Array.from(document.querySelectorAll<HTMLButtonElement>('.group-rename')).find(button=>button.dataset.group===name);(focus||document.getElementById('add')!).focus();}}
    finally{disabled(false);}
  });
  export let availableGroups:()=>string[]=()=>[];
  function fillGroups(value='',names=availableGroups()):void {
    groupChoices=[...new Set(names.filter(name=>name.trim().length>0))];
    groupSelect.replaceChildren(new Option('未分组',''),...groupChoices.map((name,index)=>new Option(name,String(index))),new Option('新建分组…','new'));
    const index=groupChoices.indexOf(value);
    groupSelect.value=value.length===0?'':index>=0?String(index):'new';
    groupInput.value=value;groupInput.hidden=groupSelect.value!=='new';
    groupInput.removeAttribute('aria-invalid');document.getElementById('groupError')!.textContent='';
  }
  groupSelect.onchange=()=>{
    groupInput.hidden=groupSelect.value!=='new';
    groupInput.value=groupSelect.value==='new'||groupSelect.value===''?'':groupChoices[Number(groupSelect.value)];
    groupInput.removeAttribute('aria-invalid');document.getElementById('groupError')!.textContent='';
    if(!groupInput.hidden)groupInput.focus();
  };
  groupInput.oninput=()=>{groupInput.removeAttribute('aria-invalid');document.getElementById('groupError')!.textContent='';};
  export function open(account?:AccountDto):void {
    if(!editor.hidden)return;
    ++operation;qrToken=undefined;qrActive=false;disabled(false);document.getElementById('qrActions')!.hidden=!!account;
    document.getElementById('save')!.textContent='保存账户';
    currentId=account?.id;form.reset();
    document.getElementById('editorTitle')!.textContent=account?'编辑账户':'添加账户';
    document.getElementById('formTitle')!.textContent=account?'账户信息':'手动填写';
    document.getElementById('editorDescription')!.textContent=account?'修改名称、分组或颜色不会更改密钥。':'从二维码开始，也可以手动填写。';
    const secret=document.getElementById('secret') as HTMLInputElement;
    secret.value='';secret.required=!account;document.getElementById('secretField')!.hidden=!!account;
    document.getElementById('secretError')!.textContent='';
    for(const [key,value] of Object.entries(account||{issuer:'',account:'',group:'',color:'blue'})){
      const input=form.elements.namedItem(key);if(input instanceof HTMLInputElement||input instanceof HTMLSelectElement||input instanceof RadioNodeList)input.value=String(value);
    }
    fillGroups(account?.group||'');
    document.getElementById('formError')!.textContent='';Ui.show('editor');document.getElementById('issuer')!.focus({preventScroll:true});editor.querySelector('.scroll')!.scrollTop=0;
  }
  export function confirmDelete(account:AccountDto):void {
    removalGroup=undefined;document.getElementById('deleteTitle')!.textContent='删除账户？';
    document.getElementById('deleteHint')!.textContent='此操作无法撤销，请先确认已在其他地方保留恢复方式。';
    removalId=account.id;document.getElementById('deleteDescription')!.textContent=`将从本机移除“${account.issuer} · ${account.account}”。`;
    document.getElementById('deleteError')!.textContent='';deleteDialog.showModal();document.getElementById('cancelDelete')!.focus();
  }
  export function confirmDeleteGroup(group:string):void {
    if(submitting||document.body.dataset.state!=='connected')return;
    removalId=undefined;removalGroup=group;
    document.getElementById('deleteTitle')!.textContent='删除空分组？';
    document.getElementById('deleteDescription')!.textContent=`将删除空分组“${group}”。`;
    document.getElementById('deleteHint')!.textContent='仅删除这个空分组，不会删除或移动账户。';
    document.getElementById('deleteError')!.textContent='';deleteDialog.showModal();document.getElementById('cancelDelete')!.focus();
  }
  export function error(message:string):void {document.getElementById(renameDialog.open?'renameGroupError':deleteDialog.open?'deleteError':'formError')!.textContent=message}
  function cancelQr():void{++operation;qrToken=undefined;if(qrActive){qrActive=false;void mfaCore.qrCancel();}}
  export function reset():void {cancelQr();disabled(false);Ui.show('home');deleteDialog.close();renameDialog.close();renamingGroup=undefined;renameInput.value='';document.getElementById('renameGroupDescription')!.textContent='';document.getElementById('renameGroupError')!.textContent='';form.reset();fillGroups('',[]);document.getElementById('deleteDescription')!.textContent='';document.getElementById('formError')!.textContent='';document.getElementById('secretError')!.textContent='';currentId=undefined;removalId=undefined;removalGroup=undefined}
  export function close():void {
    if(submitting&&(!qrActive||!!qrToken))return;
    cancelQr();disabled(false);form.reset();fillGroups('',[]);currentId=undefined;Ui.show('home');document.getElementById('add')!.focus();
  }
  function disabled(value:boolean):void {submitting=value;document.querySelectorAll<HTMLButtonElement|HTMLInputElement|HTMLSelectElement>('#editor button,#editor input,#editor select,#deleteDialog button,#renameGroupDialog button,#renameGroupDialog input').forEach(control=>control.disabled=value)}
  deleteDialog.addEventListener('cancel',event=>{if(submitting)event.preventDefault()});
  document.getElementById('cancelEdit')!.onclick=close;
  document.getElementById('editorBack')!.onclick=close;
  document.getElementById('cancelDelete')!.onclick=()=>deleteDialog.close();
  async function recognize(source:'image'|'screen'):Promise<void>{
    if(submitting)return;
    cancelQr();qrActive=true;const ticket=operation;form.reset();fillGroups();
    const secret=document.getElementById('secret') as HTMLInputElement;secret.value='';secret.required=true;document.getElementById('secretField')!.hidden=false;
    document.getElementById('editorTitle')!.textContent='添加账户';document.getElementById('save')!.textContent='保存账户';
    document.getElementById('editorDescription')!.textContent='识别后先核对账户信息，再确认添加。';
    error(source==='screen'?'正在扫描当前屏幕…':'请选择二维码图片…');disabled(true);
    (document.getElementById('cancelEdit') as HTMLButtonElement).disabled=false;
    try{
      const response=await readQr(source);if(ticket!==operation||editor.hidden)return;
      if(!response.ok||!response.result?.token){qrActive=false;error(response.result?.cancelled?'已取消识别，可继续手动填写。':response.message||'无法识别二维码。');return;}
      qrToken=response.result.token;
      for(const name of ['issuer','account','group','color'] as const){const control=form.elements.namedItem(name) as HTMLInputElement;control.value=response.result[name]|| (name==='color'?'blue':'');}
      fillGroups(groupInput.value);
      document.getElementById('editorTitle')!.textContent='核对二维码账户';document.getElementById('secretField')!.hidden=true;secret.required=false;
      document.getElementById('formTitle')!.textContent='账户信息';
      document.getElementById('editorDescription')!.textContent='请核对服务和账户名称，可调整分组与颜色。确认后加密保存到本机。';
      document.getElementById('save')!.textContent='确认添加账户';error('');
      document.getElementById('issuer')!.focus();
    }finally{if(ticket===operation)disabled(false);}
  }
  document.getElementById('qrScreen')!.onclick=()=>void recognize('screen');
  document.getElementById('qrImage')!.onclick=()=>void recognize('image');
  form.addEventListener('submit',async event=>{
    event.preventDefault();if(submitting)return;
    const data=new FormData(form);const fields:AccountFields={issuer:String(data.get('issuer')).trim(),account:String(data.get('account')).trim(),group:String(data.get('group')).trim(),color:String(data.get('color'))};
    if(!fields.issuer||!fields.account){error('请填写服务名称和账户名称。');return}
    if(groupSelect.value==='new'){
      if(!fields.group){groupInput.setAttribute('aria-invalid','true');document.getElementById('groupError')!.textContent='请输入新分组名称。';groupInput.focus();return;}
      fields.group=groupChoices.find(name=>name.toLocaleLowerCase()===fields.group.toLocaleLowerCase())??fields.group;
    }
    if(!currentId&&!qrToken){
      const secret=String(data.get('secret')).replace(/\s/g,'').toUpperCase();
      const raw=secret.replace(/=+$/,'');const remainder=raw.length%8;const tail=raw.length*5%8;
      if(secret.length>1024||! /^[A-Z2-7]{2,}=*$/.test(secret)||[1,3,6].includes(remainder)||(secret!==raw&&(secret.length%8!==0||secret.length-raw.length!==(8-remainder)%8))||(tail!==0&&('ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'.indexOf(raw.at(-1)!)&((1<<tail)-1))!==0)){
        document.getElementById('secretError')!.textContent='请输入有效的 Base32 密钥，不支持链接或其他类型。';return;
      }
      fields.secret=secret;
    }
    data.delete('secret');
    const pending=qrToken?saveQr({...fields,token:qrToken}):save(fields,currentId);fields.secret=undefined;(document.getElementById('secret') as HTMLInputElement).value='';
    disabled(true);try{if(await pending){qrActive=false;qrToken=undefined;cancelQr();form.reset();fillGroups('',[]);currentId=undefined;Ui.show('home');document.getElementById('add')!.focus();}}finally{disabled(false)}
  });
  document.getElementById('confirmDelete')!.onclick=async()=>{
    if(submitting||(removalId===undefined&&removalGroup===undefined))return;
    const id=removalId,group=removalGroup;disabled(true);
    try{if(await (group!==undefined?removeGroup(group):remove(id!))){deleteDialog.close();removalId=undefined;removalGroup=undefined;if(group!==undefined)document.getElementById('newGroup')!.focus();}}
    finally{disabled(false)}
  };
}
