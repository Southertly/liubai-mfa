import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { mkdtemp, open, rm, rmdir } from 'node:fs/promises';
import path from 'node:path';

const repository = 'Southertly/liubai-mfa';
const installerLimit = 512 * 1024 * 1024;
const releaseChannels = {
  preview: {tagPrefix: 'windows-preview-v', packagePrefix: 'LiubaiMfa-Preview', extension: 'msix', versionParts: 4},
  stable: {tagPrefix: 'windows-v', packagePrefix: 'LiubaiMfa', extension: 'msi', versionParts: 3},
} as const;
type ReleaseChannel = keyof typeof releaseChannels;
const buildVersion = /^(?:0|[1-9]\d{0,4})(?:\.(?:0|[1-9]\d{0,4})){2,3}$/;

export type WindowsUpdateDownload = {
  name: string;
  url: string;
  size: number;
  sha256: string;
  unsigned: boolean;
};
type WindowsRelease = { version: string; releaseUrl: string; download?: WindowsUpdateDownload };
export type UpdateResult =
  | { status: 'unpublished' | 'current' }
  | ({ status: 'available' } & WindowsRelease);

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isVersion(version: string): boolean {
  return buildVersion.test(version) && version.split('.').every(part => Number(part) <= 65535);
}

function compareVersions(first: string, second: string): number {
  const firstParts = first.split('.').map(Number);
  const secondParts = second.split('.').map(Number);
  for (let index = 0; index < 4; index++) {
    const difference = (firstParts[index] ?? 0) - (secondParts[index] ?? 0);
    if (difference !== 0) return difference;
  }
  return 0;
}

export function selectWindowsRelease(releases: readonly unknown[], channel: ReleaseChannel = 'preview'): WindowsRelease | undefined {
  const {tagPrefix, packagePrefix, extension, versionParts} = releaseChannels[channel];
  let latest: WindowsRelease | undefined;
  for (const release of releases) {
    if (!isRecord(release) || release.draft !== false || typeof release.tag_name !== 'string') continue;
    if (channel === 'stable' && release.prerelease !== false) continue;
    const tag = release.tag_name;
    if (!tag.startsWith(tagPrefix)) continue;
    const version = tag.slice(tagPrefix.length);
    if (!isVersion(version) || version.split('.').length !== versionParts) continue;
    const filename = `${packagePrefix}-${version}-x64`;
    const filenames = [`${filename}.${extension}`];
    if (channel === 'stable') filenames.push(`${filename}-unsigned.${extension}`);
    if (!Array.isArray(release.assets)) continue;
    const assets = release.assets.filter((asset): asset is Record<string, unknown> & {name: string; size: number} =>
      isRecord(asset) && typeof asset.name === 'string' && filenames.includes(asset.name) && asset.state === 'uploaded' &&
      typeof asset.size === 'number' && asset.size > 0
    );
    if (!assets.length) continue;
    if (!latest || compareVersions(version, latest.version) > 0) {
      latest = {version, releaseUrl: `https://github.com/${repository}/releases/tag/${tag}`};
      if (channel !== 'stable') continue;
      for (const name of filenames) {
        const asset = assets.find(asset => asset.name === name);
        if (!asset || !Number.isSafeInteger(asset.size) || asset.size > installerLimit ||
            typeof asset.digest !== 'string' || !/^sha256:[a-f0-9]{64}$/i.test(asset.digest)) continue;
        latest.download = {
          name, size: asset.size, sha256: asset.digest.slice(7).toLowerCase(),
          unsigned: name.endsWith('-unsigned.msi'),
          url: `https://github.com/${repository}/releases/download/${tag}/${name}`,
        };
        break;
      }
    }
  }
  return latest;
}

export async function checkWindowsRelease(currentVersion: string, request = fetch, channel: ReleaseChannel = 'preview'): Promise<UpdateResult> {
  if (!isVersion(currentVersion)) throw new Error('无法识别当前版本。');
  const signal = AbortSignal.timeout(8000);
  let latest: WindowsRelease | undefined;
  for (let page = 1; ; page++) {
    const response = await request(`https://api.github.com/repos/${repository}/releases?per_page=100&page=${page}`, {
      headers: {'Accept': 'application/vnd.github+json', 'User-Agent': 'Liubai-MFA-Windows'},
      credentials: 'omit',
      redirect: 'error',
      signal,
    });
    if (!response.ok) throw new Error('无法取得发布列表。');
    const releases: unknown = await response.json();
    if (!Array.isArray(releases)) throw new Error('发布列表格式无效。');
    const candidate = selectWindowsRelease(releases, channel);
    if (candidate && (!latest || compareVersions(candidate.version, latest.version) > 0)) latest = candidate;
    if (releases.length < 100) break;
  }
  if (!latest) return {status: 'unpublished'};
  if (compareVersions(latest.version, currentVersion) <= 0) return {status: 'current'};
  return {status: 'available', ...latest};
}

async function installerResponse(url: string, request: typeof fetch, signal: AbortSignal): Promise<Response> {
  for (let hop = 0; hop < 4; hop++) {
    const response = await request(url, {credentials: 'omit', redirect: 'manual', signal});
    if (![301, 302, 303, 307, 308].includes(response.status)) return response;
    const location = response.headers.get('location');
    await response.body?.cancel();
    if (!location) break;
    const next = new URL(location, url);
    if (next.protocol !== 'https:' || next.port || next.username || next.password ||
        !['release-assets.githubusercontent.com', 'objects.githubusercontent.com'].includes(next.hostname)) break;
    url = next.href;
  }
  throw new Error('安装包下载地址无效。');
}

export async function discardWindowsUpdate(file: string): Promise<void> {
  await rm(file, {force: true}).catch(() => {});
  await rmdir(path.dirname(file)).catch(() => {});
}

export async function downloadWindowsUpdate(
  asset: WindowsUpdateDownload, temp: string, progress: (bytes: number) => void,
  request = fetch, cancel?: AbortSignal
): Promise<string> {
  const folder = await mkdtemp(path.join(temp, 'liubai-mfa-update-'));
  const file = path.join(folder, asset.name);
  const idle = new AbortController();
  const timeout = setTimeout(() => idle.abort(new Error('安装包下载超时。')), 60_000);
  timeout.unref();
  const signal = cancel ? AbortSignal.any([cancel, idle.signal]) : idle.signal;
  try {
    const response = await installerResponse(asset.url, request, signal);
    if (!response.ok || !response.body) {
      await response.body?.cancel();
      throw new Error('无法下载安装包。');
    }
    const reader = response.body.getReader();
    try {
      const output = await open(file, 'wx', 0o600);
      try {
        const hash = createHash('sha256');
        let bytes = 0;
        for (;;) {
          const {done, value} = await reader.read();
          if (done) break;
          timeout.refresh();
          bytes += value.length;
          if (bytes > asset.size) throw new Error('安装包大小与发布记录不符。');
          await output.writeFile(value);
          hash.update(value);
          progress(bytes);
        }
        if (bytes !== asset.size || hash.digest('hex') !== asset.sha256) {
          throw new Error('安装包校验失败。');
        }
      } finally { await output.close(); }
    } finally { await reader.cancel().catch(() => {}); }
    signal.throwIfAborted();
    return file;
  } catch (error) {
    await discardWindowsUpdate(file);
    throw error;
  } finally { clearTimeout(timeout); }
}

export async function verifyWindowsUpdate(file: string, asset: WindowsUpdateDownload): Promise<boolean> {
  const hash = createHash('sha256');
  let bytes = 0;
  for await (const chunk of createReadStream(file)) {
    bytes += chunk.length;
    if (bytes > asset.size) return false;
    hash.update(chunk);
  }
  return bytes === asset.size && hash.digest('hex') === asset.sha256;
}
