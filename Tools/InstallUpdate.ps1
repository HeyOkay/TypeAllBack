param([Parameter(Mandatory=$true)][string]$InstallerPath, [Parameter(Mandatory=$true)][string]$TargetExecutable,
      [Parameter(Mandatory=$true)][int]$ParentProcessId, [Parameter(Mandatory=$true)][string]$LogPath,
      [Parameter(Mandatory=$true)][string]$ExpectedSha256)
$ErrorActionPreference = 'Stop'
try {
    Wait-Process -Id $ParentProcessId -Timeout 30 -ErrorAction SilentlyContinue
    if (Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue) { throw 'Application did not close' }
    if ((Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'Installer checksum changed' }
    $installArgs = '/i "' + $InstallerPath + '" /qn /norestart /l*v "' + $LogPath + '"'
    $setup = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $installArgs -WindowStyle Hidden -Wait -PassThru
    if ($setup.ExitCode -notin @(0,3010)) { throw ('Installer exit code: ' + $setup.ExitCode) }
    if (-not (Test-Path -LiteralPath $TargetExecutable)) { throw 'Installed executable not found' }
    Start-Process -FilePath $TargetExecutable -WindowStyle Hidden
} catch {
    $_.Exception.Message | Out-File -LiteralPath ($LogPath + '.failure.txt') -Encoding utf8
    if (Test-Path -LiteralPath $TargetExecutable) { Start-Process -FilePath $TargetExecutable -WindowStyle Hidden }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('Обновление не установлено. Если прежняя версия доступна, она запущена снова. Журнал: ' + $LogPath, 'TypeAllBack', 'OK', 'Error') | Out-Null
}
