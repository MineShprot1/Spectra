'use strict';
/* Built-in interface views keep native cards and their event handlers intact. */
const SpectraInterfaceViews=(()=>{
 const main=document.querySelector('#main'),shell=document.querySelector('#shell');
 const selection=new Map(),history=[];let pending=false,lastMode='',serial=0,historyIndex=-1,travelling=false;
 const supported=new Set(['steam','minecraft','gradient','modern','windows','liquid','aero']);
 const text=value=>SpectraAppearance.text(value);
 const button=(label,action,icon,raw=false)=>{const b=document.createElement('button');b.type='button';b.innerHTML=(icon?pixelIcon(icon):'')+'<span'+(raw?' data-no-i18n':'')+'>'+esc(raw?label:text(label))+'</span>';b.onclick=action;return b;};
 function mode(){const theme=document.body.dataset.theme;return supported.has(theme)?document.body.dataset.interfaceView||'normal':'normal';}
 function navigation(){
  const nav=document.querySelector('#navigation');let tabs=nav.querySelector('.reference-tabs');
  if(mode()!=='custom'||document.body.dataset.customTheme||document.body.dataset.theme==='minecraft'){tabs?.remove();return;}
  if(!tabs){tabs=document.createElement('div');tabs.className='reference-tabs';nav.append(tabs);}
  const minecraft=document.body.dataset.theme==='minecraft',edition=localStorage.getItem('spectra.versionEdition')||'java';
  const stamp=[minecraft,page,editorId,edition,SpectraAppearance.language(),state?.profile?.name,historyIndex].join(':');if(tabs.dataset.stamp===stamp)return;tabs.dataset.stamp=stamp;tabs.replaceChildren();
  if(!minecraft){for(const [offset,label] of [[-1,'Назад'],[1,'Вперёд']]){const b=button(label,async()=>{const next=historyIndex+offset;if(next<0||next>=history.length)return;travelling=true;try{await navigate(history[next]);if(page===history[next])historyIndex=next;}finally{travelling=false;schedule();}},'arrow');b.className=offset<0?'reference-back':'reference-forward';b.title=text(label);b.setAttribute('aria-label',text(label));b.disabled=historyIndex+offset<0||historyIndex+offset>=history.length;tabs.append(b);}}
  if(minecraft){const label=document.createElement('strong');label.className='reference-edition';label.textContent='MINECRAFT: '+(edition==='java'?'JAVA EDITION':'BEDROCK EDITION');tabs.append(label);}
  const entries=[['Версии','versions'],['Сборки','instances'],['Обзор','discover'],[state?.profile?.name||'Профиль','profile']];
  for(const [label,target] of entries){const b=button(label,()=>navigate(target),null,target==='profile'&&!!state?.profile?.name);b.classList.toggle('active',target===page);tabs.append(b);}
  const settings=button('Настройки',()=>navigate('settings'),'settings');settings.className='reference-settings';settings.setAttribute('aria-label',text('Настройки'));settings.title=text('Настройки');tabs.append(settings);const editor=button('Редактор интерфейса',()=>navigate('themeStudio'),'edit');editor.className='reference-editor';editor.title=text('Редактор интерфейса');editor.setAttribute('aria-label',text('Редактор интерфейса'));tabs.append(editor);
 }
 function sidebar(){
  let side=document.querySelector('#minecraftNavigation');
  if(mode()!=='custom'||document.body.dataset.theme!=='minecraft'||document.body.dataset.customTheme){side?.remove();return;}
  if(!side){side=document.createElement('aside');side.id='minecraftNavigation';side.setAttribute('aria-label',text('Навигация'));shell.append(side);}
  const name=state?.profile?.name||text('Профиль');
  const stamp=name+page+(localStorage.getItem('spectra.versionEdition')||'java')+(state?.profile?skinUrl(state.profile):'');
  if(side.dataset.stamp===stamp)return;side.dataset.stamp=stamp;
  side.replaceChildren();const account=button(name,()=>navigate('profile'),'profile',true);account.className='mc-account';
  const face=document.createElement('span');face.className='avatar';face.textContent=name[0];account.querySelector('.pixel-icon')?.replaceWith(face);side.append(account);if(state?.profile)paintFace(face,skinUrl(state.profile));
  for(const [label,target,icon,edition] of [['Обзор','discover','home'],['Java Edition','versions','cube','java'],['Bedrock Edition','versions','terrain','bedrock'],['Сборки','instances','versions'],['Профиль','profile','profile'],['Настройки','settings','settings']]){
   const b=button(label,()=>{if(edition)localStorage.setItem('spectra.versionEdition',edition);navigate(target);},icon);b.classList.toggle('active',page===target&&(!edition||(localStorage.getItem('spectra.versionEdition')||'java')===edition));if(edition){b.dataset.railEdition=edition;const image=document.createElement('img');image.className='edition-icon';image.src='assets/edition-'+edition+'.svg';image.alt='';b.querySelector('.pixel-icon').replaceWith(image);b.querySelector('span:last-child').textContent='MINECRAFT:\n'+edition.toUpperCase()+' EDITION';}if(target==='settings')b.classList.add('mc-settings');side.append(b);
  }
  const editor=button('Редактор интерфейса',()=>navigate('themeStudio'),'edit');editor.className='mc-editor';side.append(editor);
 }
 function restore(){const layout=main.querySelector('.interface-library');if(!layout)return;for(const [marker,node] of layout._moved||[])if(marker.isConnected)marker.replaceWith(node);if(layout._searchMarker?.isConnected)layout._searchMarker.replaceWith(layout.querySelector('.interface-index input'));const content=layout.querySelector('.interface-native');while(content?.firstChild)layout.before(content.firstChild);layout.remove();main.querySelectorAll('.interface-selected').forEach(el=>el.classList.remove('interface-selected'));for(const list of main.querySelectorAll('[data-interface-native-view]')){if(list.dataset.interfaceNativeView)list.classList.add(...list.dataset.interfaceNativeView.split(' '));delete list.dataset.interfaceNativeView;}main.querySelectorAll('.reference-page-tools:empty').forEach(node=>node.remove());}
 function refresh(){
  pending=false;sidebar();navigation();const custom=mode()==='custom'&&!document.body.dataset.customTheme;const signature=document.body.dataset.theme+':'+mode()+':'+!!document.body.dataset.customTheme;
  if(signature!==lastMode){lastMode=signature;restore();}
  document.body.classList.toggle('builtin-custom-view',custom);
  if(!custom||!['instances','versions'].includes(page)||editorId){restore();return;}
  const cards=[...main.querySelectorAll('.instance-card,.version-card')];if(!cards.length){const current=main.querySelector('.interface-library');if(current&&!current.querySelector('.reference-empty')){current._cards=[];current.querySelector('.interface-items').replaceChildren();current.querySelector('.reference-links').replaceChildren();current.querySelector('.reference-information').replaceChildren();const message=document.createElement('p');message.className='reference-empty';message.setAttribute('role','status');message.textContent=text('Версия не найдена');current.querySelector('.interface-native').append(message);}return;}
  let layout=main.querySelector('.interface-library');
  layout?.querySelector('.reference-empty')?.remove();
  const identity=cards.map(card=>card.dataset.id||card.dataset.version||card.dataset.bedrockSelect||card.textContent).join('|');
  if(layout&&layout._cards?.length===cards.length&&layout._cards.every((card,i)=>card===cards[i]))return;
  if(!layout){
   const section=main.querySelector('section');if(!section)return;
   const sources=[...section.children].filter(el=>el.matches('.instance-list,#versionList,#bedrockList'));
   if(!sources.length)return;
   layout=document.createElement('div');layout.className='interface-library';layout.innerHTML='<div class="interface-detail"><div class="interface-cover"><div class="interface-cover-art"></div><div class="interface-cover-copy"><small>MINECRAFT</small><h2 data-no-i18n></h2></div></div><div class="interface-native"></div></div><aside class="interface-index"><header><strong></strong></header><input type="search"><div class="interface-items"></div></aside>';
   sources[0].before(layout);layout.querySelector('.interface-native').append(...sources);
   layout.querySelector('.interface-index strong').textContent=text(page==='instances'?'Моя библиотека':'Версии');
   const search=layout.querySelector('input'),nativeSearch=main.querySelector('#versionSearch,#bedrockSearch');
   if(nativeSearch){layout._searchMarker=document.createComment('native library search');nativeSearch.before(layout._searchMarker);search.replaceWith(nativeSearch);}
   else{search.placeholder=text('Поиск сборки…');search.setAttribute('aria-label',search.placeholder);search.oninput=()=>{for(const row of layout.querySelectorAll('.interface-items>button'))row.hidden=!row.textContent.toLocaleLowerCase().includes(search.value.toLocaleLowerCase());};}
   const picker=button('Выбрать сборку',()=>{const index=layout.querySelector('.interface-index');index.classList.toggle('picker-open');picker.setAttribute('aria-expanded',String(index.classList.contains('picker-open')));});picker.className='interface-picker';picker.setAttribute('aria-expanded','false');picker.querySelector('span').setAttribute('data-no-i18n','');layout.querySelector('.interface-index').prepend(picker);
   const tools=document.createElement('div');tools.className='reference-page-tools';layout._moved=[];
   for(const node of [...section.children].filter(el=>el.matches('.heading,.toolbar'))){const marker=document.createComment('page tools');node.before(marker);layout._moved.push([marker,node]);tools.append(node);}
   if(document.body.dataset.theme==='steam')layout.querySelector('.interface-index header').after(tools);else section.prepend(tools);
   const links=document.createElement('div');links.className='reference-links';layout.querySelector('.interface-detail').append(links);
   const info=document.createElement('div');info.className='reference-information';layout.querySelector('.interface-detail').append(info);
  }
  for(const list of layout.querySelectorAll('.instance-list,.version-list')){if(list.dataset.interfaceNativeView===undefined){const classes=[...list.classList].filter(name=>name.startsWith('view-'));list.dataset.interfaceNativeView=classes.join(' ');list.classList.remove(...classes);}}
  layout._cards=cards;layout.dataset.identity=identity;const items=layout.querySelector('.interface-items');items.replaceChildren();
  const key=page+':'+(localStorage.getItem('spectra.versionEdition')||'java'),id=card=>card.dataset.id||card.dataset.version||card.dataset.bedrockSelect;
  function select(card){
   selection.set(key,id(card));for(const c of cards)c.classList.toggle('interface-selected',c===card);
   for(const b of items.children){const active=b.dataset.entry===id(card);b.classList.toggle('active',active);b.setAttribute('aria-pressed',String(active));}
   layout.querySelector('.interface-cover h2').textContent=card.querySelector('h3')?.textContent||id(card);
   layout.querySelector('.interface-picker span').textContent=card.querySelector('h3')?.textContent||id(card);
   const links=layout.querySelector('.reference-links'),info=layout.querySelector('.reference-information');links.replaceChildren();info.replaceChildren();
   if(card.dataset.id){const instance=state.instances.find(item=>item.id===card.dataset.id);for(const [label,tab] of [['Моды','mods'],['Ресурс паки','resources'],['Шейдеры','shaders'],['Скриншоты','screenshots']])links.append(button(label,()=>openEditor(card.dataset.id,tab)));if(instance){for(const [label,value] of [['Версия',instance.version],['Загрузчик',instance.loader||'Vanilla'],['Время в игре',playtimeText(playtimeFor('instance:'+instance.id))]]){const item=document.createElement('div');item.innerHTML='<small>'+esc(text(label))+'</small><strong data-no-i18n>'+esc(value)+'</strong>';info.append(item);}}const manage=document.createElement('details');manage.className='reference-manage';const summary=document.createElement('summary');summary.textContent=text('Настройки сборки');manage.append(summary);for(const action of card.querySelectorAll('.instance-utilities button'))manage.append(button(action.title,()=>action.click()));links.append(manage);}
   else{for(const action of card.querySelectorAll('[data-delete-version],[data-bedrock-download],[data-bedrock-delete]'))links.append(button(action.title||action.textContent,()=>action.click()));const item=document.createElement('div');item.innerHTML='<small>'+esc(text('Версия'))+'</small><strong data-no-i18n>'+esc(card.querySelector('h3')?.textContent||id(card))+'</strong>';info.append(item);}
   const editionLabel=layout.querySelector('.reference-cover-edition')||document.createElement('span');editionLabel.className='reference-cover-edition';editionLabel.textContent=(localStorage.getItem('spectra.versionEdition')||'java')==='java'?'JAVA EDITION':'BEDROCK EDITION';layout.querySelector('.interface-cover-copy').append(editionLabel);
   const art=layout.querySelector('.interface-cover-art');art.replaceChildren();const img=card.querySelector('.card-bg img,.version-art img'),hasArt=img&&!/\.svg(?:$|\?)/i.test(img.src);if(hasArt)art.append(img.cloneNode(true));art.parentElement.classList.toggle('has-cover-image',!!hasArt);
  }
  for(const card of cards){const row=button(card.querySelector('h3')?.textContent||id(card),()=>{select(card);layout.querySelector('.interface-index').classList.remove('picker-open');layout.querySelector('.interface-picker').setAttribute('aria-expanded','false');if(document.body.dataset.theme==='minecraft')main.scrollTop=0;},null,true);row.dataset.entry=id(card);const img=card.querySelector('.instance-icon img,.version-art img');if(img){const icon=img.cloneNode(true);icon.alt='';row.prepend(icon);}else row.insertAdjacentHTML('afterbegin',pixelIcon(page==='instances'?'cube':'versions'));items.append(row);}
  select(cards.find(card=>id(card)===selection.get(key))||cards[0]);serial++;
 }
 function schedule(){if(!pending){pending=true;requestAnimationFrame(refresh);}}
 new MutationObserver(schedule).observe(main,{childList:true,subtree:true});
 new MutationObserver(schedule).observe(document.body,{attributes:true,attributeFilter:['data-theme','data-interface-view','data-custom-theme']});
 function closePicker(){main.querySelector('.interface-index')?.classList.remove('picker-open');main.querySelector('.interface-picker')?.setAttribute('aria-expanded','false');}
 document.addEventListener('pointerdown',event=>{if(!event.target.closest('.interface-index'))closePicker();});
 document.addEventListener('keydown',event=>{if(event.key==='Escape')closePicker();});
 const originalNavigate=navigate;navigate=async target=>{const previous=page;if(historyIndex<0){history.push(previous);historyIndex=0;}const result=await originalNavigate(target);if(page!==previous){main.scrollTop=0;if(!travelling){history.splice(historyIndex+1);history.push(page);if(history.length>50)history.shift();historyIndex=history.length-1;}}schedule();return result;};
 const apply=SpectraAppearance.apply;SpectraAppearance.apply=c=>{apply(c);schedule();};
 schedule();return{refresh,schedule,get serial(){return serial;}};
})();
