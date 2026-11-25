param([int]$Run=1)
$ErrorActionPreference='Stop'
$taskProjectRoot=Split-Path $PSScriptRoot -Parent
$taskSmoke='F:\sts2modding-smoke\Covenant-Climb-1131'
$taskExecutable=Join-Path $taskSmoke 'SlayTheSpire2.exe'
if(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $taskExecutable}){throw 'The owned climb runtime is already running.'}
& (Join-Path $PSScriptRoot 'setup_smoke.ps1') -SmokeDir $taskSmoke -UserDataName 'CovenantSmoke-1131-Climb'
$taskAudit=Join-Path $taskProjectRoot 'audits\0.5'
$taskPrior=$env:COVENANT_CLIMB
try{
 $env:COVENANT_CLIMB='1'
 $taskProcess=Start-Process -FilePath $taskExecutable -WorkingDirectory $taskSmoke -ArgumentList @('--force-steam','off','--rendering-method','gl_compatibility','--windowed','--resolution','1280x720','--log-file',(Join-Path $taskAudit "climb-engine-$Run.log")) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $taskAudit "climb-console-$Run.log") -RedirectStandardError (Join-Path $taskAudit "climb-errors-$Run.log") -PassThru
 @{processId=$taskProcess.Id;console=(Join-Path $taskAudit "climb-console-$Run.log")}|ConvertTo-Json -Compress
}finally{if($null -eq $taskPrior){Remove-Item Env:COVENANT_CLIMB -ErrorAction SilentlyContinue}else{$env:COVENANT_CLIMB=$taskPrior}}
