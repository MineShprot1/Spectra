'use strict';
const SpectraContent=(()=>{
 let edition=localStorage.getItem('spectra.discoverEdition')||'java',kind='modpacks',ticket=0;
 const names={modpacks:'МОДПАКИ',skins:'СКИНЫ',worlds:'КАРТЫ',addons:'АДДОНЫ',resources:'ТЕКСТУР ПАКИ'};
 function categories(){return edition==='java'?['modpacks','worlds']:['addons','skins','worlds','resources'];}
 function editionControls(){return `<div class="edition-switch"><button data-content-edition="java" class="${edition==='java'?'active':''}">Java Edition</button><button data-content-edition="bedrock" class="${edition==='bedrock'?'active':''}">Bedrock Edition</button></div>`;}
 function categoryControl(){return `<select id="contentKind" aria-label="Категория контента">${categories().map(k=>`<option value="${k}" ${kind===k?'selected':''}>${names[k]}</option>`).join('')}</select>`;}
 function bindControls(){ $$('[data-content-edition]').forEach(b=>b.onclick=()=>{edition=b.dataset.contentEdition;localStorage.setItem('spectra.discoverEdition',edition);kind=categories()[0];return render();});$('#contentKind').onchange=()=>{kind=$('#contentKind').value;return render();};}
 async function render(){
  if(!categories().includes(kind))kind=categories()[0];const t=++ticket;
  if(kind==='modpacks'){
   const loading=renderModpacks();if(t!==ticket||page!=='discover')return;
   $('#main .hero').insertAdjacentHTML('beforebegin',editionControls());
   $('#catalogQuery').insertAdjacentHTML('afterend',categoryControl());bindControls();await loading;return;
  }
  $('#main').innerHTML=`<section>${heading('КОНТЕНТ СООБЩЕСТВА','Обзор')}${editionControls()}<div class="toolbar"><input id="contentQuery" placeholder="Поиск…">${categoryControl()}<button id="contentSearch">Найти</button><button id="contentImport">Импорт скачанного файла</button></div><div id="contentResults"></div><button id="contentMore" hidden>Показать ещё</button></section>`;bindControls();
  let offset=0;
  async function search(append=false){const request=++ticket;if(!append){offset=0;$('#contentResults').innerHTML=empty('Получаем каталог','Загружаем список…');}
   const e=edition,k=kind,source='curseforge',query=$('#contentQuery').value;
   try{const result=await api('contentSearch',{edition:e,kind:k,source,query,offset});if(request!==ticket||!$('#contentResults'))return;
    const cards=result.items.map(x=>`<article class="catalog-card"><div class="catalog-top"><div class="catalog-icon">${image(x.icon)||pixelIcon('cube')}</div><h3>${esc(x.title)}</h3></div><p>${esc(x.description||'')}</p><div class="catalog-bottom"><button data-content-link="${esc(x.url)}">Открыть сайт ↗</button><button data-content-download="${esc(x.id)}" data-url="${esc(x.url)}" class="primary">Скачать</button></div></article>`).join('');
    if(append)$('#contentResults .catalog-grid')?.insertAdjacentHTML('beforeend',cards);else $('#contentResults').innerHTML=(result.warning?`<div class="warning">${esc(result.warning)}</div>`:'')+`<div class="catalog-grid">${cards}</div>`;
    $$('[data-content-link]').forEach(b=>b.onclick=()=>run(()=>api('openLink',{url:b.dataset.contentLink})));
    $$('[data-content-download]').forEach(b=>b.onclick=()=>download({edition:e,kind:k,source,projectId:b.dataset.contentDownload,url:b.dataset.url}));
    $('#contentMore').hidden=!result.hasMore;offset+=24;
   }catch(error){if(request===ticket&&$('#contentResults'))$('#contentResults').innerHTML=empty('Не удалось загрузить каталог',esc(error.message));}
  }
  $('#contentSearch').onclick=()=>run(()=>search());$('#contentQuery').onkeydown=e=>{if(e.key==='Enter')run(()=>search());};$('#contentImport').onclick=()=>importFile();$('#contentMore').onclick=()=>run(()=>search(true));await run(()=>search());
 }
 async function targetDialog(action){
  await getVersions();modal(`${closeButton()}<h2>Куда установить карту?</h2><p>Выберите сборку или версию Minecraft. При необходимости версия сначала скачается.</p><label>Сборка или версия<select id="worldDestination">${state.instances.map(i=>`<option value="instance:${esc(i.id)}">${esc(i.name)} · ${esc(i.version)}</option>`).join('')}${versions.map(v=>`<option value="version:${esc(v.id)}">Minecraft ${esc(v.id)}</option>`).join('')}</select></label><div class="form-actions"><button id="worldCancel">Отмена</button><button id="worldInstall" class="primary">Установить карту</button></div>`);
  $('#worldCancel').onclick=()=>$('#modal').close();$('#worldInstall').onclick=()=>run(async()=>{const b=$('#worldInstall');b.disabled=true;const [type,id]=$('#worldDestination').value.split(':');try{const close=await action(type==='instance'?{instanceId:id}:{version:id});if(close!==false)$('#modal').close();}finally{if(b.isConnected)b.disabled=false;}});
 }
 async function perform(action,data){
  const result=await api(action,data);if(result?.cancelled)return false;
  if(result?.status==='manualDownload'){modal(`${closeButton()}<h2>Скачивание на сайте</h2><p>${esc(result.message)}</p><div class="form-actions"><button id="downloadOnSite" class="primary">Открыть страницу загрузки</button><button id="importAfterDownload">Импортировать файл</button></div>`);$('#downloadOnSite').onclick=()=>run(()=>api('openLink',{url:result.url}));$('#importAfterDownload').onclick=()=>{ $('#modal').close();importFile();};return false;}
  updateState(await api('state'));toast(result.message||'Контент установлен');return true;
 }
 function download(data){if(data.edition==='java'&&data.kind==='worlds')run(()=>targetDialog(target=>perform('contentDownload',{...data,...target})));else run(()=>perform('contentDownload',{...data,variant:'classic'}));}
 function importFile(){const data={edition,kind,variant:'classic'};if(edition==='java'&&kind==='worlds')run(()=>targetDialog(target=>perform('contentImport',{...data,...target})));else run(()=>perform('contentImport',data));}
 async function additions(){
  modal(`${closeButton()}<h2>Дополнения Bedrock</h2><p class="muted">Файлы открываются в Minecraft при запуске последней версии. В списке показаны файлы Spectra и дополнения, установленные в игре. Перед удалением установленного дополнения создаётся резервная копия.</p><div class="content-tabs">${['addons','worlds','resources'].map(k=>`<button data-addition-kind="${k}">${names[k]}</button>`).join('')}</div><div id="additionItems"></div>`);
  const items=await api('bedrockContent');if(!$('#additionItems'))return;
  function fill(k){$('#additionItems').innerHTML=items.filter(x=>x.kind===k).map(x=>`<div class="content-source"><span>${esc(x.name)}<small class="muted"> · ${x.installed?'Установлено в игре':'Файл Spectra для импорта'}</small></span><button data-delete-addition="${esc(x.id)}">Удалить</button></div>`).join('')||'<p class="muted">В этой категории пока пусто</p>';$$('[data-delete-addition]').forEach(b=>b.onclick=()=>confirmAction('Удалить дополнение?',items.find(x=>x.id===b.dataset.deleteAddition)?.name||'',async()=>{await api('bedrockContentDelete',{id:b.dataset.deleteAddition});setTimeout(()=>additions(),0);}));}
  $$('[data-addition-kind]').forEach(b=>b.onclick=()=>fill(b.dataset.additionKind));fill('addons');
 }
 function settings(host){host.insertAdjacentHTML('beforeend',`<div class="panel settings-section"><h2>Окно лаунчера</h2><button id="launcherFullscreen">${state.launcherFullscreen?'Выйти из полноэкранного режима':'Полноэкранный режим'} · F11</button></div><div class="panel settings-section"><h2>Скины Bedrock</h2><p id="skinDownloadFolder" data-no-i18n>${esc(state.bedrockSkinFolder||'Загрузки')}</p><button id="pickBedrockSkinFolder">Изменить папку загрузки</button></div>`);$('#launcherFullscreen').onclick=()=>run(async()=>{await api('window',{command:'fullscreen'});updateState(await api('state'));$('#launcherFullscreen').textContent=(state.launcherFullscreen?'Выйти из полноэкранного режима':'Полноэкранный режим')+' · F11';});$('#pickBedrockSkinFolder').onclick=()=>run(async()=>{updateState(await api('bedrockSkinFolder'));$('#skinDownloadFolder').textContent=state.bedrockSkinFolder;});}
 document.addEventListener('keydown',e=>{if(e.key==='F11'&&!e.repeat){e.preventDefault();api('window',{command:'fullscreen'}).catch(()=>{});}});
 return {render,settings,additions};
})();
