import { mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const allowedSeconds = [0,30,60,120,300,600];
function validateSeconds(value:unknown):asserts value is number {
  if(typeof value!=='number'||!allowedSeconds.includes(value))throw new Error('请选择有效的自动锁定时间。');
}

export function loadAutoLockSeconds(file:string):number {
  try {
    const info=statSync(file);
    if(!info.isFile()||info.size>1024)return 60;
    const value:unknown=JSON.parse(readFileSync(file,'utf8'));
    if(!value||typeof value!=='object'||Array.isArray(value)||Object.keys(value).length!==1)return 60;
    const seconds=(value as Record<string,unknown>).seconds;
    validateSeconds(seconds);return seconds;
  }catch{return 60;}
}

export function saveAutoLockSeconds(file:string,seconds:unknown):void {
  validateSeconds(seconds);
  const temporary=`${file}.${randomUUID()}.tmp`;
  try {
    mkdirSync(path.dirname(file),{recursive:true});
    writeFileSync(temporary,JSON.stringify({seconds}),{flag:'wx',mode:0o600,flush:true});
    renameSync(temporary,file);
  }catch{throw new Error('自动锁定时间保存失败，请重试。');}
  finally{rmSync(temporary,{force:true});}
}

export class AutoLockTimer {
  private timer:NodeJS.Timeout|undefined;
  private delay:number;
  constructor(private readonly onLock:()=>void,seconds=60) {validateSeconds(seconds);this.delay=seconds;}
  get seconds():number{return this.delay;}
  setSeconds(seconds:unknown):void {validateSeconds(seconds);this.cancel();this.delay=seconds;}
  cancel():void {clearTimeout(this.timer);this.timer=undefined;}
  schedule(state:{enabled:boolean;authenticating:boolean;focused:boolean}):void {
    if(!state.enabled||state.authenticating||state.focused||this.delay===0){this.cancel();return;}
    if(!this.timer)this.timer=setTimeout(()=>{this.timer=undefined;this.onLock();},this.delay*1000);
  }
}
