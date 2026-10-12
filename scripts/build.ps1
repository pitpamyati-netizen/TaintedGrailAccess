#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Core', 'Diagnostics')][string]$Target = 'Core',
    [string]$GameDir,
    [string]$AppManifest,
    [switch]$NonSteam
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Assert-NoReparsePath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        $attributes = $null
        try { $attributes = [IO.File]::GetAttributes($current) }
        catch [IO.FileNotFoundException] { }
        catch [IO.DirectoryNotFoundException] { }
        if ($null -ne $attributes -and ($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Ссылки и junction-каталоги не поддерживаются в этом действии: $current"
        }
        $parent = [IO.Directory]::GetParent($current)
        $current = if ($null -eq $parent) { $null } else { $parent.FullName }
    }
}

function Assert-RegularTree([string]$Path) {
    Assert-NoReparsePath $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "В дереве обнаружена ссылка; операция отменена: $($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
}

function Invoke-LoggedTool([string]$Executable, [string[]]$Arguments, [string]$LogPath) {
    Assert-NoReparsePath $LogPath
    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $Executable @Arguments > $LogPath 2>&1
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $oldPreference }
    if ($VerbosePreference -eq 'Continue') { Get-Content -LiteralPath $LogPath | ForEach-Object { Write-Verbose $_ } }
    if ($code -ne 0) {
        Get-Content -LiteralPath $LogPath -Tail 15 | ForEach-Object { Write-Host $_ }
        throw "Команда завершилась с кодом $code. Полный журнал: $LogPath"
    }
}

function Get-PythonCommand {
    $command = Get-Command py -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) { return @{ Executable = $command.Source; Prefix = @('-3') } }
    $command = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) { throw 'Нужен Python 3.9 или новее: команды py и python не найдены.' }
    return @{ Executable = $command.Source; Prefix = @() }
}

function Get-ConquestManifest([string]$GamePath, [string]$ExplicitManifest, [bool]$PermitNonSteam) {
    if ($ExplicitManifest -and $PermitNonSteam) { throw '-AppManifest и -NonSteam нельзя задавать одновременно.' }
    if ($PermitNonSteam) { return $null }
    $manifestPath = $null
    if ($ExplicitManifest) {
        $manifestPath = [IO.Path]::GetFullPath((Convert-Path -LiteralPath $ExplicitManifest))
    } else {
        $gameDirectory = Get-Item -LiteralPath $GamePath -Force
        $common = $gameDirectory.Parent
        if ($null -ne $common -and $common.Name -ieq 'common' -and $null -ne $common.Parent) {
            $candidate = Join-Path $common.Parent.FullName 'appmanifest_1199030.acf'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $manifestPath = $candidate }
        }
    }
    if (-not $manifestPath) {
        if ($PermitNonSteam) { return $null }
        throw 'Манифест Conquest не найден рядом с выбранной установкой Steam. Укажите -AppManifest или явно выберите -NonSteam для копии/другого магазина.'
    }
    Assert-NoReparsePath $manifestPath
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Указанный манифест не существует.' }
    if ((Get-Item -LiteralPath $manifestPath).Length -gt 1MB) { throw 'Неожиданный размер Steam-манифеста.' }
    $content = Get-Content -LiteralPath $manifestPath -Raw
    $directoryMatches = [regex]::Matches($content, '(?im)^\s*"installdir"\s+"([^"\\/:]+)"\s*$')
    if ($directoryMatches.Count -ne 1 -or $directoryMatches[0].Groups[1].Value -in @('.', '..')) { throw 'Не удалось однозначно проверить installdir в Steam-манифесте.' }
    $match = $directoryMatches[0]
    $linkedGame = [IO.Path]::GetFullPath((Join-Path (Split-Path $manifestPath -Parent) ('common\' + $match.Groups[1].Value)))
    if (-not [string]::Equals($linkedGame.TrimEnd('\'), $GamePath.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Steam-манифест связан с другой папкой игры. Для отдельной копии используйте -NonSteam без -AppManifest.'
    }
    return $manifestPath
}

function Get-DiagnosticReferences([string]$GamePath, [string]$ManifestPath, [bool]$PermitNonSteam, [string]$Logs) {
    if (-not $GamePath) { throw 'Для -Target Diagnostics обязателен явный -GameDir.' }
    $game = [IO.Path]::GetFullPath((Convert-Path -LiteralPath $GamePath))
    Assert-NoReparsePath $game
    if (-not (Test-Path -LiteralPath $game -PathType Container)) { throw 'Каталог игры не найден.' }
    if ($game -match '(?i)fall.?of.?avalon') { throw 'Выбрана The Fall of Avalon. Этот проект предназначен для Conquest.' }
    $manifest = Get-ConquestManifest $game $ManifestPath $PermitNonSteam
    $python = Get-PythonCommand
    Invoke-LoggedTool $python.Executable (@($python.Prefix) + @('-c', 'import sys; sys.exit(0 if sys.version_info >= (3, 9) else 3)')) (Join-Path $Logs 'python-version.log')
    $inventoryPath = Join-Path $Logs 'build-game-inventory.json'
    $arguments = @($python.Prefix) + @((Join-Path $PSScriptRoot 'inspect_game.py'), '--game-dir', $game, '--output', $inventoryPath)
    if ($manifest) { $arguments += @('--appmanifest', $manifest) }
    Invoke-LoggedTool $python.Executable $arguments (Join-Path $Logs 'discovery.log')
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($inventory.layout.status -ne 'expected_unique') { throw 'Нужны Tainted Grail.exe и единственная папка Tainted Grail_Data. Автоматический выбор неоднозначной установки запрещён.' }
    if ($manifest -and $inventory.identity.status -ne 'manifest_appid_matches') { throw 'AppID манифеста не подтверждает Conquest (1199030).' }
    if ($inventory.runtime.classification -ne 'mono_likely') { throw 'Нет однозначных статических свидетельств Mono. IL2CPP и конфликтующие установки этим host не поддерживаются.' }
    $exe = @($inventory.files | Where-Object { $_.role -eq 'game_executable' -and $_.read_status -eq 'ok' })
    if ($exe.Count -ne 1 -or $exe[0].binary.architecture -ne 'x64') { throw 'Нужен подтверждённый PE x64 исполняемый файл игры.' }
    $managed = Join-Path $game ($inventory.layout.selected_data_directory + '\Managed')
    Assert-NoReparsePath $managed
    foreach ($name in @('mscorlib.dll', 'UnityEngine.CoreModule.dll')) {
        $path = Join-Path $managed $name
        Assert-NoReparsePath $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Нет локальной ссылки сборки: $name" }
    }
    $bep = Join-Path $game 'BepInEx'
    $bepCore = Join-Path $bep 'core\BepInEx.dll'
    Assert-NoReparsePath $bepCore
    if (-not (Test-Path -LiteralPath $bepCore -PathType Leaf)) { throw 'Локальный BepInEx 5 не найден. Этот скрипт не устанавливает загрузчики.' }
    $loaderIdentity = [Reflection.AssemblyName]::GetAssemblyName($bepCore)
    if ($loaderIdentity.Name -ne 'BepInEx' -or $loaderIdentity.Version.Major -ne 5) { throw 'Требуется именно локальная сборка BepInEx 5; другие major-версии не подставляются.' }
    if (-not $manifest) { Write-Verbose 'NonSteam: принадлежность бинарных файлов Conquest требует проверки Application.productName и игрового журнала.' }
    return @{ Game = $game; Managed = $managed; BepInEx = $bep; ManifestVerified = [bool]$manifest;
        ReferenceHashes = [ordered]@{
            game_executable = $exe[0].sha256
            unity_core = (Get-FileHash -LiteralPath (Join-Path $managed 'UnityEngine.CoreModule.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
            framework = (Get-FileHash -LiteralPath (Join-Path $managed 'mscorlib.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
            bepinex = (Get-FileHash -LiteralPath $bepCore -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

try {
    if ($env:OS -ne 'Windows_NT') { throw 'build.ps1 рассчитан на Windows. В другой среде используйте документированную проверку ядра.' }
    Assert-NoReparsePath $projectRoot
    $logs = Join-Path $projectRoot 'artifacts\logs\build'
    Assert-NoReparsePath $logs
    [IO.Directory]::CreateDirectory($logs) | Out-Null
    $packageManifest = Join-Path $projectRoot 'artifacts\package\diagnostic-package.json'
    if ($Target -eq 'Diagnostics') {
        # A failed rebuild must not leave an installable stale package.
        Assert-NoReparsePath $packageManifest
        if (Test-Path -LiteralPath $packageManifest) { Remove-Item -LiteralPath $packageManifest -Force }
    }
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
    Invoke-LoggedTool $dotnet.Source @('--list-sdks') (Join-Path $logs 'sdk.log')
    if (-not (Select-String -LiteralPath (Join-Path $logs 'sdk.log') -Pattern '^8\.' -Quiet)) { throw 'Нужен установленный .NET 8 SDK.' }
    $references = $null
    if ($Target -eq 'Diagnostics') { $references = Get-DiagnosticReferences $GameDir $AppManifest ([bool]$NonSteam) $logs }
    $projectName = if ($Target -eq 'Diagnostics') { 'TaintedGrailAccess.BepInEx' } else { 'TaintedGrailAccess.Windows' }
    $project = Join-Path $projectRoot ('src\' + $projectName + '\' + $projectName + '.csproj')
    $arguments = @('build', $project, '--configuration', 'Release', '--nologo', '--verbosity', 'minimal', '-p:GraphTargetFramework=netstandard2.0')
    if ($null -ne $references) { $arguments += @(('-p:GameManagedDir=' + $references.Managed), ('-p:BepInExDir=' + $references.BepInEx)) }
    Invoke-LoggedTool $dotnet.Source $arguments (Join-Path $logs 'compile.log')

    $payloadNames = @('TaintedGrailAccess.Graph', 'TaintedGrailAccess.Runtime', 'TaintedGrailAccess.Windows')
    if ($Target -eq 'Diagnostics') { $payloadNames += 'TaintedGrailAccess.BepInEx' }
    $payload = if ($Target -eq 'Diagnostics') { Join-Path $projectRoot 'artifacts\package\TaintedGrailAccess' } else { Join-Path $projectRoot 'artifacts\core' }
    $staging = Join-Path (Split-Path $payload -Parent) ('.build-' + [Guid]::NewGuid().ToString('N'))
    Assert-NoReparsePath $payload
    Assert-NoReparsePath $staging
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    foreach ($name in $payloadNames) {
        $source = Join-Path $projectRoot ('src\' + $name + '\bin\Release\netstandard2.0\' + $name + '.dll')
        Assert-NoReparsePath $source
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Ожидаемая DLL не создана: $name" }
        if ([Reflection.AssemblyName]::GetAssemblyName($source).Name -ne $name) { throw "Неожиданная идентичность сборки: $name" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $staging ($name + '.dll'))
    }
    $locales = Join-Path $projectRoot 'locales'
    Assert-RegularTree $locales
    $stagedLocales = Join-Path $staging 'locales'
    [IO.Directory]::CreateDirectory($stagedLocales) | Out-Null
    foreach ($locale in @('en.properties', 'ru.properties')) {
        $source = Join-Path $locales $locale
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Отсутствует обязательная локализация: $locale" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $stagedLocales $locale)
    }
    if (Test-Path -LiteralPath $payload) { Assert-RegularTree $payload; Remove-Item -LiteralPath $payload -Recurse -Force }
    Move-Item -LiteralPath $staging -Destination $payload
    if ($Target -eq 'Diagnostics') {
        $fileRecords = @()
        foreach ($name in $payloadNames) {
            $relative = $name + '.dll'
            $fileRecords += [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $payload $relative) -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
        foreach ($locale in @('en.properties', 'ru.properties')) {
            $relative = 'locales/' + $locale
            $fileRecords += [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath (Join-Path $payload $relative) -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
        $manifest = [ordered]@{ schema_version = 1; target = 'Diagnostics'; target_framework = 'netstandard2.0';
            created_utc = [DateTime]::UtcNow.ToString('o'); reference_sha256 = $references.ReferenceHashes; files = $fileRecords }
        [IO.File]::WriteAllText($packageManifest, ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    }
    Write-Host "Сборка $Target завершена; собственных DLL: $($payloadNames.Count)."
    Write-Host "Результат: $payload"
    Write-Host "Журнал: $logs"
    if ($Target -eq 'Diagnostics') {
        Write-Host $(if ($references.ManifestVerified) { 'Установка сопоставлена с манифестом Steam Conquest.' } else { 'NonSteam: название продукта ещё требуется подтвердить в игровом журнале.' })
        Write-Host 'Это диагностический плагин. Доступность прохождения и native-речь этой сборкой не проверены.'
    }
    exit 0
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
