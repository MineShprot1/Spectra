const {chromium}=require('playwright');
const {pathToFileURL}=require('node:url');const path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch();const p=await browser.newPage({viewport:{width:1380,height:860}});const errors=[];p.on('pageerror',e=>errors.push(e.message));
 await p.goto(pathToFileURL(path.resolve(__dirname,'../src/Spectra/Web/index.html')).href+'?sample=1');await p.locator('.instance-card').first().waitFor();assert.equal(await p.locator('.instance-card').count(),2);
 await p.evaluate(()=>document.fonts.ready);assert.equal(await p.locator('.instance-card').first().evaluate(e=>getComputedStyle(e).borderTopLeftRadius),'0px');assert(await p.locator('h1').first().evaluate(e=>getComputedStyle(e).fontFamily.includes('Spectra Pixel')));assert.equal(await p.locator('#clientId').count(),0);
 fs.mkdirSync(path.resolve(__dirname,'../test-results'),{recursive:true});await p.screenshot({path:path.resolve(__dirname,'../test-results/library.png')});
 await p.click('nav [data-page="versions"]');await p.locator('.version-card').first().waitFor();const releaseCount=await p.locator('.version-card').count();await p.check('#alpha');assert.equal(await p.locator('.version-card').count(),releaseCount+1);
 await p.fill('#versionSearch','1.20');assert.equal(await p.locator('.version-card').count(),3);await p.locator('.version-card').first().click();await p.fill('#instanceName','Проверка интерфейса');await p.selectOption('#loader','fabric');await p.locator('#saveInstance:not([disabled])').waitFor();await p.click('#saveInstance');await p.waitForFunction(()=>document.querySelectorAll('.instance-card').length===3);
 await p.locator('[data-edit]').first().click();for(const tab of ['mods','shaders','resources','worlds','servers','screenshots','settings','logs','version']){await p.click('[data-tab="'+tab+'"]');await p.locator('#editorContent').waitFor();}
 await p.click('nav [data-page="settings"]');await p.fill('#minRam','1024');await p.fill('#maxRam','3072');await p.click('#settingsForm button[type=submit]');await p.locator('#toast:not([hidden])').waitFor();
 await p.click('nav [data-page="profile"]');await p.locator('#playerSearch').waitFor();await p.click('nav [data-page="discover"]');await p.locator('.warning').waitFor();
 await p.setViewportSize({width:960,height:640});await p.click('nav [data-page="instances"]');assert(await p.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth));await p.screenshot({path:path.resolve(__dirname,'../test-results/library-compact.png')});
 assert.deepEqual(errors,[]);await browser.close();console.log('PASS UI smoke: navigation, filters, creation, editor tabs, settings, compact layout');
})().catch(e=>{console.error(e);process.exit(1)});
