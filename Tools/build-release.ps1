param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$ProductVersion = '0.1.3')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $repoRoot 'artifacts'
$publishPath = Join-Path $artifactRoot ('publish-' + $ProductVersion)
$installerOutput = Join-Path $artifactRoot ('installer-' + $ProductVersion)
# Delete only this script's staging directory, inside the repository artifacts.
if (Test-Path -LiteralPath $publishPath) {
  $resolvedPublish = [IO.Path]::GetFullPath($publishPath)
  $resolvedArtifacts = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\') + '\'
  if (-not $resolvedPublish.StartsWith($resolvedArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path' }
  Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
dotnet publish (Join-Path $repoRoot 'LayoutPreviewPrototype.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$ProductVersion" "-p:AssemblyVersion=$ProductVersion.0" "-p:FileVersion=$ProductVersion.0" -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed' }
# File IDs depend on relative paths, and remain stable across releases.
$ns = 'http://wixtoolset.org/schemas/v4/wxs'
$doc = [System.Xml.Linq.XDocument]::new()
$wix = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('Wix', $ns))
$fragment = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('Fragment', $ns))
$group = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('ComponentGroup', $ns))
$group.SetAttributeValue('Id','ApplicationFiles')
$group.SetAttributeValue('Directory','INSTALLFOLDER')
$sha = [Security.Cryptography.SHA256]::Create()
try {
  foreach ($file in Get-ChildItem -LiteralPath $publishPath -File -Recurse | Sort-Object FullName) {
    if ($file.Extension -eq '.pdb') { continue }
    $relative = $file.FullName.Substring($publishPath.Length).TrimStart('\')
    $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('TypeAllBack.x64.' + $relative.ToLowerInvariant()))
    $id = 'F' + [BitConverter]::ToString($hash).Replace('-','').Substring(0,24)
    $component = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('Component',$ns))
    $component.SetAttributeValue('Id',$id)
    $component.SetAttributeValue('Guid', [Guid]::new([byte[]]$hash[0..15]).ToString())
    $parent = [IO.Path]::GetDirectoryName($relative)
    if ($parent) { $component.SetAttributeValue('Subdirectory',$parent) }
    $payload = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('File',$ns))
    $payload.SetAttributeValue('Id', $(if ($relative -eq 'TypeAllBack.exe') { 'MainExecutable' } else { $id + '_File' }))
    $payload.SetAttributeValue('Source',$file.FullName)
    $component.Add($payload)
    $key = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('RegistryValue',$ns))
    $key.SetAttributeValue('Root','HKCU'); $key.SetAttributeValue('Key','Software\TypeAllBack\Installer\Files')
    $key.SetAttributeValue('Name',$id); $key.SetAttributeValue('Value','1'); $key.SetAttributeValue('Type','integer'); $key.SetAttributeValue('KeyPath','yes')
    $component.Add($key)
    $remove = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get('RemoveFolder',$ns))
    $remove.SetAttributeValue('Id',$id + '_Folder'); $remove.SetAttributeValue('On','uninstall')
    $component.Add($remove); $group.Add($component)
  }
} finally { $sha.Dispose() }
$fragment.Add($group); $wix.Add($fragment); $doc.Add($wix)
$doc.Save((Join-Path $repoRoot 'Installer\GeneratedFiles.wxs'))
dotnet build (Join-Path $repoRoot 'Installer\TypeAllBack.Installer.wixproj') -c Release "-p:ProductVersion=$ProductVersion" "-p:PublishDir=$publishPath" "-p:OutputPath=$installerOutput\"
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed' }
Get-ChildItem -LiteralPath $installerOutput -Filter '*.msi' | Select-Object FullName,Length
