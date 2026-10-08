// Logic tests run without WebView2; Windows smoke tests cover real DOM interaction.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const inert={hidden:true,textContent:'',disabled:false,style:{},classList:{toggle(){}},addEventListener(){}};
const context={console,URLSearchParams,location:{search:''},document:{querySelector:()=>inert,querySelectorAll:()=>[],addEventListener(){}},window:{},setTimeout:()=>0,clearTimeout(){},structuredClone,Intl,Map,Set,Date};
vm.createContext(context);
const source=fs.readFileSync(require('node:path').join(__dirname,'../src/Spectra/Web/app.js'),'utf8');
// Do not bootstrap the application in the logic harness.
vm.runInContext(source.replace(/boot\(\);\s*$/,''),context);
const evaluate=x=>vm.runInContext(x,context);
assert.equal(evaluate("esc('<img onerror=alert(1)>')"),'&lt;img onerror=alert(1)&gt;');
assert.equal(evaluate("safeImage('javascript:alert(1)')"),'');
assert.equal(evaluate("safeImage('file:///C:/secret')"),'');
assert.equal(evaluate("safeImage('https://cdn.modrinth.com/icon.png')"),'https://cdn.modrinth.com/icon.png');
assert.equal(evaluate("safeImage('data:text/html;base64,AAAA')"),'');
(async()=>{
 const versions=await evaluate("previewApi('versions',{})");assert(versions.some(x=>x.type==='old_alpha'));assert(versions.some(x=>x.type==='old_beta'));
 await assert.rejects(()=>evaluate("previewApi('launch',{})"),/просмотр интерфейса/i);
 await assert.rejects(()=>evaluate("previewApi('login',{})"),/просмотр интерфейса/i);
 evaluate("pending.set('test',{resolve:value=>globalThis.replyValue=value,reject:error=>globalThis.replyError=error})");evaluate("onMessage({type:'reply',id:'test',ok:true,result:42})");assert.equal(context.replyValue,42);assert.equal(evaluate('pending.size'),0);
 evaluate("for(let x=0;x<5100;x++)onMessage({type:'log',instanceId:'bounded',line:String(x)})");assert.equal(evaluate("logs.get('bounded').length"),5000);assert.equal(evaluate("logs.get('bounded')[0]"),'100');
 console.log('PASS: escaping, image URLs, preview integrity, RPC replies, bounded log buffer');
})().catch(e=>{console.error(e);process.exit(1)});
