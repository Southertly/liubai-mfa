const repository = 'Southertly/liubai-mfa';
const releaseChannels = {
  preview: {tagPrefix: 'windows-preview-v', packagePrefix: 'LiubaiMfa-Preview', extension: 'msix', versionParts: 4},
  stable: {tagPrefix: 'windows-v', packagePrefix: 'LiubaiMfa', extension: 'msi', versionParts: 3},
} as const;
type ReleaseChannel = keyof typeof releaseChannels;
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
    if (!Array.isArray(release.assets) || !release.assets.some(asset =>
      isRecord(asset) && typeof asset.name === 'string' && filenames.includes(asset.name) && asset.state === 'uploaded' &&
      typeof asset.size === 'number' && asset.size > 0
    )) continue;
    if (!latest || compareVersions(version, latest.version) > 0) {
      latest = {version, releaseUrl: `https://github.com/${repository}/releases/tag/${tag}`};
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
