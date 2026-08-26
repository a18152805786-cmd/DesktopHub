# Desktop Hub UI V1 Proposal

状态：审计后的设计方案，等待用户确认；本轮没有开始 UI 施工。
审计基线：`develop` / `165576b5cf45c13a4855357ad31ce67a1ea3800c`（生产 Auto Organize 已验证版本）。

## 决策摘要

```text
RecommendedOption=A
TopBarRecommendation=C
SourceCodeModified=false (本轮)
```

推荐先做 **Option A — Minimal Fluent**。原因不是目标要停在 A，而是当前项目的 UI 主要由 C# 动态构建，FrameManager/Options/Search 是高耦合、高冲突文件；先建立资源和视觉边界，再逐步引入 Desktop Hub 的视觉分组能力，最容易维持 upstream 同步和安全回退。

目标形态仍然是：

```text
安全 Auto Organize 文件整理
+ 轻量 Fluent Frame 视觉分组
+ Spotlight-style SpotSearch popup
```

快捷方式、文件夹和程序的未来分组必须是 reference/membership，不是物理移动。

## A. 当前 UI 架构

```text
App : Application
├─ ProfileManager / SettingsManager / Strings(.resx)
├─ TrayManager (WinForms NotifyIcon)
├─ Framemanager (static)
│  └─ NonActivatingWindow (one WPF Window per Frame)
│     ├─ titleGrid + dynamic controls
│     └─ content / icons / tabs / portal / note
├─ SearchFormManager (SpotSearch WPF popup)
├─ FrameFocusFormManager (Frame title focus popup)
├─ OptionsFormManager (code-built settings window)
├─ CustomizeFrameFormManager
├─ ProfileManagerForm / AutoOrganizeForm / dialogs
└─ GlobalHotkeyManager (WH_KEYBOARD_LL)
```

持久化边界：

```text
Profiles\\<profile>\\frames.json   -> frame geometry, membership, tabs, appearance
Profiles\\<profile>\\options.json  -> profile settings
Languages\\*.resx                  -> optional language packs
MasterOptions.json                  -> profile/global options path where applicable
```

Frame 是独立 WPF window。Frame 的位置/尺寸/折叠状态可以是纯 JSON/UI 行为，但当前外部拖入 shortcut 的导入路径会复制到 Profile `Shortcuts`，并存在可选删除原 shortcut 的设置。因此不能把现有导入路径直接当成未来的“视觉接管而不物理移动”。

## B. 汉化方案

### 技术实现

继续使用：

```text
Localization/Strings.cs
ResourceManager
Strings.resx (English fallback)
Strings.zh-Hans.resx (existing Chinese resource)
Languages/<culture>.resx (optional packs)
```

不要新增并行 `Strings.zh-CN.xaml`。第一阶段把核心硬编码文案迁移为已有 `Strings.Get()`/typed accessor；`zh-CN` 可以在语言规范化层映射到现有 `zh-Hans`，或者以后添加同键 `Strings.zh-CN.resx`。

### 运行策略

当前语言修改需要重启；V1 保持此策略。动态刷新所有已经创建的 Frame、Tray、popup 和 settings 控件会扩大改动面，而且不是产品目标的核心。

### 文案方向

采用 `分组`、`自动整理`、`文件夹视图`、`设置` 等简洁术语，完整 mapping 见 `docs/LOCALIZATION_ZH_CN_DRAFT.md`。

## C. 搜索方案

### 保留

- `GlobalHotkeyManager` 的现有可配置 `Ctrl + \`` 入口。
- `SearchFormManager` 的 popup 生命周期：Topmost、失焦关闭、Escape。
- `LoadAllShortcuts()` 的 Frame JSON 读取能力。
- `LaunchResult()` 的 Windows Shell launch 能力。

### 重做/抽取

- 保留 backend 思路，抽出 `SearchResult` 数据模型和数据提供者。
- 结果面板改为真正可选中的列表：上下键、Enter 打开、Escape 关闭。
- 先支持 Unicode 中文直接搜索、大小写不敏感 substring、文件名/显示名/路径末段。
- 后续再按需增加 Desktop 分类目录快照、`.lnk/.url` metadata、Frame membership。
- 不做全盘索引、文件内容索引、云搜索或第二个外部服务。

### 热键

```text
CurrentSpotSearchHotkey=Ctrl + ` (VK 192)
CtrlSpaceConflictRisk=HIGH with Chinese IME/input methods and possible utilities
RecommendedDefaultHotkey=keep Ctrl + ` initially
UserConfigurable=true
```

不在本轮把 `Ctrl + Space` 强行设为默认。可发现性由一次性 Startup Tips、Tray help 或设置页说明解决。

## D. Frame 新 UI 方案

### Idle

- 半透明但更轻的背景；低对比度边框；标题清晰。
- 控制按钮默认淡出/隐藏，只保留可用 hit-test 区域。
- 不使用持续动画，不引入常驻 blur。

### Hover

- 显示 `⋯`、折叠、设置、锁定等少量控制。
- 使用 WPF Style/Template 和现有原生控件，不引入大型 UI framework。
- Hover 只改变显示，不触发文件操作和 Auto Organize。

### Edit

- 只有显式编辑时显示 resize/reorder/grid controls。
- 退出编辑后回到 Idle；不要把开发工具式 control chrome 长期留在桌面。
- 保持 `IsLocked` 语义和既有 kill switch/Auto Organize 安全逻辑。

### 视觉技术边界

当前只发现 `DwmGetWindowAttribute`（cloaking 查询），没有 Mica/Acrylic/SystemBackdrop/`DwmSetWindowAttribute`。首选 ResourceDictionary、WPF Style、ControlTemplate 和轻量 opacity；native rounded corner 可作为后续小 helper，Acrylic/Mica 暂不作为 V1 必需项。

## E. 快捷方式和文件夹的视觉分类

未来的 assignment 应采用独立 reference item：

```text
原始 .lnk/.url/文件夹继续留在原路径
Frame JSON 只保存 FrameId + OriginalPath/Uri + DisplayName + 可选 target 快照
Assign / Unassign 只改变 membership
Remove membership 不删除原对象
```

当前已有 shortcut 解析能力：`.lnk` target/arguments、Shell icon、`.url` URL/icon、部分 Steam URI 和 Store shortcut heuristic。当前没有 AppUserModelID 模型，也没有自动分类器。未来可以用 `DisplayName`、`TargetPath`、`Arguments`、Steam URI 等作为“建议分组”输入，但不应把判断失败的对象自动搬动或复制。

重要兼容边界：现有 Frame 外部 drop 是“导入/复制 shortcut”路径，可能写入 Profile `Shortcuts`，不是 reference-only。新视觉分类必须是另一条明确路径。

## F. 三个 UI 方案

### Option A — Minimal Fluent

```text
ChangeScope=本地化基础、Startup Tips 持久关闭、Frame 轻量样式 token、SpotSearch popup result UI
FilesAffected=约 8-12 个核心文件；优先新增 helper/resource 与小范围接线
Risk=LOW-MEDIUM
EstimatedComplexity=LOW-MEDIUM
```

内容：

- 复用现有 `.resx`，逐步迁移核心硬编码字符串。
- Startup Tips 增加真正的持久化 dismiss state，默认只对新 Profile/首次显示。
- Frame 只先调整颜色、边框、标题间距和 hover 控制，不改 membership/文件逻辑。
- SpotSearch 只改结果 UI和数据模型边界，保留现有 popup/backend 入口。
- 不增加永久搜索栏；搜索由 popup + Tray help 入口承担。

### Option B — Desktop Hub Recommended

```text
ChangeScope=Option A + Fluent Frame control layer + Spotlight search scope + Auto Organize status入口 + reference-only shortcut/folder membership
FilesAffected=约 15-25 个核心/新增文件
Risk=MEDIUM-HIGH
EstimatedComplexity=MEDIUM-HIGH
```

这是产品目标较完整的形态：中文、轻量 Frame、Spotlight search、Auto Organize 状态入口和视觉分组。但当前 FrameManager、Options、Search 和数据 schema 尚未分层，若一次实施会扩大回归/merge 风险。建议把它作为 A 稳定后的路线，而不是第一个施工批次。

### Option C — More Visual

```text
ChangeScope=Option B + 常驻/折叠 Search Bar、更丰富 backdrop、更多动态控制和状态视觉
FilesAffected=约 20-30 个文件或更多
Risk=HIGH
EstimatedComplexity=HIGH
```

它可能更接近截图中的“产品化”观感，但常驻搜索栏会占用桌面空间，backdrop/blur 会提高合成和内存风险，丰富控制也更容易与 Frame 输入、非激活窗口和 upstream 核心实现冲突。本阶段不推荐。

## G. 推荐方案

```text
RecommendedOption=A
```

理由：

1. 现有架构没有统一视觉层，A 可以先建立 ResourceDictionary/Style 和小型 helper，不必重写 WPF 架构。
2. SpotSearch backend 可复用，A 只重做入口/结果 UI，不重复建立搜索系统。
3. 不触碰 Auto Organize 执行链；现有 `Collision=Skip`、`OverwriteNever`、`DeleteNever`、临时文件跳过、active-file safety、debounce、kill switch 保持原样。
4. 每一阶段可独立 commit/rollback，适合持续同步 upstream。
5. 未来 A 的资源、search model、visual reference adapter 可以逐步演进到 B，而不用一次性合并高风险改动。

## H. 分阶段施工顺序

每个阶段单独 commit；每阶段先 Release build + 安全 smoke test，再进入下一阶段。

```text
UI Phase 0  本审计文档（当前）
  -> docs(ui): audit Desktop Hub interface architecture and propose UI v1

UI Phase 1  Localization infrastructure
  -> 保留 .resx/Strings.cs；迁移核心硬编码；zh-CN 规范化

UI Phase 2  Startup Tips cleanup
  -> 首次/一次显示；持久化 DoNotShowAgain；不改变 Auto Organize

UI Phase 3  Frame Fluent restyle
  -> 新增 style/token/helper；Idle/Hover/Edit；先不改数据和拖入语义

UI Phase 4  SpotSearch redesign
  -> 可选结果列表；保留 Ctrl+`；逐步扩展 Desktop Hub 管理范围

UI Phase 5  Shortcut/folder visual grouping
  -> 新增 reference-only membership；原路径不动；单独安全测试

UI Phase 6  Settings cleanup
  -> 组织为常规/外观/自动整理/分组/搜索/关于；保留现有安全开关

UI Phase 7  Product rename
  -> 最后单独做 exe/namespace/registry/mutex/profile/updater 迁移设计和测试
```

## I. 性能影响

当前 baseline：

```text
PrivateMemory≈94MB
WorkingSet≈175MB
IdleCPU≈0%
NoContinuousMemoryGrowth=true
```

| 建议 | MemoryRisk | CpuRisk | ImplementationRisk |
|---|---|---|---|
| `.resx` 文案迁移 | LOW | LOW | LOW |
| 轻量 ResourceDictionary/Style | LOW | LOW | LOW-MEDIUM |
| Startup Tips dismiss flag | LOW | LOW | LOW |
| SpotSearch popup UI restyle | LOW | LOW | LOW-MEDIUM |
| SpotSearch 轻量内存快照 | LOW-MEDIUM | LOW-MEDIUM | MEDIUM |
| Desktop Hub 范围 FileSystemWatcher/index | MEDIUM | MEDIUM | MEDIUM |
| reference-only membership | LOW-MEDIUM | LOW | MEDIUM |
| 常驻复杂 blur/Acrylic | MEDIUM | MEDIUM | MEDIUM-HIGH |
| WebView/Chromium search bar | HIGH | HIGH | HIGH（明确禁止） |

设计目标是保持 `PrivateMemory<120MB`、`WorkingSet<220MB`、Idle CPU 接近 0%，但每个施工阶段都应以当前 94/175/0 baseline 做对比，不把几十 MB 差异直接当成重构理由。

## J. Upstream 合并风险

```text
HighConflictFiles=
  Code/Desktop Frames/FrameManager.cs
  Code/Desktop Frames/App.xaml.cs
  Code/Desktop Frames/OptionsFormManager.cs
  Code/Desktop Frames/SettingsManager.cs
  Code/Desktop Frames/TrayManager.cs
  Code/Desktop Frames/GlobalHotkeyManager.cs
  Code/Desktop Frames/FrameDataManager.cs (if schema changes)
  Code/Desktop Frames/Localization/Strings.cs
  Code/Desktop Frames/Localization/Strings.resx
  Code/Desktop Frames/RegistryHelper.cs (rename/startup/context menu)

LowConflictExtensionPoints=
  new ResourceDictionary/style helpers
  new SearchResult/data-provider classes
  new isolated popup/result controls
  additional language-pack .resx
  reference-membership adapter (future, with schema review)
```

高冲突文件尽量只做接线；新增的样式、数据模型和 helper 放在独立文件中。尤其不要把 Frame UI、shortcut reference schema、Auto Organize 和产品 rename 放在一个 commit。

## Auto Organize 不变量

未来所有 UI 只允许读/调用现有 Auto Organize 状态，不复制或绕过安全逻辑：

```text
Collision=Skip
OverwriteNever=true
DeleteNever=true
TemporaryFilesSkip=true
ActiveFilesSkip=true
Debounce=true
KillSwitch=true
DefaultEnabled=false for new installs/profiles unless existing user setting says otherwise
```

本轮不修改规则、不修改 UI、不改变 hotkey、不创建 Frame、不分类 `.lnk`/`.url`/文件夹，也不改变真实 Desktop 布局。
