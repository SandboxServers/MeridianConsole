@echo off
echo Publishing Dhadgar CLI...
dotnet publish src\Dhadgar.Cli\Dhadgar.Cli.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:PublishSingleFile=true ^
  -o dist\cli

echo.
echo Published to: dist\cli\dhadgar.exe
echo.
echo To install globally, copy to a directory in your PATH:
echo   copy dist\cli\dhadgar.exe C:\Users\%USERNAME%\bin\
