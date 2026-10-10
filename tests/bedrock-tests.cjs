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
assert.equal(vm.runInContext("SpectraBedrock.editionName({format:'UWP'})",context),'Bedrock Edition');
assert.equal(vm.runInContext("SpectraBedrock.editionName({format:'GDK'})",context),'Bedrock Edition');
assert.equal(vm.runInContext("SpectraBedrock.latestPackage().id",context),'latest');
assert.equal(vm.runInContext("SpectraBedrock.visiblePackages([{id:'latest',latest:true,version:'Последняя версия',name:'Minecraft'},...items],'',true,true,true)[0].id",context),'latest');
console.log('PASS: Preview/Windows 10 labels and latest choice pinned above sorted releases');

assert.equal(vm.runInContext("SpectraBedrock.editionName({legacy:true,format:'UWP'})",context),'Windows 10 Edition');
(async()=>{
 const calls=[],dialogs=[];context.state={selection:{edition:'bedrock',bedrockId:'latest'}};context.api=async(action,data)=>{calls.push({action,data});if(action==='bedrockVersions')return {latestStatus:{installed:true,updateAvailable:true},installed:[],available:[]};if(action==='bedrockOfficialLauncher')return null;throw Error('Unexpected '+action);};context.modal=html=>dialogs.push(html);context.closeButton=()=>'';context.esc=String;
 await vm.runInContext('SpectraBedrock.load()',context);await vm.runInContext("SpectraBedrock.launch('latest')",context);
 assert.equal(calls.filter(x=>x.action==='bedrockOfficialLauncher').length,1);assert.equal(calls.filter(x=>x.action==='bedrockLaunch').length,0);assert(dialogs[0].includes('Скачайте обновление через Minecraft Launcher'));
 console.log('PASS: latest Bedrock Update opens official launcher and displays update guidance');
})().catch(error=>{console.error(error);process.exitCode=1;});
