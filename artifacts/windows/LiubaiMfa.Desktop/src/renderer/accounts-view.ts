type AccountDto = {id:string;issuer:string;account:string;group:string;color:string;code:string;expiresAt:number};
type AccountFields = {issuer:string;account:string;group:string;color:string;secret?:string};
type Reply = {ok:boolean;result?:{groups?:string[];accounts?:AccountDto[];copied?:boolean;passed?:boolean;locked?:boolean;protectionEnabled?:boolean;autoLockSeconds?:number;generation?:number;token?:string;recoveryKey?:string;saved?:boolean;cancelled?:boolean;name?:string;added?:number;skipped?:number;groupsAdded?:number;issuer?:string;account?:string;group?:string;color?:string;count?:number};message?:string};
declare const mfaCore: {
  qrRead(source:'image'|'screen'):Promise<Reply>;qrCommit(fields:Omit<AccountFields,'secret'> & {token:string}):Promise<Reply>;qrCancel():Promise<Reply>;
  exportPrepare():Promise<Reply>;exportSave(token:string):Promise<Reply>;importChoose():Promise<Reply>;importPreview(key:string):Promise<Reply>;importCommit(token:string):Promise<Reply>;backupCancel():Promise<Reply>;
  connect():Promise<Reply>;status():Promise<Reply>;accounts():Promise<Reply>;
  addAccount(fields:AccountFields):Promise<Reply>;editAccount(fields:AccountFields & {id:string}):Promise<Reply>;
  renameGroup(group:string,name:string):Promise<Reply>;
  createGroup(name:string):Promise<Reply>;
  deleteGroup(group:string):Promise<Reply>;
  moveAccount(id:string,group:string):Promise<Reply>;
  unlock():Promise<Reply>;lock():Promise<Reply>;setProtection(enabled:boolean):Promise<Reply>;onLocked(callback:()=>void):()=>void;
  setAutoLock(seconds:number):Promise<Reply>;
  deleteAccount(id:string):Promise<Reply>;copyCode(id:string):Promise<Reply>;onDisconnected(callback:()=>void):()=>void;
};
namespace AccountsView {
  // 只根据服务名称匹配。短名称 X 必须精确匹配，避免把其他 X 开头的服务误认为 X。
  const serviceBrands = [
    ['google', /^(?:google|谷歌|gmail)(?=$|[\s:_.\-\p{Script=Han}])/u],
    ['microsoft', /^(?:microsoft|微软|outlook|office 365|azure)(?=$|[\s:_.\-\p{Script=Han}])/u],
    ['meta', /^meta(?=$|[\s:_.\-\p{Script=Han}])/u],
    ['x', /^(?:x(?:\.com)?|twitter(?:\.com)?|推特)$/u],
    ['openai', /^(?:openai|chatgpt)(?=$|[\s:_.\-\p{Script=Han}])/u],
    ['github', /^github(?=$|[\s:_.\-\p{Script=Han}])/u],
    ['alibaba', /^(?:阿里云|aliyun|alibaba cloud)(?=$|[\s:_.\-\p{Script=Han}])/u],
    ['tencent', /^(?:腾讯云|tencent cloud|dnspod)(?=$|[\s:_.\-\p{Script=Han}])/u, 'png'],
    ['bitwarden', /^bitwarden(?=$|[\s:_.\-\p{Script=Han}])/u],
    ['vaultwarden', /^vaultwarden(?=$|[\s:_.\-\p{Script=Han}])/u],
    ['okx', /^(?:欧易|okx)(?=$|[\s:_.\-\p{Script=Han}])/u, 'png'],
    ['paypal', /^paypal(?=$|[\s:_.\-\p{Script=Han}])/u, 'png'],
    ['wise', /^(?:wise|transferwise)(?=$|[\s:_.\-\p{Script=Han}])/u, 'png'],
    ['volcengine', /^(?:火山引擎|火山云|volcengine|volcano engine)(?=$|[\s:_.\-\p{Script=Han}])/u, 'png'],
    ['huawei', /^(?:华为云|huawei cloud|huaweicloud)(?=$|[\s:_.\-\p{Script=Han}])/u, 'png'],
    ['jumpserver', /^jumpserver(?=$|[\s:_.\-\p{Script=Han}])/u, 'png'],
  ] as const;

  function serviceIcon(issuer: string, color: string): HTMLElement {
    const label = issuer.trim();
    const initial = Array.from(label)[0]?.toUpperCase() || '•';
    const icon = node('div', 'service-icon', initial);
    icon.dataset.color = color;
    icon.setAttribute('aria-hidden', 'true');

    const normalized = label.normalize('NFKC').toLowerCase();
    const qualifier = /\(([^()]*)\)$/.exec(normalized)?.[1].trim() ?? '';
    const match = serviceBrands.find(([, pattern]) => pattern.test(normalized) || pattern.test(qualifier));
    if (!match) return icon;

    const [brand, , extension = 'svg'] = match;
    icon.dataset.brand = brand;
    if (brand === 'vaultwarden') {
      icon.replaceChildren(Ui.icon('key'));
      return icon;
    }

    const image = document.createElement('img');
    image.alt = '';
    image.draggable = false;
    image.addEventListener('error', () => {
      delete icon.dataset.brand;
      icon.textContent = initial;
    }, {once: true});
    image.src = `icons/${brand}.${extension}`;
    icon.replaceChildren(image);
    return icon;
  }

  export const collapsed = new Set<string>();
  type Actions = {copy:(account:AccountDto)=>void;edit:(account:AccountDto)=>void;remove:(account:AccountDto)=>void;renameGroup:(group:string,count:number)=>void;deleteGroup:(group:string)=>void;move:(id:string,group:string)=>Promise<boolean>;canMove:()=>boolean;add:()=>void;clearSearch:()=>void};
  export function folderTone(group:string):string {
    if(!group)return 'neutral';
    const tones=['sage','amber','sky','lavender','rose','teal'];
    let hash=2166136261;
    for(const character of group)hash=Math.imul(hash^character.codePointAt(0)!,16777619)>>>0;
    return tones[hash%tones.length];
  }
  const dragType='application/x-liubai-account';
  let drag:{id:string;group:string;token:string}|undefined;
  let scrollFrame=0,scrollSpeed=0;
  export function isDragging():boolean {return !!drag;}
  export function cancelDrag():void {
    drag=undefined;scrollSpeed=0;cancelAnimationFrame(scrollFrame);scrollFrame=0;
    document.querySelectorAll('.drag-extra,.drag-preview').forEach(node=>node.remove());
    document.querySelectorAll('.dragging,.drop-target').forEach(node=>node.classList.remove('dragging','drop-target'));
    document.body.classList.remove('dragging-account');
  }
  function scrollAtEdge(y:number):void {
    const scroller=document.querySelector<HTMLElement>('#homePage .scroll')!;
    const bounds=scroller.getBoundingClientRect();
    scrollSpeed=y<bounds.top+44?-Math.min(14,(bounds.top+44-y)/3):y>bounds.bottom-44?Math.min(14,(y-bounds.bottom+44)/3):0;
    if(!scrollSpeed||scrollFrame)return;
    const step=()=>{scrollFrame=0;if(!drag||!scrollSpeed)return;scroller.scrollTop+=scrollSpeed;scrollFrame=requestAnimationFrame(step);};
    scrollFrame=requestAnimationFrame(step);
  }
  document.addEventListener('dragover',event=>{if(drag)scrollAtEdge(event.clientY);});
  document.addEventListener('drop',()=>{if(drag)cancelDrag();});
  window.addEventListener('blur',cancelDrag);
  function node<K extends keyof HTMLElementTagNameMap>(tag:K, className:string, text=''):HTMLElementTagNameMap[K] {
    const result=document.createElement(tag);result.className=className;result.textContent=text;return result;
  }
  export function empty(title:string,description:string,action?:()=>void,label='添加账户'):void {
    cancelDrag();
    const container=document.getElementById('accounts')!;
    const box=node('div','empty');const mark=node('div','empty-icon');mark.append(Ui.icon(document.body.dataset.state==='locked'?'lock':'shield'));box.append(mark,node('h2','',title),node('p','',description));
    if(action){const button=node('button','primary',label);button.onclick=action;box.append(button)}
    container.replaceChildren(box);
  }
  export function render(accounts:AccountDto[],query:string,actions:Actions,names:string[]=[]):void {
    if(drag)return;
    const container=document.getElementById('accounts')!;
    const search=query.trim().toLocaleLowerCase();
    const matches=accounts.filter(a=>[a.issuer,a.account,a.group].some(text=>text.toLocaleLowerCase().includes(search)));
    const known=[...new Set([...names,...accounts.map(account=>account.group)].filter(name=>name.length>0))];
    if(!accounts.length&&!known.length){empty('添加你的第一个账户','输入服务提供的密钥，账户仅在本机加密保存。',actions.add);return}
    if(!matches.length&&!known.some(name=>name.toLocaleLowerCase().includes(search))){empty('没有找到匹配的账户','试试其他服务、账户名称或分组。',actions.clearSearch,'清除搜索');return}
    const groups=new Map<string,AccountDto[]>();
    const totals=new Map<string,number>();
    for(const name of known){totals.set(name,0);if(!search||name.toLocaleLowerCase().includes(search)||matches.some(account=>account.group===name))groups.set(name,[]);}
    for(const account of accounts)totals.set(account.group,(totals.get(account.group)||0)+1);
    for(const account of matches){const name=account.group;groups.set(name,[...(groups.get(name)||[]),account])}
    if(groups.has('')){const ungrouped=groups.get('')!;groups.delete('');groups.set('',ungrouped);}
    const fragment=document.createDocumentFragment();
    for(const [name,items] of groups){
      const section=node('section','section');
      const toggle=node('button','group-toggle');const expanded=!!search||!collapsed.has(name);
      toggle.setAttribute('aria-expanded',String(expanded));
      const rows=node('div','rows');rows.hidden=!expanded;
      toggle.onclick=()=>{if(collapsed.has(name))collapsed.delete(name);else collapsed.add(name);if(!search)render(accounts,query,actions,names)};
      for(const account of items)rows.append(card(account,actions,totals));
      const header=folderHeader(name,totals.get(name)!,actions,toggle);
      section.append(header,rows);fragment.append(section);
    }
    container.replaceChildren(fragment);tick();
  }
  function folderHeader(group:string,count:number,actions:Actions,toggle=node('button','group-toggle')):HTMLElement {
    const header=node('div','group-head');header.dataset.group=group;
    header.dataset.folderTone=folderTone(group);
    const folder=Ui.icon('folder');folder.classList.add('folder-icon');
    const key=Ui.icon('key');key.classList.add('group-key');
    toggle.append(folder,node('span','group-name',group||'未分组'),node('span','group-count',String(count)),key);
    if(!toggle.hasAttribute('aria-expanded'))toggle.setAttribute('aria-expanded','false');
    header.append(toggle);
    if(group){const rename=node('button','group-rename','重命名');rename.dataset.group=group;rename.setAttribute('aria-label',`重命名分组 ${group}`);rename.onclick=()=>actions.renameGroup(group,count);header.append(rename);}
    if(group&&count===0){const remove=node('button','group-delete','删除');remove.dataset.group=group;remove.setAttribute('aria-label',`删除空分组 ${group}`);remove.onclick=()=>actions.deleteGroup(group);header.append(remove);}
    header.ondragover=event=>{
      if(!drag||!actions.canMove()||!event.dataTransfer?.types.includes(dragType))return;
      event.preventDefault();event.dataTransfer.dropEffect=drag.group===group?'none':'move';
      document.querySelectorAll('.drop-target').forEach(node=>node.classList.remove('drop-target'));
      if(drag.group!==group)header.classList.add('drop-target');
    };
    header.ondragleave=event=>{if(!(event.relatedTarget instanceof Node)||!header.contains(event.relatedTarget))header.classList.remove('drop-target');};
    header.ondrop=event=>{
      if(!drag||!actions.canMove()||event.dataTransfer?.getData(dragType)!==drag.token)return;
      event.preventDefault();const source=drag;cancelDrag();
      if(source.group!==group)void actions.move(source.id,group);
    };
    return header;
  }
  function card(account:AccountDto,actions:Actions,totals:Map<string,number>):HTMLElement {
    const card=node('article','account-card');card.dataset.id=account.id;card.dataset.color=account.color;card.dataset.expires=String(account.expiresAt);
    card.draggable=true;let dragAllowed=true;
    card.addEventListener('pointerdown',event=>{dragAllowed=!(event.target as Element).closest('button,input,select,a');});
    card.ondragstart=event=>{
      if(!dragAllowed||!actions.canMove()||!event.dataTransfer){event.preventDefault();return;}
      cancelDrag();closeMenus();drag={id:account.id,group:account.group,token:crypto.randomUUID()};
      event.dataTransfer.clearData();event.dataTransfer.effectAllowed='move';event.dataTransfer.setData(dragType,drag.token);
      const preview=node('div','drag-preview','移动账户');document.body.append(preview);event.dataTransfer.setDragImage(preview,18,18);
      card.classList.add('dragging');document.body.classList.add('dragging-account');
      const targets=new Map(totals);if(!targets.has(''))targets.set('',0);
      for(const [group,count] of targets){
        if(Array.from(document.querySelectorAll<HTMLElement>('.group-head')).some(header=>header.dataset.group===group))continue;
        const section=node('section','section drag-extra');section.append(folderHeader(group,count,actions));document.getElementById('accounts')!.append(section);
      }
    };
    card.ondragend=cancelDrag;
    const grip=node('span','drag-grip');grip.append(Ui.icon('grip'));grip.setAttribute('aria-hidden','true');grip.title='拖动到目标分组，也可在编辑账户中调整分组';
    const icon = serviceIcon(account.issuer, account.color);
    const detail=node('div','account-detail');const issuer=node('div','issuer',account.issuer);issuer.title=account.issuer;
    const name=node('div','account-name',account.account);name.title=account.account;
    const code=node('button','code',account.code.replace(/(\d{3})(\d{3})/,'$1 $2'));code.setAttribute('aria-label',`复制 ${account.issuer} 的验证码`);code.onclick=()=>actions.copy(account);
    detail.append(issuer,name,code);
    const controls=node('div','row-actions');const countdown=node('div','countdown');
    const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.setAttribute('viewBox','0 0 40 40');svg.setAttribute('aria-hidden','true');
    for(const cls of ['ring-track','ring-progress']){const circle=document.createElementNS('http://www.w3.org/2000/svg','circle');circle.setAttribute('cx','20');circle.setAttribute('cy','20');circle.setAttribute('r','16');circle.setAttribute('class',cls);svg.append(circle)}
    countdown.append(svg,node('span','seconds'));
    const copy=node('button','copy-button');copy.setAttribute('aria-label',`复制 ${account.issuer} 的验证码`);copy.append(Ui.icon('copy'),node('span','copy-label','复制'));copy.onclick=()=>actions.copy(account);
    const wrap=node('div','menu-wrap');const more=node('button','more');more.append(Ui.icon('more'));more.setAttribute('aria-label',`${account.issuer} 的更多操作`);more.setAttribute('aria-expanded','false');
    const menu=node('div','account-menu');menu.hidden=true;const edit=node('button','','编辑账户');edit.onclick=()=>{menu.hidden=true;actions.edit(account)};
    const remove=node('button','danger','删除账户');remove.onclick=()=>{menu.hidden=true;actions.remove(account)};menu.append(edit,remove);
    more.onclick=()=>{const opening=menu.hidden;closeMenus();menu.hidden=!opening;more.setAttribute('aria-expanded',String(opening));if(opening)edit.focus()};
    card.oncontextmenu=event=>{event.preventDefault();closeMenus();menu.hidden=false;more.setAttribute('aria-expanded','true');edit.focus()};
    wrap.append(more,menu);controls.append(countdown,copy,wrap);card.append(grip,icon,detail,controls);return card;
  }
  export function closeMenus():void {document.querySelectorAll<HTMLElement>('.account-menu').forEach(menu=>menu.hidden=true);document.querySelectorAll('.more').forEach(button=>button.setAttribute('aria-expanded','false'))}
  export function tick():boolean {
    let expired=false;
    document.querySelectorAll<HTMLElement>('.account-card').forEach(card=>{
      const remaining=Math.max(0,Math.min(30,Math.ceil((Number(card.dataset.expires)-Date.now())/1000)));
      card.querySelector('.seconds')!.textContent=String(remaining);
      card.querySelector('.countdown')!.classList.toggle('ending',remaining<=5);
      card.querySelector('.countdown')!.setAttribute('aria-label',`剩余 ${remaining} 秒`);
      const progress=card.querySelector('.ring-progress')!;progress.setAttribute('stroke-dasharray',`${remaining/30*100.53} 100.53`);
      if(!remaining){expired=true;const code=card.querySelector<HTMLButtonElement>('.code')!;code.textContent='暂不可用';code.disabled=true;card.querySelector<HTMLButtonElement>('.copy-button')!.disabled=true}
    });return expired;
  }
}
