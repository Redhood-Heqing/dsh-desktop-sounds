$ErrorActionPreference='Stop'
$base=[IO.Path]::GetFullPath($PSScriptRoot)
$exe=Join-Path $base 'DSH桌面提示音.exe'
$manifest=Get-Content -LiteralPath (Join-Path $base 'install-manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.Product -ne 'DSHDesktopSounds' -or -not (Test-Path -LiteralPath $exe)){throw '不是 DSH 安装目录'}
@{Product='DSHDesktopSounds';Directory=$base}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $base 'data\install-identity.json') -Encoding UTF8
$programs=Join-Path ([Environment]::GetFolderPath('Programs')) 'DSH 桌面提示音'
[void](New-Item -ItemType Directory -Path $programs -Force)
$shell=New-Object -ComObject WScript.Shell
try{
 foreach($legacy in @('安装或修复.lnk','卸载并恢复通知.lnk')){
  $old=Join-Path $base $legacy
  if(Test-Path -LiteralPath $old){$shortcut=$shell.CreateShortcut($old);if($shortcut.Arguments.Contains((Join-Path $base 'Setup.ps1'))){Remove-Item -LiteralPath $old}}
 }
 $link=$shell.CreateShortcut((Join-Path $programs 'DSH 桌面提示音.lnk'));$link.TargetPath=$exe;$link.WorkingDirectory=$base;$link.Save()
 $link=$shell.CreateShortcut((Join-Path $programs '卸载并恢复通知.lnk'));$link.TargetPath=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe';$link.Arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $base 'Uninstall.ps1')+'"';$link.WorkingDirectory=$base;$link.WindowStyle=7;$link.Save()
}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\DSHDesktopSounds')
try{
 $key.SetValue('DisplayName','DSH 桌面提示音');$key.SetValue('DisplayVersion',$manifest.Version);$key.SetValue('Publisher','DSH 桌面提示音项目');$key.SetValue('InstallLocation',$base);$key.SetValue('DisplayIcon',$exe)
 $key.SetValue('UninstallString','"'+(Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe')+'" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $base 'Uninstall.ps1')+'"')
 $key.SetValue('NoModify',1,[Microsoft.Win32.RegistryValueKind]::DWord);$key.SetValue('NoRepair',1,[Microsoft.Win32.RegistryValueKind]::DWord)
 $key.SetValue('EstimatedSize',[int][Math]::Ceiling(($manifest.Files|Measure-Object -Property Bytes -Sum).Sum/1024),[Microsoft.Win32.RegistryValueKind]::DWord)
}finally{$key.Dispose()}
