import { BrowserWindow, desktopCapturer, dialog, screen } from 'electron';
import { mkdtemp, writeFile, unlink, rmdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { CoreBridge } from './core-bridge';

export async function readQr(source:'image'|'screen',window:BrowserWindow,bridge:CoreBridge,current:()=>boolean,choose:<T>(promise:Promise<T>)=>Promise<T>):Promise<Record<string,unknown>>{
  const begin=await bridge.request('qrBegin');
  if(!current())throw new Error('操作已失效，请重新开始。');
  if(typeof begin.token!=='string')throw new Error('无法开始二维码识别。');
  let temporary:string|undefined;let file:string|undefined;let decoded=false;
  try{
    if(source==='image'){
      const selected=await choose(dialog.showOpenDialog(window,{title:'选择账户二维码图片',properties:['openFile'],filters:[{name:'二维码图片',extensions:['png','jpg','jpeg','bmp']}]}));
      if(!current())throw new Error('操作已失效，请重新开始。');
      if(selected.canceled)return {cancelled:true};
      file=selected.filePaths[0];
    }else{
      const display=screen.getDisplayMatching(window.getBounds());
      const width=Math.ceil(display.size.width*display.scaleFactor),height=Math.ceil(display.size.height*display.scaleFactor);
      if(width>10000||height>10000||width*height>16000000)throw new Error('屏幕尺寸过大，请截取二维码后从图片导入。');
      window.hide();
      try{
        await new Promise(resolve=>setTimeout(resolve,250));
        if(!current())throw new Error('操作已失效，请重新开始。');
        let timer:NodeJS.Timeout|undefined;
        const sources=await Promise.race([desktopCapturer.getSources({types:['screen'],thumbnailSize:{width,height},fetchWindowIcons:false}),new Promise<never>((_resolve,reject)=>{timer=setTimeout(()=>reject(new Error('屏幕扫描超时，请重试。')),5000);})]).finally(()=>clearTimeout(timer));
        if(!current())throw new Error('操作已失效，请重新开始。');
        const captured=sources.find(item=>item.display_id===String(display.id));
        if(!captured||captured.thumbnail.isEmpty())throw new Error('无法取得屏幕图像，请从图片导入。');
        const size=captured.thumbnail.getSize();
        if(size.width*size.height>16000000)throw new Error('屏幕图像过大，请从图片导入。');
        const bytes=captured.thumbnail.toPNG();
        try{
          if(bytes.length>25*1024*1024)throw new Error('屏幕图像过大，请从图片导入。');
          temporary=await mkdtemp(path.join(tmpdir(),'mfa-qr-capture-'));file=path.join(temporary,'screen.png');
          await writeFile(file,bytes,{flag:'wx'});
        }finally{bytes.fill(0);}
      }finally{if(!window.isDestroyed()){window.show();window.focus();}}
    }
    if(!file||!current())throw new Error('操作已失效，请重新开始。');
    const result=await bridge.request('qrDecode',{token:begin.token,path:file});
    if(!current())throw new Error('操作已失效，请重新开始。');
    decoded=true;return result;
  }finally{
    try{
      if(temporary){try{if(file)await unlink(file);}finally{await rmdir(temporary);}}
    }catch{
      decoded=false;throw new Error('临时扫描图像无法清理，本次导入已取消。');
    }finally{
      if(!decoded&&current()&&bridge.connected)await bridge.request('qrCancel').catch(()=>{});
    }
  }
}
