@echo off
cd /d "%~dp0"
dotnet publish windows\PCMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist\windows
if errorlevel 1 exit /b 1
echo Output: dist\windows\PCMonitor.exe
pause
