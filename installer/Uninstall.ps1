param([switch]$NonInteractive)
$ErrorActionPreference='Stop'
$env:PSModulePath=(Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\Modules')+';'+(Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules')
Add-Type -AssemblyName System.Windows.Forms
$base=[IO.Path]::GetFullPath($PSScriptRoot)
try{
 $manifest=Get-Content -LiteralPath (Join-Path $base 'install-manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
 if($manifest.Product -ne 'DSHDesktopSounds'){throw '安装标识不匹配，未删除文件。'}
 foreach($row in $manifest.Files){
  $path=[IO.Path]::GetFullPath((Join-Path $base $row.Path))
  if(-not $path.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw '安装清单路径超出目录'}
  for($parent=[IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($path));$null -ne $parent;$parent=$parent.Parent){if($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw '安装目录含链接，未删除任何文件。'}}
 }
 if(-not $NonInteractive -and [Windows.Forms.MessageBox]::Show('卸载提示音并恢复本程序修改的 GPT 通知设置？你的导入音频和设置将保留。','卸载 DSH 桌面提示音','YesNo','Question') -ne 'Yes'){exit 0}
 & (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $base 'Setup.ps1') -Mode Uninstall -NonInteractive
 if($LASTEXITCODE -ne 0){throw '通知恢复未完成，保留安装文件以便修复。'}
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\DSHDesktopSounds')
 $owned=$false;try{if($key){$owned=$key.GetValue('InstallLocation') -eq $base}}finally{if($key){$key.Dispose()}}
 if($owned){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\DSHDesktopSounds',$false)}
 $programs=Join-Path ([Environment]::GetFolderPath('Programs')) 'DSH 桌面提示音'
 foreach($name in @('DSH 桌面提示音.lnk','卸载并恢复通知.lnk')){if($owned -and (Test-Path -LiteralPath (Join-Path $programs $name))){Remove-Item -LiteralPath (Join-Path $programs $name)}}
 if($owned -and (Test-Path -LiteralPath $programs) -and @(Get-ChildItem -LiteralPath $programs -Force).Count -eq 0){Remove-Item -LiteralPath $programs}
 $preserved=@()
 foreach($row in $manifest.Files){
  $path=[IO.Path]::GetFullPath((Join-Path $base $row.Path))
  if(-not $path.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw '安装清单路径超出目录'}
  if(Test-Path -LiteralPath $path){$file=Get-Item -LiteralPath $path;if($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw '安装文件类型异常'};if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $row.Sha256){Remove-Item -LiteralPath $path}else{$preserved+=$row.Path}}
 }
 Remove-Item -LiteralPath (Join-Path $base 'install-manifest.json')
 $assets=Join-Path $base 'assets';if((Test-Path -LiteralPath $assets) -and @(Get-ChildItem -LiteralPath $assets -Force).Count -eq 0){Remove-Item -LiteralPath $assets}
 $record=@{at=[DateTime]::UtcNow.ToString('o');status='PASS';programRemoved=(-not (Test-Path -LiteralPath (Join-Path $base 'DSH桌面提示音.exe')));preservedModifiedFiles=$preserved;keptUserData=$true}
 $record|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $base 'data\uninstall-result.json') -Encoding UTF8
 if(-not $NonInteractive){[void][Windows.Forms.MessageBox]::Show('已卸载并恢复通知。导入音频和设置保留在原安装目录的 data 文件夹。','卸载完成')}
}catch{if(-not $NonInteractive){[void][Windows.Forms.MessageBox]::Show($_.Exception.Message,'卸载未完成')};Write-Error $_;exit 1}
