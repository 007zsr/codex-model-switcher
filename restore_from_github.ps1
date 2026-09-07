param(
    [string]$Repo = '007zsr/codex-transfer-20260907-0fff7d79',
    [string]$Tag = 'transfer-20260907-0fff7d79',
    [string]$AssetName = 'payload.enc',
    [string]$WorkDir = (Join-Path $env:USERPROFILE 'Downloads\codex-private-transfer-20260907-0fff7d79'),
    [string]$WorkspaceRoot,
    [string]$DesktopRoot,
    [switch]$SkipNetworkCleanup
)

$ErrorActionPreference = 'Stop'

function Get-PlainTextFromSecureString {
    param([Parameter(Mandatory = $true)][securestring]$SecureString)

    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

function Require-Command {
    param([Parameter(Mandatory = $true)][string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

$cryptoScript = Join-Path $PSScriptRoot 'crypto_payload.ps1'
$cleanupScript = Join-Path $PSScriptRoot 'cleanup_network_copy.ps1'

if (-not (Test-Path -LiteralPath $cryptoScript)) {
    throw "Missing helper script: $cryptoScript"
}

Require-Command gh
Require-Command tar

gh auth status | Out-Null

New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$downloadDir = Join-Path $WorkDir 'download'
$extractDir = Join-Path $WorkDir 'extracted'
New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null
New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

Write-Host "Downloading encrypted payload from $Repo release $Tag ..."
gh release download $Tag --repo $Repo --pattern $AssetName --dir $downloadDir --clobber

$encryptedFile = Join-Path $downloadDir $AssetName
if (-not (Test-Path -LiteralPath $encryptedFile)) {
    throw "Encrypted payload was not downloaded: $encryptedFile"
}

$tarFile = Join-Path $WorkDir 'payload.tar.gz'
$secure = Read-Host 'Enter migration key' -AsSecureString
$key = Get-PlainTextFromSecureString -SecureString $secure

Write-Host 'Decrypting payload ...'
powershell -ExecutionPolicy Bypass -File $cryptoScript -Mode Decrypt -InputFile $encryptedFile -OutputFile $tarFile -Passphrase $key

Write-Host 'Extracting payload ...'
tar -xzf $tarFile -C $extractDir

$privateRestore = Get-ChildItem -LiteralPath $extractDir -Recurse -Force -File -Filter 'restore_after_decrypt.ps1' |
    Select-Object -First 1

if (-not $privateRestore) {
    throw 'Private restore script was not found inside decrypted payload.'
}

$restoreArgs = @('-ExecutionPolicy', 'Bypass', '-File', $privateRestore.FullName)
if ($WorkspaceRoot) {
    $restoreArgs += @('-WorkspaceRoot', $WorkspaceRoot)
}
if ($DesktopRoot) {
    $restoreArgs += @('-DesktopRoot', $DesktopRoot)
}

Write-Host 'Restoring files locally ...'
powershell @restoreArgs

if (-not $SkipNetworkCleanup) {
    if (Test-Path -LiteralPath $cleanupScript) {
        Write-Host 'Cleaning network transfer copy ...'
        powershell -ExecutionPolicy Bypass -File $cleanupScript -Repo $Repo -Tag $Tag -DeleteRepo
    }
    else {
        Write-Warning "Cleanup helper not found: $cleanupScript"
    }
}

Write-Host 'Restore complete.'

