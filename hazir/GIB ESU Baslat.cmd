@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -WindowStyle Hidden -Command "Start-Process -FilePath '%~dp0GibEsuGonderim.exe' -WorkingDirectory '%~dp0'"

