@echo off
chcp 65001 >nul
title Nova - Web Chat
cd /d "%~dp0AIGirlfriend"

echo   Closing any previous instance...
taskkill /IM AIGirlfriend.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo.
echo   Starting Nova... (press Ctrl+C to stop)
echo.
dotnet run -- web

echo.
echo   Nova stopped.
pause
