@echo off
rem LandmarkEmu: first run sets everything up (asks for your Landmark folder), later runs start the servers and the game.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
if errorlevel 1 pause
