# DesktopPop

평소엔 바탕화면 아이콘을 숨겨 두었다가, **마우스를 화면 왼쪽/오른쪽 끝에 대면 아이콘이 튀어나오는** Windows 프로그램이에요.
원하는 그림으로 **"가방"** 을 만들어 아이콘을 보관할 수도 있어요 (클릭하면 가방 주변으로 촤르륵 펼쳐짐).

## 받아서 쓰기

1. 오른쪽 **Releases** 에서 `DesktopPop-x.x.x-win-x64.zip` 을 받아요.
2. 원하는 폴더에 압축을 풀어요.
3. `DesktopPop.exe` 를 더블클릭하면 끝! (.NET 같은 걸 따로 설치할 필요 없어요)

처음 실행하면 Windows가 "알 수 없는 게시자" 경고를 띄울 수 있어요 → **추가 정보 → 실행** 을 누르면 돼요.

자세한 사용법은 zip 안의 `사용법.txt` 를 봐 주세요.

## 주요 기능

- 화면 끝에 마우스를 대면 아이콘이 슉 튀어나오고, 떼면 쏙 들어감
- 원래 바탕화면처럼: 클릭 선택, 두 번 클릭 실행, Ctrl/Shift 클릭, 네모 끌기 선택, Windows 우클릭 메뉴
- 아이콘을 폴더·휴지통·탐색기·가방으로 끌어서 이동
- 가방(인벤토리): 그림 보관함에서 고른 그림으로 가방을 만들어 아이콘 보관
- 새로 생긴 파일은 가장자리가 열리면서 반짝 표시
- 게임·영상 전체화면일 땐 안 튀어나옴
- 색상 테마, 켤 때 시작 연출, 윈도우 시작 시 자동 실행

## 설정은 어디에?

`DesktopPop.exe` 옆에 `data` 폴더가 생기고 거기에 가방·배치·그림이 저장돼요.
폴더를 통째로 옮기면 설정도 같이 따라가요.

## 직접 빌드하기

Windows + [.NET 10 SDK](https://dotnet.microsoft.com/download) 가 있으면:

```powershell
powershell -ExecutionPolicy Bypass -File build-zip.ps1
```

`dist` 폴더에 zip 이 만들어져요.

## 새 버전 올리기 (만든 사람용)

`DesktopPop.csproj` 의 `<Version>` 을 올리고, 같은 번호로 태그를 붙여 올리면
GitHub Actions 가 zip 을 만들어 Releases 에 자동으로 올려요.

```bash
git tag v1.0.1
git push origin main --tags
```
