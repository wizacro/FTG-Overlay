@echo off
rem FTG-Overlay demo 构建脚本：使用 Windows 自带的 .NET Framework C# 编译器，零外部依赖
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
"%FW%\csc.exe" -nologo -target:winexe -platform:anycpu -optimize+ ^
  -lib:"%FW%" -lib:"%FW%\WPF" ^
  -r:System.dll -r:System.Core.dll -r:System.Web.Extensions.dll -r:System.Xaml.dll ^
  -r:PresentationCore.dll -r:PresentationFramework.dll -r:WindowsBase.dll ^
  -out:"%~dp0..\FTG-Overlay-demo.exe" ^
  "%~dp0Program.cs" "%~dp0Engine.cs" "%~dp0Store.cs" "%~dp0OverlayWindow.cs" "%~dp0ManagerWindow.cs"
if errorlevel 1 (echo 构建失败 & exit /b 1)
echo 构建完成：FTG-Overlay-demo.exe
