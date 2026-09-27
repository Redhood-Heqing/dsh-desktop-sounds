param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$project=Split-Path $PSScriptRoot -Parent
$out=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $project 'dist\DSH桌面提示音'}
[void](New-Item -ItemType Directory -Path (Join-Path $out 'assets') -Force)
$references=@('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll')
$arguments=@('/nologo','/utf8output','/optimize+','/target:winexe','/platform:x64',('/out:'+(Join-Path $out 'DSH桌面提示音.exe')))
$arguments+=@($references|ForEach-Object {'/reference:'+$_})
$arguments+=@('ClientState.cs','NativeFrame.cs','NativeAdapter.cs','ClientApp.cs','PcmPlayer.cs','HostPreferences.cs','HostLocation.cs'|ForEach-Object {Join-Path $PSScriptRoot $_})
$arguments+=@('EventGate.cs','SoundRouter.cs','QuietPolicy.cs','AudioMeter.cs','CodexState.cs','HostSoundGuard.cs'|ForEach-Object {Join-Path $PSScriptRoot "Core\$_"})
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Client build failed'}
foreach($kind in @('done','approval','error')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\$kind.m4a") -Destination (Join-Path $out "assets\$kind.m4a") -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\LICENSE-tarkov.txt') -Destination (Join-Path $out 'assets\LICENSE-tarkov.txt') -Force
foreach($kind in @('done','approval','error')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot "assets\$kind.wav") -Destination (Join-Path $out "assets\$kind.wav") -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'App.config') -Destination (Join-Path $out 'DSH桌面提示音.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'compatibility.json') -Destination $out -Force
[IO.File]::WriteAllText((Join-Path $out 'Setup.ps1'),[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Setup.ps1')),[Text.UTF8Encoding]::new($true))
$shell=New-Object -ComObject WScript.Shell
foreach($entry in @(@('安装或修复','Install'),@('卸载并恢复通知','Uninstall'))){
 $shortcut=$shell.CreateShortcut((Join-Path $out ($entry[0]+'.lnk')))
 $shortcut.TargetPath=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
 $shortcut.Arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $out 'Setup.ps1')+'" -Mode '+$entry[1]
 $shortcut.WorkingDirectory=$out;$shortcut.WindowStyle=7;$shortcut.Save()
}
[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
Copy-Item -LiteralPath (Join-Path $project 'THIRD_PARTY_NOTICES.md') -Destination $out -Force
Write-Output $out
