'use strict';
// CSS 3D cuboids: render only when a texture loads or the user rotates the model.
const skinTextures=new Map();
async function readSkin(url,cape=false){
 const cacheKey=(cape?'cape:':'skin:')+url;
 if(!/^(https:\/\/|data:image\/png;base64,)/.test(url||''))throw new Error('Текстура скина недоступна');
 if(skinTextures.has(cacheKey))return skinTextures.get(cacheKey);
 const task=new Promise((resolve,reject)=>{const img=new Image();img.crossOrigin='anonymous';img.onload=()=>{if(cape?!(img.width>=64&&img.width<=256&&img.width===img.height*2):img.width!==64||![32,64].includes(img.height))return reject(new Error('Нужна текстура 64×64 или 64×32'));resolve(img);};img.onerror=()=>reject(new Error('Не удалось загрузить текстуру скина'));if(url.startsWith('https://textures.minecraft.net/'))api('skinTexture',{url}).then(data=>img.src=data).catch(reject);else img.src=url;});if(skinTextures.size>=64)skinTextures.delete(skinTextures.keys().next().value);skinTextures.set(cacheKey,task);try{return await task;}catch(e){skinTextures.delete(cacheKey);throw e;}
}
function skinPatch(img,x,y,w,h){const c=document.createElement('canvas');c.width=w;c.height=h;c.getContext('2d').drawImage(img,x,y,w,h,0,0,w,h);return c.toDataURL('image/png');}
async function paintFace(host,url){if(!host||!url)return;host.dataset.skin=url;try{const img=await readSkin(url);if(!host.isConnected||host.dataset.skin!==url)return;const c=document.createElement('canvas');c.width=c.height=8;const ctx=c.getContext('2d');ctx.drawImage(img,8,8,8,8,0,0,8,8);ctx.drawImage(img,40,8,8,8,0,0,8,8);c.className='skin-face';c.setAttribute('aria-label','Лицо скина');host.replaceChildren(c);}catch{host.title='Текстура скина недоступна';}}
async function mountSkin(host,url,variant='classic',capeUrl=''){
 if(!host)return;const request=Symbol();host.skinRenderRequest=request;host.dataset.skin=url;host.textContent='Загрузка скина…';
 try{const img=await readSkin(url);if(!host.isConnected||host.dataset.skin!==url||host.skinRenderRequest!==request)return;host.replaceChildren();const rig=document.createElement('div');rig.className='skin-rig';host.append(rig);const unit=7,slim=String(variant).toLowerCase()==='slim',arm=slim?3:4;
 function box(w,h,d,x,y,z,u,v,overlay=false){
 const inflation=overlay?(w===8&&h===8?.5:.25):0,gw=w+inflation*2,gh=h+inflation*2,gd=d+inflation*2;
 const obj=document.createElement('div');obj.className='skin-box';obj.style.width=gw*unit+'px';obj.style.height=gh*unit+'px';obj.style.transform=`translate3d(${(x-inflation)*unit}px,${(y-inflation)*unit}px,${z*unit}px)`;rig.append(obj);
 const faces=[['front',u+d,v+d,w,h,gw,gh,`translateZ(${gd*unit/2}px)`],['back',u+d+w+d,v+d,w,h,gw,gh,`rotateY(180deg) translateZ(${gd*unit/2}px)`],['right',u,v+d,d,h,gd,gh,`rotateY(-90deg) translateZ(${gw*unit/2}px)`],['left',u+d+w,v+d,d,h,gd,gh,`rotateY(90deg) translateZ(${gw*unit/2}px)`],['top',u+d,v,w,d,gw,gd,`rotateX(90deg) translateZ(${gh*unit/2}px)`],['bottom',u+d+w,v,w,d,gw,gd,`rotateX(-90deg) translateZ(${gh*unit/2}px)`]];
 for(const [name,tx,ty,tw,th,fw,fh,transform] of faces){const face=document.createElement('div');face.className='skin-plane '+name;face.style.width=fw*unit+'px';face.style.height=fh*unit+'px';face.style.left=(gw-fw)*unit/2+'px';face.style.top=(gh-fh)*unit/2+'px';face.style.transform=transform;face.style.backgroundImage=`url("${skinPatch(img,tx,ty,tw,th)}")`;obj.append(face);}}

 box(8,8,8,-4,-16,0,0,0);box(8,8,8,-4,-16,0,32,0,true);box(8,12,4,-4,-8,0,16,16);box(arm,12,4,-4-arm,-8,0,40,16);box(4,12,4,-4,4,0,0,16);
 if(img.height===64){box(arm,12,4,4,-8,0,32,48);box(4,12,4,0,4,0,16,48);box(8,12,4,-4,-8,0,16,32,true);box(arm,12,4,-4-arm,-8,0,40,32,true);box(arm,12,4,4,-8,0,48,48,true);box(4,12,4,-4,4,0,0,32,true);box(4,12,4,0,4,0,0,48,true);}else{box(arm,12,4,4,-8,0,40,16);box(4,12,4,0,4,0,0,16);}
 if(capeUrl)attachCape(rig,capeUrl,()=>host.isConnected&&host.dataset.skin===url);
 let yaw=-25,pitch=-8,point=null;const rotate=()=>rig.style.transform=`rotateX(${pitch}deg) rotateY(${yaw}deg)`;rotate();host.tabIndex=0;host.setAttribute('aria-label','3D скин. Стрелки или перетаскивание для вращения');host.onpointerdown=e=>{if(e.button!==0)return;e.preventDefault();host.focus();point=[e.clientX,e.clientY];try{host.setPointerCapture(e.pointerId);}catch{point=null;}};host.onpointermove=e=>{if(!point)return;yaw+=(e.clientX-point[0])*.6;pitch=Math.max(-65,Math.min(65,pitch-(e.clientY-point[1])*.4));point=[e.clientX,e.clientY];rotate();};host.onpointerup=host.onpointercancel=host.onlostpointercapture=()=>point=null;host.onkeydown=e=>{if(!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key))return;e.preventDefault();yaw+=e.key==='ArrowLeft'?-10:e.key==='ArrowRight'?10:0;pitch=Math.max(-65,Math.min(65,pitch+(e.key==='ArrowUp'?-5:e.key==='ArrowDown'?5:0)));rotate();};
 }catch(e){if(host.isConnected&&host.dataset.skin===url)host.textContent=e.message;}
}

async function paintSkinPair(host,url,variant='classic'){
 if(!host)return;host.dataset.skin=url;
 try{const img=await readSkin(url);if(!host.isConnected||host.dataset.skin!==url)return;const c=document.createElement('canvas');c.width=36;c.height=32;const ctx=c.getContext('2d');ctx.imageSmoothingEnabled=false;const arm=String(variant).toLowerCase()==='slim'?3:4;
 function part(x,y,w,h,u,v,layerU,layerV,back){const tx=u+4+(back?w+4:0);ctx.drawImage(img,tx,v+4,w,h,x,y,w,h);if(img.height===64||h===8)ctx.drawImage(img,layerU+4+(back?w+4:0),layerV+4,w,h,x,y,w,h);}
 for(let side=0;side<2;side++){const off=side*20,back=!!side;ctx.drawImage(img,back?24:8,8,8,8,off+4,0,8,8);ctx.drawImage(img,back?56:40,8,8,8,off+4,0,8,8);part(off+4,8,8,12,16,16,16,32,back);part(off+4-arm,8,arm,12,back&&img.height===64?32:40,back&&img.height===64?48:16,back?48:40,back?48:32,back);part(off+12,8,arm,12,!back&&img.height===64?32:40,!back&&img.height===64?48:16,!back?48:40,!back?48:32,back);part(off+4,20,4,12,back&&img.height===64?16:0,back&&img.height===64?48:16,0,back?48:32,back);part(off+8,20,4,12,!back&&img.height===64?16:0,!back&&img.height===64?48:16,0,!back?48:32,back);}
 c.className='skin-pair';c.setAttribute('aria-label','Скин спереди и сзади');host.replaceChildren(c);
 }catch(e){if(host.isConnected)host.textContent='Превью недоступно';}
}

async function paintCape(host,url){
 if(!host||!url)return;host.dataset.cape=url;
 try{const img=await readSkin(url,true);if(!host.isConnected||host.dataset.cape!==url)return;const c=document.createElement('canvas');c.width=23;c.height=16;const ctx=c.getContext('2d'),scale=img.width/64;ctx.drawImage(img,scale,scale,10*scale,16*scale,0,0,10,16);ctx.drawImage(img,12*scale,scale,10*scale,16*scale,13,0,10,16);c.className='cape-pair';c.setAttribute('aria-label','Плащ с внешней и внутренней стороны');host.replaceChildren(c);}catch{host.textContent='Превью недоступно';}
}
async function attachCape(rig,url,isCurrent){
 try{const img=await readSkin(url,true);if(!isCurrent())return;const scale=img.width/64;const obj=document.createElement('div');obj.className='skin-cape';obj.style.transform='translate3d(-35px,-56px,-21px) rotateX(-8deg)';
 const faces=[['rear',1,1,10,16,70,112,'rotateY(180deg) translateZ(3.5px)',0,0],['inside',12,1,10,16,70,112,'translateZ(3.5px)',0,0],['edge-r',0,1,1,16,7,112,'rotateY(-90deg) translateZ(35px)',31.5,0],['edge-l',11,1,1,16,7,112,'rotateY(90deg) translateZ(35px)',31.5,0],['top',1,0,10,1,70,7,'rotateX(90deg) translateZ(56px)',0,52.5],['bottom',11,0,10,1,70,7,'rotateX(-90deg) translateZ(56px)',0,52.5]];
 for(const [,x,y,w,h,pw,ph,transform,left,top] of faces){const c=document.createElement('canvas');c.width=w*scale;c.height=h*scale;c.getContext('2d').drawImage(img,x*scale,y*scale,w*scale,h*scale,0,0,c.width,c.height);c.className='skin-plane';Object.assign(c.style,{width:pw+'px',height:ph+'px',left:left+'px',top:top+'px',transform});obj.append(c);}rig.append(obj);
 }catch{/* Keep the player visible if the cape texture cannot load. */}
}

function lazySkinPair(host,url,variant){
 if(!host)return;if(!window.IntersectionObserver){paintSkinPair(host,url,variant);return;}
 const observer=new IntersectionObserver(entries=>{if(entries.some(e=>e.isIntersecting)){observer.disconnect();paintSkinPair(host,url,variant);}},{root:document.querySelector('#main'),rootMargin:'100px'});observer.observe(host);
}
