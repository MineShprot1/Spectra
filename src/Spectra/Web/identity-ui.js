'use strict';
const SpectraIdentity=(()=>{
 const menu=$('#accountMenu'),banner=menu.querySelector('.account-menu-identity');let lastKey='';
 const choices=document.createElement('div');choices.className='presence-choices';choices.innerHTML='<button type="button" id="accountOnline">В сети</button><button type="button" id="accountInvisible">Невидимый</button>';banner.after(choices);
 function refresh(){const value=SpectraPresence.own(state),name=state.profile?.name||'Войти';$('#account').dataset.presence=value.state;$('#account').title=name+' · '+SpectraAppearance.text(value.label)+(value.detail?' · '+SpectraAppearance.text(value.detail):'');const status=JSON.stringify([name,state.profile?.id,skinUrl(state.profile),value]);if(status!==lastKey){lastKey=status;banner.dataset.presence=value.state;banner.innerHTML=`<div class="account-menu-face"><img src="assets/default-face.svg" alt=""></div><div><strong ${state.profile?.name?'data-no-i18n':'data-i18n="Войти"'}>${esc(name)}</strong><span class="identity-status">${esc(value.label)}</span><small class="identity-activity">${esc(value.detail)}</small></div>`;SpectraAvatars.paint(banner.querySelector('.account-menu-face'),SpectraAvatars.own());}
  for(const [id,hidden] of [['accountOnline',false],['accountInvisible',true]]){const button=$('#'+id);button.disabled=!state.networkAccount?.id;button.setAttribute('aria-pressed',String(!!state.hideOnlineStatus===hidden));}
  const self=$('#friendsPanel .friends-self');if(self){SpectraAvatars.paint(self.querySelector('.friends-self-face'),SpectraAvatars.own());self.dataset.presence=value.state;self.querySelector('strong').textContent=name;self.querySelector('.identity-status').textContent=SpectraAppearance.text(value.label);self.querySelector('.identity-activity').textContent=SpectraAppearance.text(value.detail);}
 }
 for(const [id,hidden] of [['accountOnline',false],['accountInvisible',true]])$('#'+id).onclick=()=>run(async()=>{const button=$('#'+id);button.disabled=true;try{updateState(await api('socialVisibility',{hidden}));refresh();}finally{button.disabled=!state.networkAccount?.id;}});
 const open=$('#account').onclick;$('#account').onclick=e=>{open(e);lastKey='';refresh();};
 const shellRefresh=SpectraShell.refresh;SpectraShell.refresh=()=>{shellRefresh();refresh();};
 const receive=onMessage;onMessage=m=>{receive(m);if(['gameStatus','started','exited'].includes(m.type))refresh();if(m.type==='presenceState')updateState(m.state);};
 refresh();return{refresh};
})();
