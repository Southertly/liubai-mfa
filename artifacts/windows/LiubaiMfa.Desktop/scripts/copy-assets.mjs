import { copyFile, cp, mkdir, rm } from 'node:fs/promises';
await mkdir(new URL('../dist/renderer/',import.meta.url),{recursive:true});
for(const name of ['index.html','styles.css'])await copyFile(new URL('../src/renderer/'+name,import.meta.url),new URL('../dist/renderer/'+name,import.meta.url));
await cp(new URL('../src/renderer/icons/', import.meta.url), new URL('../dist/renderer/icons/', import.meta.url), {recursive: true});
await copyFile(new URL('../../LiubaiMfa.Windows/Assets/Square150x150Logo.png', import.meta.url), new URL('../dist/renderer/app-icon.png', import.meta.url));
// 清理撤下功能的旧编译产物，避免增量构建将残留脚本带入安装包。
for(const file of ['renderer/migration-dialog.js','main/migration-test.js'])await rm(new URL('../dist/'+file,import.meta.url),{force:true});
