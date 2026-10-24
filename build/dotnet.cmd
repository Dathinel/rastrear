@echo off
rem SDK de .NET del entorno propio mediante el envoltorio de Rodaje (submodulo externo\rodaje). Uso: build\dotnet.cmd build src\Rastrear.slnx
setlocal
set "RAIZ=%~dp0.."
if not defined RODAJE_ENTORNO set "RODAJE_ENTORNO=%RAIZ%\.entorno"
if not exist "%RAIZ%\externo\rodaje\build\dotnet-local.cmd" (
  git -C "%RAIZ%" submodule update --init --depth 1 externo/rodaje || exit /b 1
)
rem El enlazador de Native AOT necesita vswhere.exe, que el instalador de Visual Studio no pone en PATH (como build\comun.psm1 de Rodaje).
set "PATH=%PATH%;%ProgramFiles(x86)%\Microsoft Visual Studio\Installer"
call "%RAIZ%\externo\rodaje\build\dotnet-local.cmd" %*
exit /b %errorlevel%
