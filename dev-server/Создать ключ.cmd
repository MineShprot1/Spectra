@echo off
chcp 65001 >nul
setlocal
set "DEV_PY=%LOCALAPPDATA%\SpectraDeveloper\python"
if not exist "%DEV_PY%\Scripts\python.exe" py -m venv "%DEV_PY%"
if errorlevel 1 goto failed
"%DEV_PY%\Scripts\python.exe" -m pip show cryptography >nul 2>&1
if errorlevel 1 "%DEV_PY%\Scripts\python.exe" -m pip install -r "%~dp0requirements.txt"
if errorlevel 1 goto failed
"%DEV_PY%\Scripts\python.exe" "%~dp0testing_keys.py"
if errorlevel 1 goto failed
pause
exit /b 0
:failed
echo Не удалось создать ключ. Подробности указаны выше.
pause
exit /b 1
