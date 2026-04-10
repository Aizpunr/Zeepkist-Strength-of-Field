@echo off
rem Build Strength of Field plugin DLL.
rem Requires Zeepkist installed at the default Steam path with BepInEx + ZeepSDK present.

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set GAME=C:\Program Files (x86)\Steam\steamapps\common\Zeepkist
set MANAGED=%GAME%\Zeepkist_Data\Managed
set BEPINEX=%GAME%\BepInEx\core
set ZEEPSDK=%GAME%\BepInEx\plugins\Mods\3082296_7274361

if not exist bin mkdir bin

"%CSC%" ^
  -target:library ^
  -noconfig ^
  -nostdlib ^
  -out:bin\StrengthOfField.dll ^
  -reference:"%MANAGED%\mscorlib.dll" ^
  -reference:"%MANAGED%\netstandard.dll" ^
  -reference:"%MANAGED%\System.dll" ^
  -reference:"%MANAGED%\System.Core.dll" ^
  -reference:"%BEPINEX%\BepInEx.dll" ^
  -reference:"%MANAGED%\UnityEngine.dll" ^
  -reference:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  -reference:"%MANAGED%\ZeepkistNetworking.dll" ^
  -reference:"%MANAGED%\Zeepkist.dll" ^
  -reference:"%ZEEPSDK%\ZeepSDK.dll" ^
  -reference:"%MANAGED%\Newtonsoft.Json.dll" ^
  -optimize ^
  src\Plugin.cs

if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

echo Built bin\StrengthOfField.dll
