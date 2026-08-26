# Desktop Hub UI Architecture Map

审计日期：2026-08-26
审计范围：`C:\Users\71966\Desktop\DesktopHub\Code\Desktop Frames` 及其项目文件。
本轮只读审计；没有修改生产源码、规则、真实 Desktop 或运行中的程序。

## 1. 源码结构

```text
App.xaml / App.xaml.cs  (Application 启动编排；没有 StartupUri/MainWindow)
├─ ProfileManager
│  ├─ SettingsManager              -> Profiles\\<profile>\\options.json
│  ├─ FrameDataManager              -> Profiles\\<profile>\\frames.json
│  └─ Strings                       -> Localization\\Strings*.resx / Languages\\*.resx
├─ TrayManager                      -> WinForms NotifyIcon + ContextMenuStrip
├─ Framemanager (FrameManager.cs)   -> 创建和管理每一个 Frame Window
│  ├─ NonActivatingWindow           -> WPF Frame 外壳、拖动、淡出、非激活窗口
│  ├─ IconManager / LazyIconLoader  -> Shell icon / shortcut icon
│  ├─ IconDragDropManager           -> Frame 内图标重排
│  ├─ PortalFrameManager            -> 文件夹内容视图及其操作
│  └─ NoteFramemanager / TabManager -> Note / tab 内容
├─ GlobalHotkeyManager              -> WH_KEYBOARD_LL
│  ├─ SearchFormManager              -> SpotSearch popup
│  └─ FrameFocusFormManager          -> Frame 标题筛选/聚焦 popup
├─ OptionsFormManager               -> 代码构建的设置窗口
├─ CustomizeFrameFormManager        -> Frame 外观设置
├─ ProfileManagerForm               -> Profile 管理
├─ AutoOrganizeForm / Manager       -> 自动整理设置和执行链
└─ About / MessageBoxes / Toasts    -> 其他代码构建窗口
```

项目文件 `Desktop Frames.csproj` 同时启用 WPF 与 WinForms：

```text
TargetFramework=net8.0-windows7.0
UseWPF=true
UseWindowsForms=true
OutputType=WinExe
RootNamespace=Desktop_Frames
```

项目只有一个空的 `App.xaml` 资源节点，没有 `StartupUri`，主要 UI 是 C# 动态构建的 WPF 控件；`TrayManager` 使用 WinForms `NotifyIcon`。

## 2. 关键定位结果

| 项目字段 | 当前实现 | 证据 |
|---|---|---|
| `ApplicationRoot` | `App : Application`；启动顺序由 `App.xaml.cs` 编排 | `App.xaml.cs` |
| `MainWindows` | 没有传统单一主窗口；Frame、设置、搜索等是独立窗口 | `App.xaml`、各 `*FormManager.cs` |
| `FrameWindowClass` | `NonActivatingWindow : Window` | `NonActivatingWindow.cs` |
| `FrameManager` | `public static class Framemanager`（类名保留上游的大小写） | `FrameManager.cs` |
| `FrameTitleBarImplementation` | `Framemanager.CreateFrame` 中动态创建 `Grid titleGrid`、标题 `Label`、filter/lock 控件；非 XAML template | `FrameManager.cs` |
| `StartupTipsImplementation` | 没有独立 `StartupTips` 类；新/空 Profile 的初始化路径在 `FrameManager.cs` 创建一个 Note frame，标题为 `Desktop Frames + Startup Tips` | `FrameManager.cs` 的 default frame/note frame 初始化段 |
| `GlobalStyles` | 未发现 App 级 WPF `Style`/`ControlTemplate` 资源；大量颜色、字体、尺寸直接在 C# 设置 | `App.xaml`、`FrameManager.cs`、`OptionsFormManager.cs` |
| `ThemeResources` | 文案有 `.resx` 资源；视觉主题资源字典不存在 | `Localization/Strings.cs`、`App.xaml` |
| `HardcodedColors` | 较多；`SolidColorBrush(Color.FromRgb/FromArgb)`、`DropShadowEffect`、inline XAML 均分散在 UI 文件 | `FrameManager.cs` 等 |
| `HardcodedFonts` | 主要为动态控件设置的 `Segoe UI` 及字号；没有统一字体 token | `FrameManager.cs`、`OptionsFormManager.cs` |
| `HardcodedStrings` | 存在；例如默认 Frame 标题、Startup Tips 标题/正文、Search 标题、锁定 tooltip、绘制提示 | `FrameManager.cs`、`SearchFormManager.cs` |
| `SettingsWindow` | `OptionsFormManager.ShowOptionsForm()` 创建无边框 WPF Window；左侧按钮导航 + 右侧自定义 `TabControl` | `OptionsFormManager.cs` |
| `TrayManager` | WinForms `NotifyIcon` 与 `ContextMenuStrip`；包含设置、Profile、Auto Organize、Frame 管理和退出 | `TrayManager.cs` |
| `SearchEntryPoint` | `GlobalHotkeyManager` 的低级键盘 hook 调用 `SearchFormManager.ToggleSearch()`；Frame 内也有打开入口 | `GlobalHotkeyManager.cs`、`FrameManager.cs` |
| `SpotSearchClass` | `SearchFormManager : Window` | `SearchFormManager.cs` |
| `SpotSearchWindow` | 600px 宽、无边框、透明外层、白色圆角 card、Topmost、失焦关闭的 WPF popup | `SearchFormManager.cs` |
| `SpotSearchBackend` | 构造时读取 `Framemanager.GetFrameData()`，扫描 `ItemsType == Data` 的主列表或 tabs | `SearchFormManager.LoadAllShortcuts()` |
| `IconSearchMechanism` | `Utility.GetShellIcon` / `IconManager` 等 Shell icon 获取；不是搜索索引 | `Utility.cs`、`IconManager.cs` |
| `SearchIndexMechanism` | 没有持久化或增量索引；使用内存 `_allShortcuts` 快照 | `SearchFormManager.cs` |
| `SearchScope` | 当前只覆盖已保存到 Data Frame 的 item；不主动扫描 Desktop 根目录，也不覆盖 Portal 内容 | `SearchFormManager.cs` |
| `FramePersistence` | `FrameDataManager` 序列化到当前 Profile 的 `frames.json` | `FrameDataManager.cs`、`ProfileManager.cs` |
| `FrameAppearancePersistence` | `frames.json` 保存位置、尺寸、隐藏/折叠、置顶、锁定、AutoRoll、标题样式、颜色、icon size/spacing、tabs 等 | `FrameDataManager.CreateNewFrame`、`SaveFrameData` |

## 3. Frame 创建与数据边界

当前 Frame 不是 Windows Explorer 的文件夹，也不是一个固定嵌入 Desktop 的 child control。它是一个独立 WPF `Window`，通过 `WindowStyle=None`、透明窗口、`WS_EX_NOACTIVATE` 和 Topmost/位置设置，呈现为桌面上的视觉层。

Frame 的窗口位置与尺寸是 JSON 数据；Frame membership 也是 JSON 数据。需要特别区分：

1. **窗口层**：`Left/Top/Width/Height`、折叠、淡出、置顶等，不改变文件系统。
2. **Frame 内重排**：`IconDragDropManager` 调整 `Items` 的顺序和 `DisplayOrder`，不移动原始文件。
3. **外部拖入导入**：`FrameManager` 对 `.lnk`/`.url` 通常复制到 Profile 的 `Shortcuts`；对普通路径可能创建 Profile shortcut；还存在一个默认关闭但可配置的“删除原快捷方式”选项。这条路径不是纯引用模式，未来视觉分组不能直接复用为“无副作用 assignment”。

## 4. 当前缺口与建议边界

- UI 视觉 token 没有集中层，导致 Frame、设置和弹窗的改造容易触碰高冲突文件。
- 文案资源基础已经存在，但硬编码可见文本仍会绕过本地化。
- 搜索入口/结果 UI 可以独立重做；搜索数据模型和 Frame 数据读取应先保留。
- 未来新增视觉分组应另建 reference/membership 路径，禁止把现有 shortcut copy/delete 导入路径误当成“只记录 FrameId”。
- Auto Organize 是已验证的安全功能，任何 UI 改造都必须只调用现有开关和安全执行链，不复制一套移动逻辑。
