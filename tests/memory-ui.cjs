const {chromium}=require('playwright');
const {pathToFileURL}=require('node:url'),path=require('node:path'),assert=require('node:assert/strict');
let browser;
(async()=>{
 browser=await chromium.launch({executablePath:process.env.SPECTRA_CHROMIUM_PATH||undefined,args:['--no-sandbox']});
 const p=await browser.newPage({viewport:{width:1200,height:800}}),errors=[];p.on('pageerror',e=>errors.push(e.message));
 await p.route('https://**/*',r=>r.abort());
 await p.goto(pathToFileURL(path.resolve(__dirname,'../src/Spectra/Web/index.html')).href+'?sample=1');
 await p.locator('.instance-card').first().waitFor();
 // Close callbacks must still see the old DOM to save a chat draft. Afterwards
 // even decoded images and handler closures should become collectible.
 assert.equal(await p.evaluate(async()=>{
  modal('<textarea id="memoryDraft">draft</textarea><img src="assets/aero-landscape.jpg">');
  let draft='';$('#modal').addEventListener('close',()=>draft=$('#memoryDraft').value,{once:true});$('#modal').close();
  await new Promise(r=>setTimeout(r,80));return draft==='draft'&&!$('#modalContent').childNodes.length;
 }),true);
 assert.equal(await p.evaluate(async()=>{
  modal('<h2>Old</h2>');$('#modal').addEventListener('close',()=>modal('<h2 id="replacement">New</h2>'),{once:true});$('#modal').close();
  await new Promise(r=>setTimeout(r,80));const ok=$('#modalContent h2')?.textContent==='New'&&$('#modal').open;$('#modal').close();return ok;
 }),true,'cleanup must preserve a replacement dialog opened by a close listener');
 await p.waitForTimeout(100);
 const cdp=await p.context().newCDPSession(p);
 // Warm the store cache before comparing retained DOM, then repeatedly leave it.
 await p.evaluate(async()=>{await navigate('discover');await navigate('instances');});await p.waitForTimeout(100);
 await cdp.send('HeapProfiler.collectGarbage');const before=await cdp.send('Memory.getDOMCounters');
 for(let cycle=0;cycle<12;cycle++){
  await p.evaluate(()=>{
   const main=$('#main'),host=document.createElement('div');host.style.cssText='position:absolute;top:100000px';main.append(host);
   for(let n=0;n<200;n++){const card=document.createElement('div');host.append(card);lazySkinPair(card,'https://test.invalid/skin','classic');}
   host.remove();modal('<div>'+('<img src="assets/aero-landscape.jpg">'.repeat(10))+'</div>');$('#modal').close();
  });await p.waitForTimeout(40);
  await p.evaluate(async()=>{await navigate('discover');await navigate('instances');});
 }
 await p.waitForTimeout(100);
 await cdp.send('HeapProfiler.collectGarbage');const after=await cdp.send('Memory.getDOMCounters');
 assert.equal(await p.evaluate(()=>pendingSkinPairs.size),0,'offscreen previews must be released after removing a list');
 assert(after.nodes<before.nodes+180,JSON.stringify({before,after}));
 const retained=await p.evaluate(()=>{
  for(let id=0;id<24;id++)for(let line=0;line<300;line++)onMessage({type:'log',instanceId:'memory-'+id,line:String(line)+':'+('x'.repeat(40000))});
  return{streams:logs.size,characters:[...logs.values()].reduce((sum,a)=>sum+a.join('\n').length,0),latest:logs.get('memory-23').at(-1).slice(0,4)};
 });assert(retained.streams<=8);assert(retained.characters<=8*262144);assert.equal(retained.latest,'299:');
 assert.equal(errors.length,0,errors.join('\n'));
 console.log('PASS memory: 2,400 removed offscreen previews and closed-image dialogs become collectible; close callbacks/replacements preserved; log flood bounded to 8 tails / 2 Mi characters');
 console.log(JSON.stringify({before,after,retained}));await browser.close();
})().catch(async e=>{console.error(e);await browser?.close();process.exitCode=1;});
