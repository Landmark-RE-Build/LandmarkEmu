@echo off
rem LandmarkEmu host: first run sets everything up, later runs start the servers and the game. Settings: server.ini. Players on other PCs use play.bat.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
if errorlevel 1 pause
