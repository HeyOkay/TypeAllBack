param([Parameter(Mandatory=$true)][string]$InstallerPath, [Parameter(Mandatory=$true)][string]$TargetExecutable,
      [Parameter(Mandatory=$true)][int]$ParentProcessId, [Parameter(Mandatory=$true)][string]$LogPath,
      [Parameter(Mandatory=$true)][string]$ExpectedSha256, [Parameter(Mandatory=$true)][string]$ReadyFile)
$ErrorActionPreference = 'Stop'
$readyWritten = $false
function Verify-Installer {
    $inputStream = [IO.File]::OpenRead($InstallerPath)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($hasher.ComputeHash($inputStream)).Replace('-','') }
    finally { $hasher.Dispose(); $inputStream.Dispose() }
    if ($digest -ne $ExpectedSha256) { throw 'Installer checksum changed' }
}
function Start-Application {
    $launch = [Diagnostics.ProcessStartInfo]::new($TargetExecutable)
    $launch.UseShellExecute = $false
    $launch.CreateNoWindow = $true
    $launch.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $applicationProcess = [Diagnostics.Process]::Start($launch)
    if ($applicationProcess) { $applicationProcess.Dispose() }
}
try {
    Verify-Installer
    [IO.File]::WriteAllText($ReadyFile, 'ready')
    $readyWritten = $true
    $parent = $null
    try { $parent = [Diagnostics.Process]::GetProcessById($ParentProcessId) }
    catch [ArgumentException] { }
    if ($parent) {
        try { if (-not $parent.WaitForExit(30000)) { throw 'Application did not close' } }
        finally { $parent.Dispose() }
    }
    Verify-Installer
    $setupInfo = [Diagnostics.ProcessStartInfo]::new([IO.Path]::Combine([Environment]::GetFolderPath('Windows'),'System32','msiexec.exe'))
    $setupInfo.Arguments = '/i "' + $InstallerPath + '" /qn /norestart /l*v "' + $LogPath + '"'
    $setupInfo.UseShellExecute = $false
    $setupInfo.CreateNoWindow = $true
    $setupInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $setup = [Diagnostics.Process]::Start($setupInfo)
    try { $setup.WaitForExit(); $exitCode = $setup.ExitCode } finally { $setup.Dispose() }
    if ($exitCode -notin @(0,3010)) { throw ('Installer exit code: ' + $exitCode) }
    if (-not [IO.File]::Exists($TargetExecutable)) { throw 'Installed executable not found' }
    Start-Application
} catch {
    [IO.File]::WriteAllText($LogPath + '.failure.txt', $_.Exception.Message, [Text.UTF8Encoding]::new($true))
    if (-not $readyWritten) { exit 1 }
    if ([IO.File]::Exists($TargetExecutable)) { Start-Application }
    [void][Reflection.Assembly]::Load('PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35')
    [void][System.Windows.MessageBox]::Show('Обновление не установлено. Если прежняя версия доступна, она запущена снова. Журнал: ' + $LogPath, 'TypeAllBack', 'OK', 'Error')
}
