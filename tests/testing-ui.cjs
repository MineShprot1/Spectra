const {chromium}=require('playwright'),path=require('node:path'),{pathToFileURL}=require('node:url'),assert=require('node:assert/strict');
let browser;
(async()=>{
 browser=await chromium.launch({executablePath:process.env.SPECTRA_CHROMIUM_PATH||undefined,args:['--no-sandbox']});
 const p=await browser.newPage({viewport:{width:1200,height:850}}),errors=[];p.on('pageerror',e=>errors.push(e.message));await p.route('https://**/*',r=>r.abort());
 await p.goto(pathToFileURL(path.resolve(__dirname,'../src/Spectra/Web/index.html')).href+'?sample=1');await p.locator('.instance-card').first().waitFor();
 await p.evaluate(()=>{
  state.testing={enabled:false,buildChannel:'stable'};window.testCalls=[];const original=api;
  api=async(action,data={})=>{
   if(action.startsWith('testing'))testCalls.push({action,hasKey:!!data.key});
   if(action==='testingActivate'){if(data.key!=='valid-fixture')throw Error('Ключ недействителен или срок его действия истёк');state.testing={enabled:true,expires:Date.now()/1000+3600,buildChannel:'stable',connected:true};return state;}
   if(action==='testingDisable'){state.testing={enabled:false,buildChannel:'stable'};return state;}
   if(action==='bootstrap')return{checked:true,available:true,version:'Next',commit:'a'.repeat(40),channel:state.testing.enabled?'testing':'stable'};
   if(action==='updateSource'){window.updateRequest=data;return{restarting:true};}
   return original(action,data);
  };
  updateState(state);navigate('settings');
 });
 await p.locator('#testingSettings').waitFor({state:'attached'});
 await p.getByRole('tab',{name:'Windows и фоновая работа',exact:true}).click();
 assert(await p.locator('#testingChannelBadge').isHidden());await p.click('#enterTesting');assert.equal(await p.locator('#testingKey').getAttribute('type'),'password');
 await p.fill('#testingKey','bad-key');await p.click('#testingKeyForm button');await p.locator('#testingKeyError:not([hidden])').waitFor();assert.equal(await p.evaluate(()=>state.testing.enabled),false);
 await p.fill('#testingKey','valid-fixture');await p.click('#testingKeyForm button');await p.locator('#leaveTesting').waitFor();assert(await p.locator('#testingChannelBadge').isVisible());
 assert.equal(await p.locator('#modal').evaluate(e=>e.open),false);await p.waitForTimeout(50);assert.equal(await p.locator('#testingKey').count(),0);
 await p.click('#footerUpdate');await p.waitForFunction(()=>window.updateRequest);assert.equal(await p.evaluate(()=>updateRequest.channel),'testing');
 await p.evaluate(()=>openShell());await p.evaluate(()=>navigate('settings'));await p.getByRole('tab',{name:'Windows и фоновая работа',exact:true}).click();await p.click('#leaveTesting');await p.locator('#enterTesting').waitFor();assert(await p.locator('#testingChannelBadge').isHidden());
 await p.click('#footerUpdate');await p.waitForFunction(()=>window.updateRequest.channel==='stable');
 assert.deepEqual(errors,[]);console.log('PASS testing UI: quiet settings entry, hidden input, rejected key, activation, badge, secret removal, private update channel and return to stable');await browser.close();
})().catch(async error=>{console.error(error);await browser?.close();process.exitCode=1;});
