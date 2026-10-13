@echo off
rem LandmarkEmu host: first run sets everything up, later runs start the servers and the game. Settings: server.ini. When hosting it writes "Play LandmarkEmu.bat": the one file to send players.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
if errorlevel 1 pause
