# 다른 사람에게 줄 zip 만들기 (.NET 설치 없이 exe 하나로 실행되는 버전)
# 사용: powershell -ExecutionPolicy Bypass -File build-zip.ps1
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ($Version -eq "") { $Version = ([xml](Get-Content "$root\DesktopPop.csproj")).Project.PropertyGroup.Version }

$out = Join-Path $root "publish"
$dist = Join-Path $root "dist"
Remove-Item $out, $dist -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish "$root\DesktopPop.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$root\사용법.txt" $out
$zip = Join-Path $dist "DesktopPop-$Version-win-x64.zip"
Compress-Archive -Path "$out\DesktopPop.exe", "$out\사용법.txt" -DestinationPath $zip -Force
"만들어짐: $zip"
