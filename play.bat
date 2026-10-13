@echo off
rem LandmarkEmu: play on a server someone else runs (first run asks for your Landmark folder, the server address and an account).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0play.ps1" %*
if errorlevel 1 pause
