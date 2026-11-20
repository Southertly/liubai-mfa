const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { test } = require('node:test');
const { windowsInstallerArguments, waitForInstallerStartup } = require('../dist/main/windows-installer.js');

test('MSI 参数保留带空格和中文的路径，安装属性仅给值加引号', () => {
  const file = String.raw`C:\Users\Test User\AppData\Local\Temp\留白 更新\setup.msi`;
  const executable = String.raw`D:\软件\留白 MFA\LiubaiMfa.exe`;
  assert.deepEqual(windowsInstallerArguments(file, executable, '0'), [
    '/i', `"${file}"`, '/qf', '/norestart',
    'INSTALLFOLDER="D:\\软件\\留白 MFA"', 'ALLUSERS=2', 'MSIINSTALLPERUSER=1',
    '/l*v', '"C:\\Users\\Test User\\AppData\\Local\\Temp\\留白 更新\\install.log"',
  ]);
  const machine = windowsInstallerArguments(file, executable, '1');
  assert.ok(machine.includes('ALLUSERS=1'));
  assert.ok(!machine.includes('MSIINSTALLPERUSER=1'));
  assert.throws(() => windowsInstallerArguments('bad.msi', executable, '0'));
  assert.throws(() => windowsInstallerArguments('C:\\bad".msi', executable, '0'));
});

test('Windows 安装程序刚启动就失败时，返回实际错误码', async () => {
  const child = new EventEmitter();
  child.unref = () => { assert.fail('已退出的安装程序不需要分离'); };
  const result = waitForInstallerStartup(child);
  child.emit('exit', 1639);
  assert.equal(await result, 1639);
});

test('Windows 安装窗口继续运行时，允许应用退出并释放程序文件', async () => {
  const child = new EventEmitter();
  let detached = false;
  child.unref = () => { detached = true; };
  assert.equal(await waitForInstallerStartup(child, 1), null);
  assert.equal(detached, true);
});
