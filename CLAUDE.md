# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

`ProjectManagementBoard` is a WPF desktop app (UI text in Korean, .NET 10, `net10.0-windows`) that is the **base shell** for a project-management board: a pannable/zoomable canvas of "관리 툴" (management tool) cards and group boxes. Each card has a `DockHost` area into which a tool (WPF plugin DLL, see "Plugin model") is docked. There are no tests and no NuGet packages. The repo is a monorepo (board + contract + tools) developed by a team on GitHub; see `README.md` for the workflow.

It is a trimmed derivative of the standalone VisionAlgorithmDeck app (same camera, card/group motion, popup editor, theme system). Deliberately **not** carried over: adding algorithms, Item View, the `.vad` format, the recent-project list, thumbnails. Settings (background, last project) live in `%AppData%\ProjectManagementBoard\settings.json`.

## Commands

Run from the repo root (where `ProjectManagementBoard.slnx` lives):

```powershell
.\build-all.ps1 -Run      # build board + contract + all tools, then start the board
.\build-all.ps1           # Debug build only (-Release for Release)
dotnet build ProjectManagementBoard.slnx
dotnet run --project ProjectManagementBoard/ProjectManagementBoard.csproj
```

The root solution contains the board, `ProjectManagementBoard.Contracts` and every project under `Tools/`; a tool build copies itself into the board's `Plugins\<Tool>\`. A running board locks its DLLs, so close it before rebuilding. CI (`.github/workflows/build.yml`) builds the whole solution on every PR.

## Design
- UX/UI 디자이너급의 디자인으로 구현
- Control의 이동 및 사이즈 조절은 동적인 움직임을 보여 줄것

## Plugin model

Tools (memo, checklist, schedule, ...) live under `Tools/` as WPF class libraries (.NET 10) whose DLLs are copied to `Plugins\<ToolName>\` next to the exe and docked into cards in-process (so they zoom/move/resize with the board; no external-exe embedding, no WinForms hosting). Each tool keeps its own data as a string via `IBoardToolInstance.SaveState/LoadState`, stored inside the board project file.

- `ProjectManagementBoard.Contracts` holds `IBoardTool` (factory: Id, DisplayName, CreateInstance) and `IBoardToolInstance` (View, SaveState, LoadState). Tool projects reference the Contracts project with `Private=false` (done once in `Tools/Directory.Build.props`; never ship a second copy of the contract DLL, or the types won't match). Treat the interfaces as stable and never change a tool `Id`.
- `Services/ToolCatalog` scans `Plugins\**\*.dll`, loads into the default AssemblyLoadContext (so XAML pack URIs inside tools work; no hot reload) and collects load errors in `Errors`.
- Docking works: `+ 새 관리 툴` / double-click shows a picker (or makes an empty card when `Plugins` has no tools); `ToolCardView.DockTool` puts the instance view into `ToolHost` (a `ToolContentHost`, which also makes the board leave presses, right-clicks, plain wheel and Ctrl shortcuts to the tool). Holding Ctrl overrides that for mouse gestures (`MainWindow.BoardOverride`): Ctrl+wheel always zooms and Ctrl+right-drag always pans, even over a tool or a button, because the Viewport's Preview handlers run before the tool sees the event. `ToolModel.ToolId`/`State` hold the link and the tool data; `CaptureState()` refreshes `State` before copy/delete.
- Individual tool files: a card's save button (`SaveToolButton`, hidden on empty cards) writes just that tool to its own file whose extension tells the tool (`ToolCatalog.FileExtensionFor`: the tool's `IBoardToolFileExtension` — memo `.pmmo`, checkmemo `.pmck`, calendar `.pmcd` — else ".pm" + first two letters of the id; clashes are reported in `Errors`; dialogs filter by it) (`ToolFileData`: ToolId, Title, CardColor, TitleFontSize, Width, Height, State — no position) via `MainWindow.SaveToolFile`; `ToolModel.ToolFile` remembers the path (cleared on paste). Adding a tool is two steps: `+ 새 관리 툴` picks the tool (`AddCard`, "다음"), then `AskNewOrLoad` offers 새 파일 / 불러오기; 불러오기 (`LoadToolFileInto`) rejects a file of another tool (ToolId check) and creates the card from the file. These files only feed 불러오기 — the board as a whole is still saved in the project file.
- Group files: a group's save button (`SaveGroupButton`) writes the group with everything geometrically inside it (`MembersOf`: nested groups + tools with their captured state, tools in board z-order) to a `*.pmgr` (`GroupFileData`; positions relative to the group's top-left, `ToolFile` links dropped) via `MainWindow.SaveGroupFile`. `+ 그룹` now asks 새 그룹 / 불러오기 (`AddGroup` → `CreateGroup` or `LoadGroupFile`); loading places the group at the next free slot in view, recreates groups then cards (staggered entrance) and reports tools that are not installed.
- Panels: the left panel (MENU/PROJECTS) toggles via the title-bar hamburger / Ctrl+B (animated `LeftWidth` DP that also drives the title bar's logo column); the top bar (+ 새 관리 툴 ...) via the chevron tab on the board's top edge (`LayoutTransform` ScaleY). Both states persist in `AppSettings` (`LeftPanelOpen`, `TopBarOpen`).
- Move mode: top-bar segmented switch 자유이동 / 맞춤이동 (Ctrl+G, persisted as `AppSettings.SnapMove`). `Controls/BoardSnap` holds the global step (20 board units, 0 = free); `ToolCardView` / `GroupView` run every dragged position and resized size through `BoardSnap.Apply`. Group drags snap the group and move members by the same delta.
- Projects: `*.pmb` JSON (`ProjectFile`: Tools incl. `ToolId`/`State`, Groups, BackgroundColor), manual save (title bar, Ctrl+S / Ctrl+Shift+S / Ctrl+O). `MainWindow` "Saving / projects" region: dirty = serialized cards+groups+background differ from the baseline (a 500 ms timer calls `CaptureToolStates` first), unsaved-changes prompt before new/open/close, last file reopened at startup (`AppSettings.LastFilePath`). No project list yet — PROJECTS only has the 새 프로젝트 / 불러오기 tiles.

### Tools (`Tools/<ToolName>/<ToolName>/<ToolName>.csproj`)

`Tools/Directory.Build.props` gives every tool `net10.0-windows`, `UseWPF`, the `Private=false` Contracts reference and `BoardPluginsDir`; `Tools/Directory.Build.targets` adds the `CopyToBoard` step (copies `<Tool>.dll/.pdb` into `ProjectManagementBoard\bin\<Configuration>\net10.0-windows\Plugins\<Tool>\`, override with `/p:BoardPluginsDir=`). A new tool's csproj only needs `RootNamespace`/nullable settings; add it to the root `.slnx` under `/Tools/`. Tools build without the board being built first.

- `MemoTool` (id `memo`): the minimal reference tool (a TextBox; the text is the state).
- `CheckMemoTool` (id `checkmemo`): port of the standalone CheckMemo (.NET Framework 4.8 floating checklist, kept outside this repo) to a .NET 10 plugin. `ListWindow` became the `ChecklistView` UserControl (white sheet filling the card; no window chrome, auto-height, title bar, tray, single-instance, opacity or multi-list); the card title replaces the list title; state is `ChecklistState` JSON (font size + items) via `SaveState/LoadState` instead of `DataStore`/`data.json`. Inside a card Ctrl+wheel zooms the board, so font size changes via the ⋯ menu or Ctrl +/-. `Nullable` is disabled there (ported code predates annotations).
- `CalendarTool` (id `calendar`): port of the standalone ProjectCalendar app (kept outside this repo). `MainWindow` became the `CalendarView` UserControl; kept: goals panel (add/rename/colour/reorder/delete, slide-out toggle), 8-week grid with drag selection, right-click menu (assign goal — max 2 per date, shown as a diagonal split — clear, done check, memo), memos (plain text; shown in the day tooltip), schedule sheets as bottom tabs (new/rename/copy/tab colour/delete/drag reorder). The original Gantt window is `SchedulePanel` (a `Border`, toggled by the header's Schedule icon) in a row between the calendar and the sheet tabs: opening raises `HeightChangeRequested(+300)` → `IBoardToolResizeRequest.ResizeRequested`, the board eases the card taller, and `FollowCardSize` hands exactly the gained height to `ScheduleRow`, so the calendar never changes size (closing reverses it; a 1.5 s timeout settles it if the card can't follow). `CalendarState.ScheduleOpen` is saved; a saved card's height already includes the panel. Removed: server/login/streaming, change tracking, history, mini-map window, CSV/file import-export, tray, opacity, pin. The header colour dot (`Theme_Click`, same presets/picker as the original) changes the calendar background. State is `CalendarState` JSON (sheets, active sheet, panel open, `BgHex`). `ConfirmDialog`/`MemoDialog` take the control as `host` (styles come from its resources; the board window is the owner). Implements `IBoardToolDefaultSize` so a new card starts at 1100×760.
- `IBoardToolDefaultSize` (Contracts, optional): implement it on the `IBoardTool` class to set the size of a newly added card; `MainWindow.CreateCard` applies it.
- `IBoardToolFileExtension` (Contracts, optional): implement it on the `IBoardTool` class to give its individually saved files a short, unique extension (3–4 letters, e.g. `.pmmo`); like the Id, never change it.
- `IBoardToolResizeRequest` (Contracts, optional): implement it on the `IBoardToolInstance` to ask for a bigger/smaller card (`ResizeRequested`, a size delta); `ToolCardView.OnToolResizeRequested` applies it through the model, so it eases like a manual resize.

## Architecture

- No DI, no MVVM framework: views own their logic in code-behind. Models implement `INotifyPropertyChanged` (`Models/ToolModels.cs`: `ToolModel`, `GroupModel`, `AppSettings`).
- **`MainWindow`** (`MainWindow.xaml(.cs)`) is the whole board controller: custom `WindowChrome` title bar, left MENU (Group View only) + PROJECTS tiles, and the board. `Viewport` (Grid, hosts the dot-grid background) contains `Board` (Canvas) with a `Scale`+`Translate` transform = the camera. Camera state (`_zoom`, `_panX/_panY`, fly/glide/inertia) is advanced by a `CompositionTarget.Rendering` loop (`OnCameraFrame`); the dot grid brush shares the board transform. Cards live in `_cards` (z-order = list order) and groups in `_groups`, with parallel `_views` / `_groupViews` dictionaries. Undo is a single `_undo` closure (last delete/paste) shown via a toast.
- **`Controls/ToolCardView`**: one card, built so it owns its motion — pointer input writes target X/Y/Width/Height into the model and a render loop eases the displayed geometry toward it (with drag tilt). Clicking the title raises `EditRequested` (handled by `MainWindow` → `EditorOverlay`). Events: `Activated`, `DragMoved`, `Released`, `DeleteRequested`, `CopyRequested`.
- **`Controls/GroupView`**: group boxes drawn below all cards (`GroupZBase`, restacked by area). Membership is **geometric, not stored** (`GroupModel.Contains` tests a center point), so dragging a group moves whatever it currently contains (`MembersOf`); drop-target highlighting is driven from `MainWindow`.
- **Edit popups**: `EditorOverlay` renders an `EditRequest` (fields of kind Text/Color/Path/Choice, Save/Cancel/Discard callbacks, `Tint`). `ToolEditors` builds the requests for tool and group names. `EditableText` is the click-to-edit label (`EditableField` says which editor to open).
- **Theme**: `Themes/OnDark.xaml` holds dark-board brushes (`ThemeText`, `Fg##` alpha tints, accents); `AppTheme.Apply` swaps them when the chosen board color is light (`ColorUtil.PrefersDarkText`). Use `DynamicResource` for these; surfaces that must stay dark (card body, toast, zoom bar, dialogs) merge their own copy of `OnDark.xaml`. A new `Fg##` alpha must be added to both `OnDark.xaml` and `AppTheme.Alphas`. Shared styles (buttons, scrollbar, tooltip, `AppLogo`) are in `App.xaml`.
- `Services/BoardStore`: settings load/save and JSON snapshot (de)serialization of a `ToolModel` for copy/paste.

## Gotchas

- `UseWindowsForms` is on, so `ProjectManagementBoard.csproj` removes the `System.Windows.Forms` / `System.Drawing` implicit usings; without that, `Color`, `Control`, `Application`, `UserControl`, `MouseEventArgs` etc. become ambiguous with the WPF types. When docking WinForms content, fully qualify those types.
- Resource pack URIs (`/ProjectManagementBoard;component/Themes/OnDark.xaml`) embed the assembly name; keep them in sync if the project is renamed.
