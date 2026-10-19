@echo off
rem Build an APK for device testing (never uploaded). e.g. build_android_apk.bat -Profile dev
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Format apk %*
pause
