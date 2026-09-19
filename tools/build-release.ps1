$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$version = '2.7.1'
$output = Join-Path $root 'artifacts'
$package = Join-Path $output ('CodexModelSwitcher-' + $version + '-windows')
if (Test-Path -LiteralPath $package) { throw 'Release staging directory already exists. Choose a fresh staging directory before rebuilding.' }
New-Item -ItemType Directory -Path $package -Force | Out-Null
$files = @('CodexModelSwitcher.exe','Program.cs','Launcher.ps1','build.ps1','create-shortcut.ps1',
    'deepseek-models.json','kimi-models.json','README.md','DOWNLOAD.md','CHANGELOG.md')
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $package }
Copy-Item -LiteralPath (Join-Path $root 'assets') -Destination $package -Recurse
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $package -Recurse
$zip = Join-Path $output ('CodexModelSwitcher-' + $version + '-windows.zip')
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), $hash + '  ' + [IO.Path]::GetFileName($zip) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $zip
