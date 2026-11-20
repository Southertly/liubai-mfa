import { execFile, spawn, type ChildProcess } from 'node:child_process';
import { win32 as path } from 'node:path';
import { promisify } from 'node:util';

const findInstalledScope = `
$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$products = $installer.RelatedProducts('{D6777106-A66C-4DE7-B65E-5E1CD3364A4C}')
for ($index = 0; $index -lt $products.Count; $index++) {
  $product = $products.Item($index)
  if ($installer.ProductState($product) -ne 5) { continue }
  $executable = $installer.ComponentPath($product, '{0E8DFFC3-9D26-4B16-9E8F-71C602E40A85}')
  if ($executable -eq $env:LIUBAI_MFA_UPDATE_EXE) {
    $installer.ProductInfo($product, 'AssignmentType')
  }
}
`;

function quotePath(value: string): string {
  if (!path.isAbsolute(value) || /["\u0000-\u001f]/.test(value)) throw new Error('安装程序路径无效。');
  return `"${value}"`;
}

export function windowsInstallerArguments(file: string, executable: string, scope: '0' | '1'): string[] {
  // Windows Installer 要求 INSTALLFOLDER="目录"。通用 Windows 参数转义会将整个属性也放入引号。
  return ['/i', quotePath(file), '/qf', '/norestart', `INSTALLFOLDER=${quotePath(path.dirname(executable))}`,
    ...(scope === '1' ? ['ALLUSERS=1'] : ['ALLUSERS=2', 'MSIINSTALLPERUSER=1']),
    '/l*v', quotePath(path.join(path.dirname(file), 'install.log'))];
}

export function waitForInstallerStartup(installer: ChildProcess, delay = 2000): Promise<number | null> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      installer.unref();
      resolve(null);
    }, delay);
    installer.once('error', error => {
      clearTimeout(timer);
      reject(error);
    });
    installer.once('exit', code => {
      clearTimeout(timer);
      if (code === null) reject(new Error('Windows 安装程序已退出。'));
      else resolve(code === 0 ? null : code);
    });
  });
}

export async function prepareWindowsInstaller(file: string, executable: string): Promise<() => Promise<number | null>> {
  const root = process.env.SystemRoot;
  if (!root || !path.isAbsolute(root)) throw new Error('无法定位 Windows 安装程序。');
  const system = path.join(root, 'System32');
  const {stdout} = await promisify(execFile)(
    path.join(system, 'WindowsPowerShell', 'v1.0', 'powershell.exe'),
    ['-NoProfile', '-NonInteractive', '-Command', findInstalledScope],
    {windowsHide: true, timeout: 8000, env: {...process.env, LIUBAI_MFA_UPDATE_EXE: executable}}
  );
  const scope = stdout.trim();
  if (scope !== '0' && scope !== '1') throw new Error('无法确认原安装范围，请从发布页手动升级。');
  const args = windowsInstallerArguments(file, executable, scope);
  return () => waitForInstallerStartup(spawn(path.join(system, 'msiexec.exe'), args, {
    detached: true, stdio: 'ignore', windowsVerbatimArguments: true,
  }));
}
