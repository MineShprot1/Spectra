const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const storage=new Map();
const styles=new Map(),text={nodeValue:'Играть',parentElement:{closest:()=>false}};
const context={console,localStorage:{getItem:key=>storage.get(key)||null,setItem(key,value){storage.set(key,value)}},NodeFilter:{SHOW_TEXT:4},MutationObserver:class{observe(){}},document:{body:{dataset:{}},documentElement:{},head:{append(s){styles.set('#'+s.id,s)}},querySelector:s=>styles.get(s),createElement:()=>({}),createTreeWalker:()=>{let done=false;return{nextNode(){if(done)return null;done=true;return text;}}},addEventListener(){}}};
vm.createContext(context);vm.runInContext(fs.readFileSync(require('node:path').join(__dirname,'../src/Spectra/Web/translations.js'),'utf8'),context);vm.runInContext(fs.readFileSync(require('node:path').join(__dirname,'../src/Spectra/Web/locales.js'),'utf8'),context);vm.runInContext(fs.readFileSync(require('node:path').join(__dirname,'../src/Spectra/Web/appearance.js'),'utf8'),context);
vm.runInContext("SpectraAppearance.apply({language:'en',theme:'light',css:'.hero{display:none}'})",context);assert.equal(text.nodeValue,'Play');assert.equal(context.document.body.dataset.theme,'light');assert.equal(styles.get('#customThemeStyle').textContent,'.hero{display:none}');
vm.runInContext("SpectraAppearance.apply({language:'ru',theme:'gradient',colors:['#112233','#abcdef'],angle:90})",context);assert.equal(text.nodeValue,'Играть');assert(styles.get('#themeStyle').textContent.includes('linear-gradient(90deg,#112233,#abcdef)'));
const clean=vm.runInContext("SpectraAppearance.validate({language:'bad',theme:'bad',colors:['red','#123456'],angle:999})",context);assert.equal(clean.language,'ru');assert.equal(clean.angle,360);assert.equal(clean.colors.length,1);
vm.runInContext("SpectraAppearance.hydrate({theme:'steam'});SpectraAppearance.apply({theme:'aero'});SpectraAppearance.hydrate({theme:'steam'})",context);assert.equal(context.document.body.dataset.theme,'aero');
assert.equal(vm.runInContext("SpectraLocales.dictionary('de')['Исполняемый файл Java']",context),'Java-Programm');vm.runInContext("for(const [language,row] of Object.entries(SpectraLocales.rows)){if(row.split('|').length!==SpectraLocales.keys.length)throw Error(language+' locale length');}",context);vm.runInContext("SpectraAppearance.apply({theme:'windows'})",context);assert(styles.get('#themeStyle').textContent.includes('--theme-bg:#171717'));
vm.runInContext("for(const [language,row] of Object.entries(SpectraLocales.extraRows)){if(row.split('|').length!==SpectraLocales.extraKeys.length)throw Error(language+' extra locale length');}",context);assert.equal(vm.runInContext("SpectraLocales.dictionary('en')['Поиск версии…']",context),'Search versions…');assert.equal(vm.runInContext("SpectraLocales.dictionary('de')['ТВОЯ КОЛЛЕКЦИЯ']",context),'DEINE SAMMLUNG');
const nav={textContent:''},profile={textContent:'',hasAttribute:()=>true};const oldQuery=context.document.querySelector;context.document.querySelector=selector=>selector==='nav [data-page="discover"]'?nav:selector==='#profileTab'?profile:oldQuery(selector);
vm.runInContext("SpectraAppearance.apply({language:'en',theme:'dark'})",context);assert.equal(nav.textContent,'DISCOVER');assert.equal(profile.textContent,'PROFILE');vm.runInContext("SpectraAppearance.apply({language:'ru',theme:'dark'})",context);assert.equal(nav.textContent,'ОБЗОР');assert.equal(profile.textContent,'ПРОФИЛЬ');
assert.equal(vm.runInContext("SpectraLocales.dictionary('en')['Обзор']",context),'Discover');assert.equal(vm.runInContext("SpectraLocales.dictionary('en')['Профиль']",context),'Profile');
console.log('PASS: theme validation, CSS override, gradient and reversible translation');

vm.runInContext("SpectraAppearance.apply({theme:'dark',headingFont:'arial',bodyFont:'consolas'})",context);assert(styles.get('#themeStyle').textContent.includes("--heading-font:'Arial'"));assert(styles.get('#themeStyle').textContent.includes("--ui-font:'Consolas'"));
const invalid=vm.runInContext("SpectraAppearance.validate({headingFont:'Arial;bad',bodyFont:'custom:../file.ttf'})",context);assert.equal(invalid.headingFont,'pixel');assert.equal(invalid.bodyFont,'reading');
vm.runInContext("SpectraAppearance.apply({theme:'gradient',headingFont:'custom:'+ 'a'.repeat(64)+'.ttf',bodyFont:'reading'})",context);assert(styles.get('#themeStyle').textContent.includes('https://fonts.spectra.local/'+ 'a'.repeat(64)+'.ttf'));
vm.runInContext("SpectraAppearance.apply({theme:'windows',headingFont:'arial',bodyFont:'consolas'})",context);assert(!styles.get('#themeStyle').textContent.includes("--ui-font:'Consolas'"));
console.log('PASS: separate font roles, custom-font URLs, rejected font injection, themed font isolation');

vm.runInContext("SpectraAppearance.apply({theme:'steam',navigationPosition:'left'})",context);assert.equal(context.document.body.dataset.navigation,'left');assert.equal(vm.runInContext("SpectraAppearance.validate({navigationPosition:'sideways'}).navigationPosition",context),'top');vm.runInContext("SpectraAppearance.apply({navigationPosition:'bottom'});SpectraAppearance.apply({navigationPosition:'right'})",context);assert.equal(context.document.body.dataset.navigation,'right');console.log('PASS: navigation placement persists across themes and rejects invalid positions');

for(const language of ['en','de','fr','es','pt','it','pl','uk','tr','zh','ja','ko']){
 vm.runInContext(`SpectraAppearance.apply({language:${JSON.stringify(language)}})`,context);
 for(const key of ['МОДПАКИ','КАРТЫ','СКИНЫ','АДДОНЫ','ДОПОЛНЕНИЯ','У вас есть несохранённые настройки','Расположение навигации','Верифицироваться через Google','Полноэкранный режим']){
  const translated=vm.runInContext(`SpectraAppearance.text(${JSON.stringify(key)})`,context);
  assert(translated&&translated!==key,language+': '+key);
 }
}
vm.runInContext("SpectraAppearance.apply({language:'en'})",context);
for(const [source,expected] of [['Установленные версии (12)','Installed versions (12)'],['  ▷ Играть  ','  ▷ Play  '],['Выбрано: 3','Selected: 3'],['Полноэкранный режим · F11','Fullscreen · F11'],['Minecraft: Alice · верифицирован','Minecraft: Alice · verified'],['Совместимость: 1.21 · fabric','Compatibility: 1.21 · fabric'],['Удалить Minecraft 1.21?','Delete Minecraft 1.21?'],['Состав (42)','Contents (42)'],['Скачивание ресурс паков','Download resource packs']]){
 assert.equal(vm.runInContext(`SpectraAppearance.text(${JSON.stringify(source)})`,context),expected);
}
for(const source of ['My Russian сборка','Alice','https://example.test/Играть','$`$&','1.21.4'])assert.equal(vm.runInContext(`SpectraAppearance.text(${JSON.stringify(source)})`,context),source);
text.nodeValue='Показать ещё';vm.runInContext("SpectraAppearance.apply({language:'de'})",context);assert.equal(text.nodeValue,'Mehr anzeigen');vm.runInContext("SpectraAppearance.apply({language:'ja'})",context);assert.equal(text.nodeValue,'もっと表示');vm.runInContext("SpectraAppearance.apply({language:'ru'})",context);assert.equal(text.nodeValue,'Показать ещё');
assert.equal(vm.runInContext("SpectraLocales.dictionary('de')['Исполняемый файл Java']",context),'Java-Programm');
console.log('PASS: 12 locale additions, decorated labels, counts, dynamic prefixes, unknown content and reversible language changes');

vm.runInContext("SpectraAppearance.apply({language:'en',theme:'gradient',colors:['#112233','#445566'],angle:40,gradientTargets:{top:{inherit:false,colors:['#abcdef','#123456'],angle:80},bottom:{enabled:false},hud:{inherit:true}}});var savedId=SpectraAppearance.addPreset('My gradient');",context);
let gradientStyle=styles.get('#themeStyle').textContent;
assert(gradientStyle.includes('body[data-theme="gradient"] #titlebar'));
assert(gradientStyle.includes('linear-gradient(80deg,#abcdef,#123456)!important'));
assert(gradientStyle.includes('body[data-theme="gradient"] .glass'));
assert(!gradientStyle.includes('body[data-theme="gradient"] #shell > footer'));
vm.runInContext("SpectraAppearance.selectPreset(savedId);",context);
const savedAppearance=JSON.parse(storage.get('spectraAppearance'));
assert.equal(savedAppearance.gradientPresets.length,1);assert.equal(savedAppearance.gradientPresets[0].name,'My gradient');
vm.runInContext(`SpectraAppearance.apply({theme:'dark'});SpectraAppearance.apply(SpectraAppearance.validate(${JSON.stringify(savedAppearance)}));SpectraAppearance.selectPreset(savedId);`,context);

gradientStyle=styles.get('#themeStyle').textContent;
assert(gradientStyle.includes('linear-gradient(40deg,#112233,#445566)'));
assert(gradientStyle.includes('linear-gradient(80deg,#abcdef,#123456)!important'));
assert.equal(vm.runInContext("SpectraAppearance.selectPreset('missing')",context),false);
const invalidPresets=vm.runInContext("SpectraAppearance.validate({gradientPresets:[{id:'bad\"',name:'x'},{id:'safe',name:' A ',colors:['red','url(x)','#112233','#445566']},{id:'safe',name:'duplicate'}],gradientTargets:{top:{angle:Infinity,colors:['url(x)','#112233','#445566']}}})",context);
assert.equal(invalidPresets.gradientPresets.length,1);assert.equal(invalidPresets.gradientPresets[0].name,'A');assert.equal(invalidPresets.gradientTargets.top.angle,135);assert.equal(invalidPresets.gradientTargets.top.colors.length,2);
vm.runInContext("SpectraAppearance.removePreset(savedId);",context);assert.equal(vm.runInContext("SpectraAppearance.selectPreset(savedId)",context),false);
for(const lang of ['en','de','fr','es','pt','it','pl','uk','tr','zh','ja','ko']){vm.runInContext(`SpectraAppearance.apply({language:'${lang}'})`,context);assert.notEqual(vm.runInContext("SpectraAppearance.text('Пресеты градиента')",context),'Пресеты градиента');}
vm.runInContext("SpectraAppearance.apply({theme:'dark'})",context);assert(!styles.get('#themeStyle').textContent.includes('background-image:linear-gradient'));
console.log('PASS: independent gradient targets, presets survive JSON roundtrip, invalid inputs, removal and theme isolation');
