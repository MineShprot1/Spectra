const {chromium}=require('playwright'),path=require('node:path'),{pathToFileURL}=require('node:url'),assert=require('node:assert/strict');
let browser;
(async()=>{
 browser=await chromium.launch({executablePath:process.env.SPECTRA_CHROMIUM_PATH||undefined,args:['--no-sandbox']});const p=await browser.newPage({viewport:{width:1200,height:850}}),errors=[];p.on('pageerror',e=>errors.push(e.message));await p.route('https://**/*',r=>r.abort());
 await p.goto(pathToFileURL(path.resolve(__dirname,'../src/Spectra/Web/index.html')).href+'?sample=1');await p.locator('.instance-card').first().waitFor();
 await p.evaluate(()=>{
  state.testing={enabled:false,registered:false,verified:false,buildChannel:'stable'};window.testCalls=[];window.updateRequests=[];window.fakeAccess=true;const original=api;
  api=async(action,data={})=>{
   if(action.startsWith('testing'))testCalls.push({action,hasKey:!!data.key});
   if(action==='testingActivate'){if(data.key&&data.key!=='valid-once')throw Error('Ключ недействителен или срок его действия истёк');state.testing={...state.testing,enabled:true,registered:true,verified:true};return state;}
   if(action==='testingDisable'){state.testing={...state.testing,enabled:false,registered:true,verified:false};return state;}
   if(action==='testingSession'){state.testing.verified=fakeAccess;state.testing.error=fakeAccess?'':'Доступ отозван';return state;}
   if(action==='bootstrap')return{checked:true,available:true,version:state.testing.enabled?'0.1.0-test.2':'0.28.1',commit:'a'.repeat(40),channel:state.testing.enabled?'testing':'stable'};
   if(action==='updateSource'){updateRequests.push(data);return{restarting:true};}
   return original(action,data);
  };updateState(state);navigate('settings');
 });
 await p.locator('#testingSettings').waitFor();assert(await p.locator('#enterTesting').isVisible(),'entry banner must be visible at the top of ordinary settings');
 await p.click('#enterTesting');assert.equal(await p.locator('#testingKey').getAttribute('type'),'password');await p.fill('#testingKey','bad');await p.click('#testingKeyForm button');await p.locator('#testingKeyError:not([hidden])').waitFor();assert.equal(await p.evaluate(()=>updateRequests.length),0);
 await p.fill('#testingKey','valid-once');await p.click('#testingKeyForm button');await p.waitForFunction(()=>updateRequests.length===1);assert.equal(await p.evaluate(()=>updateRequests[0].channel),'testing');await p.waitForTimeout(50);assert.equal(await p.locator('#testingKey').count(),0);assert.equal(await p.locator('#updateAccept').count(),0,'no second update confirmation');
 await p.evaluate(async()=>{state.testing.buildChannel='testing';state.launcherVersion='0.1.0-test.2';updateState(state);openShell();await navigate('settings');});assert.equal(await p.locator('#enterTesting').count(),0,'testing build must have no developer-key login');assert(await p.locator('#leaveTesting').isVisible());
 assert.equal(await p.evaluate(()=>SpectraTesting.authorizeBoot()),true);await p.evaluate(()=>fakeAccess=false);await p.evaluate(()=>boot());assert.equal(await p.evaluate(()=>updateRequests.length),1);assert(await p.locator('#shell').isHidden());assert.equal(await p.locator('#continueWithoutUpdate').count(),0);assert(await p.locator('#returnStable').isVisible());
 await p.click('#returnStable');await p.waitForFunction(()=>updateRequests.length===2);assert.equal(await p.evaluate(()=>updateRequests[1].channel),'stable');
 await p.evaluate(async()=>{state.testing.buildChannel='stable';state.launcherVersion='0.28.1';updateState(state);openShell();await navigate('settings');});assert.equal(await p.locator('#enterTesting').textContent(),'Перейти в тестовую версию');await p.click('#enterTesting');await p.waitForFunction(()=>updateRequests.length===3);assert.equal(await p.locator('#testingKey').count(),0);assert.equal(await p.evaluate(()=>testCalls.filter(c=>c.action==='testingActivate').at(-1).hasKey),false);
 assert.deepEqual(errors,[]);console.log('PASS testing UI: visible ordinary entry, rejected key, immediate update, no key form in testing, saved-access gate, no bypass, automatic return and re-entry without another key');await browser.close();
})().catch(async error=>{console.error(error);await browser?.close();process.exitCode=1;});
