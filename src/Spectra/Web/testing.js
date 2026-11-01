'use strict';
const SpectraTesting=(()=>{
 const status=()=>state.testing||{enabled:false,buildChannel:'stable'};
 function resetUpdateOffer(){queuedSourceUpdate=null;clearTimeout(sourceUpdateOfferTimer);latestCommitLabel=null;const button=$('#footerUpdate');if(button){button.hidden=true;button.onclick=null;}}
 function refresh(){
  let badge=$('#testingChannelBadge');const value=status();
  if(!badge){badge=document.createElement('span');badge.id='testingChannelBadge';badge.className='testing-channel-badge';$('#installedVersion')?.parentElement?.append(badge);}
  badge.hidden=!value.enabled&&value.buildChannel!=='testing';badge.textContent=SpectraAppearance.text(value.buildChannel==='testing'?'ТЕСТОВАЯ СБОРКА':'РЕЖИМ ТЕСТИРОВАНИЯ');
 }
 function mount(){
  const section=$('#startupWindows')?.closest('.settings-section');if(!section)return;
  section.querySelector('#testingSettings')?.remove();const value=status(),host=document.createElement('div');host.id='testingSettings';host.className='testing-settings';
  host.innerHTML=value.enabled?`<div class="testing-access-status"><span>${pixelIcon('edit')} <span>Режим тестирования включён</span></span><button id="leaveTesting" type="button" class="text-button">Выйти из режима тестирования</button></div><small class="muted">Действует до <span id="testingExpiry" data-no-i18n></span></small><p class="muted">Обновления из Spectra-Testing</p>`:'<button id="enterTesting" type="button" class="text-button">Войти в режим тестирования</button>';
  section.append(host);
  $('#enterTesting')?.addEventListener('click',open);
  if(value.enabled){$('#testingExpiry').textContent=new Date(value.expires*1000).toLocaleDateString(SpectraAppearance.language());$('#leaveTesting').onclick=()=>run(async()=>{const button=$('#leaveTesting');button.disabled=true;try{updateState(await api('testingDisable'));resetUpdateOffer();mount();registerSourceUpdate(await api('bootstrap'));}finally{if(button.isConnected)button.disabled=false;}});}
 }
 function open(){
  modal(`${closeButton()}<div class="eyebrow">SPECTRA LABS</div><h2>Режим тестирования</h2><p>Введите ключ разработчика для доступа к тестовому каналу.</p><form id="testingKeyForm" autocomplete="off"><label>Ключ разработчика<input id="testingKey" type="password" maxlength="8192" autocomplete="off" spellcheck="false" required></label><p id="testingKeyError" role="alert" hidden></p><div class="form-actions"><button type="submit" class="primary">Войти в режим тестирования</button></div></form>`);
  $('#testingKey').focus();$('#testingKeyForm').onsubmit=async event=>{
   event.preventDefault();const form=event.currentTarget,input=form.querySelector('input'),button=form.querySelector('button'),error=form.querySelector('[role=alert]');button.disabled=true;error.hidden=true;
   try{const next=await api('testingActivate',{key:input.value.trim()});input.value='';updateState(next);resetUpdateOffer();$('#modal').close();mount();toast('Режим тестирования включён');registerSourceUpdate(await api('bootstrap'));}
   catch(failure){if(form.isConnected){error.textContent=SpectraAppearance.text(failure.message);error.hidden=false;input.focus();}}
   finally{if(button.isConnected)button.disabled=false;}
  };
 }
 const settings=renderSettings;renderSettings=()=>{settings();mount();};
 const update=updateState;updateState=value=>{update(value);refresh();};
 refresh();return{refresh,mount};
})();
