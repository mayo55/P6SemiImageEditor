@echo off
setlocal
pushd "%~dp0" || exit /b 1
set "editorCSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%editorCSC%" set "editorCSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%editorCSC%" goto missing
if not exist "bin\Release" mkdir "bin\Release"
if errorlevel 1 goto fail
if exist "bin\Release\.selftest-ok" del "bin\Release\.selftest-ok"
"%editorCSC%" @Sources.rsp /out:bin\Release\P6SemiImageEditor.exe
if errorlevel 1 goto fail
copy /y App.config bin\Release\P6SemiImageEditor.exe.config >nul
if errorlevel 1 goto fail
copy /y font-atlas.png bin\Release\font-atlas.png >nul
if errorlevel 1 goto fail
start "" /wait "bin\Release\P6SemiImageEditor.exe" --self-test
if errorlevel 1 goto fail
>bin\Release\.selftest-ok echo OK
if errorlevel 1 goto fail
echo Build and editor self-tests passed.
popd
exit /b 0
:missing
echo Windows .NET Framework 4.8 and its C# compiler are required.
:fail
popd
exit /b 1
