$ErrorActionPreference = 'Stop'

$desktopDirectory = [Environment]::GetFolderPath('Desktop')
$targetPath = Join-Path $PSScriptRoot 'CodexModelSwitcher.exe'
$launcherPath = Join-Path $PSScriptRoot 'Launcher.ps1'
$powerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$displayName = 'Codex ' + [char]0x6A21 + [char]0x578B + [char]0x5207 + [char]0x6362 + [char]0x5668
$shortcutPath = Join-Path $desktopDirectory ($displayName + '.lnk')
$oldCompatibilityShortcutPath = Join-Path $desktopDirectory ($displayName + [char]0xFF08 +
    [char]0x517C + [char]0x5BB9 + [char]0x542F + [char]0x52A8 + [char]0xFF09 + '.lnk')

if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
    throw "Application not found: $targetPath"
}
if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw "Smart launcher not found: $launcherPath"
}
if (-not (Test-Path -LiteralPath $powerShellPath -PathType Leaf)) {
    throw "Windows PowerShell not found: $powerShellPath"
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $powerShellPath
$shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "' + $launcherPath + '"'
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.Description = 'Codex Model Switcher: automatic compatibility fallback'
$shortcut.IconLocation = $targetPath + ',0'
$shortcut.Save()

# v2.4.1 combines the two launch methods into this single desktop entry.
if (Test-Path -LiteralPath $oldCompatibilityShortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $oldCompatibilityShortcutPath -Force
}

Write-Output $shortcutPath
