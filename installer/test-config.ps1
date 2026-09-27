$ErrorActionPreference='Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'client\Setup.ps1') -LibraryOnly
$fixture=Join-Path $PSScriptRoot '.config-tests'
if(Test-Path -LiteralPath $fixture){throw 'Preserve prior test fixture'}
[void](New-Item -ItemType Directory -Path $fixture)
$checks=@()
[void](New-Item -ItemType Directory -Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'build-results') -Force)
$cases=@('',"model = `"unchanged`"`n",'[desktop]',"[desktop]`n","[desktop]`nnotifications-sound = `"default`"`nnotifications-turn-mode = `"off`"`n[models]`nvalue = 42`n","[desktop]`r`nnotifications-sound = `"none`" # mine`r`n")
try{
 for($i=0;$i -lt $cases.Count;$i++){
  $config=Join-Path $fixture ('case-'+$i+'.toml');[IO.File]::WriteAllText($config,$cases[$i],[Text.UTF8Encoding]::new($false))
  $before=Read-Preferences
  Write-Preference 'notifications-sound' $before.Sound 'none';Write-Preference 'notifications-turn-mode' $before.Turn 'always'
  $after=Read-Preferences
  if($after.Sound -ne 'none' -or $after.Turn -ne 'always'){throw 'Fresh install defaults failed'}
  Write-Preference 'notifications-sound' 'none' $before.Sound;Write-Preference 'notifications-turn-mode' 'always' $before.Turn
  $restored=Read-Preferences
  if($restored.Sound -ne $before.Sound -or $restored.Turn -ne $before.Turn){throw 'Restore absent/explicit values failed'}
  if($cases[$i].Contains('unchanged') -and -not $restored.Raw.Contains('model = "unchanged"')){throw 'Unrelated model modified'}
  $checks+=@{case=$i;pass=$true}
 }
 foreach($raw in @("[desktop]`nnotifications-sound = `"custom`"`n","[desktop]`nnotifications-sound = `"none`"`n[desktop]`n","[desktop]`nnotifications-turn-mode = `"unknown`"`n")){
  [IO.File]::WriteAllText($config,$raw);$rejected=$false;try{Read-Preferences|Out-Null}catch{$rejected=$true}
  if(-not $rejected -or [IO.File]::ReadAllText($config) -cne $raw){throw 'Ambiguous configuration not preserved'}
  $checks+=@{case='reject ambiguous';pass=$true}
 }
 @{status='PASS';checks=$checks;scope='Real file operations on isolated config fixtures; no live host settings changed'}|ConvertTo-Json -Depth 4|Set-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'build-results\config-tests.json') -Encoding UTF8
}finally{
 if([IO.Path]::GetFullPath($fixture) -ne [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.config-tests'))){throw 'Test cleanup guard'}
 foreach($f in Get-ChildItem -LiteralPath $fixture -File){Remove-Item -LiteralPath $f.FullName}
 if(@(Get-ChildItem -LiteralPath $fixture -Force).Count -eq 0){Remove-Item -LiteralPath $fixture}
}
Write-Output ('PASS '+$checks.Count+' isolated installation/restoration cases')
