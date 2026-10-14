@echo off
rem Optional helper: sets up .env on the first run, then runs docker compose. Options: -Stop -Update -Logs -Setup. Settings: .env
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
if errorlevel 1 pause
