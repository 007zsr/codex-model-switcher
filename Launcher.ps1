$ErrorActionPreference = 'Stop'

$appDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$executablePath = Join-Path $appDirectory 'CodexModelSwitcher.exe'
$sourcePath = Join-Path $appDirectory 'Program.cs'
$scriptArguments = [string[]]$args
$dataDirectory = if ([string]::IsNullOrWhiteSpace($env:CODEX_MODEL_SWITCHER_DATA_HOME)) {
    Join-Path $appDirectory 'data'
} else {
    [Environment]::ExpandEnvironmentVariables($env:CODEX_MODEL_SWITCHER_DATA_HOME)
}
$logsDirectory = Join-Path $dataDirectory 'logs'
$logPath = Join-Path $logsDirectory ('launcher-' + (Get-Date -Format 'yyyyMMdd') + '.log')

function Write-LauncherLog {
    param([string]$Level, [string]$Message)
    try {
        [IO.Directory]::CreateDirectory($logsDirectory) | Out-Null
        $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'), $Level, $Message
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    } catch {
        # A logging failure must never prevent the application from starting.
    }
}

function Start-DirectApplication {
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        Write-LauncherLog 'WARN' 'Primary EXE is missing; switching to compatibility mode.'
        return $false
    }

    try {
        $startParameters = @{
            FilePath = $executablePath
            WorkingDirectory = $appDirectory
            PassThru = $true
        }
        if ($scriptArguments.Count -gt 0) { $startParameters.ArgumentList = $scriptArguments }
        $process = Start-Process @startParameters
        Write-LauncherLog 'INFO' ('Primary EXE started; PID=' + $process.Id + '.')

        # Tests have no window: wait for and forward their real result.
        if ($scriptArguments.Count -gt 0 -and $scriptArguments[0] -eq '--self-test') {
            $process.WaitForExit()
            if ($process.ExitCode -eq 0) {
                Write-LauncherLog 'INFO' 'Primary EXE self-test passed.'
                $script:launcherExitCode = 0
                return $true
            }
            Write-LauncherLog 'WARN' ('Primary EXE self-test failed; exit=' + $process.ExitCode + '; switching to compatibility mode.')
            return $false
        }

        # A successful desktop launch should create a top-level window. Allow slower Windows
        # machines enough time before deciding that the process is stuck during startup.
        $deadline = [DateTime]::UtcNow.AddSeconds(12)
        while ([DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
            if ($process.HasExited) {
                if ($process.ExitCode -eq 0) {
                    Write-LauncherLog 'INFO' 'Primary EXE ended normally; compatibility mode is not needed.'
                    $script:launcherExitCode = 0
                    return $true
                }
                Write-LauncherLog 'WARN' ('Primary EXE exited during startup; exit=' + $process.ExitCode + '; switching to compatibility mode.')
                return $false
            }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                Write-LauncherLog 'INFO' 'Primary EXE window detected; startup succeeded.'
                $script:launcherExitCode = 0
                return $true
            }
        }

        Write-LauncherLog 'WARN' 'Primary EXE created no window in 12 seconds; stopping it and switching to compatibility mode.'
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        try { $process.WaitForExit(3000) | Out-Null } catch { }
        return $false
    } catch {
        Write-LauncherLog 'WARN' ('Primary EXE could not start; switching to compatibility mode: ' + $_.Exception.Message)
        return $false
    }
}

function Start-CompatibleApplication {
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Primary startup failed and the compatibility source is missing: $sourcePath"
    }

    # PowerShell hosts the UI in-process, so tell the program where its own model catalogs,
    # documentation and assets live.
    $env:CODEX_MODEL_SWITCHER_APP_HOME = $appDirectory
    Write-LauncherLog 'INFO' 'Starting PowerShell compatibility mode.'

    $types = Add-Type -Path $sourcePath -ReferencedAssemblies @(
        'System.dll',
        'System.Core.dll',
        'System.Drawing.dll',
        'System.Windows.Forms.dll',
        'System.Web.Extensions.dll'
    ) -PassThru -WarningAction SilentlyContinue

    $assembly = $types[0].Assembly
    $programType = $assembly.GetType('CodexModelSwitcher.Program', $true)
    $flags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic
    $mainMethod = $programType.GetMethod('Main', $flags)
    if ($null -eq $mainMethod) { throw 'Compatibility mode could not find the application entry point.' }

    $null = $mainMethod.Invoke($null, [object[]](,$scriptArguments))
    $script:launcherExitCode = [Environment]::ExitCode
    Write-LauncherLog 'INFO' ('PowerShell compatibility mode ended; exit=' + $script:launcherExitCode + '.')
}

$launcherExitCode = 1
if (Start-DirectApplication) { exit $launcherExitCode }

try {
    Start-CompatibleApplication
    exit $launcherExitCode
} catch {
    Write-LauncherLog 'ERROR' ('Both startup methods failed: ' + $_.Exception.ToString())
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [Windows.Forms.MessageBox]::Show(
            "Both normal and compatibility startup failed.`r`n`r`n$($_.Exception.Message)`r`n`r`nLog: $logPath",
            'Codex Model Switcher',
            [Windows.Forms.MessageBoxButtons]::OK,
            [Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    } catch { }
    exit 1
}
