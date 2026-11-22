@echo off
setlocal
set "inputFile="
if not "%~1"=="" set "inputFile=%~f1"
pushd "%~dp0" || exit /b 1
if exist "bin\Release\.selftest-ok" if exist "bin\Release\P6SemiImageEditor.exe" if exist "bin\Release\font-atlas.png" if exist "bin\Release\P6SemiImageEditor.exe.config" goto run
call Build.cmd
if errorlevel 1 goto failed
:run
if defined inputFile (
    start "" "bin\Release\P6SemiImageEditor.exe" "%inputFile%"
) else (
    start "" "bin\Release\P6SemiImageEditor.exe"
)
popd
exit /b 0
:failed
echo Editor build or self-test failed.
pause
popd
exit /b 1
