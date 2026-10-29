'use strict';
const SpectraPresence=(()=>{
 function own(s){const playing=(s.running||[]).length>0||s.bedrockRunning;if(s.hideOnlineStatus)return{state:'invisible',label:'Невидимый',detail:'Активность скрыта от друзей'};if(playing)return{state:'playing',label:'В игре',detail:s.bedrockRunning?'Minecraft · Bedrock Edition':'Minecraft · Java Edition'};if(s.networkAccount?.id)return{state:'online',label:'В сети',detail:'Spectra Network'};return{state:'offline',label:'Не в сети',detail:s.profile?'Spectra Network не подключён':'Войдите в аккаунт'};}
 function friend(f){return f.online?f.presence?.playing?'playing':'online':'offline';}
 return{own,friend};
})();
