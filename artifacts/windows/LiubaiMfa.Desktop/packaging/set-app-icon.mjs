import { readFile, writeFile } from 'node:fs/promises';
import { Data, NtExecutable, NtExecutableResource, Resource } from 'resedit';

const [path] = process.argv.slice(2);
const executable = NtExecutable.from(await readFile(path));
const resources = NtExecutableResource.from(executable);
const groups = Resource.IconGroupEntry.fromEntries(resources.entries);
if (groups.length !== 1) {
  throw new Error('无法定位 Electron 的应用图标资源。');
}

const icon = Data.IconFile.from(await readFile(new URL('./LiubaiMfa.ico', import.meta.url)));
const [{ id, lang }] = groups;
Resource.IconGroupEntry.replaceIconsForResource(
  resources.entries, id, lang, icon.icons.map(({ data }) => data)
);
resources.outputResource(executable);
await writeFile(path, Buffer.from(executable.generate()));
