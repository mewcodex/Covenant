param(
 [Parameter(Mandatory=$true)][string]$CompatibilityLog,
 [string]$Configuration='Release',
 [string]$GameData='F:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $taskRoot 'Covenant.csproj') -c $Configuration "-p:GameData=$GameData"
if($LASTEXITCODE -ne 0){throw 'Mod build failed.'}
$taskDll=Join-Path $taskRoot "bin\$Configuration\net9.0\Covenant.dll"
$taskSha=(Get-FileHash -LiteralPath $taskDll -Algorithm SHA256).Hash
$taskNativeSha=(Get-FileHash -LiteralPath (Join-Path $GameData 'sts2.dll') -Algorithm SHA256).Hash
if(!(Select-String -LiteralPath $CompatibilityLog -SimpleMatch "COVENANT_COMPAT_COMPLETE mod=$taskSha native=$taskNativeSha" -Quiet)){
 throw 'The compatibility log does not match this build and game assembly.'
}
$taskManifestPath=Join-Path $taskRoot 'Covenant.json'
$taskManifest=Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json
if($taskManifest.id -ne 'Covenant' -or !$taskManifest.has_dll -or $taskManifest.has_pck -or @($taskManifest.dependencies).Count -ne 0){throw 'Unexpected standalone mod manifest.'}
$taskDist=Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $taskDist -Force | Out-Null
$taskZip=Join-Path $taskDist "Covenant-$($taskManifest.version)-sts2-$($taskManifest.min_game_version).zip"
$taskPending=Join-Path $taskDist ([Guid]::NewGuid().ToString('N')+'.zip')
$taskFiles=@{'Covenant/Covenant.dll'=$taskDll; 'Covenant/Covenant.json'=$taskManifestPath}
try{
 $taskArchive=[IO.Compression.ZipFile]::Open($taskPending,[IO.Compression.ZipArchiveMode]::Create)
 try{
  foreach($taskEntry in $taskFiles.GetEnumerator()){
   [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,$taskEntry.Value,$taskEntry.Key) | Out-Null
  }
  $taskReadme=[IO.StreamWriter]::new($taskArchive.CreateEntry('安装说明.txt').Open(),[Text.UTF8Encoding]::new($false))
  try{
   $taskReadme.WriteLine("杀戮尖塔2：盟约 v$($taskManifest.version)")
   $taskReadme.WriteLine("适配 Steam public-beta v$($taskManifest.min_game_version)。")
   $taskReadme.WriteLine('关闭游戏，将 Covenant 文件夹放入游戏安装目录的 mods，在模组菜单中启用后重启。建议开始新的一局。')
  }finally{$taskReadme.Dispose()}
 }finally{$taskArchive.Dispose()}
 $taskArchive=[IO.Compression.ZipFile]::OpenRead($taskPending)
 try{
  foreach($taskEntry in $taskFiles.GetEnumerator()){
   $taskStream=$taskArchive.GetEntry($taskEntry.Key).Open()
   try{$taskPackedSha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskStream))}finally{$taskStream.Dispose()}
   if($taskPackedSha -ne (Get-FileHash -LiteralPath $taskEntry.Value -Algorithm SHA256).Hash){throw "Archive verification failed: $($taskEntry.Key)"}
  }
 }finally{$taskArchive.Dispose()}
 [IO.File]::Move($taskPending,$taskZip,$true)
}finally{if(Test-Path -LiteralPath $taskPending){Remove-Item -LiteralPath $taskPending}}
Get-FileHash -LiteralPath $taskZip -Algorithm SHA256
