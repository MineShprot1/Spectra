const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const styles=new Map(),text={nodeValue:'Играть',parentElement:{closest:()=>false}};
const context={console,localStorage:{getItem:()=>null,setItem(){}},NodeFilter:{SHOW_TEXT:4},MutationObserver:class{observe(){}},document:{body:{dataset:{}},documentElement:{},head:{append(s){styles.set('#'+s.id,s)}},querySelector:s=>styles.get(s),createElement:()=>({}),createTreeWalker:()=>{let done=false;return{nextNode(){if(done)return null;done=true;return text;}}},addEventListener(){}}};
vm.createContext(context);vm.runInContext(fs.readFileSync(require('node:path').join(__dirname,'../src/Spectra/Web/appearance.js'),'utf8'),context);
vm.runInContext("SpectraAppearance.apply({language:'en',theme:'light',css:'.hero{display:none}'})",context);assert.equal(text.nodeValue,'Play');assert.equal(context.document.body.dataset.theme,'light');assert.equal(styles.get('#customThemeStyle').textContent,'.hero{display:none}');
vm.runInContext("SpectraAppearance.apply({language:'ru',theme:'gradient',colors:['#112233','#abcdef'],angle:90})",context);assert.equal(text.nodeValue,'Играть');assert(styles.get('#themeStyle').textContent.includes('linear-gradient(90deg,#112233,#abcdef)'));
const clean=vm.runInContext("SpectraAppearance.validate({language:'bad',theme:'bad',colors:['red','#123456'],angle:999})",context);assert.equal(clean.language,'ru');assert.equal(clean.angle,360);assert.equal(clean.colors.length,1);
console.log('PASS: theme validation, CSS override, gradient and reversible translation');
