# 일해라

바탕화면에 띄워 두는 할 일 위젯. "내일 오후 3시 회의"처럼 적으면 시간이 정해지고, 일정 30분 전에 알려 줍니다.

| | Windows | macOS |
|---|---|---|
| 폴더 | [`windows/`](windows) | [`mac/`](mac) |
| 기술 | C# WinForms, .NET Framework 4.8 | Swift, SwiftUI + AppKit |
| 요구 사항 | Windows 10, 11 | macOS 13 이상 (Apple 실리콘·인텔) |
| 결과물 | `TodoWidget.exe` 하나 | `일해라.app` (`Ilhaera-mac.zip`) |
| 데이터 | `%APPDATA%\TodoWidget\todos.json` | `~/Library/Application Support/Ilhaera/todos.json` |

두 버전은 같은 규칙(문장 해석, 반복, 알림)과 같은 저장 형식을 씁니다.

## 기능
- 일력처럼 큰 날짜(토 파랑, 일 빨강), 오늘 진행률, 다음 일정까지 남은 시간
- 지난 일정 / 오늘 / 내일 / 이후 / 기한 없음으로 묶은 목록, 완료 접기
- 문장으로 시간 정하기: `내일 오후 3시 회의`, `금요일 2시 반 치과`, `12/24 저녁 7시 파티`
- 반복: `매일 9시 영양제`, `평일 9시 반 스탠드업`, `매주 월요일 10시 회의`
- 시간 선택기: 날짜 칩·달력, 오전/오후·시·분, 빠른 선택, 반복
- 일정 30분 전 알림(5분~2시간 전으로 변경 가능), 10분 뒤 다시 알림, 놓친 일정 알림
- 로그인/부팅 시 자동 실행, 밝게·어둡게 테마, 투명도, 항상 위에 표시
- 색 바꾸기: 강조 색(체크·버튼·진행률)과 형광펜 색을 미리 고른 색이나 직접 고른 색으로
- 리퀴드 글래스 테마: 뒤 화면이 흐리게 비치는 유리, 투명도 조절
  - Windows: 10(1803 이상)·11. 설정의 "투명 효과"가 꺼져 있으면 불투명하게 보임
  - Mac: macOS 26 이상에서는 시스템 리퀴드 글래스, 13~15에서는 직접 그린 유리. "투명도 줄이기"가 켜져 있으면 불투명하게 보임

## 빌드
GitHub Actions(`.github/workflows/build.yml`)가 push 마다 두 버전을 테스트하고 빌드합니다. `v*` 태그를 올리면 릴리스에 두 파일이 붙습니다.

직접 빌드할 때:

```bash
cd mac && swift test && ./build-app.sh        # macOS, Xcode 명령행 도구 필요
```

```bash
dotnet run -c Release --project windows/tests/CoreTests.csproj
dotnet build -c Release windows/src/TodoWidget.csproj   # .NET 8 SDK
```

## Mac에서 처음 열 때
애플 개발자 서명이 없어서 처음 한 번은 막힙니다. `일해라.app`을 응용 프로그램 폴더로 옮겨 열고, 막히면 **시스템 설정 → 개인정보 보호 및 보안 → 그래도 열기**를 누르세요.
