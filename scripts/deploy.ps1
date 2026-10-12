#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$AppManifest,
    [switch]$NonSteam,
    [switch]$Disable
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
    $inventoryPath = Join-Path $Logs 'deploy-game-inventory.json'
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
    foreach ($name in @('mscorlib.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.UIModule.dll', 'UnityEngine.InputLegacyModule.dll', 'UnityEngine.IMGUIModule.dll')) {
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
            harmony = (Get-FileHash -LiteralPath (Join-Path $bep 'core\0Harmony.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
            unity_ui = (Get-FileHash -LiteralPath (Join-Path $managed 'UnityEngine.UIModule.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
            unity_input = (Get-FileHash -LiteralPath (Join-Path $managed 'UnityEngine.InputLegacyModule.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
            unity_imgui = (Get-FileHash -LiteralPath (Join-Path $managed 'UnityEngine.IMGUIModule.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

function Assert-GameClosed {
    # Process names are readable without inspecting another process's memory.
    $running = @(Get-Process -ErrorAction Stop | Where-Object { $_.ProcessName -ieq 'Tainted Grail' })
    if ($running.Count -gt 0) { throw 'Закройте Tainted Grail перед установкой или отключением плагина. Скрипт не завершает игровые процессы.' }
}

function Assert-OwnedPlugin([string]$Path) {
    Assert-RegularTree $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw 'Ожидалась папка установленного плагина.' }
    $pluginDll = Join-Path $Path 'TaintedGrailAccess.BepInEx.dll'
    if (-not (Test-Path -LiteralPath $pluginDll -PathType Leaf)) { throw "Папка не распознана как наш диагностический плагин и оставлена без изменений: $Path" }
    if ([Reflection.AssemblyName]::GetAssemblyName($pluginDll).Name -ne 'TaintedGrailAccess.BepInEx') {
        throw 'Идентичность DLL существующего плагина не совпадает. Автоматическое перемещение отменено.'
    }
}

function Get-TreeSnapshot([string]$Path) {
    Assert-RegularTree $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "Не найден каталог: $Path" }
    $prefix = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $fileHashes = @{}
    $directories = @()
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Во время операции обнаружена ссылка; действие отменено.' }
            $relative = $item.FullName.Substring($prefix.Length).Replace('\', '/')
            if ($item.PSIsContainer) {
                $directories += $relative
                $pending.Push($item.FullName)
            } else {
                $fileHashes[$relative] = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    }
    return @{ Files = $fileHashes; Directories = @($directories | Sort-Object) }
}

function Assert-SameTree($Expected, $Actual) {
    if ($Expected.Files.Count -ne $Actual.Files.Count -or $Expected.Directories.Count -ne $Actual.Directories.Count) {
        throw 'Копия не прошла проверку состава дерева. Исходная папка не удаляется.'
    }
    foreach ($relative in $Expected.Files.Keys) {
        if (-not $Actual.Files.ContainsKey($relative) -or $Actual.Files[$relative] -cne $Expected.Files[$relative]) {
            throw "Проверка SHA-256 не пройдена: $relative. Исходная папка не удаляется."
        }
    }
    foreach ($relative in $Expected.Directories) {
        if ($Actual.Directories -notcontains $relative) { throw 'Набор каталогов в копии не совпадает.' }
    }
}

function Copy-VerifiedTree([string]$Source, [string]$Destination) {
    $snapshot = Get-TreeSnapshot $Source
    Assert-NoReparsePath $Destination
    if (Test-Path -LiteralPath $Destination) { throw "Папка назначения уже существует: $Destination" }
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($relative in $snapshot.Directories) {
        $targetDirectory = Join-Path $Destination $relative
        Assert-NoReparsePath $targetDirectory
        [IO.Directory]::CreateDirectory($targetDirectory) | Out-Null
    }
    foreach ($relative in @($snapshot.Files.Keys | Sort-Object)) {
        $sourceFile = Join-Path $Source $relative
        $targetFile = Join-Path $Destination $relative
        Assert-NoReparsePath $sourceFile
        Assert-NoReparsePath $targetFile
        Copy-Item -LiteralPath $sourceFile -Destination $targetFile
    }
    Assert-SameTree $snapshot (Get-TreeSnapshot $Destination)
    # Detect a concurrent change to the source before callers may remove it.
    Assert-SameTree $snapshot (Get-TreeSnapshot $Source)
}

function Get-DiagnosticPackage {
    $payload = Join-Path $projectRoot 'artifacts\package\TaintedGrailAccess'
    $manifestPath = Join-Path $projectRoot 'artifacts\package\diagnostic-package.json'
    Assert-NoReparsePath $manifestPath
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Сначала успешно выполните build.ps1 -Target Diagnostics. Манифест диагностической сборки отсутствует.' }
    if ((Get-Item -LiteralPath $manifestPath).Length -gt 1MB) { throw 'Неожиданный размер манифеста пакета.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.schema_version -ne 1 -or $manifest.target -cne 'Diagnostics' -or $manifest.target_framework -cne 'netstandard2.0') {
        throw 'Это не диагностический пакет поддерживаемого формата.'
    }
    $assemblyNames = @('TaintedGrailAccess.Graph', 'TaintedGrailAccess.Runtime', 'TaintedGrailAccess.Windows', 'TaintedGrailAccess.BepInEx')
    $allowed = @($assemblyNames | ForEach-Object { $_ + '.dll' }) + @('locales/en.properties', 'locales/ru.properties')
    $snapshot = Get-TreeSnapshot $payload
    if ($snapshot.Files.Count -ne $allowed.Count -or $snapshot.Directories.Count -ne 1 -or $snapshot.Directories[0] -cne 'locales') {
        throw 'В пакете разрешены только четыре DLL проекта и две таблицы locales. Посторонние файлы не устанавливаются.'
    }
    $records = @($manifest.files)
    if ($records.Count -ne $allowed.Count) { throw 'Состав манифеста пакета не совпадает с ожидаемым.' }
    $seen = @{}
    foreach ($record in $records) {
        $relative = [string]$record.path
        if ($allowed -cnotcontains $relative -or $seen.ContainsKey($relative)) { throw 'Неизвестный или повторяющийся путь в манифесте пакета.' }
        $seen[$relative] = $true
        if ([string]$record.sha256 -cnotmatch '^[a-f0-9]{64}$' -or -not $snapshot.Files.ContainsKey($relative) -or $snapshot.Files[$relative] -cne [string]$record.sha256) {
            throw "Сборка изменена или неполна: $relative. Повторите build.ps1 -Target Diagnostics."
        }
    }
    foreach ($name in $assemblyNames) {
        if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $payload ($name + '.dll'))).Name -cne $name) {
            throw "Неожиданная идентичность сборки: $name"
        }
    }
    return @{ Path = $payload; Manifest = $manifest; Snapshot = $snapshot }
}

function Assert-MatchingReferences($Package, $References) {
    foreach ($key in @('game_executable', 'unity_core', 'framework', 'bepinex', 'harmony', 'unity_ui', 'unity_input', 'unity_imgui')) {
        $expectedHash = [string]$Package.Manifest.reference_sha256.$key
        if ($expectedHash -cnotmatch '^[a-f0-9]{64}$' -or $expectedHash -cne $References.ReferenceHashes[$key]) {
            throw 'Локальные игровые/загрузочные файлы отличаются от использованных при сборке. Соберите Diagnostics заново для выбранной GameDir.'
        }
    }
}

function Get-NewBackupPath {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $backup = Join-Path $projectRoot ('artifacts\backups\' + $stamp + '\TaintedGrailAccess')
    Assert-NoReparsePath $backup
    return $backup
}

try {
    if ($env:OS -ne 'Windows_NT') { throw 'deploy.ps1 рассчитан на локальную установку Windows.' }
    if (-not $GameDir) { throw 'Обязателен явный -GameDir; автоматического поиска игры нет.' }
    if ($AppManifest -and $NonSteam) { throw '-AppManifest и -NonSteam нельзя задавать одновременно.' }
    $game = [IO.Path]::GetFullPath((Convert-Path -LiteralPath $GameDir))
    Assert-NoReparsePath $projectRoot
    Assert-NoReparsePath $game
    if (-not (Test-Path -LiteralPath $game -PathType Container)) { throw 'Указанная папка игры не найдена.' }
    $gamePrefix = $game.TrimEnd('\') + '\'
    if ($projectRoot.StartsWith($gamePrefix, [StringComparison]::OrdinalIgnoreCase) -or [string]::Equals($projectRoot.TrimEnd('\'), $game.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Распакуйте проект вне каталога игры: резервные копии и журналы должны храниться отдельно от установки.'
    }
    $pluginParent = Join-Path $game 'BepInEx\plugins'
    $targetPath = Join-Path $pluginParent 'TaintedGrailAccess'
    Assert-NoReparsePath $targetPath
    Assert-GameClosed

    if ($Disable) {
        # Recovery deliberately does not depend on Python, SDK, a Steam manifest,
        # or the current game backend: updates must not prevent disabling our mod.
        if (-not (Test-Path -LiteralPath $targetPath)) {
            Write-Host 'Диагностический плагин уже отключён: его папка отсутствует.'
            exit 0
        }
        Assert-OwnedPlugin $targetPath
        $backupPath = Get-NewBackupPath
        Copy-VerifiedTree $targetPath $backupPath
        Assert-GameClosed
        Assert-OwnedPlugin $targetPath
        Assert-SameTree (Get-TreeSnapshot $backupPath) (Get-TreeSnapshot $targetPath)
        try { Remove-Item -LiteralPath $targetPath -Recurse -Force }
        catch { Write-Host "Отключение не завершено. Проверенная резервная копия: $backupPath"; throw }
        Write-Host 'Диагностический плагин отключён; прежняя папка перенесена в резервную копию.'
        Write-Host "Резервная копия: $backupPath"
        Write-Host 'Остальные моды, загрузчик, конфигурации BepInEx и сохранения не изменены.'
        exit 0
    }

    $package = Get-DiagnosticPackage
    $logs = Join-Path $projectRoot 'artifacts\logs\deploy'
    Assert-NoReparsePath $logs
    [IO.Directory]::CreateDirectory($logs) | Out-Null
    $references = Get-DiagnosticReferences $game $AppManifest ([bool]$NonSteam) $logs
    Assert-MatchingReferences $package $references
    if (Test-Path -LiteralPath $targetPath) { Assert-OwnedPlugin $targetPath }
    Assert-NoReparsePath $pluginParent
    [IO.Directory]::CreateDirectory($pluginParent) | Out-Null
    # Keep staging outside BepInEx/plugins: a leftover staging directory must not
    # be discovered as a second plugin on a later game launch.
    $stagingParent = Join-Path $game 'BepInEx'
    $stagingPath = Join-Path $stagingParent ('.TaintedGrailAccess-install-' + [Guid]::NewGuid().ToString('N'))
    $backupPath = $null
    $backupReady = $false
    $oldRemoved = $false
    try {
        Assert-GameClosed
        Copy-VerifiedTree $package.Path $stagingPath
        Assert-SameTree $package.Snapshot (Get-TreeSnapshot $stagingPath)
        Assert-GameClosed
        if (Test-Path -LiteralPath $targetPath) {
            Assert-OwnedPlugin $targetPath
            $backupPath = Get-NewBackupPath
            Copy-VerifiedTree $targetPath $backupPath
            $backupReady = $true
            Assert-GameClosed
            Assert-OwnedPlugin $targetPath
            Assert-SameTree (Get-TreeSnapshot $backupPath) (Get-TreeSnapshot $targetPath)
            Remove-Item -LiteralPath $targetPath -Recurse -Force
            $oldRemoved = $true
        }
        Assert-GameClosed
        Assert-NoReparsePath $targetPath
        Assert-RegularTree $stagingPath
        [IO.Directory]::Move($stagingPath, $targetPath)
        $stagingPath = $null
    } catch {
        $installationError = $_
        if ($backupReady -and -not (Test-Path -LiteralPath $targetPath)) {
            $restorePath = Join-Path $stagingParent ('.TaintedGrailAccess-restore-' + [Guid]::NewGuid().ToString('N'))
            try {
                Assert-GameClosed
                Copy-VerifiedTree $backupPath $restorePath
                Assert-GameClosed
                Assert-NoReparsePath $targetPath
                [IO.Directory]::Move($restorePath, $targetPath)
                Write-Host 'Установка не завершена; прежний плагин восстановлен из проверенной копии.'
            } catch {
                Write-Host "Автоматическое восстановление не завершено. Проверенная резервная копия: $backupPath"
            } finally {
                if (Test-Path -LiteralPath $restorePath) {
                    try { Assert-RegularTree $restorePath; Remove-Item -LiteralPath $restorePath -Recurse -Force }
                    catch { Write-Host "Промежуточная папка восстановления требует проверки: $restorePath" }
                }
            }
        } elseif ($backupReady -and $oldRemoved) {
            Write-Host "Папка назначения появилась повторно и не перезаписана. Проверенная резервная копия: $backupPath"
        } elseif ($backupReady) {
            Write-Host "Прежняя папка удалена не полностью; проверенная резервная копия: $backupPath"
        }
        throw $installationError
    } finally {
        if ($stagingPath -and (Test-Path -LiteralPath $stagingPath)) {
            try { Assert-RegularTree $stagingPath; Remove-Item -LiteralPath $stagingPath -Recurse -Force }
            catch { Write-Host "Промежуточная папка требует проверки: $stagingPath" }
        }
    }
    Write-Host 'Установлен диагностический плагин; управление игровыми экранами пока не подключено.'
    Write-Host "Папка: $targetPath"
    Write-Host $(if ($backupPath) { "Прежняя версия: $backupPath" } else { 'Прежней папки плагина не было.' })
    Write-Host $(if ($references.ManifestVerified) { 'Steam-манифест и локальные файлы сопоставлены со сборкой.' } else { 'NonSteam: подтвердите название продукта по игровому диагностическому журналу.' })
    Write-Host "Для отключения: .\scripts\deploy.ps1 -GameDir `"$game`" -Disable"
    exit 0
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
