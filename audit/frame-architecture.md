# Frame Architecture Audit

审计日期：2026-08-26
本轮只做架构和改造边界分析；没有创建、移动或重绘生产 Frame。

## 1. 架构结论

```text
FrameManager.cs
└─ Framemanager (static)
   ├─ CreateFrame(dynamic frame, TargetChecker)
   │  └─ NonActivatingWindow : Window
   │     └─ transparent outer Window
   │        └─ Border cborder
   │           └─ DockPanel
   │              ├─ titleGrid
   │              │  ├─ heart/menu TextBlock
   │              │  ├─ title Label/TextBlock
   │              │  ├─ filter control
   │              │  └─ lock TextBlock
   │              └─ content host / WrapPanel / note / portal / tabs
   ├─ IconManager / LazyIconLoader
   ├─ IconDragDropManager
   ├─ PortalFrameManager
   └─ NoteFramemanager / TabManager
FrameDataManager
└─ Profiles\\<profile>\\frames.json
```

## 2. 要求字段

| 字段 | 当前实现 |
|---|---|
| `FrameWindow` | 独立 WPF `NonActivatingWindow : Window`；不是 Explorer child，也不是 WinUI control |
| `FrameManager` | `Framemanager` static class in `FrameManager.cs` |
| `FrameData` | dynamic/JObject-like frame records：`Id`、`Title`、`X/Y`、`Width/Height`、`ItemsType`、`Items`、tabs 和外观字段 |
| `FramePersistence` | `FrameDataManager.SaveFrameData()` 写当前 Profile `frames.json` |
| `FrameStyle` | C# 动态创建 Border/Grid/TextBlock/Label，缺少统一 Style layer |
| `FrameTitleBar` | `titleGrid`；左侧 heart/menu、标题、filter、右侧 lock；标题高度和字体直接在代码设置 |
| `FrameResize` | `ResizeMode=CanResizeWithGrip`（未锁定时）并由 `NonActivatingWindow`/FrameManager 处理尺寸变化 |
| `FrameDrag` | 标题区域/窗口事件配合 `DragMove`/鼠标处理；标题双击折叠，Ctrl-click 等进入编辑路径 |
| `FrameOpacity` | outer/background alpha、菜单 tint、`NonActivatingWindow` 的 fade timer/animation；由 Settings 控制部分淡出行为 |
| `FrameBackground` | 当前默认 `Color.FromArgb(100, 0, 0, 0)` 的半透明深色 Border，形成偏灰的视觉重量 |
| `FrameBorder` | frame data 的 `FrameBorderColor` 或灰色 fallback，默认 thickness 约 2 |
| `FrameShadow` | WPF `DropShadowEffect` 在若干 UI/toast/icon 路径使用；没有统一 Win11 shadow abstraction |
| `FrameIconLayout` | WrapPanel/网格布局，icon size/spacing 等存入 frame JSON；`IconDragDropManager` 修改 `DisplayOrder` |

## 3. 窗口操作和文件系统操作必须分开

### 窗口层（当前 Frame 本身）

```text
READ       查找/读取 frame JSON、icon metadata
POSITION   设置 Left/Top/Width/Height，保存坐标
SHOW_HIDE  隐藏/显示/折叠 Frame window
REFRESH    重建 visual tree、刷新 items
```

这些操作本身不等于移动或删除文件。

### 文件/shortcut 导入层（外部拖入 Frame）

当前 `FrameManager` 的 drop handler 另有副作用：

- 对普通路径可能在 Profile `Shortcuts` 中创建新的 `.lnk`。
- 对 `.lnk`/`.url` 可能复制 shortcut 文件到 Profile `Shortcuts`。
- `DeleteOriginalShortcutsOnDrop` 存在，默认关闭；开启时可能删除来自 Desktop 的原快捷方式。
- Frame item JSON 保存的是 Profile shortcut 文件名及显示信息，而不只是原路径引用。

因此以下结论必须精确表述：

```text
FrameAssignmentMovesFilesystem=通常不移动原普通文件；但现有导入路径会创建/复制 Profile shortcut，并存在可选删除原 shortcut 的路径
FrameAssignmentOnlyCoordinates=false（仅窗口位置/Frame 内重排是坐标/JSON；完整 assignment 不是纯坐标）
FrameMembershipPersisted=true（存入 frames.json 的 Items/Tabs）
CanAutoAssignIconToFrame=false（当前没有基于 target/display name 的自动分类器；主要靠手动拖入）
```

`IconDragDropManager` 的 Frame 内重排只改变 JSON 顺序，不调用文件系统移动；这条路径可以作为视觉层设计参考，但不能代表外部拖入是 reference-only。

## 4. 当前视觉问题与源码原因

| 观察到的问题 | 现有实现原因 |
|---|---|
| 背景过灰、视觉重量大 | 外层 Border 使用低 alpha 黑色背景；多个组件直接写色值 |
| 标题栏偏厚 | title grid、标题字号和多个固定 control 列均直接在 FrameManager 中构造 |
| 控制元素长期存在 | heart/menu、filter、lock 没有统一 Idle/Hover template 状态 |
| 边框偏老式 | Border/颜色/圆角是逐个 Frame 代码设置，没有 Fluent token |
| Startup Tips 与普通 Frame 混在同一机制 | tips 是特殊 Note frame，而不是独立 onboarding surface |
| 不像 Windows 11 原生元素 | 未发现 `DwmSetWindowAttribute`、Mica、Acrylic、SystemBackdrop；只发现 `DwmGetWindowAttribute` 的 cloaking 查询 |
| 图标与空间利用率不足 | icon layout、spacing、标题/控制高度分散在动态代码中 |

## 5. V1 视觉状态设计（仅方案）

### Idle

- 轻透明背景，低 alpha、低对比度边框。
- 标题保留可读对比度；控制按钮默认不抢视觉焦点。
- 保留 hit-test 区域，但不显示完整编辑 chrome。
- 不增加持续动画；使用现有淡出能力或一次性的 100–200ms transition。

### Hover

- 仅在 Frame 或标题 hover 时显示 `⋯`/设置、折叠、锁定等控制。
- 控制按钮使用现有 WPF TextBlock/Button 能力，先不引入第三方控件。
- hover 不改变 item membership，也不触发 Auto Organize。

### Edit

- 只有显式进入编辑/自定义状态时显示 resize/reorder/grid controls。
- 退出编辑后恢复 Idle；不要把调试式 control chrome 长期留在桌面。
- 锁定 Frame 时仍保留现有 lock 语义，不能让视觉改造绕过 `IsLocked`。

## 6. Windows 11 能力审计

```text
DwmInteropExisting=DwmGetWindowAttribute only (DesktopStateDetector cloaking check)
MicaExisting=false detected
AcrylicExisting=false detected
RoundedCornersExisting=partial WPF CornerRadius only; native DWM rounded corner not present
DarkModeInteropExisting=false detected
```

低风险先后顺序：

1. ResourceDictionary/Style token，统一颜色、字号、圆角和边框。
2. 抽取 Frame title/header builder，逐步替换直接色值。
3. 可选增加很小的 `DwmSetWindowAttribute` native rounded-corner helper，并提供失败回退。
4. Acrylic/Mica 只作为可选实验；透明 WPF Window、Topmost 和桌面合成的交互风险高于普通 Style 改造。
