import { readFile, writeFile } from 'node:fs/promises';
import { Data, NtExecutable, NtExecutableResource, Resource } from 'resedit';

const [path, shortcutIconPath] = process.argv.slice(2);
const executable = NtExecutable.from(await readFile(path));
const resources = NtExecutableResource.from(executable);
const groups = Resource.IconGroupEntry.fromEntries(resources.entries);
if (groups.length !== 1) {
  throw new Error('无法定位 Electron 的应用图标资源。');
}

const icon = Data.IconFile.from(await readFile(new URL('./LiubaiMfa.ico', import.meta.url)));
const frames = icon.icons.map(({ data }) => data);
const [{ id, lang }] = groups;
Resource.IconGroupEntry.replaceIconsForResource(
  resources.entries, id, lang, frames
);
resources.outputResource(executable);
await writeFile(path, Buffer.from(executable.generate()));

if (shortcutIconPath) {
  // MSI 为每个产品版本缓存一份快捷方式图标，避免复用原 EXE 路径的旧缓存。
  const shortcutIcon = NtExecutable.createEmpty(false, false);
  const shortcutResources = NtExecutableResource.from(shortcutIcon);
  Resource.IconGroupEntry.replaceIconsForResource(shortcutResources.entries, 1, lang, frames);
  shortcutResources.outputResource(shortcutIcon);
  await writeFile(shortcutIconPath, Buffer.from(shortcutIcon.generate()));
}
