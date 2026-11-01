const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url'),{DatabaseSync}=require('node:sqlite');
(async()=>{
 const worker=(await import(pathToFileURL(path.resolve(__dirname,'../dev-server/worker.mjs')))).default;
 const sqlite=new DatabaseSync(':memory:');sqlite.exec(fs.readFileSync(path.resolve(__dirname,'../dev-server/schema.sql'),'utf8'));
 const env={ADMIN_KEY:'admin-fixture',GITHUB_TOKEN:'server-only-fixture',DB:{prepare(sql){return{bind(...values){return{async first(){return sqlite.prepare(sql).get(...values)||null;},async run(){const result=sqlite.prepare(sql).run(...values);return{meta:{changes:Number(result.changes)}};}};}};}}};
 const digest=async text=>Buffer.from(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(text))).toString('hex');
 const request=(route,method='GET',data=null,token='')=>worker.fetch(new Request('https://dev.example'+route,{method,headers:{...(token?{Authorization:'Bearer '+token}:{}),'Content-Type':'application/json'},...(data?{body:JSON.stringify(data)}:{})}),env);
 const coupon='SPTEST2.signed-fixture',expires=Math.floor(Date.now()/1000)+600;
 assert.equal((await request('/admin/keys','POST',{},'wrong')).status,401);
 assert.equal((await request('/admin/keys','POST',{keyHash:await digest(coupon),label:'Developer',expires},env.ADMIN_KEY)).status,201);
 const attempts=await Promise.all(Array.from({length:24},(_,i)=>{const id=i.toString(16).padStart(32,'0'),secret=(i+1).toString(16).padStart(64,'0');return request('/v1/testing/redeem','POST',{key:coupon,installationId:id,secret}).then(response=>({response,id,secret}));}));
 const winners=attempts.filter(a=>a.response.status===200);assert.equal(winners.length,1,'one global redemption across concurrent PCs');assert.equal(attempts.filter(a=>a.response.status===409).length,23);
 const winner=winners[0];assert.equal((await request('/v1/testing/redeem','POST',{key:coupon,installationId:winner.id,secret:winner.secret})).status,200,'lost response recovery on original installation');
 assert.equal((await request('/v1/testing/session','GET',null,winner.secret)).status,200);assert.equal((await request('/v1/testing/session','GET',null,'f'.repeat(64))).status,401);
 const nativeFetch=globalThis.fetch;let upstream=[];
 try{globalThis.fetch=async(url,options)=>{upstream.push({url,options});return new Response('[]',{headers:{'Content-Type':'application/json'}});};
  assert.equal((await request('/v1/testing/github/commits','GET',null,winner.secret)).status,200);
  assert.equal(upstream[0].url,'https://api.github.com/repos/MineShprot1/Spectra-Testing/commits?per_page=1&sha=main');assert.equal(upstream[0].options.headers.Authorization,'Bearer server-only-fixture');
  assert.equal((await request('/v1/testing/github/contents/other-repo','GET',null,winner.secret)).status,404);assert.equal((await request('/v1/testing/github/git/blobs/'+'a'.repeat(40),'GET',null,'x')).status,401);assert.equal(upstream.length,1);
 }finally{globalThis.fetch=nativeFetch;}
 const expired='SPTEST2.expired';await request('/admin/keys','POST',{keyHash:await digest(expired),label:'Expired',expires},env.ADMIN_KEY);sqlite.prepare('UPDATE testing_keys SET expires=0 WHERE key_hash=?').run(await digest(expired));assert.equal((await request('/v1/testing/redeem','POST',{key:expired,installationId:'f'.repeat(32),secret:'f'.repeat(64)})).status,409);
 await request('/admin/revoke','POST',{installationId:winner.id},env.ADMIN_KEY);assert.equal((await request('/v1/testing/session','GET',null,winner.secret)).status,401);assert.equal((await request('/v1/testing/redeem','POST',{key:coupon,installationId:winner.id,secret:winner.secret})).status,409);
 sqlite.close();console.log('PASS Spectra Network Dev: real SQLite, 24 concurrent claims / one winner, replay refusal, original-PC retry, persistent sessions, expiry, revocation and fixed private GitHub proxy');
})().catch(error=>{console.error(error);process.exitCode=1;});
