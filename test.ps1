param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
& $Dotnet build modules/HotTests/Test.csproj -c Release
if($LASTEXITCODE -ne 0){throw 'Offline feed test build failed'}
& $Dotnet modules/HotTests/bin/Release/net10.0-windows/Test.dll
if($LASTEXITCODE -ne 0){throw 'Offline feed tests failed'}
& $Dotnet tools/bin/Release/net10.0-windows/CardTools.dll telegram-test
if($LASTEXITCODE -ne 0){throw 'Telegram deduplication tests failed'}
# Run the renderer verifier against the rebuilt assembly, not the old app binary.
Copy-Item artifacts/app/AIHOT.Desktop.dll tools/bin/Release/net10.0-windows/AIHOT.Desktop.dll -Force
& $Dotnet tools/bin/Release/net10.0-windows/CardTools.dll verify artifacts/app/AIHOT.Desktop.dll
if($LASTEXITCODE -ne 0){throw 'Share rendering integration failed'}
& $Dotnet tools/bin/Release/net10.0-windows/CardTools.dll tibo-test artifacts/tibo-preview.png
if($LASTEXITCODE -ne 0){throw 'Tibo monitor tests failed'}
