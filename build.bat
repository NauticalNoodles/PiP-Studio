@echo off
setlocal
cd /d "%~dp0"
title Building PiP Studio

rem   build.bat           build and start PiP Studio (needs the .NET 8 Desktop Runtime to run)
rem   build.bat release   self-contained single exe in publish-release\, a portable zip and,
rem                       if Inno Setup 6 is installed, the installer - all in out\

where dotnet >nul 2>&1
if errorlevel 1 (
  echo The .NET 8 SDK was not found.
  echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run this again.
  pause
  exit /b 1
)

if /i "%~1"=="release" goto release

taskkill /im PiPStudio.exe /f >nul 2>&1

dotnet publish PiPStudio.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
if errorlevel 1 goto failed

echo.
echo Built publish\PiPStudio.exe - starting it now.
start "" "%~dp0publish\PiPStudio.exe"
exit /b 0

:release
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content PiPStudio.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set VER=%%v
echo Building PiP Studio %VER% for release...

dotnet publish PiPStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-release
if errorlevel 1 goto failed

if not exist out mkdir out
copy /y LICENSE publish-release\LICENSE.txt >nul
copy /y THIRD-PARTY-NOTICES.md publish-release\ >nul
powershell -NoProfile -Command "Compress-Archive -Path publish-release\PiPStudio.exe, publish-release\LICENSE.txt, publish-release\THIRD-PARTY-NOTICES.md -DestinationPath 'out\PiPStudio-%VER%-win-x64-portable.zip' -Force"
if errorlevel 1 goto failed
echo Made out\PiPStudio-%VER%-win-x64-portable.zip

set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" (
  echo.
  echo Inno Setup 6 isn't installed, so no installer this time.
  echo Get it from https://jrsoftware.org/isdl.php to build PiPStudio-Setup-%VER%.exe here too.
  echo The GitHub release workflow builds the installer either way.
  pause
  exit /b 0
)
"%ISCC%" /Q "/DAppVersion=%VER%" "/DSourceDir=..\publish-release" "/DOutputDir=..\out" installer\PiPStudio.iss
if errorlevel 1 goto failed
echo Made out\PiPStudio-Setup-%VER%.exe
echo.
echo Done - everything is in the out folder.
pause
exit /b 0

:failed
echo.
echo Build failed - see the errors above.
pause
exit /b 1
