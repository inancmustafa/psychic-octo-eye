@echo off
rem Usage Notch: derleme, kurulum, Claude girisi ve baslatma tek adimda.
rem Giris reddedilince widget bunu "oto" ile kendisi acar; elle calistirmak da yeterli.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Baslat.ps1"
rem oto: basariliysa pencere kendiliginden kapanir, hata varsa okunabilsin diye bekler.
if /i "%~1"=="oto" if not errorlevel 1 exit /b 0
echo.
pause
