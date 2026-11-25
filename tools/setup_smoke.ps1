param([string]$GameDir='F:\SteamLibrary\steamapps\common\Slay the Spire 2',[string]$SmokeDir='F:\sts2modding-smoke\Covenant-1140',[string]$UserDataName='CovenantSmoke-1140')
$ErrorActionPreference='Stop'
$taskProjectRoot=Split-Path $PSScriptRoot -Parent
$taskAbsolute=[IO.Path]::GetFullPath($SmokeDir)
if(!$taskAbsolute.StartsWith('F:\sts2modding-smoke\Covenant-', [StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected smoke target.'}
if($UserDataName -notmatch '^CovenantSmoke-\d+(?:-[A-Za-z0-9]+)?$'){throw 'Unexpected disposable user-data name.'}
New-Item -ItemType Directory -Path $taskAbsolute -Force | Out-Null
foreach($taskFile in Get-ChildItem -LiteralPath $GameDir -File | Where-Object {$_.Extension -in @('.exe','.dll','.pck') -or $_.Name -eq 'release_info.json'}){
 $taskTarget=Join-Path $taskAbsolute $taskFile.Name
 if(!(Test-Path -LiteralPath $taskTarget)){New-Item -ItemType HardLink -Path $taskTarget -Target $taskFile.FullName | Out-Null}
}
$taskLibraries=Join-Path $taskAbsolute 'data_sts2_windows_x86_64'
if(!(Test-Path -LiteralPath $taskLibraries)){New-Item -ItemType Junction -Path $taskLibraries -Target (Join-Path $GameDir 'data_sts2_windows_x86_64') | Out-Null}
Set-Content -LiteralPath (Join-Path $taskAbsolute 'override.cfg') -Encoding utf8 -Value "[application]`nconfig/use_custom_user_dir=true`nconfig/custom_user_dir_name=`"$UserDataName`""
$taskSettingsDir=Join-Path $env:APPDATA "$UserDataName\default\1"
New-Item -ItemType Directory -Path $taskSettingsDir -Force | Out-Null
$taskSettingsFile=Join-Path $taskSettingsDir 'settings.save'
if(!(Test-Path -LiteralPath $taskSettingsFile)){
 $taskBaseline=Join-Path $env:APPDATA 'Zhs113FixSmoke-20261112-1131\default\1\settings.save'
 $taskSettings=Get-Content -LiteralPath $taskBaseline -Raw | ConvertFrom-Json
 $taskSettings.mod_settings.mods_enabled=$true;$taskSettings.mod_settings.mod_list=@();$taskSettings.language='zhs'
 $taskSettings | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $taskSettingsFile -Encoding utf8
}
$taskSettings=Get-Content -LiteralPath $taskSettingsFile -Raw | ConvertFrom-Json
$taskSettings.fullscreen=$false
$taskSettings.window_size.X=1280;$taskSettings.window_size.Y=720
$taskSettings | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $taskSettingsFile -Encoding utf8
dotnet build (Join-Path $taskProjectRoot 'checks\RuntimeCheck\RuntimeCheck.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Runtime probe build failed.'}
$taskMods=Join-Path $taskAbsolute 'mods'
New-Item -ItemType Directory -Path (Join-Path $taskMods 'Covenant'),(Join-Path $taskMods 'Covenant.RuntimeCheck') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskProjectRoot 'bin\Release\net9.0\Covenant.dll'),(Join-Path $taskProjectRoot 'Covenant.json') -Destination (Join-Path $taskMods 'Covenant') -Force
Copy-Item -LiteralPath (Join-Path $taskProjectRoot 'checks\RuntimeCheck\bin\Release\net9.0\Covenant.RuntimeCheck.dll') -Destination (Join-Path $taskMods 'Covenant.RuntimeCheck') -Force
@{id='Covenant.RuntimeCheck';name='Covenant Runtime Check';author='Local';version='0.1.0';has_dll=$true;has_pck=$false;affects_gameplay=$false;dependencies=@(@{id='Covenant';min_version='0.1.0'})}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $taskMods 'Covenant.RuntimeCheck\Covenant.RuntimeCheck.json') -Encoding utf8
Write-Output "Prepared isolated runtime: $taskAbsolute"
