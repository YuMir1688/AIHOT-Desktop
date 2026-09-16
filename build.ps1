param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
function Run([string[]]$Arguments){& $Dotnet @Arguments;if($LASTEXITCODE -ne 0){throw "dotnet failed: $Arguments"}}
Run @('publish','core/AiHot.Desktop.csproj','-c','Release','-o','artifacts/app','--self-contained','false')
# Regenerate the legacy reference assembly from the included source.
Copy-Item artifacts/app/AIHOT.Desktop.dll lib/AIHOT.Desktop.dll -Force
Copy-Item artifacts/app/QRCoder.dll lib/QRCoder.dll -Force
Run @('build','tools/CardTools.csproj','-c','Release')
Run @('build','modules/HotFeed/HotFeed.csproj','-c','Release')
Run @('build','modules/HotPatch/Patch.csproj','-c','Release')
Run @('build','modules/NoHotkey/NoHotkey.csproj','-c','Release')
Copy-Item src/bin/Release/net10.0-windows/AIHOT.Cards.dll artifacts/app/
Copy-Item modules/HotFeed/bin/Release/net10.0-windows/AIHOT.HotFeed.dll artifacts/app/
Run @('tools/bin/Release/net10.0-windows/CardTools.dll','patch','artifacts/app/AIHOT.Desktop.dll','artifacts/app/AIHOT.Cards.dll','artifacts/cards.dll')
Copy-Item artifacts/cards.dll artifacts/app/AIHOT.Desktop.dll -Force
Run @('modules/HotPatch/bin/Release/net10.0/Patch.dll','artifacts/app/AIHOT.Desktop.dll','artifacts/app/AIHOT.HotFeed.dll','artifacts/AIHOT.Desktop.dll')
Run @('modules/NoHotkey/bin/Release/net10.0/NoHotkey.dll','artifacts/AIHOT.Desktop.dll','artifacts/nohotkey.dll')
Copy-Item artifacts/nohotkey.dll artifacts/app/AIHOT.Desktop.dll -Force
Copy-Item artifacts/AIHOT.Desktop.deps.json artifacts/app/AIHOT.Desktop.deps.json -Force
$deps=Get-Content artifacts/app/AIHOT.Desktop.deps.json -Raw|ConvertFrom-Json -AsHashtable
$target=$deps.targets[$deps.runtimeTarget.name]
$target['AIHOT.Desktop/1.2.0'].dependencies['AIHOT.Cards']='1.0.0'
$target['AIHOT.Cards/1.0.0']=@{runtime=@{'AIHOT.Cards.dll'=@{}}}
$deps.libraries['AIHOT.Cards/1.0.0']=@{type='project';serviceable=$false;sha512=''}
$deps|ConvertTo-Json -Depth 50|Set-Content artifacts/app/AIHOT.Desktop.deps.json -Encoding utf8
Write-Output 'Built from source: artifacts/app/AIHOT.Desktop.exe'
