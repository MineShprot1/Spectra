const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const nodes=new Map();
function node(id){if(!nodes.has(id))nodes.set(id,{isConnected:true,disabled:false,value:'',checked:false,valid:true,listeners:[],reportValidity(){return this.valid;},addEventListener(name,fn){if(name==='close')this.listeners.push(fn);},close(){const callbacks=this.listeners.splice(0);callbacks.forEach(fn=>fn());},classList:{toggle(){}},hidden:false});return nodes.get(id);}
const context={console,URLSearchParams,location:{search:''},document:{querySelector:s=>node(s),querySelectorAll:()=>[],addEventListener(){}},window:{},setTimeout:()=>0,clearTimeout(){},structuredClone,Intl,Map,Set,Date};vm.createContext(context);
const source=fs.readFileSync(path.join(__dirname,'../src/Spectra/Web/app.js'),'utf8').replace(/boot\(\)\.then\([\s\S]*$/,'');vm.runInContext(source,context);
const evaluate=s=>vm.runInContext(s,context);let values={javaPath:'',gpu:'system',minRam:1024,maxRam:2048,width:854,height:480,hideOnLaunch:true},fail=false,saves=0;
context.readValues=()=>({...values});context.save=async()=>{saves++;if(fail)throw Error('Save failed');return {};};context.show=html=>{context.html=html;};
evaluate('readSettings=readValues;api=save;updateState=()=>{};toast=()=>{};modal=show;run=fn=>fn();');
function draft(){evaluate("settingsDraft={form:$('#settingsForm'),global:true,instance:null,snapshot:JSON.stringify(readSettings()),saving:false,pending:null}");}
(async()=>{
 draft();assert.equal(await evaluate('leaveSettings()'),true);assert.equal(saves,0);
 draft();values.maxRam=4096;const cancel=evaluate('leaveSettings()');assert(context.html.includes('несохранённые'));node('#settingsStay').onclick();assert.equal(await cancel,false);assert.equal(evaluate('hasSettingsChanges()'),true);assert.equal(values.maxRam,4096);
 const save=evaluate('leaveSettings()');await node('#settingsSaveAndLeave').onclick();assert.equal(await save,true);assert.equal(saves,1);assert.equal(evaluate('settingsDraft'),null);
 draft();values.width=1280;fail=true;const failed=evaluate('leaveSettings()');await assert.rejects(node('#settingsSaveAndLeave').onclick,/Save failed/);assert.equal(evaluate('hasSettingsChanges()'),true);assert.equal(node('#settingsSaveAndLeave').disabled,false);node('#modal').close();assert.equal(await failed,false);fail=false;
 const invalid=evaluate('leaveSettings()');node('#settingsForm').valid=false;await node('#settingsSaveAndLeave').onclick();assert.equal(evaluate('hasSettingsChanges()'),true);node('#settingsStay').onclick();assert.equal(await invalid,false);node('#settingsForm').valid=true;
 // Navigation must stay on the settings page after cancellation and resume only after saving.
 draft();values.height=720;evaluate("page='settings';renderProfile=()=>{globalThis.rendered='profile';}");const navigation=evaluate("navigate('profile')");node('#settingsStay').onclick();await navigation;assert.equal(evaluate('page'),'settings');assert.equal(context.rendered,undefined);
 const savedNavigation=evaluate("navigate('profile')");await node('#settingsSaveAndLeave').onclick();await savedNavigation;assert.equal(evaluate('page'),'profile');assert.equal(context.rendered,'profile');
 // A rendered save button must belong to the settings panel, rather than follow it.
 context.formSettings=values;const html=evaluate('settingsForm(formSettings,true)');const stack=[];let saveInsidePanel=false;
 for(const m of html.matchAll(/<\/?([a-z]+)\b[^>]*>/g)){const tag=m[1];if(m[0].startsWith('</')){if(stack.at(-1)?.tag===tag)stack.pop();}else if(!['input','br','img'].includes(tag)){if(tag==='button'&&m[0].includes('type="submit"'))saveInsidePanel=stack.some(x=>x.panel);stack.push({tag,panel:m[0].includes('class="panel settings-section"')});}}
 assert(saveInsidePanel);console.log('PASS: unsaved settings cancel, save, failures, validation and save-button panel placement');
})().catch(e=>{console.error(e);process.exitCode=1;});
