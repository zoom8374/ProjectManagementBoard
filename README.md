# Project Management Board

WPF(.NET 10) 보드 앱입니다. 캔버스 위에 **관리 툴** 카드와 **그룹**을 놓고, 툴은 별도 DLL(플러그인)로 개발해서 카드 안에 도킹합니다.

## 시작하기

필요한 것: .NET 10 SDK, Windows.

```powershell
.\build-all.ps1 -Run      # 보드 + Contracts + 모든 툴을 빌드하고 실행
.\build-all.ps1           # 빌드만 (Debug)
.\build-all.ps1 -Release  # Release 빌드
```

Visual Studio에서는 `ProjectManagementBoard.slnx`를 열면 보드와 모든 툴이 한 솔루션에 들어 있습니다.
툴은 빌드할 때마다 `ProjectManagementBoard\bin\<구성>\net10.0-windows\Plugins\<툴이름>\`에 자동 복사됩니다.

## 저장소 구조

| 폴더 | 내용 |
|---|---|
| `ProjectManagementBoard/` | 보드 앱(호스트) |
| `ProjectManagementBoard.Contracts/` | 보드와 툴이 공유하는 인터페이스 (`IBoardTool`, `IBoardToolInstance`) |
| `Tools/<툴이름>/` | 관리 툴 하나당 폴더 하나 (WPF 클래스 라이브러리) |

## 새 툴 만들기

1. `Tools\<툴이름>\<툴이름>\<툴이름>.csproj`를 만들고 `Tools\MemoTool`처럼 최소 설정만 둡니다. (`TargetFramework`, `UseWPF`, Contracts 참조, Plugins 복사는 `Tools\Directory.Build.props/.targets`가 처리합니다.)
2. `IBoardTool`을 구현합니다. `Id`는 프로젝트 파일에 저장되므로 **한 번 정하면 바꾸지 않습니다**.
3. `ProjectManagementBoard.slnx`의 `Tools` 폴더에 프로젝트를 추가합니다.
4. 빌드 후 보드에서 `+ 새 관리 툴`로 추가합니다.

참고용 예: `Tools/MemoTool`(가장 단순), `Tools/CheckMemoTool`(MVVM, 상태 저장).

## 팀 개발 규칙

- `main`에는 직접 푸시하지 않고 PR로 합칩니다. PR마다 CI(`.github/workflows/build.yml`)가 전체 빌드를 확인합니다.
- **Contracts 변경은 보드 담당자의 리뷰가 필요합니다.** 기존 인터페이스는 삭제하거나 이름을 바꾸지 말고, 멤버를 추가하는 방식으로만 바꿉니다.
- 툴은 `Contracts`만 참조하고 보드 내부 코드는 참조하지 않습니다. (나중에 툴을 별도 저장소로 분리할 수 있게 하기 위함입니다.)
- `bin/`, `obj/`, `Plugins/`의 DLL, 개인 설정(`%AppData%\ProjectManagementBoard`)은 커밋하지 않습니다.
- 담당자 지정은 `.github/CODEOWNERS`에 GitHub 사용자명을 채워서 사용합니다.
