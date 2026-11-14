const vm=require('node:vm'),fs=require('node:fs'),assert=require('node:assert/strict');
const c=vm.createContext({});vm.runInContext(fs.readFileSync('src/Spectra/Web/presence.js','utf8')+';globalThis.p=SpectraPresence',c);
assert.equal(c.p.own({networkAccount:{id:'a'},idle:true}).state,'away');
assert.equal(c.p.own({networkAccount:{id:'a'},idle:true,running:['a']}).state,'playing-away');
assert.equal(c.p.own({networkAccount:{id:'a'},idle:false}).state,'online');
assert.equal(c.p.own({networkAccount:{id:'a'},idle:true,hideOnlineStatus:true}).state,'invisible');
assert.equal(c.p.friend({online:false,presence:{idle:true}}),'offline');
assert.equal(c.p.friend({online:true,presence:{idle:true,playing:true}}),'playing-away');
console.log('PASS presence: away, activity recovery, playing, offline and privacy precedence');
