#requires -Version 5.1
[CmdletBinding()]
param([switch]$Offline)

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
        Get-Content -LiteralPath $LogPath -Tail 20 | ForEach-Object { Write-Host $_ }
        throw "Проверка завершилась с кодом $code. Полный журнал: $LogPath"
    }
}

try {
    Assert-NoReparsePath $projectRoot
    $logs = Join-Path $projectRoot 'artifacts\logs\tests'
    Assert-NoReparsePath $logs
    [IO.Directory]::CreateDirectory($logs) | Out-Null
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
    Invoke-LoggedTool $dotnet.Source @('--list-sdks') (Join-Path $logs 'sdk.log')
    if (-not (Select-String -LiteralPath (Join-Path $logs 'sdk.log') -Pattern '^(?:[89]|[1-9][0-9]+)\.' -Quiet)) { throw 'Для тестов нужен установленный .NET SDK версии 8 или новее.' }
    $python = Get-Command py -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $pythonPrefix = @('-3')
    if ($null -eq $python) {
        $python = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        $pythonPrefix = @()
    }
    if ($null -eq $python) { throw 'Для тестов инвентаризации нужен Python 3.9 или новее.' }
    Invoke-LoggedTool $python.Source ($pythonPrefix + @('-c', 'import sys; sys.exit(0 if sys.version_info >= (3, 9) else 3)')) (Join-Path $logs 'python-version.log')

    $buildExtra = @()
    if ($Offline) {
        # No sources remain in this temporary NuGet configuration. The installed
        # SDK must already provide its .NET 8 reference/runtime packs.
        $nugetConfig = Join-Path $logs 'NuGet.offline.config'
        Assert-NoReparsePath $nugetConfig
        [IO.File]::WriteAllText($nugetConfig, '<configuration><packageSources><clear /></packageSources></configuration>', [Text.UTF8Encoding]::new($false))
        $buildExtra = @('-p:GraphTargetFramework=net8.0', ('-p:RestoreConfigFile=' + $nugetConfig), '-p:NuGetAudit=false')
    }
    $suiteNames = @('Graph', 'Runtime', 'Windows')
    foreach ($suite in $suiteNames) {
        $name = 'TaintedGrailAccess.' + $suite + '.Tests'
        $project = Join-Path $projectRoot ('tests\' + $name + '\' + $name + '.csproj')
        $buildArgs = @('build', $project, '--configuration', 'Release', '--nologo', '--verbosity', 'minimal') + $buildExtra
        Invoke-LoggedTool $dotnet.Source $buildArgs (Join-Path $logs ($suite + '-build.log'))
        $binary = Join-Path $projectRoot ('tests\' + $name + '\bin\Release\net8.0\' + $name + '.dll')
        Assert-NoReparsePath $binary
        if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw "Не найден тестовый executable: $name" }
        $runArgs = @($binary)
        if ($VerbosePreference -eq 'Continue') { $runArgs += '--verbose' }
        Invoke-LoggedTool $dotnet.Source $runArgs (Join-Path $logs ($suite + '-run.log'))
        Write-Host "$suite`: тесты пройдены."
    }

    # unittest discovery uses absolute paths; caller's working directory is irrelevant.
    $pythonArgs = $pythonPrefix + @('-m', 'unittest', 'discover', '-s', (Join-Path $projectRoot 'tests'), '-p', 'test_*.py')
    if ($VerbosePreference -eq 'Continue') { $pythonArgs += '-v' }
    Invoke-LoggedTool $python.Source $pythonArgs (Join-Path $logs 'Python-run.log')
    Write-Host 'Python: тесты инвентаризации пройдены.'
    Write-Host "Журналы: $logs. Игра и native-речь не проверялись."
    if ($Offline) { Write-Verbose 'Offline проверяет .NET 8 модель. Production netstandard2.0 и игровой host нужно собирать отдельно.' }
    exit 0
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
