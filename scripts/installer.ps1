param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipPackage
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$issPath = Join-Path $root "installer\MDRedactor.iss"
$publishPath = Join-Path $root "artifacts\publish\win-x64"
$installerPath = Join-Path $root "artifacts\installer\MDRedactorSetup-x64.exe"

function Assert-NativeSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Step
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Step завершился с кодом $LASTEXITCODE."
    }
}

function Test-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    return [bool](Get-Command -Name $Name -ErrorAction SilentlyContinue)
}

function Install-WithWinget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageId,

        [switch]$UserScope
    )

    if (-not (Test-Command -Name "winget")) {
        throw "winget не найден. Невозможно автоматически установить пакет $PackageId."
    }

    $arguments = @(
        "install",
        "--id", $PackageId,
        "--exact",
        "--source", "winget",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--silent"
    )

    if ($UserScope) {
        $arguments += @("--scope", "user")
    }

    & winget @arguments
    return $LASTEXITCODE -eq 0
}

function Get-InnoSetupCompiler {
    $command = Get-Command -Name "ISCC.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"
    }

    $localInnoPath = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
    $candidates += $localInnoPath

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Ensure-InnoSetupCompiler {
    $compilerPath = Get-InnoSetupCompiler
    if ($null -ne $compilerPath) {
        Write-Host "Inno Setup найден: $compilerPath"
        return $compilerPath
    }

    Write-Host "Inno Setup не найден. Пробую установить JRSoftware.InnoSetup через winget."
    if (-not (Install-WithWinget -PackageId "JRSoftware.InnoSetup" -UserScope)) {
        Write-Host "Установка Inno Setup в профиль пользователя не удалась. Пробую стандартную установку winget."
        if (-not (Install-WithWinget -PackageId "JRSoftware.InnoSetup")) {
            throw "Не удалось установить Inno Setup."
        }
    }

    $compilerPath = Get-InnoSetupCompiler
    if ($null -eq $compilerPath) {
        throw "ISCC.exe не найден после установки Inno Setup. Перезапустите терминал и повторите сборку."
    }

    Write-Host "Inno Setup найден: $compilerPath"
    return $compilerPath
}

if ($Runtime -ne "win-x64") {
    throw "Инсталлятор MVP поддерживает только Runtime=win-x64."
}

if (-not (Test-Path -LiteralPath $issPath)) {
    throw "Не найден сценарий Inno Setup: $issPath"
}

& (Join-Path $PSScriptRoot "bootstrap.ps1")

$compiler = Ensure-InnoSetupCompiler

if (-not $SkipPackage) {
    & (Join-Path $PSScriptRoot "package.ps1") -Configuration $Configuration -Runtime $Runtime
    Assert-NativeSuccess -Step "scripts\package.ps1"
}

$publishedExe = Join-Path $publishPath "MDRedactor.App.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Не найден publish output: $publishedExe"
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $installerPath)) | Out-Null

& $compiler $issPath
Assert-NativeSuccess -Step "ISCC.exe"

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Инсталлятор не создан: $installerPath"
}

Write-Host "Инсталлятор готов: $installerPath"
