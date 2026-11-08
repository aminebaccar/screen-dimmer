@echo off
rem Builds ScreenDimmer.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Could not find %CSC%
    echo .NET Framework 4.x is required; it is included with Windows 10 and 11.
    exit /b 1
)
cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /out:ScreenDimmer.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll ScreenDimmer.cs
if errorlevel 1 exit /b 1
echo Built ScreenDimmer.exe
