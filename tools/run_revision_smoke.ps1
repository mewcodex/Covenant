param([Parameter(Mandatory=$true)][int]$Run,[ValidateSet('all','resources','supplement','layout','badge','closing','descriptions','staged-descriptions','balance','balance-058','renewed-bonds','adjusted-bonds','shipping-bonds','solitude','new-bonds','stars','curses','profit','release','rewards','resume','merchant','shop-library-ui','acquisition-preview','library','compendium','event','traits','scaling','reworks','approved-traits','settlement','random','random-resume','attrition','performance','relics','trait-activation','strike-growth','generation-pools','divine-shot','decay','status-selection','bad-luck','guilty','ascenders-bane','balance-cleanup','fate-local','oracle','spectrum','entropy-growth','ember-cleanup','aid-mastery','count-rebalance','reflow','activation-audit','trait-trim','orbs','envenom','growth-triggers','ritual-latch','compatibility')][string]$Focus='all')
$ErrorActionPreference='Stop'
$taskProjectRoot=Split-Path $PSScriptRoot -Parent
$taskSmoke='F:\sts2modding-smoke\Covenant-Revision-1140'
$taskExecutable=Join-Path $taskSmoke 'SlayTheSpire2.exe'
if(Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $taskExecutable}){throw 'The owned isolated runtime is already running.'}
& (Join-Path $PSScriptRoot 'setup_smoke.ps1') -SmokeDir $taskSmoke -UserDataName 'CovenantSmoke-1140-Revision'
if($Focus -ne 'resume'){
 # Save verification deliberately turns these settings off for the fresh-process resume.
 $taskPreferences=Join-Path $env:APPDATA 'CovenantSmoke-1140-Revision\Covenant\settings.json'
 if(Test-Path -LiteralPath $taskPreferences){
  $taskConfigured=Get-Content -LiteralPath $taskPreferences -Raw | ConvertFrom-Json -AsHashtable
  $taskConfigured.EnableAllCardPools=$true
  $taskConfigured.ShowCardTraits=$true
  $taskConfigured.HideMerchantHand=$false
  $taskConfigured.ShowFormulas=$false
  $taskConfigured.EnemyFloorScaling=$false
  $taskConfigured | ConvertTo-Json | Set-Content -LiteralPath $taskPreferences -Encoding utf8
 }
}
$taskAudit=Join-Path $taskProjectRoot 'audits\0.5'
$taskTag=$Run.ToString('00')
$taskPriorOnly=$env:COVENANT_REVISION_ONLY;$taskPriorFocus=$env:COVENANT_REVISION_FOCUS;$taskPriorLayout=$env:COVENANT_UI_LAYOUT
try{
 $env:COVENANT_REVISION_ONLY='1'
 if($Focus -eq 'layout'){$env:COVENANT_UI_LAYOUT='1'}else{Remove-Item Env:COVENANT_UI_LAYOUT -ErrorAction SilentlyContinue}
 if($Focus -eq 'all'){Remove-Item Env:COVENANT_REVISION_FOCUS -ErrorAction SilentlyContinue}else{$env:COVENANT_REVISION_FOCUS=$Focus}
 $taskProcess=Start-Process -FilePath $taskExecutable -WorkingDirectory $taskSmoke -ArgumentList @('--force-steam','off','--rendering-method','gl_compatibility','--windowed','--resolution','1280x720','--log-file',(Join-Path $taskAudit "revision-engine-$taskTag.log")) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $taskAudit "revision-console-$taskTag.log") -RedirectStandardError (Join-Path $taskAudit "revision-errors-$taskTag.log") -PassThru
 @{processId=$taskProcess.Id;focus=$Focus;console=(Join-Path $taskAudit "revision-console-$taskTag.log")}|ConvertTo-Json -Compress
}finally{
 if($null -eq $taskPriorOnly){Remove-Item Env:COVENANT_REVISION_ONLY -ErrorAction SilentlyContinue}else{$env:COVENANT_REVISION_ONLY=$taskPriorOnly}
 if($null -eq $taskPriorFocus){Remove-Item Env:COVENANT_REVISION_FOCUS -ErrorAction SilentlyContinue}else{$env:COVENANT_REVISION_FOCUS=$taskPriorFocus}
 if($null -eq $taskPriorLayout){Remove-Item Env:COVENANT_UI_LAYOUT -ErrorAction SilentlyContinue}else{$env:COVENANT_UI_LAYOUT=$taskPriorLayout}
}
