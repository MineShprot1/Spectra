'use strict';
const SpectraAvatars=(()=>{
 const cache=new Map();
 function own(){return{minecraftId:state.profile?.id,name:state.profile?.name,skin:skinUrl(state.profile)};}
 function paint(host,player={}){
  if(!host)return;
  const value={minecraftId:player.minecraftId||'',name:player.minecraftName||player.name||'',skin:player.skin||player.skinUrl||''};
  const key=JSON.stringify(value),now=Date.now();if(host.dataset.avatarKey===key&&now-Number(host.dataset.avatarAt||0)<300000)return;
  host.dataset.avatarAt=String(now);
  host.dataset.avatarKey=key;host.dataset.skin='';host.textContent=(value.name||'?')[0];
  if(!cache.has(key)||cache.get(key).expires<=now){if(cache.size>=128)cache.delete(cache.keys().next().value);cache.set(key,{expires:now+300000,task:api('playerAvatar',value).catch(()=>'' )});}
  const entry=cache.get(key);entry.task.then(data=>{if(!host.isConnected||host.dataset.avatarKey!==key)return;if(!/^data:image\/png;base64,[a-z0-9+/]+=*$/i.test(data||'')){entry.expires=Date.now()+30000;host.dataset.avatarAt=String(Date.now()-270000);return;}const img=document.createElement('img');img.alt=value.name;img.src=data;img.draggable=false;host.replaceChildren(img);});
 }
 function markup(player){return `<span class="player-avatar" data-player-avatar="${esc(JSON.stringify(player))}">${esc((player.minecraftName||player.name||'?')[0])}</span>`;}
 function hydrate(root=document){for(const host of root.querySelectorAll('[data-player-avatar]')){try{paint(host,JSON.parse(host.dataset.playerAvatar));}catch{}}}
 return{own,paint,markup,hydrate};
})();
