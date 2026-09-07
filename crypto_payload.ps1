param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Encrypt', 'Decrypt')]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string]$InputFile,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile,

    [string]$Passphrase
)

$ErrorActionPreference = 'Stop'

$MagicText = 'CDXMIG1'
$Iterations = 200000

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

function New-RandomBytes {
    param([Parameter(Mandatory = $true)][int]$Length)

    $bytes = New-Object byte[] $Length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }
    return $bytes
}

function Get-DerivedKeys {
    param(
        [Parameter(Mandatory = $true)][string]$Passphrase,
        [Parameter(Mandatory = $true)][byte[]]$Salt
    )

    $derive = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($Passphrase, $Salt, $Iterations)
    try {
        [byte[]]$all = $derive.GetBytes(64)
    }
    finally {
        $derive.Dispose()
    }

    return [pscustomobject]@{
        AesKey = [byte[]]$all[0..31]
        MacKey = [byte[]]$all[32..63]
    }
}

function Join-Bytes {
    param([Parameter(ValueFromRemainingArguments = $true)][byte[][]]$Parts)

    $length = 0
    foreach ($part in $Parts) {
        $length += $part.Length
    }

    $result = New-Object byte[] $length
    $offset = 0
    foreach ($part in $Parts) {
        [Buffer]::BlockCopy($part, 0, $result, $offset, $part.Length)
        $offset += $part.Length
    }
    return $result
}

function Test-BytesEqual {
    param([byte[]]$Left, [byte[]]$Right)

    if ($Left.Length -ne $Right.Length) {
        return $false
    }

    $diff = 0
    for ($i = 0; $i -lt $Left.Length; $i++) {
        $diff = $diff -bor ($Left[$i] -bxor $Right[$i])
    }
    return $diff -eq 0
}

function Protect-File {
    param(
        [Parameter(Mandatory = $true)][string]$InputFile,
        [Parameter(Mandatory = $true)][string]$OutputFile,
        [Parameter(Mandatory = $true)][string]$Passphrase
    )

    [byte[]]$plain = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $InputFile))
    [byte[]]$salt = New-RandomBytes 16
    [byte[]]$iv = New-RandomBytes 16
    [byte[]]$magic = [Text.Encoding]::ASCII.GetBytes($MagicText)
    $keys = Get-DerivedKeys -Passphrase $Passphrase -Salt $salt

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $keys.AesKey
        $aes.IV = $iv

        $encryptor = $aes.CreateEncryptor()
        try {
            [byte[]]$cipher = $encryptor.TransformFinalBlock($plain, 0, $plain.Length)
        }
        finally {
            $encryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    [byte[]]$body = Join-Bytes $magic $salt $iv $cipher
    $hmac = New-Object System.Security.Cryptography.HMACSHA256 -ArgumentList (,$keys.MacKey)
    try {
        [byte[]]$tag = $hmac.ComputeHash($body)
    }
    finally {
        $hmac.Dispose()
    }

    [byte[]]$out = Join-Bytes $body $tag
    $parent = Split-Path -Parent $OutputFile
    if ($parent) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    [IO.File]::WriteAllBytes($OutputFile, $out)
}

function Unprotect-File {
    param(
        [Parameter(Mandatory = $true)][string]$InputFile,
        [Parameter(Mandatory = $true)][string]$OutputFile,
        [Parameter(Mandatory = $true)][string]$Passphrase
    )

    [byte[]]$data = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $InputFile))
    [byte[]]$magic = [Text.Encoding]::ASCII.GetBytes($MagicText)
    $minimumLength = $magic.Length + 16 + 16 + 1 + 32
    if ($data.Length -lt $minimumLength) {
        throw 'Encrypted payload is too small or corrupt.'
    }

    [byte[]]$actualMagic = New-Object byte[] $magic.Length
    [Buffer]::BlockCopy($data, 0, $actualMagic, 0, $magic.Length)
    if (-not (Test-BytesEqual $magic $actualMagic)) {
        throw 'Encrypted payload magic header mismatch.'
    }

    [byte[]]$salt = New-Object byte[] 16
    [byte[]]$iv = New-Object byte[] 16
    [Buffer]::BlockCopy($data, $magic.Length, $salt, 0, 16)
    [Buffer]::BlockCopy($data, $magic.Length + 16, $iv, 0, 16)

    $cipherLength = $data.Length - $magic.Length - 16 - 16 - 32
    [byte[]]$cipher = New-Object byte[] $cipherLength
    [byte[]]$expectedTag = New-Object byte[] 32
    [Buffer]::BlockCopy($data, $magic.Length + 32, $cipher, 0, $cipherLength)
    [Buffer]::BlockCopy($data, $data.Length - 32, $expectedTag, 0, 32)

    [byte[]]$body = New-Object byte[] ($data.Length - 32)
    [Buffer]::BlockCopy($data, 0, $body, 0, $body.Length)

    $keys = Get-DerivedKeys -Passphrase $Passphrase -Salt $salt
    $hmac = New-Object System.Security.Cryptography.HMACSHA256 -ArgumentList (,$keys.MacKey)
    try {
        [byte[]]$actualTag = $hmac.ComputeHash($body)
    }
    finally {
        $hmac.Dispose()
    }

    if (-not (Test-BytesEqual $expectedTag $actualTag)) {
        throw 'Encrypted payload authentication failed. The key may be wrong or the file may be corrupt.'
    }

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.KeySize = 256
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = $keys.AesKey
        $aes.IV = $iv

        $decryptor = $aes.CreateDecryptor()
        try {
            [byte[]]$plain = $decryptor.TransformFinalBlock($cipher, 0, $cipher.Length)
        }
        finally {
            $decryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    $parent = Split-Path -Parent $OutputFile
    if ($parent) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    [IO.File]::WriteAllBytes($OutputFile, $plain)
}

if (-not $Passphrase) {
    $secure = Read-Host 'Enter migration key' -AsSecureString
    $Passphrase = Get-PlainTextFromSecureString -SecureString $secure
}

if ($Mode -eq 'Encrypt') {
    Protect-File -InputFile $InputFile -OutputFile $OutputFile -Passphrase $Passphrase
}
else {
    Unprotect-File -InputFile $InputFile -OutputFile $OutputFile -Passphrase $Passphrase
}
