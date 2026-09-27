param([string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\快速安装包'))
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$payload=Join-Path $PSScriptRoot '.payload'
& (Join-Path $project 'client\build.ps1') -OutputDirectory $payload
if($LASTEXITCODE -ne 0){throw 'Payload build failed'}
$encoding=[Text.UTF8Encoding]::new($true)
foreach($name in @('RegisterInstall.ps1','Uninstall.ps1')){[IO.File]::WriteAllText((Join-Path $payload $name),[IO.File]::ReadAllText((Join-Path $PSScriptRoot $name)),$encoding)}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '给使用者.txt') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $project 'LICENSE') -Destination (Join-Path $payload 'LICENSE.txt') -Force
$allowed=@('LICENSE.txt','DSH桌面提示音.exe','DSH桌面提示音.exe.config','Setup.ps1','compatibility.json','THIRD_PARTY_NOTICES.md','RegisterInstall.ps1','Uninstall.ps1','给使用者.txt','assets\LICENSE-tarkov.txt','assets\done.m4a','assets\approval.m4a','assets\error.m4a','assets\done.wav','assets\approval.wav','assets\error.wav')
$rows=@(foreach($name in $allowed){$f=Get-Item -LiteralPath (Join-Path $payload $name);@{Path=$name.Replace('\','/');Sha256=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant();Bytes=$f.Length}})
$manifest=@{Product='DSHDesktopSounds';Version='1.0.0';Files=$rows}
[IO.File]::WriteAllText((Join-Path $payload 'install-manifest.json'),($manifest|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$zip=Join-Path $PSScriptRoot '.payload.zip'
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
$archive=[IO.Compression.ZipFile]::Open($zip,'Create')
try{foreach($name in ($allowed+@('install-manifest.json'))){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $payload $name),$name.Replace('\','/'),'Optimal')}}finally{$archive.Dispose()}
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
$out=Join-Path $OutputDirectory 'DSH桌面提示音_一键安装.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /utf8output /target:winexe /platform:x64 /optimize+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll ('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')) ('/resource:'+$zip+',payload.zip') ('/out:'+$out) (Join-Path $PSScriptRoot 'Installer.cs')
if($LASTEXITCODE -ne 0){throw 'Installer compile failed'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '给使用者.txt') -Destination (Join-Path $OutputDirectory '安装说明.txt') -Force
$hash=(Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256.txt'),($hash+'  '+[IO.Path]::GetFileName($out)+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))
[void](New-Item -ItemType Directory -Path (Join-Path $project 'build-results') -Force)
@{at=[DateTime]::UtcNow.ToString('o');installer=$out;sha256=$hash;bytes=(Get-Item -LiteralPath $out).Length;payload=$manifest;dataIncluded=$false}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $project 'build-results\package-build.json') -Encoding UTF8
Write-Output $out
