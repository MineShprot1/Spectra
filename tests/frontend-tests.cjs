// Logic tests run without WebView2; Windows smoke tests cover real DOM interaction.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const inert={hidden:true,textContent:'',disabled:false,style:{},classList:{toggle(){}},addEventListener(){}};
const context={console,Number,URLSearchParams,location:{search:''},document:{querySelector:()=>inert,querySelectorAll:()=>[],addEventListener(){}},window:{},setTimeout:()=>0,clearTimeout(){},structuredClone,Intl,Map,Set,Date};
vm.createContext(context);
const source=fs.readFileSync(require('node:path').join(__dirname,'../src/Spectra/Web/app.js'),'utf8');
// Do not bootstrap the application in the logic harness.
vm.runInContext(source.replace(/boot\(\)\.then\([\s\S]*$/,''),context);
const evaluate=x=>vm.runInContext(x,context);
assert.equal(evaluate("esc('<img onerror=alert(1)>')"),'&lt;img onerror=alert(1)&gt;');
assert.equal(evaluate("safeImage('javascript:alert(1)')"),'');
assert.equal(evaluate("safeImage('file:///C:/secret')"),'');
assert.equal(evaluate("safeImage('https://cdn.modrinth.com/icon.png')"),'https://cdn.modrinth.com/icon.png');
assert.equal(evaluate("safeImage('data:text/html;base64,AAAA')"),'');
assert.match(evaluate('bytes(1048576)'),/1 МиБ/);
assert.equal(evaluate("skinUrl({skins:[{url:'http://textures.minecraft.net/texture/test'}]})"),'https://textures.minecraft.net/texture/test');
assert.equal(evaluate("findInstance('vanilla').loader"),'vanilla');
assert.match(evaluate("quickCard('1.20.1',null)"),/assets\/terrain.svg/);
assert.match(evaluate("quickCard('',{name:'<script>',version:'1.20.1',loader:'fabric',banner:'javascript:alert(1)'})"),/&lt;script&gt;/);
assert(!evaluate("quickCard('',{name:'X',banner:'javascript:alert(1)'})").includes('javascript:'));
(async()=>{
 await evaluate("previewApi('select',{version:'1.20.1',instanceId:''})");assert.equal(evaluate('state.instances.length'),0);assert.equal(evaluate('state.selection.version'),'1.20.1');
 await evaluate("previewApi('view',{kind:'versions',mode:'list'})");assert.equal(evaluate('state.views.versions'),'list');
 const versions=await evaluate("previewApi('versions',{})");assert(versions.some(x=>x.type==='old_alpha'));assert(versions.some(x=>x.type==='old_beta'));
 await assert.rejects(()=>evaluate("previewApi('launch',{})"),/просмотр интерфейса/i);
 await assert.rejects(()=>evaluate("previewApi('login',{})"),/просмотр интерфейса/i);
 evaluate("pending.set('test',{resolve:value=>globalThis.replyValue=value,reject:error=>globalThis.replyError=error})");evaluate("onMessage({type:'reply',id:'test',ok:true,result:42})");assert.equal(context.replyValue,42);assert.equal(evaluate('pending.size'),0);
 evaluate("for(let x=0;x<5100;x++)onMessage({type:'log',instanceId:'bounded',line:String(x)})");assert.equal(evaluate("logs.get('bounded').length"),5000);assert.equal(evaluate("logs.get('bounded')[0]"),'100');
 evaluate("onMessage({type:'logBatch',items:[{type:'log',instanceId:'batched',line:'first'},{type:'log',instanceId:'batched',line:'second'}]})");assert.equal(evaluate("logs.get('batched').join(',')"),'first,second');
 console.log('PASS: escaping, image URLs, preview integrity, RPC replies, bounded log buffer');
})().catch(e=>{console.error(e);process.exit(1)});

assert.equal(evaluate("creationVersions([{type:'release'},{type:'snapshot'},{type:'old_beta'},{type:'old_alpha'}]).length"),1);
assert.equal(evaluate("creationVersions([{type:'release'},{type:'snapshot'},{type:'old_beta'},{type:'old_alpha'}],{beta:true}).length"),2);
assert.equal(evaluate("creationVersions([{type:'release'},{type:'snapshot'},{type:'old_beta'},{type:'old_alpha'}],{snapshots:true,beta:true,alpha:true}).length"),4);
console.log('PASS: creation filters exclude snapshots, beta and alpha by default');

// Exercise real pointer handlers: toggling selected rows preserves other selections,
// empty space clears them, and mouse capture starts only on drag (so image double-click works).
context.selectionHost={querySelectorAll:()=>[],setPointerCapture(){throw Error('Capture must not begin on a click');}};
context.selectionRow={dataset:{select:'1'}};
context.selectionChanges=0;
evaluate('globalThis.testSelection=new Set([1,2]);bindFileSelection(selectionHost,testSelection,()=>selectionChanges++)');
const pointer=(row,ctrl=false)=>({button:0,ctrlKey:ctrl,clientX:20,clientY:20,pointerId:1,target:{closest:selector=>selector==='[data-select]'?row:null}});
context.selectionHost.onpointerdown(pointer(context.selectionRow));assert.deepEqual([...context.testSelection],[2]);context.selectionHost.onpointerup();
context.selectionHost.onpointerdown(pointer(context.selectionRow,true));assert.deepEqual([...context.testSelection],[2,1]);context.selectionHost.onpointerup();
context.selectionHost.onpointerdown(pointer(null));assert.equal(context.testSelection.size,0);context.selectionHost.onpointerup();
console.log('PASS: selection toggle, additive selection, empty-space clearing, no pointer capture on click');

// Screenshot click/drag behavior without a renderer: drag must never act as a click.
const viewerImage={clientWidth:400,clientHeight:200,style:{},onload:null};
const viewerStage={clientWidth:400,clientHeight:200,classList:{toggle(){}},querySelector:()=>viewerImage,setPointerCapture(){},releasePointerCapture(){},getBoundingClientRect:()=>({left:0,top:0,width:400,height:200})};
context.requestAnimationFrame=fn=>fn();
const viewerDialog={setAttribute(){},classList:{add(){},remove(){}},showModal(){}};
const previousQuery=context.document.querySelector;
context.document.querySelector=selector=>selector==='#shotViewport'?viewerStage:selector==='#modal'?viewerDialog:selector==='#modalContent'?{innerHTML:''}:null;
evaluate("screenshotViewer('https://example.test/screenshot.png')");
const point=(x,y)=>({button:0,pointerId:1,clientX:x,clientY:y,preventDefault(){}});
viewerStage.onpointerdown(point(200,100));viewerStage.onpointerup(point(200,100));assert.match(viewerImage.style.transform,/scale\(2.5\)/);
viewerStage.onpointerdown(point(200,100));viewerStage.onpointermove(point(250,110));viewerStage.onpointerup(point(250,110));assert.equal(viewerImage.style.transform,'translate(50px,10px) scale(2.5)');
viewerStage.onpointerdown(point(200,100));viewerStage.onpointerup(point(200,100));assert.equal(viewerImage.style.transform,'translate(0px,0px) scale(1)');
context.document.querySelector=previousQuery;
console.log('PASS: screenshot zoom, drag pan, click-to-reset');
