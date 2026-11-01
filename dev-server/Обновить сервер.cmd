@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
call npx wrangler deploy
pause
