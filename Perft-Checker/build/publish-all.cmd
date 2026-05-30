@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0\.."

if not exist dist mkdir dist

for %%R in (linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64) do (
    echo === publishing %%R ===
    dotnet publish Perft-Checker.csproj -c Release -r %%R --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o "dist\%%R" || exit /b 1
)

echo.
echo Binaries:
for %%R in (linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64) do (
    set BIN=dist\%%R\perftcheck
    if "%%R"=="win-x64" set BIN=!BIN!.exe
    if exist "!BIN!" echo   %%R: !BIN!
)
