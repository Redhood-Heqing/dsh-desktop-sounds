param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$result=[IO.Path]::GetFullPath($EvidenceDirectory)
if(Test-Path -LiteralPath $result){throw 'Preserve existing evidence; choose a fresh output directory'}
[void](New-Item -ItemType Directory -Path $result)
$temporary=Join-Path $PSScriptRoot '.test-build'
if(Test-Path -LiteralPath $temporary){throw 'Unexpected existing test build directory'}
[void](New-Item -ItemType Directory -Path $temporary)
$cases=@(
 @{name='state';files=@('ClientState.cs','ClientStateTests.cs','HostPreferences.cs','PcmPlayer.cs','Core\QuietPolicy.cs','Core\EventGate.cs','Core\SoundRouter.cs','Core\HostSoundGuard.cs');args=@((Join-Path $result 'state-fixtures'))},
 @{name='projection';files=@('NativeFrame.cs','NativeFrameTests.cs','NativeAdapter.cs','ClientState.cs','Core\CodexState.cs','Core\EventGate.cs');args=@()},
 @{name='canonical';files=@('Core\CodexState.cs','Core\CodexStateTests.cs');args=@()},
 @{name='gate';files=@('Core\EventGate.cs','Core\SoundRouter.cs','Core\QuietPolicy.cs','Core\GateTests.cs');args=@()},
 @{name='host-guard';files=@('Core\HostSoundGuard.cs','Core\HostSoundGuardTests.cs');args=@()},
 @{name='host-location';files=@('HostLocation.cs','HostLocationTests.cs');args=@((Join-Path $result 'location-fixtures'))}
)
$reports=@()
try{
 foreach($case in $cases){
  $exe=Join-Path $temporary ($case.name+'.exe')
  $sources=@($case.files|ForEach-Object{Join-Path $PSScriptRoot $_})
  $compile=@(& (Join-Path $framework 'csc.exe') /nologo /utf8output /optimize+ /reference:System.Web.Extensions.dll ("/out:$exe") @sources 2>&1)
  if($LASTEXITCODE -ne 0){throw ($compile -join "`n")}
  $testArgs=$case.args;$output=@(& $exe @testArgs 2>&1);$exitCode=$LASTEXITCODE
  $reports+=@{name=$case.name;exitCode=$exitCode;output=($output -join "`n");synthetic=$true}
  if($exitCode -ne 0){throw ('Test failed: '+$case.name)}
 }
}finally{
 $reports|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $result 'results.json') -Encoding UTF8
 if([IO.Path]::GetFullPath($temporary) -ne (Join-Path $PSScriptRoot '.test-build')){throw 'Unexpected cleanup path'}
 Remove-Item -LiteralPath $temporary -Recurse -Force
}
$reports|ForEach-Object {$_.output}
