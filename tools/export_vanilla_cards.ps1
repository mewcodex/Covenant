param([string]$GameDir='F:\SteamLibrary\steamapps\common\Slay the Spire 2')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskRelease=Get-Content -LiteralPath (Join-Path $GameDir 'release_info.json') -Raw | ConvertFrom-Json
$taskVersion=$taskRelease.version.TrimStart('v')
$taskProfile='CovenantVanillaExport-'+$taskVersion.Replace('.','')
$taskSmoke=Join-Path 'F:\sts2modding-smoke' ('VanillaCardExport-'+$taskVersion)
$taskOutput=Join-Path $taskRoot ('outputs\claude-vanilla-cards-'+$taskVersion)
New-Item -ItemType Directory -Path $taskSmoke,$taskOutput -Force | Out-Null
foreach($taskFile in Get-ChildItem -LiteralPath $GameDir -File | Where-Object {$_.Extension -in @('.exe','.dll','.pck') -or $_.Name -eq 'release_info.json'}){
 $taskTarget=Join-Path $taskSmoke $taskFile.Name
 if(!(Test-Path -LiteralPath $taskTarget)){New-Item -ItemType HardLink -Path $taskTarget -Target $taskFile.FullName | Out-Null}
}
$taskLibraries=Join-Path $taskSmoke 'data_sts2_windows_x86_64'
if(!(Test-Path -LiteralPath $taskLibraries)){New-Item -ItemType Junction -Path $taskLibraries -Target (Join-Path $GameDir 'data_sts2_windows_x86_64') | Out-Null}
Set-Content -LiteralPath (Join-Path $taskSmoke 'override.cfg') -Encoding utf8 -Value "[application]`nconfig/use_custom_user_dir=true`nconfig/custom_user_dir_name=`"$taskProfile`""
$taskPrefsDir=Join-Path $env:APPDATA "$taskProfile\default\1"
New-Item -ItemType Directory -Path $taskPrefsDir -Force | Out-Null
$taskPrefsPath=Join-Path $taskPrefsDir 'settings.save'
if(!(Test-Path -LiteralPath $taskPrefsPath)){
 $taskPrefs=Get-Content -LiteralPath (Join-Path $env:APPDATA 'CovenantSmoke-1131\default\1\settings.save') -Raw | ConvertFrom-Json
 $taskPrefs.mod_settings.mods_enabled=$true;$taskPrefs.mod_settings.mod_list=@();$taskPrefs.language='zhs'
 $taskPrefs | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $taskPrefsPath -Encoding utf8
}
$taskPrefs=Get-Content -LiteralPath $taskPrefsPath -Raw | ConvertFrom-Json
$taskPrefs.fullscreen=$false;$taskPrefs.window_size=@{X=1280;Y=720}
$taskPrefs | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $taskPrefsPath -Encoding utf8
dotnet build (Join-Path $taskRoot 'checks\VanillaCardExport\VanillaCardExport.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Vanilla exporter build failed.'}
$taskMods=Join-Path $taskSmoke 'mods'
$taskMod=Join-Path $taskMods 'VanillaCardExport'
New-Item -ItemType Directory -Path $taskMod -Force | Out-Null
if(@(Get-ChildItem -LiteralPath $taskMods -Directory | Where-Object {$_.Name -ne 'VanillaCardExport'}).Count -gt 0){throw 'Export copy must contain only the vanilla exporter.'}
Copy-Item -LiteralPath (Join-Path $taskRoot 'checks\VanillaCardExport\bin\Release\net9.0\VanillaCardExport.dll') -Destination $taskMod -Force
@{id='VanillaCardExport';name='Vanilla card data export';author='Local';version='0.1.0';has_dll=$true;has_pck=$false;affects_gameplay=$false;dependencies=@()} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskMod 'VanillaCardExport.json') -Encoding utf8
$taskEnvironment=@{ COVENANT_CARD_EXPORT_OUTPUT=$taskOutput; COVENANT_CARD_EXPORT_GAME="$($taskRelease.version)/$($taskRelease.commit)" }
$taskProcess=Start-Process -FilePath (Join-Path $taskSmoke 'SlayTheSpire2.exe') -WorkingDirectory $taskSmoke -ArgumentList @('--force-steam','off','--rendering-method','gl_compatibility','--windowed','--resolution','1280x720','--log-file',($taskOutput.Replace('\','/')+'/engine.log')) -Environment $taskEnvironment -WindowStyle Hidden -RedirectStandardOutput (Join-Path $taskOutput 'engine.stdout.log') -RedirectStandardError (Join-Path $taskOutput 'engine.stderr.log') -PassThru
Write-Output "Vanilla-only export process: $($taskProcess.Id)"
