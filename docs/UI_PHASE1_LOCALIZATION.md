# Desktop Hub UI Phase 1 — Localization

实施日期：2026-08-26
分支：`develop`
本阶段范围：`.resx` 本地化基础设施、简体中文 culture 规范化，以及高可见 UI 文案迁移。

## 1. 实施结论

```text
LocalizationMechanism=.resx + ResourceManager via Localization/Strings.cs
EnglishResource=Localization/Strings.resx
ChineseResource=Localization/Strings.zh-Hans.resx
CultureNormalization=zh-CN/zh-SG/zh-Hans and zh-Hans-* -> zh-Hans
MigratedStringCount=16 new semantic keys
ExistingResourceTerminologyAligned=38 existing zh-Hans values
RestartRequiredForLanguageChange=true
SourceCodeBehaviorChanges=none
AutoOrganizeManagerChanged=false
NewDependencies=0
```

没有新增 `Strings.zh-CN.xaml`、WPF `ResourceDictionary` 本地化系统或第三方 localization framework。保存到 `frames.json`、`options.json` 和 `auto_organize.json` 的内部值没有改 schema，也没有改写现有用户配置。

## 2. Culture 规范化

`Strings.Culture` 现在在资源查找前调用 `NormalizeCulture`：

- `zh-CN`、`zh-SG`、`zh-Hans` 统一使用已存在的 `zh-Hans` 资源。
- `zh-Hans-*` 子文化也归一化到 `zh-Hans`。
- 繁体中文和其他语言保持原 culture，继续使用现有 ResourceManager fallback。
- 空的语言设置仍表示跟随 Windows `CurrentUICulture`。

语言选择仍由现有 `OptionsFormManager` 保存，并在应用启动时由 `App.xaml.cs` 调用 `Strings.SetLanguage`。改变语言后需要重启；本阶段没有引入 live rebinding。

## 3. 新增并接入的资源 key

以下 16 个 key 同时存在于英文资源和 `zh-Hans` 资源，并已通过 `Strings` 门面接入：

| Key | 主要调用点 | 中文值方向 |
|---|---|---|
| `AppDisplayName` | Search、Tray、刷新等待窗口 | 保留 `Desktop Frames +` 产品身份 |
| `FrameNewTitle` | 默认 Data Frame | 新建分组 - 将快捷方式拖到这里 |
| `FrameNamePrompt` | 新建 Frame 输入框 | 请输入新分组名称： |
| `FrameCreateTitle` | 新建 Frame 对话框 | 新建分组 |
| `FrameLockedTooltip` | Frame 锁定控件 | 分组已锁定（点击解锁） |
| `FrameUnlockedTooltip` | Frame 锁定控件 | 分组未锁定（点击锁定） |
| `FrameDrawInstruction` | 绘制 Frame 提示 | 拖出区域以创建分组（按 Esc 取消） |
| `FrameRefreshingConfiguration` | Frame 刷新等待窗口 | 正在刷新配置… |
| `FrameRefreshingFrames` | Frame 刷新等待窗口 | 正在刷新分组，请稍候… |
| `StartupTipsTitle` | 新 Profile 的提示 Note Frame | Desktop Frames + 使用提示 |
| `StartupTipsContent` | 新 Profile 的提示正文 | 中文操作提示，保留快捷键和产品名 |
| `ProfileNamePrompt` | 新建 Profile 输入框 | 请输入新配置方案名称： |
| `ProfileCreateTitle` | 新建 Profile 对话框 | 新建配置方案 |
| `TrayIconTitle` | NotifyIcon tooltip | Desktop Frames +（配置方案名） |
| `AutoOrgNewRuleName` | Auto Organize 新规则默认名 | 新规则 |
| `SearchResultTooltip` | SpotSearch 结果 tooltip | 位置 / 路径 |

同时将既有 `zh-Hans` 资源中的高频术语统一为：`Frame=分组`、`Profile=配置方案`、`Portal=文件夹视图`、`Auto Organize=自动整理`、`Customize=个性化`。这只是显示文案调整，不改变内部枚举、JSON 值或执行链。

## 4. 明确未做的事情

- 没有修改 `AutoOrganizeManager.cs`，没有修改规则匹配、碰撞、覆盖、删除、临时文件、稳定性、debounce 或 kill switch 行为。
- 没有改变 SpotSearch 的 hotkey、`_allShortcuts`、`Name.Contains()`、Frame JSON loading、结果选择或 launch 行为；仅资源化标题和 tooltip。
- 没有实现 Startup Tips 的 dismiss / `DoNotShowAgain`。
- 没有重画 Frame、调整布局体系、扩展搜索范围或开始快捷方式/文件夹视觉分组。
- 没有改 AssemblyName、可执行文件名、registry key、mutex、配置目录、update manifest 或 updater identity。
- 现有 Profile 中已经持久化的旧 Frame 标题/Startup Tips 正文不会被自动重写；新建/初始化路径使用本地化资源，既有 JSON 保持原样，避免本阶段修改用户数据。

## 5. 验证记录

```text
EnglishResourceValid=true
ZhHansResourceValid=true
MigratedKeysInEnglish=16/16
MigratedKeysInZhHans=16/16
PlaceholderMismatches=0
ZhCnNormalization=PASS (reflection test against compiled assembly)
FallbackValidation=PASS (en-US and sw-KE returned English; zh-CN/zh-SG/zh-Hans returned Chinese)
Fallback=base Strings.resx remains present
BuildConfiguration=Release
BuildCommand=Visual Studio 2022 BuildTools MSBuild /t:Build /p:Configuration=Release
BuildWarnings=1133 existing warnings total, including 2 COM/MSBuild warnings; no warning was introduced in modified call sites
NewWarnings=0
```

资源完整性由 XML 解析和 key/placeholder 对比检查；英文资源仍是 ResourceManager 的基础 fallback。`Strings.resx` 和 `Strings.zh-Hans.resx` 的资源文件均可正常解析。

## 6. Runtime smoke

使用独立的 `build\phase1-release` 输出进行只观察启动，没有点击设置、Auto Organize、文件操作、Tray 菜单或搜索热键：

```text
ProcessStarted=true
UACPrompt=false
ProcessResponding=true
AutoOrganizeDefault=false
DefaultFrameChinese=true
StartupTipsChinese=true
SearchBinding=Strings resource lookup verified; popup not targetable by Computer Use
TrayBinding=Strings resource lookup verified; tray automation incomplete
WorkingSetMB=175.39-175.96
PrivateMemoryMB=79.80-80.12
IdleCPUPercentApprox=near 0% (no sustained CPU growth observed)
NoContinuousMemoryGrowth=true
```

由于 Frame 是非激活、无任务栏的窗口，Windows Computer Use 没有暴露可操作顶层窗口；因此没有伪造鼠标点击或“已打开搜索框”的结论。程序实际生成的独立 `Profiles\Default\frames.json` 已包含本地化的默认分组标题和 Startup Tips 正文。

程序启动路径还会按上游既有逻辑更新 HKCU `SOFTWARE\Desktop_Frames_Plus\ProgramManagement\ProgramPath`。该值短暂指向测试输出，测试结束后已恢复为启动前运行的旧 Release DLL 路径；没有新增 `HKCU\...\Run` 值，也没有创建 Desktop context-menu 项。没有修改 HKLM。

## 7. 已知剩余缺口

低频设置、帮助、About、更新提示和部分历史 C# 动态控件仍可能包含英文或旧术语，详见 `audit/localization-phase1-remaining.md`。下一阶段可单独处理 Startup Tips 生命周期；不应在本阶段混入 Frame 视觉或搜索重构。
