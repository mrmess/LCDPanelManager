@echo off
rem StripView: the .NET runtime on this PC lives in D:\Tools\dotnet.
set DOTNET_ROOT=D:\Tools\dotnet
start "" "%~dp0src\StripView\bin\Debug\net8.0-windows\StripView.exe"
