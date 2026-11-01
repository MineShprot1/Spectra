const repository='MineShprot1/Spectra-Testing';
const json=(data,status=200)=>new Response(JSON.stringify(data),{status,headers:{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store'}});
const digest=async value=>[...new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value)))].map(x=>x.toString(16).padStart(2,'0')).join('');
async function equalSecret(a,b){return !!a&&!!b&&(await digest(a))===(await digest(b));}
const bearer=request=>(request.headers.get('Authorization')||'').replace(/^Bearer /,'');
async function body(request){if(Number(request.headers.get('Content-Length')||0)>16384)throw Error('large');const reader=request.body?.getReader();if(!reader)throw Error('empty');let size=0;const chunks=[];for(;;){const part=await reader.read();if(part.done)break;size+=part.value.length;if(size>16384){await reader.cancel();throw Error('large');}chunks.push(part.value);}const raw=new Uint8Array(size);let offset=0;for(const chunk of chunks){raw.set(chunk,offset);offset+=chunk.length;}return JSON.parse(new TextDecoder().decode(raw));}
async function session(request,env){const token=bearer(request);if(!/^[a-f0-9]{64}$/.test(token))return null;return env.DB.prepare('SELECT installation_id,label FROM testing_keys WHERE session_hash=? AND redeemed IS NOT NULL AND revoked IS NULL').bind(await digest(token)).first();}
export default {
 async fetch(request,env){
  try{
   const url=new URL(request.url),now=Math.floor(Date.now()/1000);
   if(url.pathname==='/health'&&request.method==='GET'){let ready=!!env.DB&&!!env.ADMIN_KEY&&!!env.GITHUB_TOKEN;if(ready)try{await env.DB.prepare('SELECT key_hash FROM testing_keys LIMIT 1').bind().first();}catch{ready=false;}return json({service:'Spectra Network Dev',ready});}
   if(url.pathname.startsWith('/admin/')){
    if(!await equalSecret(bearer(request),env.ADMIN_KEY))return json({error:'Unauthorized'},401);
    if(request.method!=='POST')return json({error:'Method not allowed'},405);
    const value=await body(request);
    if(url.pathname==='/admin/keys'){
     if(!/^[a-f0-9]{64}$/.test(value.keyHash||'')||typeof value.label!=='string'||!value.label.trim()||value.label.length>80||!Number.isSafeInteger(value.expires)||value.expires<=now||value.expires>now+86400)return json({error:'Invalid key metadata'},400);
     const result=await env.DB.prepare('INSERT OR IGNORE INTO testing_keys(key_hash,label,expires,created) VALUES(?,?,?,?)').bind(value.keyHash,value.label,value.expires,now).run();
     return json({created:result.meta.changes===1},result.meta.changes===1?201:409);
    }
    if(url.pathname==='/admin/revoke'){
     if(!/^[a-f0-9]{32}$/.test(value.installationId||''))return json({error:'Invalid installation'},400);
     const result=await env.DB.prepare('UPDATE testing_keys SET revoked=? WHERE installation_id=?').bind(now,value.installationId).run();return json({revoked:result.meta.changes>0});
    }
    return json({error:'Not found'},404);
   }
   if(url.pathname==='/v1/testing/redeem'&&request.method==='POST'){
    const value=await body(request);
    if(typeof value.key!=='string'||value.key.length>8192||!value.key.startsWith('SPTEST2.')||!/^[a-f0-9]{32}$/.test(value.installationId||'')||!/^[a-f0-9]{64}$/.test(value.secret||''))return json({error:'Invalid key'},400);
    const hash=await digest(value.key),secret=await digest(value.secret);
    // A single conditional write arbitrates simultaneous redemption on all PCs.
    const claimed=await env.DB.prepare('UPDATE testing_keys SET installation_id=?,session_hash=?,redeemed=? WHERE key_hash=? AND redeemed IS NULL AND revoked IS NULL AND expires>?').bind(value.installationId,secret,now,hash,now).run();
    if(claimed.meta.changes===1){const entry=await env.DB.prepare('SELECT label FROM testing_keys WHERE key_hash=?').bind(hash).first();return json({active:true,installationId:value.installationId,label:entry.label});}
    // Recover a lost response only for the original pending installation, which
    // persisted its random secret before sending the first request.
    const existing=await env.DB.prepare('SELECT installation_id,session_hash,label,revoked FROM testing_keys WHERE key_hash=?').bind(hash).first();
    if(existing&&existing.revoked===null&&existing.installation_id===value.installationId&&existing.session_hash===secret)return json({active:true,installationId:existing.installation_id,label:existing.label});
    return json({error:'Key expired or already used'},409);
   }
   if(url.pathname==='/v1/testing/session'&&request.method==='GET'){
    const current=await session(request,env);return current?json({active:true,installationId:current.installation_id,label:current.label}):json({error:'Access revoked or invalid'},401);
   }
   if(url.pathname.startsWith('/v1/testing/github/')&&request.method==='GET'){
    if(!await session(request,env))return json({error:'Access revoked or invalid'},401);
    const path=url.pathname.slice('/v1/testing/github'.length);
    // Never accept an arbitrary URL, repository, write method or GitHub path.
    const allowed=path==='/commits'||/^\/commits\/[a-f0-9]{40}$/.test(path)||/^\/git\/(trees|blobs)\/[a-f0-9]{40}$/.test(path)||path==='/contents/launcher-channel.json';
    if(!allowed)return json({error:'Not found'},404);
    const query=new URLSearchParams();
    if(path==='/commits'){query.set('per_page','1');query.set('sha','main');}
    if(path.startsWith('/git/trees/'))query.set('recursive','1');
    if(path==='/contents/launcher-channel.json'){const ref=url.searchParams.get('ref');if(!/^[a-f0-9]{40}$/.test(ref||''))return json({error:'Invalid ref'},400);query.set('ref',ref);}
    const suffix=query.size?'?'+query.toString():'';
    const response=await fetch('https://api.github.com/repos/'+repository+path+suffix,{headers:{'Authorization':'Bearer '+env.GITHUB_TOKEN,'User-Agent':'Spectra-Network-Dev','Accept':path.startsWith('/git/blobs/')||path.startsWith('/contents/')?'application/vnd.github.raw+json':'application/vnd.github+json','Accept-Encoding':'identity'},redirect:'error'});
    if(!response.ok)return json({error:'Testing repository unavailable',status:response.status},response.status===429?429:502);
    return new Response(response.body,{status:200,headers:{'Content-Type':response.headers.get('Content-Type')||'application/octet-stream','Cache-Control':'no-store'}});
   }
   return json({error:'Not found'},404);
  }catch{return json({error:'Request failed'},503);}
 }
};
