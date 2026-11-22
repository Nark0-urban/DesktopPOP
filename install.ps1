# 이 컴퓨터에 DesktopPop 을 설치·업데이트 (zip 풀 필요 없이)
#  1) 배포용으로 빌드 (build-zip.ps1 과 같은 결과)
#  2) 켜져 있는 DesktopPop 을 끔 (트레이 "종료"와 같음 → 원래 아이콘이 다시 보임)
#  3) 설치 폴더에 덮어씀 (data 폴더 = 내 설정·가방·폰트는 그대로)
#  4) 바탕화면에 "DesktopPop" 바로 가기, 자동 실행이 켜져 있으면 새 경로로 고침
#  5) 다시 켬
# 사용: powershell -ExecutionPolicy Bypass -File install.ps1 [-ImportDataFrom <예전 DesktopPop 폴더>] [-NoStart]
param(
  [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\DesktopPop"),
  [string]$ImportDataFrom = "",
  [switch]$NoStart
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# 1) 빌드
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "build-zip.ps1")
if ($LASTEXITCODE -ne 0) { throw "빌드 실패" }
$publish = Join-Path $root "publish"
$newExe = Join-Path $publish "DesktopPop.exe"

# 2) 켜져 있는 DesktopPop 끄기 (어느 폴더에서 켰든 같은 신호를 받음)
$quit = Start-Process $newExe -ArgumentList "--quit" -PassThru -Wait -WindowStyle Hidden
if ($quit.ExitCode -eq 0) { "켜져 있던 DesktopPop 을 껐어요." }
# --quit 를 모르는 예전 버전(1.1.7 이전)이 켜져 있으면: 본체만 끝내면 복구 감시가 원래 아이콘을 다시 보이게 함
$old = @(Get-CimInstance Win32_Process -Filter "Name='DesktopPop.exe'" | Where-Object { $_.CommandLine -notmatch '--restore-desktop-on-exit|--shell-menu|--shell-action' })
foreach ($p in $old) {
  "예전 버전 DesktopPop 을 끝냄: $($p.ExecutablePath)"
  Stop-Process -Id $p.ProcessId -Force
}
if ($old.Count -gt 0) { Start-Sleep -Seconds 2 }
# 대기 중인 메뉴 도우미·복구 감시가 마저 끝날 때까지 잠깐
for ($i = 0; $i -lt 50; $i++) {
  $left = @(Get-CimInstance Win32_Process -Filter "Name='DesktopPop.exe'" | Where-Object { $_.ExecutablePath -like "$InstallDir\*" })
  if ($left.Count -eq 0) { break }
  Start-Sleep -Milliseconds 200
}

# 3) 설치 폴더에 덮어쓰기 (data 는 건드리지 않음)
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Get-ChildItem $publish | ForEach-Object { Copy-Item $_.FullName $InstallDir -Recurse -Force }
$exe = Join-Path $InstallDir "DesktopPop.exe"
$data = Join-Path $InstallDir "data"
if ($ImportDataFrom -and -not (Test-Path (Join-Path $data "config.json"))) {
  $from = Join-Path $ImportDataFrom "data"
  if (Test-Path (Join-Path $from "config.json")) {
    Copy-Item $from $InstallDir -Recurse -Force
    "예전 설정·가방을 가져왔어요: $from"
  }
}
"설치함: $exe ($((Get-Item $exe).VersionInfo.ProductVersion))"

# 4) 바탕화면 바로 가기 + 자동 실행 경로
$shell = New-Object -ComObject WScript.Shell
$lnkPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "DesktopPop.lnk"
$lnk = $shell.CreateShortcut($lnkPath)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $InstallDir
$lnk.IconLocation = "$exe,0"
$lnk.Description = "DesktopPop - 바탕화면 팝업"
$lnk.Save()
"바로 가기: $lnkPath"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$current = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).DesktopPop
if ($current -and $current.Trim('"') -ne $exe) {
  Set-ItemProperty $runKey -Name DesktopPop -Value "`"$exe`""
  "자동 실행 경로를 새 설치 폴더로 고쳤어요."
}

# 5) 다시 켜기
if (-not $NoStart) {
  Start-Process $exe -WorkingDirectory $InstallDir
  "DesktopPop 을 켰어요."
}
