type UpdateViewResult = { status: 'unpublished' | 'current' } | { status: 'available'; version: string };
declare const mfaUpdates: {
  version(): Promise<string>;
  channel(): Promise<'stable' | 'preview'>;
  check(): Promise<UpdateViewResult>;
  open(): Promise<boolean>;
};

namespace UpdateView {
  const version = document.getElementById('appVersion')!;
  const status = document.getElementById('updateStatus')!;
  const checkButton = document.getElementById('checkUpdate') as HTMLButtonElement;
  const openButton = document.getElementById('openUpdate') as HTMLButtonElement;

  export let check = (): Promise<UpdateViewResult> => mfaUpdates.check();
  export let open = (): Promise<boolean> => mfaUpdates.open();

  let stable = false;
  void Promise.all([mfaUpdates.version(), mfaUpdates.channel()]).then(([value, channel]) => {
    stable = channel === 'stable';
    document.title = stable ? '留白 MFA' : '留白 MFA · 预览版';
    version.textContent = `${stable ? '正式版' : '预览版'} ${value}`;
  }).catch(() => {});

  checkButton.onclick = async () => {
    checkButton.disabled = true;
    openButton.hidden = true;
    status.textContent = '正在检查更新…';
    try {
      const result = await check();
      if (result.status === 'available') {
        status.textContent = `发现新版 ${result.version}`;
        openButton.hidden = false;
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
