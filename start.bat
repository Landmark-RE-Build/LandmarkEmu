@echo off
rem LandmarkEmu servers in Docker: the first run sets everything up, later runs start them. Options: -Stop -Update -Logs -Setup. Network settings: network.env.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
if errorlevel 1 pause
