@echo off
rem Build Android. Extra args are passed through, e.g. build_android.bat -Profile dev  or  -NoUpload
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
pause
