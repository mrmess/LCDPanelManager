@echo off
rem Self-contained win-x64 build: no installed .NET Desktop Runtime is required.
start "" "%~dp0publish\win-x64\StripView.exe"
