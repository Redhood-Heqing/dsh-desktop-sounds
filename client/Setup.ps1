param([ValidateSet('Install','Uninstall','Check')][string]$Mode='Install',[switch]$NonInteractive,[switch]$LibraryOnly,[string]$ConfigPath)
$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$exe=Join-Path $base 'DSH桌面提示音.exe'
$data=Join-Path $base 'data'
$receipt=Join-Path $data 'host-setup.json'
$config=$ConfigPath
if($config -and (-not [IO.Path]::IsPathRooted($config) -or -not (Test-Path -LiteralPath $config))){throw '客户端配置路径无效。'}
foreach($location in @('host-location.json','host-setup.json')){
 $file=Join-Path $data $location
 if(Test-Path -LiteralPath $file){$saved=Get-Content -LiteralPath $file -Raw -Encoding UTF8|ConvertFrom-Json;if($saved.Config -and [IO.Path]::IsPathRooted($saved.Config) -and (Test-Path -LiteralPath $saved.Config)){$config=$saved.Config;break}}
}
if(-not $config){foreach($homePath in @($env:CODEX_HOME,[Environment]::GetEnvironmentVariable('CODEX_HOME','User'),(Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex'))){if($homePath -and [IO.Path]::IsPathRooted($homePath)){$candidate=Join-Path $homePath 'config.toml';if(Test-Path -LiteralPath $candidate){$config=[IO.Path]::GetFullPath($candidate);break}}}}
function Write-Atomic([string]$path,[string]$text){
 $temp=$path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
 try{[IO.File]::WriteAllText($temp,$text,[Text.UTF8Encoding]::new($false));if(Test-Path -LiteralPath $path){[IO.File]::Replace($temp,$path,[NullString]::Value)}else{[IO.File]::Move($temp,$path)}}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
}
function Read-Preferences {
 $raw=[IO.File]::ReadAllText($config)
 if($raw.Contains('"""') -or $raw.Contains("'''")){throw '配置格式无法安全识别，未修改。'}
 $section=[regex]::Matches($raw,'(?ms)^\[desktop\][^\r\n]*(?:\r?\n|\z)(?<body>.*?)(?=^\[|\z)')
 if($section.Count -gt 1){throw '桌面配置分组重复，未修改。'}
 if($section.Count -eq 0){return @{Sound=$null;Turn=$null;Raw=$raw;HasSection=$false}}
 $s=[regex]::Matches($section[0].Groups['body'].Value,'(?m)^\s*notifications-sound\s*=\s*"(?<v>none|default)"[ \t]*(?:#[^\r\n]*)?\r?$')
 $t=[regex]::Matches($section[0].Groups['body'].Value,'(?m)^\s*notifications-turn-mode\s*=\s*"(?<v>always|off|unfocused)"[ \t]*(?:#[^\r\n]*)?\r?$')
 if($s.Count -gt 1 -or $t.Count -gt 1 -or ($section[0].Groups['body'].Value -match '(?m)^\s*notifications-sound\s*=' -and $s.Count -ne 1) -or ($section[0].Groups['body'].Value -match '(?m)^\s*notifications-turn-mode\s*=' -and $t.Count -ne 1)){throw '现有自定义通知配置不受自动安装支持；未覆盖。请先在 GPT 设置中选择默认或无提示音，再重试。'}
 @{Sound=$(if($s.Count){$s[0].Groups['v'].Value}else{$null});Turn=$(if($t.Count){$t[0].Groups['v'].Value}else{$null});Raw=$raw;HasSection=$true}
}
function Write-Preference([string]$name,$expected,$target){
 $p=Read-Preferences;$key=if($name -eq 'notifications-sound'){'Sound'}else{'Turn'}
 if($p[$key] -ne $expected){throw '设置已被其他操作修改，停止覆盖。'}
 $nl=if($p.Raw.Contains("`r`n")){"`r`n"}else{"`n"}
 if(-not $p.HasSection){
  if($null -eq $target){return}
  $after=$p.Raw+$(if($p.Raw.EndsWith("`n") -or $p.Raw.Length -eq 0){''}else{$nl})+'[desktop]'+$nl+$name+' = "'+$target+'"'+$nl
  if([IO.File]::ReadAllText($config) -cne $p.Raw){throw '配置已变化，停止覆盖。'}
  Write-Atomic $config $after;return
 }
 $after=[regex]::Replace($p.Raw,'(?ms)^\[desktop\][^\r\n]*(?:\r?\n|\z)(?<body>.*?)(?=^\[|\z)',[Text.RegularExpressions.MatchEvaluator]{param($m)
  if($null -eq $expected){return $m.Value+$(if($m.Value.EndsWith("`n")){''}else{$nl})+($name+' = "'+$target+'"'+$nl)}
  $pattern='(?m)^(\s*'+[regex]::Escape($name)+'\s*=\s*)"(?:none|default|off|unfocused|always)"([^\r\n]*)(\r?\n|$)'
  [regex]::Replace($m.Value,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($n) if($null -eq $target){return ''};$n.Groups[1].Value+'"'+$target+'"'+$n.Groups[2].Value+$n.Groups[3].Value})
 })
 if([IO.File]::ReadAllText($config) -cne $p.Raw){throw '配置已变化，停止覆盖。'}
 Write-Atomic $config $after
}
function Set-LivePreferences($sound,$turn){
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
 Add-Type @'
using System;using System.Runtime.InteropServices;
public static class DshSetupUI {
 [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 [DllImport("oleacc.dll")] public static extern int AccessibleObjectFromWindow(IntPtr h,uint id,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object o);
}
'@
 $root=[System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
 $iid=[Guid]'618736E0-3C3D-11CF-810C-00AA00389B71';$acc=$null
 [void][DshSetupUI]::AccessibleObjectFromWindow($app.MainWindowHandle,[uint32]4294967292,[ref]$iid,[ref]$acc)
 if($acc -and [Runtime.InteropServices.Marshal]::IsComObject($acc)){[void][Runtime.InteropServices.Marshal]::ReleaseComObject($acc)}
 function Find-Elements($type,$names){
  $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,$type))|Where-Object {$_.Current.Name -in $names -and -not $_.Current.IsOffscreen}
 }
 function Wait-Control($type,$names){$end=[DateTime]::UtcNow.AddSeconds(8);do{$found=@(Find-Elements $type $names);if($found.Count -eq 1){return $found[0]};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $end);throw '未找到唯一的通知设置控件；未操作聊天或任务。'}
 function Click-Control($type,$names){$control=Wait-Control $type $names;$r=$control.Current.BoundingRectangle
  if(-not $control.Current.IsEnabled -or $r.IsEmpty -or -not $root.Current.BoundingRectangle.Contains($r) -or [DshSetupUI]::GetForegroundWindow() -ne $app.MainWindowHandle){throw '设置窗口焦点已变化，已停止。'}
  [void][DshSetupUI]::SetCursorPos([int]($r.Left+$r.Width/2),[int]($r.Top+$r.Height/2));[DshSetupUI]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[DshSetupUI]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 300
 }
 [void][DshSetupUI]::ShowWindowAsync($app.MainWindowHandle,9);[void][DshSetupUI]::SetForegroundWindow($app.MainWindowHandle);try{$root.SetFocus()}catch{}
 $bt=[System.Windows.Automation.ControlType]::Button;$mi=[System.Windows.Automation.ControlType]::MenuItem;$ed=[System.Windows.Automation.ControlType]::Edit
 if([DshSetupUI]::GetForegroundWindow() -ne $app.MainWindowHandle){
  # Focusing an existing control does not click it or modify its draft.
  $targets=@(Find-Elements $ed @('搜索设置','随心输入','使用 ChatGPT Work','描述你的任务以生成套餐…'))+@(Find-Elements $bt @('返回','项目','新聊天','常规'))
  foreach($control in $targets){try{$control.SetFocus()}catch{};Start-Sleep -Milliseconds 100;if([DshSetupUI]::GetForegroundWindow() -eq $app.MainWindowHandle){break}}
 }
 if([DshSetupUI]::GetForegroundWindow() -ne $app.MainWindowHandle){throw '不能安全打开设置。'}
 if(@(Find-Elements $ed @('搜索设置','Search settings')).Count -ne 1){[System.Windows.Forms.SendKeys]::SendWait('^,')}
 $search=Wait-Control $ed @('搜索设置','Search settings');$value=$null;$english=$search.Current.Name -eq 'Search settings'
 if(-not $search.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$value)){throw '设置搜索不可用。'}
 $value.SetValue($(if($english){'Notification sound'}else{'通知提示音'}));Click-Control $bt @('通知提示音，常规','Notification sound, General')
 if($null -ne $sound){
  # Toggle once so both the live preference store and disk are refreshed.
  $label=if($sound -eq 'none'){if($english){'None'}else{'无'}}else{if($english){'Default'}else{'默认'}};$other=if($sound -eq 'none'){if($english){'Default'}else{'默认'}}else{if($english){'None'}else{'无'}}
  Click-Control $bt @('通知提示音','Notification sound');Click-Control $mi @($other)
  Click-Control $bt @('通知提示音','Notification sound');Click-Control $mi @($label)
 }
 if($null -ne $turn){
  $labels=if($english){@{always='Always';unfocused='When unfocused';off='Never'}}else{@{always='始终';unfocused='仅在未聚焦时';off='从不'}};$other=if($turn -eq 'off'){$labels.always}else{$labels.off}
  Click-Control $bt @('始终','仅在未聚焦时','从不','Always','When unfocused','Never');Click-Control $mi @($other)
  Click-Control $bt @($other);Click-Control $mi @($labels[$turn])
 }
 Click-Control $bt @('返回','Back')
}
if($LibraryOnly){return}
try {
 if(-not $config){throw '请先启动并登录包含 Codex / GPT Work 的 GPT 桌面客户端，再重新安装。未找到该用户的客户端配置。'}
 $apps=@(Get-Process ChatGPT -ErrorAction SilentlyContinue|Where-Object MainWindowHandle -NE 0)
 if($apps.Count -gt 1){throw '存在多个客户端窗口进程，未修改。'}
 $app=if($apps.Count){$apps[0]}else{$null}
 $compat=Get-Content -LiteralPath (Join-Path $base 'compatibility.json') -Raw -Encoding UTF8|ConvertFrom-Json
 if($app){$asar=Join-Path (Split-Path $app.Path) 'resources\app.asar'}else{
  $packages=@(Get-AppxPackage -Name OpenAI.Codex|Sort-Object Version -Descending)
  if(-not $packages.Count){throw '未找到包含 Codex / GPT Work 的兼容 GPT 桌面客户端。普通 Chat 不在支持范围。'}
  $asar=Join-Path $packages[0].InstallLocation 'app\resources\app.asar'
 }
 if((Get-FileHash -LiteralPath $asar -Algorithm SHA256).Hash.ToLowerInvariant() -notin @($compat.hosts.sha256)){throw ('此 GPT 客户端版本尚未适配。支持版本：'+($compat.hosts.version -join '、')+'。未更改客户端设置，请更新提示音安装包。')}
 $p=Read-Preferences
 if($Mode -eq 'Check'){Write-Output 'PASS';return}
 [void](New-Item -ItemType Directory -Path $data -Force)
 if($Mode -eq 'Install'){
  if(-not (Test-Path -LiteralPath $receipt)){Write-Atomic $receipt (@{Schema=2;Config=$config;Sound=$p.Sound;Turn=$p.Turn;HasSection=$p.HasSection;At=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json)}
  $r=Get-Content -LiteralPath $receipt -Raw -Encoding UTF8|ConvertFrom-Json;if($r.Config -ne $config){throw '恢复记录属于其他配置路径。'}
  & $exe --exit | Out-Null;Start-Sleep -Seconds 2
  if($app){Set-LivePreferences 'none' 'always'}else{if($p.Sound -ne 'none'){Write-Preference 'notifications-sound' $p.Sound 'none'};if($p.Turn -ne 'always'){Write-Preference 'notifications-turn-mode' $p.Turn 'always'}}
  $p=Read-Preferences;if($p.Sound -ne 'none' -or $p.Turn -ne 'always'){throw '通知设置没有保存成功，未启用。'}
  Write-Atomic (Join-Path $data 'host-location.json') (@{Config=$config}|ConvertTo-Json)
  $settingsPath=Join-Path $data 'settings.json'
  $settings=if(Test-Path -LiteralPath $settingsPath){Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8|ConvertFrom-Json}else{[pscustomobject]@{Volume=25;Codex=$true;Work=$true;Done=$true;Approval=$true;Error=$true;Sounds=@{}}}
  $settings|Add-Member Enabled $true -Force;$settings|Add-Member AutoStart $true -Force
  Write-Atomic $settingsPath ($settings|ConvertTo-Json -Depth 5)
  $run=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run');try{$run.SetValue('DSHDesktopSounds','"'+$exe+'" --tray')}finally{$run.Dispose()}
  Start-Process -FilePath $exe -ArgumentList '--tray' -WindowStyle Hidden
 }else{
  & $exe --exit | Out-Null;Start-Sleep -Seconds 2
  $owned=@(Get-Process -Name 'DSH桌面提示音' -ErrorAction SilentlyContinue|Where-Object Path -EQ $exe);if($owned.Count){throw '提示音程序尚未退出，未继续卸载。'}
  if(Test-Path -LiteralPath $receipt){
   $r=Get-Content -LiteralPath $receipt -Raw -Encoding UTF8|ConvertFrom-Json;if($r.Config -ne $config){throw '恢复记录属于其他配置路径。'}
   $restoreSound=$p.Sound -eq 'none';$restoreTurn=$p.Turn -eq 'always'
   $sound=if($restoreSound){if($null -eq $r.Sound){'default'}else{$r.Sound}}else{$null};$turn=if($restoreTurn){if($null -eq $r.Turn){'unfocused'}else{$r.Turn}}else{$null}
   if($app -and ($null -ne $sound -or $null -ne $turn)){Set-LivePreferences $sound $turn}
   elseif(-not $app){if($null -ne $sound){Write-Preference 'notifications-sound' $p.Sound $sound};if($null -ne $turn){Write-Preference 'notifications-turn-mode' $p.Turn $turn}}
   if($null -ne $turn -and $null -eq $r.Turn){Write-Preference 'notifications-turn-mode' 'unfocused' $null}
   if($restoreSound -and $null -eq $r.Sound){Write-Preference 'notifications-sound' 'default' $null}
   if($r.Schema -ge 2 -and -not $r.HasSection){
    $raw=[IO.File]::ReadAllText($config)
    $after=[regex]::Replace($raw,'(?m)^\[desktop\][ \t]*\r?\n(?=\s*(?:\[|\z))','')
    if($after -cne $raw){Write-Atomic $config $after}
   }
   Remove-Item -LiteralPath $receipt
  }
  $run=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run',$true);try{if($run -and $run.GetValue('DSHDesktopSounds') -ceq ('"'+$exe+'" --tray')){$run.DeleteValue('DSHDesktopSounds',$false)}}finally{if($run){$run.Dispose()}}
  $settingsPath=Join-Path $data 'settings.json';if(Test-Path -LiteralPath $settingsPath){$s=Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8|ConvertFrom-Json;$s.Enabled=$false;$s.AutoStart=$false;Write-Atomic $settingsPath ($s|ConvertTo-Json -Depth 5)}
 }
 Write-Atomic (Join-Path $data 'setup-result.json') (@{At=[DateTime]::UtcNow.ToString('o');Mode=$Mode;Result='PASS'}|ConvertTo-Json)
}catch{
 Add-Type -AssemblyName System.Windows.Forms
 if(Test-Path -LiteralPath $data){Write-Atomic (Join-Path $data 'setup-result.json') (@{At=[DateTime]::UtcNow.ToString('o');Mode=$Mode;Result='FAIL';Error=$_.Exception.Message}|ConvertTo-Json)}
 if(-not $NonInteractive){[void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message+' 原应用任务没有被停止。','DSH 设置未完成')};Write-Error $_.Exception.Message;exit 1
}
