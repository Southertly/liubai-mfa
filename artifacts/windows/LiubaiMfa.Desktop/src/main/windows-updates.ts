const repository = 'Southertly/liubai-mfa';
const tagPrefix = 'windows-preview-v';
const buildVersion = /^(?:0|[1-9]\d{0,4})(?:\.(?:0|[1-9]\d{0,4})){2,3}$/;

type WindowsRelease = { version: string; releaseUrl: string };
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

export function selectWindowsRelease(releases: readonly unknown[]): WindowsRelease | undefined {
  let latest: WindowsRelease | undefined;
  for (const release of releases) {
    if (!isRecord(release) || release.draft !== false || typeof release.tag_name !== 'string') continue;
    const tag = release.tag_name;
    if (!tag.startsWith(tagPrefix)) continue;
    const version = tag.slice(tagPrefix.length);
    if (!isVersion(version) || version.split('.').length !== 4) continue;
    const filename = `LiubaiMfa-Preview-${version}-x64.msix`;
    if (!Array.isArray(release.assets) || !release.assets.some(asset =>
      isRecord(asset) && asset.name === filename && asset.state === 'uploaded' &&
      typeof asset.size === 'number' && asset.size > 0
    )) continue;
    if (!latest || compareVersions(version, latest.version) > 0) {
      latest = {version, releaseUrl: `https://github.com/${repository}/releases/tag/${tag}`};
    }
  }
  return latest;
}

export async function checkWindowsRelease(currentVersion: string, request = fetch): Promise<UpdateResult> {
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
    const candidate = selectWindowsRelease(releases);
    if (candidate && (!latest || compareVersions(candidate.version, latest.version) > 0)) latest = candidate;
    if (releases.length < 100) break;
  }
  if (!latest) return {status: 'unpublished'};
  if (compareVersions(latest.version, currentVersion) <= 0) return {status: 'current'};
  return {status: 'available', ...latest};
}
