const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const context={busy:new Set()};vm.createContext(context);vm.runInContext(fs.readFileSync(path.join(__dirname,'../src/Spectra/Web/bedrock.js'),'utf8'),context);
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

assert.equal(vm.runInContext("SpectraBedrock.matchesInstalled({name:'Microsoft.MinecraftUWP',format:'UWP',version:'1.21.50',packageVersion:'1.21.5000.0'},{name:'microsoft.minecraftuwp',version:'1.21.5000.0'})",context),true);
assert.equal(vm.runInContext("SpectraBedrock.matchesInstalled({name:'Microsoft.MinecraftUWP',format:'UWP',version:'1.21.50',packageVersion:'1.21.5000.0'},{name:'Microsoft.MinecraftUWP',version:'1.21.50'})",context),true);
assert.equal(vm.runInContext("SpectraBedrock.matchesInstalled({name:'Microsoft.MinecraftUWP',format:'UWP',version:'0.14.3',packageVersion:'0.143.0.0'},{name:'Microsoft.MinecraftUWP',version:'0.1403.0.0'})",context),true);
assert.equal(vm.runInContext("SpectraBedrock.matchesInstalled({name:'Microsoft.MinecraftUWP',format:'UWP',version:'1.21.50',packageVersion:'1.21.5000.0'},{name:'Microsoft.MinecraftWindowsBeta',version:'1.21.5000.0'})",context),false);
assert.equal(vm.runInContext("SpectraBedrock.matchesInstalled({name:'Microsoft.MinecraftUWP',format:'UWP',version:'1.21.50',packageVersion:'1.21.5000.0'},{name:'Microsoft.MinecraftUWP',version:'1.21.5100.0'})",context),false);
(async()=>{
 const calls=[],dialogs=[];const ctx={state:{selection:{}},busy:new Set(),syncPlayButtons(){},settleGameStatus(){},toast(){},esc:String,closeButton:()=>'',page:'instances',run:fn=>fn(),modal:html=>dialogs.push(html),$:()=>({close(){}}),api:async(action,data)=>{calls.push({action,data});if(action==='bedrockVersions')return {installed:[{id:'windows-entry',name:'Microsoft.MinecraftUWP',version:'1.21.5000.0',installed:true}],available:[{id:'online:test',name:'Microsoft.MinecraftUWP',version:'1.21.50',packageVersion:'1.21.5000.0',format:'UWP'}],imported:[]};if(action==='bedrockLaunch')return {status:'launched'};throw Error(action);}};
 vm.createContext(ctx);vm.runInContext(fs.readFileSync(path.join(__dirname,'../src/Spectra/Web/bedrock.js'),'utf8'),ctx);
 await vm.runInContext("SpectraBedrock.launch('online:test')",ctx);
 assert.equal(dialogs.length,0);assert.equal(calls.find(x=>x.action==='bedrockLaunch').data.install,false);assert.equal(ctx.busy.size,0);
 console.log('PASS: installed online Bedrock launches directly, version aliases match and Preview stays separate');
})().catch(error=>{console.error(error);process.exitCode=1;});

(async()=>{
 const calls=[],toasts=[];const ctx={state:{selection:{}},busy:new Set(),syncPlayButtons(){},settleGameStatus(){},toast:message=>toasts.push(message),page:'instances',run:fn=>fn(),modal(){throw Error('No install dialog expected');},$:()=>({close(){}}),api:async(action,data)=>{calls.push({action,data});if(action==='bedrockVersions')return {installed:[{id:'installed',name:'Microsoft.MinecraftUWP',version:'1.21.5000.0',installed:true}],available:[]};if(action==='bedrockLaunch')return {status:'importingContent',count:2,message:'Import pending packs'};throw Error(action);}};
 vm.createContext(ctx);vm.runInContext(fs.readFileSync(path.join(__dirname,'../src/Spectra/Web/bedrock.js'),'utf8'),ctx);await vm.runInContext("SpectraBedrock.launch('installed')",ctx);
 assert.deepEqual(toasts,['Import pending packs']);assert.equal(calls.filter(x=>x.action==='bedrockLaunch').length,1);assert.equal(ctx.busy.size,0);
 console.log('PASS: pending-pack response ends launch flow and displays import guidance');
})().catch(error=>{console.error(error);process.exitCode=1;});
