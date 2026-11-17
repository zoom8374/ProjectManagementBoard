# HANDOFF — Project Management Board

작업 인수인계 문서입니다. 지금까지 만든 것, 그렇게 정한 이유, 남은 일을 정리합니다.
코드 구조의 상세 설명은 `CLAUDE.md`, 빌드와 팀 규칙은 `README.md`를 참고하세요.

- 저장소: `zoom8374/ProjectManagementBoard` (GitHub, `main`)
- 최신 커밋: `7b3f3c1 메모 Font Size` (그 전: `8dde89e 구조변경`)
- CI: GitHub Actions `build` 통과 확인됨

---

## 1. 이 프로그램은 무엇인가

WPF(.NET 10) 보드 앱입니다. 확대/축소와 이동이 되는 캔버스 위에 **관리 툴** 카드와 **그룹** 박스를 놓습니다.
각 관리 툴은 별도로 개발한 프로그램(WPF 클래스 라이브러리 DLL)이며, 카드 안에 도킹되어 보드와 함께 이동, 크기 조절, 확대/축소됩니다.

예: 메모장, 체크리스트, (예정) Schedule.

## 2. 진행 이력

| 단계 | 내용 |
|---|---|
| 베이스 구성 | 참고 앱 VisionAlgorithmDeck에서 보드 기본 기능만 가져옴: 카메라(휠 줌, 우클릭 드래그 이동, 관성, 전체 보기), 카드/그룹의 애니메이션 이동·리사이즈, 그룹 중첩, 배경색과 밝은/어두운 테마, 복사/붙여넣기, 삭제 실행 취소. 알고리즘 추가, Item View는 제외. `+ 새알고리즘` → `+ 새 관리 툴`. |
| 이름 | 프로젝트명 `ProjectManagementBoad` → `ProjectManagementBoard`로 정정. 좌측 상단 표기는 `PM Board`. |
| 1. 플러그인 계약 | `ProjectManagementBoard.Contracts` 프로젝트(`IBoardTool`, `IBoardToolInstance`)와 `ToolCatalog`(Plugins 폴더 스캔) 추가. |
| 2. 도킹 | `+ 새 관리 툴`/더블클릭 시 툴 선택 창 → 카드 안에 툴 화면 도킹. 툴을 못 찾거나 오류가 나면 카드에 안내만 표시. |
| 3. 저장/불러오기 | `*.pmb` 프로젝트 파일, 저장/다른 이름으로 저장/불러오기/새 프로젝트, 미저장 확인, 마지막 프로젝트 자동 열기. |
| 4. 샘플 툴 | `MemoTool`(메모장). 프로젝트 파일 → 툴 상태 복원까지 화면으로 검증. |
| 패널 숨기기 | 왼쪽 패널(☰ 또는 Ctrl+B), 위쪽 바(보드 상단 가운데 화살표 탭). 상태 저장됨. |
| CheckMemo 이식 | 기존 CheckMemo(.NET Framework 4.8 플로팅 체크리스트)를 `CheckMemoTool`(id `checkmemo`)로 이식. |
| 이동 모드 | 우측 상단 `자유이동 / 맞춤이동` 전환(Ctrl+G). 맞춤이동은 20 단위 칸으로 이동·리사이즈. |
| 글자 크기 | 그룹 제목 18→21, 관리 툴 제목 18→20, 메모장 본문 14→17. |
| 팀 개발 구성 | 모노레포로 재구성(`Tools/`), 빌드 스크립트, CI, `.gitignore`/`.gitattributes`/`.editorconfig`, `CODEOWNERS` 템플릿, `README.md`. |

## 3. 주요 결정 사항과 이유

### 3.1 툴 도킹 방식: 같은 프로세스의 WPF 플러그인 DLL
- **결정**: 툴은 .NET 10 WPF 클래스 라이브러리로 만들고, 보드가 `Plugins\<툴이름>\*.dll`을 로드해 `UserControl`을 카드에 넣는다.
- **이유**: 보드는 WPF `ScaleTransform`으로 줌을 한다. 외부 exe 임베딩(`SetParent`/`HwndHost`)이나 `WindowsFormsHost`는 HWND라서 줌이 적용되지 않고, 항상 다른 요소 위에 그려지며(airspace), 기울기·페이드 애니메이션도 안 된다. 사용자 요구가 "줌이 되는 방향"이었으므로 WPF 플러그인만 지원한다.
- **대가**: 툴이 크래시하면 보드도 같이 죽을 수 있다(프로세스 격리 없음). WinForms 툴은 지원하지 않는다.
- 보드 csproj에는 `UseWindowsForms`가 켜져 있지만 현재 쓰는 곳은 없다(초기 계획의 흔적). 대신 WinForms/System.Drawing 암시적 using을 제거해 WPF 타입과의 충돌을 막아 두었다.

### 3.2 플러그인 로딩: 기본 AssemblyLoadContext
- **결정**: `Assembly.LoadFrom`으로 기본 컨텍스트에 로드한다.
- **이유**: 툴 내부 XAML의 pack URI(`/툴이름;component/...`)가 별도 로드 컨텍스트에서는 깨질 수 있다.
- **대가**: 핫 리로드 불가. 툴 DLL을 바꾸면 보드를 재시작해야 한다. 실행 중인 보드는 DLL을 잠그므로 빌드 전에 닫아야 한다.

### 3.3 계약 DLL은 한 벌만
- 툴은 `Contracts`를 `Private=false`로 참조해 출력에 계약 DLL을 복사하지 않는다. 두 벌이 있으면 `IBoardTool`이 서로 다른 타입이 되어 툴이 인식되지 않는다. `ToolCatalog`도 Plugins 안의 계약 DLL은 건너뛴다.

### 3.4 툴 데이터는 보드 프로젝트 파일 안에
- **결정**: 각 툴은 `SaveState()`/`LoadState(string)`로 자기 데이터를 문자열(보통 JSON)로 주고받고, 보드가 `.pmb`에 카드 정보와 함께 저장한다.
- **이유**: 사용자 결정("보드프로젝트 안에서 관리"). 카드 복사/붙여넣기, 삭제 후 되돌리기 때도 툴 내용이 따라간다.
- 미저장 감지를 위해 보드가 0.5초마다 모든 툴의 `SaveState()`를 호출한다. 툴의 `SaveState`는 가볍고 결정적이어야 한다(호출할 때마다 시각 등이 바뀌면 항상 "미저장"으로 보인다).

### 3.5 툴 ID는 영구
- `IBoardTool.Id`는 `.pmb`에 저장되어 툴을 다시 찾는 열쇠다. 한 번 정하면 바꾸지 않는다. 현재: `memo`, `checkmemo`.

### 3.6 입력 우선순위
- 툴 영역(`ToolContentHost`) 안에서는 클릭, 우클릭, 키보드, 일반 휠을 툴이 받는다. 카드 이동은 제목줄이나 테두리로 한다.
- **Ctrl+휠은 툴 위에서도 보드 줌**이다. 그래서 CheckMemo의 Ctrl+휠 글자 크기 기능은 ⋯ 메뉴/Ctrl +/-로 대체했다.
- 툴에 포커스가 있으면 보드 단축키(Ctrl+N, Ctrl+Z 등)는 꺼진다. 단, Ctrl+S / Ctrl+Shift+S / Ctrl+O는 항상 보드가 처리한다.

### 3.7 CheckMemo 이식에서 뺀 것
- 원본의 창 기능(제목줄 드래그, 자동 높이, 트레이, 단일 실행, 다중 리스트, 배경 투명도, 리스트 삭제/종료 메뉴)은 카드가 대신하므로 제거했다. 리스트 제목은 카드 제목이 대신한다.
- 데이터는 원본의 `%LocalAppData%\CheckMemo\data.json` 대신 `.pmb`에 저장된다. **기존 data.json 가져오기는 구현하지 않았다.**
- 원본 소스는 저장소 밖(로컬 백업 폴더)에 있고 수정하지 않았다.

### 3.8 저장소 구조: 모노레포(A안)
- **결정**: 보드, Contracts, 모든 툴을 한 저장소에 둔다. 팀원이 같이 개발하고, 툴을 보드와 따로 배포할 필요가 없다는 결정에 따름.
- 나중에 툴을 별도 저장소로 분리할 수 있도록 다음 규칙을 지킨다.
  - 툴은 `Contracts`만 참조하고 보드 내부 코드는 참조하지 않는다.
  - `Contracts`는 추가 방식으로만 바꾼다(삭제·이름 변경 금지).
- 분리가 필요해지면 `Contracts`를 버전이 붙은 NuGet 패키지(사내 피드)로 배포하는 B안으로 전환한다.

### 3.9 글자 크기 단위
- 사용자가 말한 "포인트"는 앱의 크기 숫자(WPF 단위, px) 그대로 더하는 것으로 해석해 적용했다(3pt ≈ 4px 환산은 하지 않음).

## 4. 현재 저장소 구조

```
ProjectManagementBoard.slnx        보드 + Contracts + 모든 툴
Directory.Build.props              RepoRoot, BoardOutDir
build-all.ps1                      전체 빌드 (-Release, -Run)
ProjectManagementBoard/            보드 앱
ProjectManagementBoard.Contracts/  IBoardTool, IBoardToolInstance
Tools/
  Directory.Build.props/.targets   툴 공통 설정 + Plugins 자동 복사
  MemoTool/                        id: memo
  CheckMemoTool/                   id: checkmemo
.github/workflows/build.yml        PR/푸시마다 빌드 + 툴 복사 확인
.github/CODEOWNERS                 주석 처리된 템플릿
```

## 5. 남은 일

### GitHub 설정 (사용자 작업)
- [ ] `.github/CODEOWNERS`에 팀원 GitHub 사용자명을 채우고 주석 해제
- [ ] `main` 브랜치 보호: PR 필수, `build` 체크 필수, 승인 1명 이상, Code Owner 리뷰 필수
- [ ] 팀원을 Collaborators로 초대

### 기능
- [ ] Schedule 툴 개발 (`Tools/` 아래 새 툴로)
- [ ] PROJECTS 영역의 최근 프로젝트 목록 (지금은 `새 프로젝트`/`불러오기` 타일만 있음)
- [ ] (선택) CheckMemo의 기존 `data.json` 가져오기
- [ ] (선택) 맞춤이동 칸 간격 조절 UI (현재 `BoardSnap.GridStep = 20` 고정)

### 확인이 덜 된 부분
- 화면 캡처로는 확인했지만 **직접 조작은 해보지 않은 것**: 툴 선택 창, 저장 버튼 동작, 체크리스트의 입력·완료·드래그 정렬, 맞춤이동 드래그, 패널 접기 애니메이션.
- 테스트 프로젝트는 없다.

## 6. 알아 두면 좋은 점

- **빌드가 DLL 잠금으로 실패하면** 실행 중인 보드를 닫는다.
- **새 툴 추가 순서**: `Tools\<이름>\<이름>\<이름>.csproj`(MemoTool처럼 최소 설정) → `IBoardTool` 구현 → 루트 `.slnx`의 `/Tools/` 폴더에 추가 → 빌드 → 보드에서 `+ 새 관리 툴`.
- 툴 화면은 카드 크기에 맞춰 늘어나므로 고정 `Width`/`Height`를 두지 않는다.
- 카드 본체는 항상 어두운 배경이다. 흰 배경이 필요한 툴(체크리스트처럼)은 자체 배경을 깔고, 보드의 전역 스크롤바 스타일이 흰 배경에서 안 보이므로 툴 안에서 다시 정의한다.
- 설정 파일: `%AppData%\ProjectManagementBoard\settings.json` (배경색, 마지막 프로젝트, 패널 상태, 이동 모드). 커밋 대상 아님.
