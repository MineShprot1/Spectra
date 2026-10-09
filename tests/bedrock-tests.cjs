const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const context={};vm.createContext(context);vm.runInContext(fs.readFileSync(path.join(__dirname,'../src/Spectra/Web/bedrock.js'),'utf8'),context);
const items=[{name:'MinecraftUWP',version:'1.20.0'},{name:'MinecraftUWP',version:'1.9.0'},{name:'WindowsBeta',version:'1.30.0',preview:true},{name:'MinecraftUWP',version:'1.1.0',legacy:true}];context.items=items;
const versions=expression=>Array.from(vm.runInContext(expression,context),p=>p.version);
assert.deepEqual(versions('SpectraBedrock.visiblePackages(items)'),['1.20.0','1.9.0']);
assert.deepEqual(versions("SpectraBedrock.visiblePackages(items,'',true,true,true)"),['1.1.0','1.9.0','1.20.0','1.30.0']);
assert.deepEqual(versions("SpectraBedrock.visiblePackages(items,'BETA',true)"),['1.30.0']);
assert.equal(items[0].version,'1.20.0');
console.log('PASS: Bedrock hides preview and legacy by default, filters case-insensitively, sorts versions numerically without mutating catalogue');

assert.equal(vm.runInContext("SpectraBedrock.editionName({preview:true,legacy:true})",context),'Bedrock Preview');
assert.equal(vm.runInContext("SpectraBedrock.editionName({format:'UWP'})",context),'Windows 10 Edition');
assert.equal(vm.runInContext("SpectraBedrock.editionName({format:'GDK'})",context),'Bedrock Edition');
assert.equal(vm.runInContext("SpectraBedrock.latestPackage().id",context),'latest');
assert.equal(vm.runInContext("SpectraBedrock.visiblePackages([{id:'latest',latest:true,version:'Последняя версия',name:'Minecraft'},...items],'',true,true,true)[0].id",context),'latest');
console.log('PASS: Preview/Windows 10 labels and latest choice pinned above sorted releases');
