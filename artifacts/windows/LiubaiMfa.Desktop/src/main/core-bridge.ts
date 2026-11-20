import { spawn, ChildProcessWithoutNullStreams } from 'node:child_process';
import { EventEmitter } from 'node:events';
type Pending = { resolve: (value: Record<string, unknown>) => void; reject: (error: Error) => void; timer: NodeJS.Timeout };
export class CoreBridge extends EventEmitter {
  private child?: ChildProcessWithoutNullStreams;
  private pending = new Map<string, Pending>();
  private buffer = Buffer.alloc(0);
  private sequence = 0;
  private shutdown: Promise<void> = Promise.resolve();
  connected = false;
  get processId(): number | undefined { return this.child?.pid; }
  constructor(private command: string, private args: string[], private timeout = 5000) { super(); }
  async start(): Promise<void> {
    this.stop();
    await this.shutdown;
    const child = spawn(this.command, this.args, { stdio: ['pipe','pipe','pipe'], windowsHide: true, shell: false });
    this.child = child;
    child.stderr.resume();
    child.on('error', () => { if(this.child === child)this.fail(new Error('无法启动本地核心。')); });
    child.on('exit', () => { if(this.child === child)this.fail(new Error('本地核心已断开。')); });
    child.stdin.on('error', () => { if(this.child === child)this.fail(new Error('本地核心连接中断。')); });
    child.stdout.on('data', (chunk: Buffer) => { if(this.child === child)this.receive(chunk); });
    try {
      const status = await this.requestInternal('status');
      if(status.service !== 'LiubaiMfa.CoreHost' || status.protocolVersion !== 1 || typeof status.accountAccess !== 'boolean') throw new Error('本地核心版本不匹配。');
      this.connected = true;
    } catch(error) { this.stop(); throw error; }
  }
  request(method: 'status'|'ping'|'handshake'|'unlock'|'lock'|'setProtection'|'qrBegin'|'qrDecode'|'qrCommit'|'qrCancel'|'exportPrepare'|'exportSave'|'importPreview'|'importCommit'|'backupCancel'|'accounts'|'addAccount'|'editAccount'|'createGroup'|'deleteGroup'|'renameGroup'|'moveAccount'|'deleteAccount'|'copyCode', payload: Record<string,unknown> = {}): Promise<Record<string,unknown>> {
    if(!this.connected)return Promise.reject(new Error('本地核心尚未连接。'));
    return this.requestInternal(method, payload);
  }
  private requestInternal(method: string, payload: Record<string,unknown> = {}): Promise<Record<string,unknown>> {
    const child=this.child;
    if(!child)return Promise.reject(new Error('本地核心尚未连接。'));
    if(this.pending.size >= 16)return Promise.reject(new Error('请求过于频繁，请稍后重试。'));
    const id=String(++this.sequence);
    return new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>this.fail(new Error('本地核心响应超时。')),['unlock','setProtection','exportPrepare','qrDecode'].includes(method)?120000:this.timeout);
      this.pending.set(id,{resolve,reject,timer});
      child.stdin.write(JSON.stringify({version:1,id,method,payload})+'\n');
    });
  }
  private receive(chunk: Buffer): void {
    this.buffer=Buffer.concat([this.buffer,chunk]);
    while(true){
      const newline=this.buffer.indexOf(10);
      if(newline < 0){if(this.buffer.length > 1024*1024)this.fail(new Error('本地核心返回无效数据。'));return;}
      if(newline > 1024*1024){this.fail(new Error('本地核心返回无效数据。'));return;}
      const line=this.buffer.subarray(0,newline);this.buffer=this.buffer.subarray(newline+1);
      try{
        const message=JSON.parse(line.toString('utf8'));
        if(message.version!==1 || typeof message.id!=='string' || typeof message.ok!=='boolean')throw new Error();
        const item=this.pending.get(message.id);if(!item)throw new Error();
        if(message.ok && (!message.result || typeof message.result!=='object' || Array.isArray(message.result)))throw new Error();
        clearTimeout(item.timer);this.pending.delete(message.id);
        if(message.ok)item.resolve(message.result);else {
          const messages:Record<string,string>={QR_BUSY:'上一张图片仍在识别，请稍后重试。',QR_IMAGE_INVALID:'图片格式或尺寸无效，请使用 PNG、JPEG 或 BMP 图片。',QR_INVALID:'二维码内容无效，无法导入。',QR_UNSUPPORTED:'仅支持 SHA-1、6 位、30 秒的 TOTP 账户。',QR_NOT_FOUND:'未找到二维码，请选择清晰图片或放大屏幕上的二维码。',QR_MULTIPLE:'发现多个二维码，请使用只包含一个二维码的图片。',QR_TOO_LARGE:'二维码图片过大，请缩小后重试。',QR_EXPIRED:'本次识别已失效，请重新开始。',BACKUP_TOO_LARGE:'合并后的账户数量或内容超过限制。',BACKUP_CHANGED:'账户已发生变化，请重新预览备份。',BACKUP_INVALID:'备份或恢复密钥无效，请检查后重试。',BACKUP_EXPIRED:'本次操作已过期，请重新开始。',BACKUP_WRITE_FAILED:'备份未保存，请选择新的文件名重试。',STORE_UNAVAILABLE:'无法读取或写入本地账户，已阻止更改。请检查文件后重新连接。',LOCKED:'应用已锁定，请先验证身份。',AUTH_CANCELLED:'验证已取消，请重试。',AUTH_FAILED:'未能验证身份，请重试。',AUTH_UNAVAILABLE:'Windows 身份验证暂不可用。',DUPLICATE_ACCOUNT:'此密钥已存在，请勿重复添加。',INVALID_SECRET:'请输入有效的 Base32 密钥。',INVALID_REQUEST:'输入内容无效，请检查后重试。'};
          messages.GROUP_EXISTS='已有同名分组，请使用其他名称。';
          messages.GROUP_NOT_FOUND='此分组已不存在，请刷新后重试。';
          messages.GROUP_LIMIT='分组数量已达上限。';
          messages.GROUP_NOT_EMPTY='此分组还有账户，请先移出全部账户。';
          if(message.error?.code==='LOCKED'||message.error?.code==='STORE_UNAVAILABLE')this.emit('locked');
          item.reject(new Error(messages[message.error?.code]||'本地核心拒绝了请求，请重试。'));
        }
      }catch{this.fail(new Error('本地核心返回无效数据。'));return;}
    }
  }
  private fail(error: Error): void {

    this.connected=false;
    const child=this.child;this.child=undefined;
    this.buffer=Buffer.alloc(0);
    for(const item of this.pending.values()){clearTimeout(item.timer);item.reject(error)}
    this.pending.clear();
    if (child && child.exitCode === null && child.signalCode === null) {
      this.shutdown = new Promise(resolve => {
        const timer = setTimeout(() => { child.kill(); resolve(); }, 1000);
        timer.unref();
        child.once('exit', () => { clearTimeout(timer); resolve(); });
        child.once('error', () => { clearTimeout(timer); resolve(); });
        child.stdin.end();
      });
    }
    this.emit('disconnected');
  }
  stop(): void { this.fail(new Error('本地核心连接已关闭。')); }
  async close(): Promise<void> { this.stop(); await this.shutdown; }
}
