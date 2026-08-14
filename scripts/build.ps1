[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'

function Invoke-Dotnet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

Invoke-Dotnet build-server shutdown
Invoke-Dotnet restore CodexTokenBar.sln --disable-parallel
Invoke-Dotnet build CodexTokenBar.sln -c Debug --no-restore --disable-build-servers '-m:1'

$testDll = Join-Path $root 'tests\CodexTokenBar.Tests\bin\Debug\net8.0-windows\CodexTokenBar.Tests.dll'
& dotnet $testDll
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE"
}

Invoke-Dotnet build CodexTokenBar.sln -c Release --no-restore --disable-build-servers '-m:1'

Invoke-Dotnet restore src\CodexTokenBar\CodexTokenBar.csproj `
    -r win-x64 --disable-parallel

$publishDirectory = Join-Path $root 'artifacts\win-x64'
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

Invoke-Dotnet publish src\CodexTokenBar\CodexTokenBar.csproj `
    -c Release -r win-x64 --self-contained true --no-restore `
    '-p:PublishSingleFile=true' `
    '-p:IncludeNativeLibrariesForSelfExtract=true' `
    '-p:DebugType=None' `
    '-p:DebugSymbols=false' `
    --disable-build-servers '-m:1' `
    "--output=$publishDirectory"

$files = @(Get-ChildItem -LiteralPath $publishDirectory -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'CodexTokenBar.exe') {
    $names = $files.Name -join ', '
    throw "Publish output must contain only CodexTokenBar.exe; found: $names"
}

$hash = Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256
[pscustomobject]@{
    Path = $files[0].FullName
    Bytes = $files[0].Length
    SHA256 = $hash.Hash
} | Format-List
