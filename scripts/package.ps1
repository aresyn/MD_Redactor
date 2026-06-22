param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $root "MDRedactor.sln"
$appProjectPath = Join-Path $root "src\MDRedactor.App\MDRedactor.App.csproj"
$editorPath = Join-Path $root "web\editor"
$publishPath = Join-Path $root "artifacts\publish\$Runtime"
$webDistPath = Join-Path $editorPath "dist"
$publishWebDistPath = Join-Path $publishPath "web\editor\dist"

function Assert-NativeSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Step
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Step завершился с кодом $LASTEXITCODE."
    }
}

function Assert-PathInsideRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathToCheck
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($root)
    $resolvedPath = [System.IO.Path]::GetFullPath($PathToCheck)
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Путь находится вне каталога проекта: $resolvedPath"
    }
}

& (Join-Path $PSScriptRoot "bootstrap.ps1")

Push-Location -LiteralPath $editorPath
try {
    if (Test-Path -LiteralPath "package-lock.json") {
        npm ci
        Assert-NativeSuccess -Step "npm ci"
    }
    else {
        npm install
        Assert-NativeSuccess -Step "npm install"
    }

    npm run build
    Assert-NativeSuccess -Step "npm run build"

    npm test
    Assert-NativeSuccess -Step "npm test"
}
finally {
    Pop-Location
}

dotnet restore $solutionPath
Assert-NativeSuccess -Step "dotnet restore"

dotnet test $solutionPath --configuration $Configuration --no-restore
Assert-NativeSuccess -Step "dotnet test"

Assert-PathInsideRoot -PathToCheck $publishPath
if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

[System.IO.Directory]::CreateDirectory($publishPath) | Out-Null

dotnet publish $appProjectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --output $publishPath `
    -p:PublishSingleFile=false
Assert-NativeSuccess -Step "dotnet publish"

if (-not (Test-Path -LiteralPath (Join-Path $webDistPath "index.html"))) {
    throw "Web-редактор не собран: не найден web\editor\dist\index.html."
}

[System.IO.Directory]::CreateDirectory($publishWebDistPath) | Out-Null
Get-ChildItem -LiteralPath $webDistPath -Force | Copy-Item -Destination $publishWebDistPath -Recurse -Force

if (-not (Test-Path -LiteralPath (Join-Path $publishWebDistPath "index.html"))) {
    throw "В publish output не попал web/editor/dist/index.html."
}

Write-Host "Release-сборка готова: $publishPath"
