param([Parameter(Mandatory=$true)][string]$MsiPath, [string]$ProductVersion = '0.1.4')
$ErrorActionPreference='Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase((Resolve-Path -LiteralPath $MsiPath).Path,0)
function Read-Rows([string]$query) {
  $view = $db.OpenView($query)
  try {
    $view.Execute() | Out-Null
    while ($record = $view.Fetch()) {
      $fieldCount = $record.GetType().InvokeMember('FieldCount',[Reflection.BindingFlags]::GetProperty,$null,$record,$null)
      $cells = [string[]]::new($fieldCount)
      for ($i=1;$i -le $fieldCount;$i++) { $cells[$i-1]=$record.StringData($i) }
      Write-Output -NoEnumerate $cells
      [Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null
    }
  } finally { $view.Close() | Out-Null; [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null }
}
try {
  $props=@{}
  foreach ($row in Read-Rows 'SELECT `Property`, `Value` FROM `Property`') { $props[$row[0]]=$row[1] }
  if ($props.ProductName -ne 'TypeAllBack' -or $props.ProductVersion -ne $ProductVersion) { throw 'Incorrect product identity' }
  if ($props.ALLUSERS) { throw 'Package is not strictly per-user' }
  if ($props.UpgradeCode -ne '{B43F914B-3100-4C9B-A9A1-BD9F5E8BD4CA}') { throw 'Upgrade identity changed' }
  $files=@(Read-Rows 'SELECT `FileName` FROM `File`')
  if (-not ($files | Where-Object { $_[0] -match 'TypeAllBack\.exe$' })) { throw 'Application missing' }
  if (-not ($files | Where-Object { $_[0] -match 'hostfxr\.dll$' })) { throw 'Self-contained runtime missing' }
  if ($files | Where-Object { $_[0] -match 'settings\.json$' }) { throw 'Installer overwrites user settings' }
  $actions=@{}
  foreach ($row in Read-Rows 'SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`') { $actions[$row[0]]=[int]$row[1] }
  if ($actions.RemoveExistingProducts -le $actions.InstallInitialize -or $actions.RemoveExistingProducts -ge $actions.InstallFiles) { throw 'Upgrade is outside rollback transaction' }
  if ($actions.CleanupAutostart -ge $actions.RemoveFiles) { throw 'Cleanup executable removed too early' }
  foreach($row in Read-Rows 'SELECT `FileName` FROM `RemoveFile`') { if($row[0]) { throw 'Unexpected wildcard deletion' } }
  "PASS: MSI identity/per-user scope, $($files.Count) files, bundled runtime, upgrade sequence, settings preserved, cleanup before file removal"
  $summary=$db.SummaryInformation(0)
  "Platform: $($summary.Property(7)); version: $($props.ProductVersion)"
  [Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) | Out-Null
} finally {
  [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) | Out-Null
  [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
}
