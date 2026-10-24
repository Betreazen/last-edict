@echo off
rem Запуск Last Edict для разработки (docs/HANDOFF.md): графика импорта — D2_ASSETS_DIR, иначе ..\disciples-godot\assets рядом с
rem репозиторием; Godot — GODOT, иначе godot из PATH. Ключи после play.cmd идут игре (например: play.cmd --no-mods).
chcp 65001 >nul
setlocal
if "%D2_ASSETS_DIR%"=="" set "D2_ASSETS_DIR=%~dp0..\disciples-godot\assets"
if not exist "%D2_ASSETS_DIR%\gfx" (
  echo Нет графики игры в %D2_ASSETS_DIR% — задайте D2_ASSETS_DIR ^(каталог assets импорта^).
  pause
  exit /b 1
)
if "%GODOT%"=="" set "GODOT=godot"
dotnet build "%~dp0godot\LastEdict.Godot.csproj" -nologo -v q || (pause & exit /b 1)
call "%GODOT%" --path "%~dp0godot" -- %*
