$ErrorActionPreference = 'Stop'

$projectDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$source = Join-Path $projectDirectory 'Program.cs'
$output = Join-Path $projectDirectory 'CodexModelSwitcher.exe'

if ([string]::IsNullOrWhiteSpace($compiler)) {
    throw 'C# compiler not found. Install or enable .NET Framework 4.x, then try again.'
}

# The icon lives inside this folder (assets\), so the project can be moved anywhere. Regenerate it
# with: powershell -ExecutionPolicy Bypass -File tools\make-assets.ps1
$icon = Join-Path $projectDirectory 'assets\app-icon.ico'
$iconArgument = @()
if (Test-Path -LiteralPath $icon) {
    $iconArgument = @("/win32icon:$icon")
} else {
    Write-Warning "Icon not found: $icon (the executable will use the default icon)"
}

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 `
    /reference:System.Web.Extensions.dll /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    $iconArgument "/out:$output" $source

if ($LASTEXITCODE -ne 0) {
    throw "Build failed. Exit code: $LASTEXITCODE"
}

Write-Host "Built: $output" -ForegroundColor Green
