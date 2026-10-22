<#
.SYNOPSIS
  Сборка порта для Windows (x86_64, .NET): папка dist/win и архив dist/disciples-godot-win64.zip — БЕЗ данных и графики игры
  (их игра импортирует из копии игрока при первом запуске, docs/publishing.md).

.DESCRIPTION
  1. Экспорт пресета «Windows Desktop» (godot/export_presets.cfg) движком Godot 4.7.2 mono. Шаблоны экспорта нужны для .NET (mono):
     %APPDATA%\Godot\export_templates\4.7.2.stable.mono (ставит редактор: Editor → Manage Export Templates) или свой каталог
     -Templates с windows_release_x86_64.exe и windows_debug_x86_64.exe из Godot_v4.7.2-stable_mono_export_templates.tpz — тогда
     пресет на время экспорта получает custom_template и после возвращается как был. Обычные (не mono) шаблоны C# не запускают.
  2. Рядом с исполняемым файлом — наш контент: data/ui (переходы, начальные значения, шрифты), data/fonts (EB Garamond, OFL),
     data/music/playlists.json, data/import/CustomLobby.dlg (разметка лобби D2ModdingToolset, GPL-3), mods/_examples, README.md,
     LICENSE, licenses/*.
  3. Проверка, что в сборку не попало ни одного файла игры (форматы игры и каталоги выведенных из неё данных).
  4. Архив zip.
  5. -Check: запуск собранного exe без окна до главного меню (--input "wait 1": строка «flow step=1 … stack=DLG_MAIN_MENU», код 0) —
     на готовом импорте -DataDir или, с -ImportFrom <папка игры>, после импорта в пустой -DataDir (так проверяется первый запуск).

.EXAMPLE
  pwsh -File tools/publish/build_windows.ps1
  pwsh -File tools/publish/build_windows.ps1 -Templates C:\godot-templates\4.7.2.stable.mono -Check -DataDir C:\d2\imported
  pwsh -File tools/publish/build_windows.ps1 -Check -ImportFrom "C:\GOG Games\Disciples II" -DataDir "$env:TEMP\d2-first-run"
#>
param(
    [string]$Godot = $(if ($env:GODOT) { $env:GODOT } else { "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" }),
    [string]$Out = "",
    [string]$Templates = (Join-Path $env:APPDATA "Godot\export_templates\4.7.2.stable.mono"),
    [switch]$Check,
    [string]$DataDir = "",
    [string]$ImportFrom = "",
    [string]$L10n = ""
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $Out) { $Out = Join-Path $repo "dist" }
$win = Join-Path $Out "win"
if (-not (Test-Path $Godot)) { throw "нет Godot: $Godot (переменная GODOT)" }
$release = Join-Path $Templates "windows_release_x86_64.exe"
$debug = Join-Path $Templates "windows_debug_x86_64.exe"
if (-not (Test-Path $release)) { throw "нет шаблонов экспорта 4.7.2 mono в $Templates (ключ -Templates)" }
$standard = Join-Path $env:APPDATA "Godot\export_templates\4.7.2.stable.mono"
$presets = Join-Path $repo "godot\export_presets.cfg"
$presetsText = [System.IO.File]::ReadAllText($presets)

# 1. Экспорт: каталог сборки — заново (только dist/win, который этот скрипт и создаёт).
if (Test-Path $win) { Remove-Item -Recurse -Force -Confirm:$false $win }
New-Item -ItemType Directory -Force $win | Out-Null
$exe = Join-Path $win "Disciples.exe"
$code = 1
try {
    if ([System.IO.Path]::GetFullPath($Templates).TrimEnd('\') -ne $standard) {
        $custom = $presetsText.Replace('custom_template/debug=""', 'custom_template/debug="' + $debug.Replace('\', '/') + '"').
                               Replace('custom_template/release=""', 'custom_template/release="' + $release.Replace('\', '/') + '"')
        [System.IO.File]::WriteAllText($presets, $custom)
    }
    & $Godot --headless --path (Join-Path $repo "godot") --export-release "Windows Desktop" $exe 2>&1 | ForEach-Object { "$_" } |
        Where-Object { $_ -match "error|ERROR|savepack" } | Select-Object -Last 20
    $code = $LASTEXITCODE
}
finally {
    [System.IO.File]::WriteAllText($presets, $presetsText)
}
if ($code -ne 0 -or -not (Test-Path $exe)) { throw "экспорт не удался (код $code)" }
if (-not (Test-Path (Join-Path $win "data_D2.Godot_windows_x86_64"))) { throw "в сборке нет сборок .NET (data_D2.Godot_windows_x86_64)" }
# Обёртка консоли (вывод в терминал): с custom_template Godot 4.7 её не кладёт — берётся та же из шаблонов.
$wrapper = Join-Path $Templates "windows_release_x86_64_console.exe"
if (-not (Test-Path (Join-Path $win "Disciples.console.exe")) -and (Test-Path $wrapper)) { Copy-Item $wrapper (Join-Path $win "Disciples.console.exe") }

# 2. Наш контент рядом с exe.
$own = @(
    "data/ui/defaults.json", "data/ui/flow.json", "data/ui/flow.manual.json", "data/ui/fonts.json", "data/ui/transitions.json",
    "data/music/playlists.json", "README.md", "LICENSE"
)
foreach ($rel in $own) {
    $to = Join-Path $win $rel
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Copy-Item (Join-Path $repo $rel) $to
}
Copy-Item -Recurse (Join-Path $repo "data/fonts") (Join-Path $win "data/fonts")
New-Item -ItemType Directory -Force (Join-Path $win "data/import") | Out-Null
Copy-Item -LiteralPath (Join-Path $repo "tools/d2probe/Examples/Custom Lobby/CustomLobby.dlg") (Join-Path $win "data/import/CustomLobby.dlg")
New-Item -ItemType Directory -Force (Join-Path $win "mods") | Out-Null
Copy-Item -Recurse (Join-Path $repo "mods/_examples") (Join-Path $win "mods/_examples")
New-Item -ItemType Directory -Force (Join-Path $win "licenses") | Out-Null
Copy-Item (Join-Path $repo "data/fonts/OFL.txt") (Join-Path $win "licenses/EBGaramond-OFL.txt")
Copy-Item (Join-Path $repo "tools/ui-assets/NOTICE") (Join-Path $win "licenses/Disciples.Net-NOTICE.txt")
Copy-Item (Join-Path $repo "tools/d2probe/LICENSE") (Join-Path $win "licenses/D2ModdingToolset-LICENSE.txt")

# 3. Ни одного файла игры: форматы игры и выведенные из неё данные.
$gameExt = '\.(ff|wdb|wdt|dbf|sg|csg|ldr|bik|mft|webp|ogv|mp3|wav|dat|dlg)$'
$bad = Get-ChildItem -Recurse -File $win | Where-Object { $_.FullName -notmatch '\\mods\\_examples\\' } | Where-Object {
    ($_.Name -match $gameExt -and $_.Name -ne "CustomLobby.dlg") -or
    ($_.FullName -match '\\data\\(game|gfx|map|rules|scenarios|sound|video)\\') -or
    ($_.FullName -match '\\data\\ui\\(assets|screens|capital|strings\.[a-z]+|leader-names\.[a-z]+)\.json$')
}
if ($bad) { throw "в сборке файлы игры: $($bad.FullName -join ', ')" }
$assets = Get-ChildItem -Recurse -Directory $win | Where-Object { $_.Name -eq "assets" -and $_.FullName -notmatch '\\mods\\' }
if ($assets) { throw "в сборке каталог assets: $($assets.FullName -join ', ')" }

# 4. Архив.
$sha = (git -C $repo rev-parse --short HEAD).Trim()
$zip = Join-Path $Out "disciples-godot-win64.zip"
if (Test-Path $zip) { Remove-Item -Force -Confirm:$false $zip }
Compress-Archive -Path (Join-Path $win "*") -DestinationPath $zip -CompressionLevel Optimal
$size = (Get-Item $zip).Length
$folder = (Get-ChildItem -Recurse -File $win | Measure-Object Length -Sum).Sum
"сборка $sha -> $win ($([math]::Round($folder / 1MB, 1)) МиБ), архив $zip ($([math]::Round($size / 1MB, 1)) МиБ)"

# 5. Проверка запуском.
if ($Check) {
    if (-not $DataDir) { throw "-Check требует -DataDir (готовый импорт или пустой каталог для -ImportFrom)" }
    $console = Join-Path $win "Disciples.console.exe"
    $run = @("--headless", "--", "--data-dir", $DataDir, "--no-mods", "--input", "wait 1")
    if ($ImportFrom) { $run += @("--game-dir", $ImportFrom) }
    if ($L10n) { $run += @("--l10n", $L10n) }
    $env:D2_PORT_PROBE = "0"
    # Свой процесс с пределом времени: зависший exe (исключение C# Godot не роняет) не держит проверку вечно.
    $stdout = Join-Path $Out "check.out.txt"
    $stderr = Join-Path $Out "check.err.txt"
    $quoted = $run | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $console -ArgumentList $quoted -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $p.WaitForExit(1800 * 1000)) { $p.Kill(); throw "exe не завершился за 30 мин" }
    $code = $p.ExitCode
    $log = @(Get-Content $stdout) + @(Get-Content $stderr)
    $log | Where-Object { $_ -match "^\[|import |flow step|mods:|content:|ERROR|failed|UiFlowView|Boot" } | Select-Object -Last 30
    if ($code -ne 0 -or -not ($log -match "flow step=1 .*stack=DLG_MAIN_MENU")) { throw "exe не дошёл до главного меню (код $code)" }
    "проверка: exe дошёл до главного меню (код 0)"
}
