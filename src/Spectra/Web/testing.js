'use strict';
const SpectraTesting=(()=>{
 const status=()=>state.testing||{enabled:false,registered:false,buildChannel:'stable'};
 function resetUpdateOffer(){queuedSourceUpdate=null;clearTimeout(sourceUpdateOfferTimer);latestCommitLabel=null;const button=$('#footerUpdate');if(button){button.hidden=true;button.onclick=null;}}
 function refresh(){let badge=$('#testingChannelBadge');const value=status();if(!badge){badge=document.createElement('span');badge.id='testingChannelBadge';badge.className='testing-channel-badge';$('#installedVersion')?.parentElement?.append(badge);}badge.hidden=!value.enabled&&value.buildChannel!=='testing';badge.textContent=SpectraAppearance.text(value.buildChannel==='testing'?'ТЕСТОВАЯ СБОРКА':'Переход в тестовую версию');}
 async function installSelected(channel){resetUpdateOffer();const update=await api('bootstrap');if(!update?.checked||!update.available||update.channel!==channel)throw Error(update?.update||'Не удалось получить выбранную версию');await updateFromCommit(update.commit,channel);}
 async function enter(key=''){updateState(await api('testingActivate',{key}));await installSelected('testing');}
 async function leave(){updateState(await api('testingDisable'));await installSelected('stable');}
 function mount(){
  const root=$('.settings-page')||$('#main>section');if(!root)return;$('#testingSettings')?.remove();const value=status(),host=document.createElement('section');host.id='testingSettings';host.className='panel testing-settings';
  host.innerHTML=value.buildChannel==='testing'?'<div><strong>Тестовая версия лаунчера</strong><p class="muted">Доступ сохранён на этом компьютере.</p></div><button id="leaveTesting" type="button">Вернуться к обычной версии</button>':`<div><strong>Режим тестирования</strong><p class="muted">Доступ по одноразовому ключу разработчика.</p></div><button id="enterTesting" type="button">${value.registered?'Перейти в тестовую версию':'Войти в режим тестирования'}</button>${value.registered?'<button id="replaceTestingKey" type="button" class="text-button">Ключ разработчика</button>':''}`;
  const heading=root.querySelector('.heading');if(heading)heading.after(host);else root.prepend(host);
  $('#replaceTestingKey')?.addEventListener('click',open);
  $('#enterTesting')?.addEventListener('click',()=>{if(!status().registered){open();return;}run(async()=>{const button=$('#enterTesting');button.disabled=true;try{await enter();}catch(error){openShell();mount();throw error;}});});
  $('#leaveTesting')?.addEventListener('click',()=>run(async()=>{const button=$('#leaveTesting');button.disabled=true;try{await leave();}catch(error){gate(error.message);throw error;}}));
 }
 function open(){
  if(status().buildChannel==='testing')return;
  modal(`${closeButton()}<div class="eyebrow">SPECTRA LABS</div><h2>Режим тестирования</h2><p>Введите одноразовый ключ разработчика.</p><form id="testingKeyForm" autocomplete="off"><label>Ключ разработчика<input id="testingKey" type="password" maxlength="8192" autocomplete="off" spellcheck="false" required></label><p id="testingKeyError" role="alert" hidden></p><div class="form-actions"><button type="submit" class="primary">Перейти в тестовую версию</button></div></form>`);
  $('#testingKey').focus();$('#testingKeyForm').onsubmit=async event=>{event.preventDefault();const form=event.currentTarget,input=form.querySelector('input'),button=form.querySelector('button'),error=form.querySelector('[role=alert]');button.disabled=true;error.hidden=true;
   try{updateState(await api('testingActivate',{key:input.value.trim()}));input.value='';$('#modal').close();await installSelected('testing');}
   catch(failure){if($('#modal').open&&form.isConnected){error.textContent=SpectraAppearance.text(failure.message);error.hidden=false;input.focus();}else{openShell();mount();toast(failure.message);}}
   finally{if(button.isConnected)button.disabled=false;}
  };
 }
 function gate(message){updateStage('Доступ к тестовой версии',message||'Нет сохранённого доступа к тестовой версии');$('#updateTrack').classList.remove('indeterminate');let actions=$('#updateActions');if(!actions){actions=document.createElement('div');actions.id='updateActions';actions.className='update-actions';($('#updateScreen .entry-content')||$('#updateScreen')).append(actions);}actions.hidden=false;actions.innerHTML='<button id="retryTestingAccess" class="primary">Повторить проверку</button><button id="returnStable">Вернуться к обычной версии</button>';$('#retryTestingAccess').onclick=()=>run(async()=>{if(await authorizeBoot())await checkStartupUpdates();});$('#returnStable').onclick=()=>run(async()=>{try{await leave();}catch(error){gate(error.message);}});}
 async function authorizeBoot(){if(status().buildChannel!=='testing')return true;try{updateState(await api('testingSession'));if(status().enabled&&status().verified)return true;gate(status().error);}catch(error){gate(error.message);}return false;}
 const settings=renderSettings;renderSettings=()=>{settings();mount();};const update=updateState;updateState=value=>{update(value);refresh();};refresh();return{refresh,mount,authorizeBoot};
})();
