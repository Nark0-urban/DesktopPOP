# 다른 사람에게 줄 zip 만들기 (.NET 설치 없이 실행되는 버전)
# 꼭 필요한 부품(dll)은 임시 폴더에 풀지 않고 exe 옆에 같이 둠 → 폴더만 지우면 깨끗하게 제거됨
# 사용: powershell -ExecutionPolicy Bypass -File build-zip.ps1
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ($Version -eq "") { $Version = ([xml](Get-Content "$root\DesktopPop.csproj")).Project.PropertyGroup.Version }

$out = Join-Path $root "publish"
$dist = Join-Path $root "dist"
Remove-Item $out, $dist -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish "$root\DesktopPop.csproj" -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }

New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$root\사용법.txt", "$root\먼저 읽어주세요.txt" $out

# exe 안에 .NET 실행 환경이 같이 들어가므로, 그 라이선스와 제3자 고지를 notices 폴더에 함께 넣음
$pkgs = $env:NUGET_PACKAGES
if (-not $pkgs) { $pkgs = Join-Path $env:USERPROFILE ".nuget\packages" }
foreach ($pack in @(
    @{ Id = "microsoft.netcore.app.runtime.win-x64"; Dir = "dotnet-runtime" },
    @{ Id = "microsoft.windowsdesktop.app.runtime.win-x64"; Dir = "windows-desktop-runtime" })) {
  $latest = Get-ChildItem (Join-Path $pkgs $pack.Id) -Directory -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
  if (-not $latest) { throw "라이선스 파일을 찾지 못함: $($pack.Id)" }
  $dest = Join-Path $out "notices\$($pack.Dir)"
  New-Item -ItemType Directory -Force $dest | Out-Null
  $files = Get-ChildItem $latest.FullName -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' }
  if (-not $files) { throw "라이선스 파일이 비어 있음: $($latest.FullName)" }
  $files | Copy-Item -Destination $dest
}
$zip = Join-Path $dist "DesktopPop-$Version-win-x64.zip"
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
"만들어짐: $zip"
