param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $root "MDRedactor.sln"
$editorPath = Join-Path $root "web\editor"

function Assert-NativeSuccess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Step
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Step завершился с кодом $LASTEXITCODE."
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
dotnet build $solutionPath --configuration $Configuration --no-restore
Assert-NativeSuccess -Step "dotnet build"
dotnet test $solutionPath --configuration $Configuration --no-build
Assert-NativeSuccess -Step "dotnet test"
