const assert = require('node:assert/strict');
const { createHash } = require('node:crypto');
const { mkdtemp, readdir, readFile, rm, writeFile } = require('node:fs/promises');
const { tmpdir } = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const { selectWindowsRelease, downloadWindowsUpdate, verifyWindowsUpdate } = require('../dist/main/windows-updates.js');

const content = Buffer.from('测试安装包');
const hash = createHash('sha256').update(content).digest('hex');
const release = {
  tag_name: 'windows-v1.0.2', draft: false, prerelease: false, assets: [
    {name: 'LiubaiMfa-1.0.2-x64-unsigned.msi', state: 'uploaded', size: content.length, digest: 'sha256:' + hash},
  ],
};
const asset = () => selectWindowsRelease([release], 'stable').download;
const cdn = 'https://release-assets.githubusercontent.com/test-installer';

test('仅对带有效摘要的正式安装包开放应用内下载', () => {
  assert.equal(asset().sha256, hash);
  assert.equal(asset().unsigned, true);
  assert.equal(asset().url, 'https://github.com/Southertly/liubai-mfa/releases/download/windows-v1.0.2/LiubaiMfa-1.0.2-x64-unsigned.msi');
  const noDigest = {...release, assets: [{...release.assets[0], digest: null}]};
  assert.equal(selectWindowsRelease([noDigest], 'stable').download, undefined);
  assert.equal(selectWindowsRelease([{...release, draft: true}], 'stable'), undefined);
  assert.equal(selectWindowsRelease([{...release, prerelease: true}], 'stable'), undefined);
});

test('下载到隔离目录并校验长度与摘要，安装前能发现文件被改动', async t => {
  const folder = await mkdtemp(path.join(tmpdir(), 'liubai-update-test-'));
  t.after(() => rm(folder, {recursive: true, force: true}));
  const responses = [Response.redirect(cdn), new Response(content)];
  const requests = [];
  const progress = [];
  const installer = await downloadWindowsUpdate(asset(), folder, done => progress.push(done), async (url, options) => {
    requests.push([url, options.redirect]);
    return responses.shift();
  });
  assert.deepEqual(requests, [[asset().url, 'manual'], [cdn, 'manual']]);
  assert.deepEqual(await readFile(installer), content);
  assert.equal(progress.at(-1), content.length);
  assert.equal(await verifyWindowsUpdate(installer, asset()), true);
  await writeFile(installer, Buffer.alloc(content.length));
  assert.equal(await verifyWindowsUpdate(installer, asset()), false);
});

test('越界跳转、超长/截断下载和摘要错误均不保留安装包', async t => {
  const folder = await mkdtemp(path.join(tmpdir(), 'liubai-update-test-'));
  t.after(() => rm(folder, {recursive: true, force: true}));
  for (const respond of [
    () => Response.redirect('https://example.com/installer.msi'),
    () => new Response(Buffer.alloc(content.length + 1)),
    () => new Response(Buffer.alloc(content.length - 1)),
    () => new Response(Buffer.alloc(content.length)),
  ]) {
    await assert.rejects(downloadWindowsUpdate(asset(), folder, () => {}, async () => respond()));
    assert.deepEqual(await readdir(folder), []);
  }
});

test('取消下载后清除临时文件，并可重新下载', async t => {
  const folder = await mkdtemp(path.join(tmpdir(), 'liubai-update-test-'));
  t.after(() => rm(folder, {recursive: true, force: true}));
  const cancel = new AbortController();
  const pending = downloadWindowsUpdate(asset(), folder, () => {}, async (_url, {signal}) => {
    return new Response(new ReadableStream({
      start(controller) {
        signal.addEventListener('abort', () => controller.error(signal.reason), {once: true});
        cancel.abort();
      },
    }));
  }, cancel.signal);
  await assert.rejects(pending);
  assert.deepEqual(await readdir(folder), []);
  const installer = await downloadWindowsUpdate(asset(), folder, () => {}, async () => new Response(content));
  assert.equal(await verifyWindowsUpdate(installer, asset()), true);
});
