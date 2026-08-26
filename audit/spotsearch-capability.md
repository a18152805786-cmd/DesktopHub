# SpotSearch Capability Audit

审计日期：2026-08-26
审计结论：现有 SpotSearch 适合复用为 V1 popup 的数据入口，但当前搜索范围和交互明显小于 Desktop Hub 的目标范围。

## 1. 能力矩阵

| 字段 | 当前结论 | 证据/说明 |
|---|---|---|
| `SpotSearchEntryPoint` | `GlobalHotkeyManager.HookCallback` -> `SearchFormManager.ToggleSearch()` | `GlobalHotkeyManager.cs` |
| `CurrentHotkey` | `Control + VK 192`，即 `Ctrl + \``/`Ctrl + ~`；默认启用且可配置 | `SettingsManager.EnableSpotSearchHotkey=true`、`SpotSearchKey=192`、`SpotSearchModifier="Control"` |
| `CurrentSearchTargets` | 已保存到 Data Frame 的 item | `SearchFormManager.LoadAllShortcuts()` 读取 `Framemanager.GetFrameData()` |
| `DesktopIcons` | 间接支持；只有已成为 Frame item 的对象会进入缓存，不扫描桌面 Shell icon 列表 | 不是 Desktop root index |
| `Shortcuts` | 支持；读取 Frame item 的 `Filename`/`DisplayName`，可显示 Shell icon | 仅限已在 Frame 数据中的项目 |
| `Folders` | 支持已保存为 Frame item 的文件夹 | `IsFolder` 会被保存/读取 |
| `Files` | 部分支持；普通文件只有被表示为 Frame item 才会搜到 | 不扫描 Desktop 分类目录的全部文件 |
| `PortalItems` | 当前 SpotSearch 不直接读取 Portal 内容 | Portal 有自己的窗口/数据路径 |
| `SearchByFilename` | 部分支持；`Name` 是 `DisplayName` 或去掉扩展名的 `Filename`，不搜索完整路径 | `_allShortcuts` 构建时确定 Name |
| `SearchByDisplayName` | 支持 | `DisplayName` 优先 |
| `SearchByShortcutTarget` | 不支持 | 未把 `TargetPath` 纳入 query/index |
| `SearchByShortcutArguments` | 不支持 | 未把 `Arguments` 纳入 query/index |
| `FuzzySearch` | 不支持 | 当前是 `Contains` |
| `SubstringSearch` | 支持 | `x.Name.ToLower().Contains(query)` |
| `PinyinSearch` | 不支持 | 无拼音库/转换层 |
| `InitialsSearch` | 不支持 | 无首字母索引 |
| `IndexRequired` | 当前不需要 | popup 构造时建立内存快照 |
| `RealtimeScan` | 不支持 | 缓存不是 FileSystemWatcher 增量更新 |
| `SearchLatency` | 未做 benchmark；复杂度约为一次 Frame 数据读取 + O(n) 字符串过滤，最多显示 21 条 | 不应在本轮虚构毫秒数 |
| `ResultRendering` | 32px Shell icon + 名称；tooltip 显示 Frame location 和 path；最多约 21 条 | `SearchFormManager.cs` |
| `KeyboardNavigation` | 有 Escape；没有真正的结果选择/上下移动模型 | 结果控件不是可选列表 |
| `EnterToOpen` | 支持，但打开的是第一个名称匹配项，不是当前选中行 | `Key.Enter` -> first matching item |

## 2. 当前实现路径

```text
Ctrl + `
  -> WH_KEYBOARD_LL in GlobalHotkeyManager
  -> SearchFormManager.ToggleSearch()
  -> construct WPF popup
  -> LoadAllShortcuts()
  -> read Data frames/main Items or Tabs
  -> cache Name/Path/IsFolder/Frame metadata
  -> query Name.Contains(query)
  -> show up to 21 icon rows
  -> mouse click or Enter -> LaunchResult()
```

这是“Frame item launcher”，还不是“Desktop root search”。它不能满足目标中的 `Desktop\\文档`、`Desktop\\PDF` 等目录全量搜索，除非这些文件已经被 Frame/其他索引表示。

## 3. V1 推荐

保留：

- `GlobalHotkeyManager` 的现有可配置 hotkey 入口。
- `SearchFormManager` 的 popup 生命周期（Topmost、失焦关闭、Escape）。
- `LoadAllShortcuts()` 所依赖的 Frame JSON 数据读取。
- `LaunchResult()` 的 Shell launch 逻辑，但以后应补充类型/路径显示。

重做或抽取：

- 将 `SearchResult` 从窗口代码中独立出来，字段至少包含 `DisplayName`、`Path/Uri`、`Kind`、`SourceFrameId`、`TargetPath`（可选快照）。
- 将“数据采集”与“结果 UI”分离；第一阶段仍可用轻量内存快照。
- 增加真正的可选中列表、上下键、Enter 打开、Ctrl+清空、无结果提示。
- 使用 `StringComparison.OrdinalIgnoreCase` 取代对字符串直接 `.ToLower()`，保持 Unicode 中文直接字符搜索。
- 先支持 Desktop Hub 管理范围内的文件名/显示名/路径末段；不做全文索引，不扫描整个 C: 盘。

## 4. 搜索范围分阶段

```text
V1a: Data Frame items + Desktop root 的非目录普通文件快照
V1b: Desktop\\文档/PPT/PDF/数据表格/图片/压缩包/安装包 的文件名快照
V1c: Desktop folders + .lnk/.url metadata + Frame membership
Later: TargetPath/Arguments/Steam URI/AppUserModelID fields
Not V1: full-disk index, file contents, cloud search, external search service
```

如果加入 Desktop root 快照，应只读、过滤 hidden/system/`desktop.ini`、不跟随目录递归，且不能复制 Auto Organize 的移动逻辑。

## 5. Hotkey 取舍

```text
CurrentSpotSearchHotkey=Ctrl + ` (VK 192)
CtrlSpaceConflictRisk=HIGH for Chinese IME/input method; also possible PowerToys/application conflicts
RecommendedDefaultHotkey=keep Ctrl + ` for first UI iteration; expose configuration
UserConfigurable=true (existing SettingsManager fields)
```

不建议本轮强制改成 `Ctrl + Space`。如果未来需要更容易发现的默认值，应在中文输入法、PowerToys 和常驻软件环境中实测后再选；搜索 popup 入口和 Tray help 可以提高可发现性，而不必牺牲现有快捷键兼容性。
