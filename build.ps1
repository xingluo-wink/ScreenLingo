param([switch]$Publish)
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
if(!$env:DOTNET_CLI_HOME){$env:DOTNET_CLI_HOME=Join-Path $projectRoot 'tools\cli-home'}
if(!$env:NUGET_PACKAGES){$env:NUGET_PACKAGES=Join-Path $projectRoot 'tools\packages'}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$dotnet=Join-Path $projectRoot 'tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$output=Join-Path $projectRoot 'release\ScreenLingo'
if($Publish){
 & $dotnet publish "$projectRoot\src\ScreenLingo.Ocr\ScreenLingo.Ocr.csproj" -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o "$projectRoot\work\ocr-publish"
 if($LASTEXITCODE -ne 0){throw 'OCR publish failed'}
 New-Item -ItemType Directory -Path $output -Force | Out-Null
 # Copy shared runtime-compatible worker files without the unused default Latin models.
 Get-ChildItem -LiteralPath "$projectRoot\work\ocr-publish" -File | Copy-Item -Destination $output -Force
 $native=Join-Path $projectRoot 'work\ocr-publish\runtimes\win-x64\native'
 if(Test-Path -LiteralPath $native){Get-ChildItem -LiteralPath $native -File | Copy-Item -Destination $output -Force}
 # Desktop runtime assemblies must win over the worker's core-runtime facades
 # (notably WindowsBase.dll). Both executables share one self-contained runtime.
 & $dotnet publish "$projectRoot\src\ScreenLingo\ScreenLingo.csproj" -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $output
 if($LASTEXITCODE -ne 0){throw 'UI publish failed'}
 New-Item -ItemType Directory -Path "$output\models" -Force | Out-Null
 Copy-Item -LiteralPath "$projectRoot\models\det.onnx","$projectRoot\models\rec.onnx","$projectRoot\models\cls.onnx","$projectRoot\models\dict.txt" -Destination "$output\models" -Force
 if(Test-Path -LiteralPath "$projectRoot\licenses"){Copy-Item -LiteralPath "$projectRoot\licenses" -Destination $output -Recurse -Force}
 Copy-Item -LiteralPath "$projectRoot\README.md","$projectRoot\THIRD-PARTY.md" -Destination $output -Force
 Write-Output "RELEASE_READY: $output"
}else{
 foreach($project in @('ScreenLingo','ScreenLingo.Ocr')){
  & $dotnet build "$projectRoot\src\$project\$project.csproj" -c Release -r win-x64
  if($LASTEXITCODE -ne 0){throw "$project build failed"}
 }
}
