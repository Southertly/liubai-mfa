type UpdateViewResult = { status: 'unpublished' | 'current' } | { status: 'available'; version: string; canDownload?: boolean };
declare const mfaUpdates: {
  version(): Promise<string>;
  channel(): Promise<'stable' | 'preview'>;
  check(): Promise<UpdateViewResult | false>;
  open(): Promise<boolean>;
  download(): Promise<boolean>;
  cancel(): Promise<boolean>;
  install(): Promise<boolean | 'download-required'>;
  onProgress(callback: (percent: number) => void): void;
};

namespace UpdateView {
  const version = document.getElementById('appVersion')!;
  const status = document.getElementById('updateStatus')!;
  const checkButton = document.getElementById('checkUpdate') as HTMLButtonElement;
  const openButton = document.getElementById('openUpdate') as HTMLButtonElement;
  const downloadButton = document.getElementById('downloadUpdate') as HTMLButtonElement;

  export let check = (): Promise<UpdateViewResult | false> => mfaUpdates.check();
  export let open = (): Promise<boolean> => mfaUpdates.open();

  let stable = false;
  let downloaded = false;
  let downloading = false;
  void Promise.all([mfaUpdates.version(), mfaUpdates.channel()]).then(([value, channel]) => {
    stable = channel === 'stable';
    document.title = stable ? '留白 MFA' : '留白 MFA · 预览版';
    version.textContent = `${stable ? '正式版' : '预览版'} ${value}`;
  }).catch(() => {});

  mfaUpdates.onProgress(percent => {
    if (downloading && !downloadButton.disabled) status.textContent = `正在下载更新…${percent}%`;
  });

  checkButton.onclick = async () => {
    checkButton.disabled = true;
    openButton.hidden = true;
    downloadButton.hidden = true;
    downloaded = false;
    downloadButton.textContent = '下载更新';
    status.textContent = '正在检查更新…';
    try {
      const result = await check();
      if (!result) throw new Error();
      if (result.status === 'available') {
        status.textContent = `发现新版 ${result.version}`;
        openButton.hidden = false;
        downloadButton.hidden = !result.canDownload;
      } else {
        status.textContent = result.status === 'current'
          ? '当前版本无需更新。'
          : `还没有发布 Windows ${stable ? '正式版' : '预览版'}。`;
      }
    } catch {
      status.textContent = '暂时无法检查更新，请稍后重试。';
    } finally {
      checkButton.disabled = false;
    }
  };

  downloadButton.onclick = async () => {
    if (downloading) {
      downloadButton.disabled = true;
      status.textContent = '正在取消下载…';
      await mfaUpdates.cancel().catch(() => false);
      return;
    }
    checkButton.disabled = openButton.disabled = downloadButton.disabled = true;
    try {
      if (downloaded) {
        status.textContent = '正在打开安装确认…';
        const result = await mfaUpdates.install();
        if (result === 'download-required') {
          status.textContent = '安装包已失效，请重新下载。';
          downloaded = false;
          downloadButton.textContent = '下载更新';
        } else if (!result) {
          status.textContent = '安装尚未启动。可再次安装，或打开发布页。';
        }
      } else {
        downloading = true;
        downloadButton.disabled = false;
        downloadButton.textContent = '取消下载';
        status.textContent = '正在下载更新…';
        if (!await mfaUpdates.download()) {
          status.textContent = '已停止下载。';
          return;
        }
        downloaded = true;
        downloadButton.textContent = '安装更新';
        status.textContent = '下载完成，安装包已校验。';
      }
    } catch {
      status.textContent = downloaded
        ? '暂时无法安装更新，请重试或打开发布页。'
        : '下载或校验失败，请重试或打开发布页。';
    } finally {
      downloading = false;
      if (!downloaded) downloadButton.textContent = '下载更新';
      checkButton.disabled = openButton.disabled = downloadButton.disabled = false;
    }
  };

  openButton.onclick = async () => {
    openButton.disabled = true;
    try {
      if (!await open()) throw new Error('新版地址不可用。');
    } catch {
      status.textContent = '无法打开发布页，请重新检查更新。';
      openButton.hidden = true;
      checkButton.focus();
    } finally {
      openButton.disabled = false;
    }
  };
}
