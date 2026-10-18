const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../src/Spectra/Web/social.js'),'utf8');
const me='1'.repeat(32),other='2'.repeat(32),timers=new Map(),sent=[];let serial=0;
class PC{
 constructor(){this.connectionState='new';this.signalingState='stable';this.remoteDescription=null;this.added=[];}
 createDataChannel(){return{readyState:'connecting'};}
 setConfiguration(config){this.configuration=config;}
 async createOffer(){return{type:'offer',sdp:'test offer'};}
 async createAnswer(){return{type:'answer',sdp:'test answer'};}
 async setLocalDescription(d){this.localDescription={...d,toJSON:()=>d};this.signalingState=d.type==='offer'?'have-local-offer':'stable';}
 async setRemoteDescription(d){this.remoteDescription=d;this.signalingState=d.type==='offer'?'have-remote-offer':'stable';}
 async addIceCandidate(c){this.added.push(c);}
 close(){this.connectionState='closed';this.onconnectionstatechange?.();this.dc?.onclose?.();}
 async getStats(){return new Map();}
}
const context={console,crypto:require('node:crypto').webcrypto,RTCPeerConnection:PC,document:{addEventListener(){}},SpectraWorkspaceTranslations:{},setTimeout:(f,ms)=>{const id=++serial;timers.set(id,{f,ms});return id;},clearTimeout:id=>timers.delete(id),api:async()=>null,$:()=>null,$$:()=>[],run:f=>Promise.resolve().then(f).catch(()=>{}),preview:false,structuredClone};
vm.createContext(context);
const injected=source.replace('return{tick,message,accountState,',`return{setup(){scope='scope';owner='${me}';vault={connected:true,pins:{},iceServers:[],turnAutomatic:false};party={id:'${'3'.repeat(32)}',members:[{id:'${other}',online:true}]};trusted=async()=>'';sendTo=async(id,body)=>{capture(id,body);};},auto(){vault.turnAutomatic=true;iceData=null;},expire(){iceData.expiresAt=Date.now()/1000+40;},validIce,resolveIce,refreshIce,makePeer,signal,mesh,drop,getPeer:id=>peers.get(id),tick,message,accountState,`);
context.capture=(id,body)=>sent.push({id,body});vm.runInContext(injected,context);const social=vm.runInContext('SpectraSocial',context);
(async()=>{social.setup();const peer=await social.makePeer(other,true);assert.equal(sent[0].body.description.type,'offer','SDP is sent without waiting for all ICE');assert.equal(timers.size,1);
 const candidate={candidate:'candidate:late',sdpMid:'0',sdpMLineIndex:0};peer.pc.onicecandidate({candidate:{toJSON:()=>candidate}});await peer.sending;assert.equal(sent.at(-1).body.candidate.candidate,'candidate:late','late candidate forwarded');
 await social.signal(other,{candidate,session:peer.session});assert.equal(peer.remoteCandidates.length,1,'remote candidate waits for answer');
 await social.signal(other,{description:{type:'answer',sdp:'remote answer'},session:'f'.repeat(32)});assert.equal(peer.pc.remoteDescription,null,'stale answer ignored');
 await social.signal(other,{description:{type:'answer',sdp:'remote answer'},session:peer.session});assert.equal(peer.pc.added.length,1,'queued candidate applied after answer');
 await social.signal(other,{candidate,session:'f'.repeat(32)});assert.equal(peer.pc.added.length,1,'stale ICE ignored');
 const timeout=timers.get(peer.deadline);timeout.f();assert.equal(peer.status,'failed');assert(peer.error.includes('TURN'));const count=sent.length;await social.mesh();assert.equal(sent.length,count,'failed peer is not endlessly recreated');
 social.drop(other);assert.equal(timers.size,0,'disconnect clears deadline');const replacement=await social.makePeer(other,true);assert.notEqual(replacement.session,peer.session,'retry uses a fresh session');replacement.pc.connectionState='connected';replacement.pc.onconnectionstatechange();assert.equal(timers.size,0,'connected peer clears timeout');social.drop(other);
 // Answerer accepts a low-ID offer and trickles its candidates too.
 social.setup();const higherSource=injected.replace(`owner='${me}'`,`owner='${other}'`);const ctx2={...context,capture:(id,body)=>sent.push({id,body})};vm.createContext(ctx2);vm.runInContext(higherSource,ctx2);const answerer=vm.runInContext('SpectraSocial',ctx2);answerer.setup();await answerer.signal(me,{description:{type:'offer',sdp:'incoming'},session:'a'.repeat(32)});assert.equal(sent.at(-1).body.description.type,'answer');const answerPeer=answerer.getPeer(me);await answerer.signal(me,{candidate,session:answerPeer.session});assert.equal(answerPeer.pc.added.length,1);await answerer.signal(me,{description:{type:'offer',sdp:'ice restart'},session:answerPeer.session});assert.equal(answerer.getPeer(me),answerPeer,'same-session ICE restart preserves peer');answerer.drop(me);
 social.setup();social.auto();let issued=0;context.api=async(action,data)=>{if(action==='socialCall'&&data.path==='/social/ice'){issued++;return{enabled:true,expiresAt:Date.now()/1000+3600,iceServers:[{urls:['turn:turn.cloudflare.com:3478'],username:'user'+issued,credential:'temporary'}]};}return null;};
 await social.resolveIce();await social.resolveIce();assert.equal(issued,1,'automatic TURN cached');const autoPeer=await social.makePeer(other,true);await social.signal(other,{description:{type:'answer',sdp:'answer'},session:autoPeer.session});social.expire();await social.refreshIce();assert.equal(issued,2,'temporary TURN refreshed');assert.equal(autoPeer.pc.configuration.iceServers[0].username,'user2');assert.equal(social.getPeer(other),autoPeer,'rotation preserves peer');assert.throws(()=>social.validIce([{urls:'turn:turn.cloudflare.com:3478'}]),/username/);social.drop(other);
 console.log('PASS: automatic TURN cache/refresh and validation; trickle ICE, queued candidates, stale-session rejection, offer/answer, finite timeout and explicit retry');
})().catch(e=>{console.error(e);process.exitCode=1});
