# DesktopHub Core V1

DesktopHub Core V1 是当前 `develop` 分支经过真实桌面验收后的稳定功能基线。它把文件整理、Windows 原生桌面图标布局、可交互分组区域和快速搜索组合在一起，同时保持分组操作与用户文件系统操作相互分离。

## 1. Core V1 功能

- 自动整理普通桌面文件
- 原生 Windows 桌面图标枚举与布局
- 左右 Rail 分类布局与中央净空区域
- 可见 Group Header
- 可拖动 Group；成员图标整体移动
- 可按网格 Resize Group Zone，并拒绝小于成员容量的尺寸
- Manual Position Persistence
- Manual Size Persistence
- Manual Classification Persistence
- SpotSearch <kbd>Ctrl</kbd>+<kbd>`</kbd>
- User Desktop + Public Desktop search
- 中文 UI
- Snapshot / Restore

## 2. Auto Organize 安全原则

Auto Organize 只处理当前用户 Desktop 根目录中的普通文件，不递归扫描目录。生产规则按扩展名把受支持的普通文件移动到明确配置的目标文件夹。

- 用户需要显式启用 Auto Organize。
- `desktop.ini`、隐藏/系统文件、临时下载文件和 Office 锁文件被跳过。
- 目录不会被物理移动。
- `.lnk` 和 `.url` 不会被物理移动。
- 移动前执行有限次数的稳定性与锁定检查；无法确认稳定时 fail closed。
- `File.Move(..., overwrite: false)` 保证不覆盖目标文件。
- 当前生产规则全部使用 `Collision=Skip`；源文件和既有目标文件都保留。
- 执行路径不删除文件，也不使用覆盖或替换操作。

Auto Organize 是唯一预期会物理移动已匹配普通文件的 Core V1 功能。Group Zone、Resize、Header Drag、Manual Classification 和 Native Desktop Layout 只改变元数据或 Explorer 图标坐标。

## 3. SpotSearch 使用方式

按 <kbd>Ctrl</kbd>+<kbd>`</kbd> 打开 SpotSearch，输入名称后使用上下方向键选择，按 Enter 打开，或双击结果；按 Esc 关闭。

搜索范围：

- User Desktop
- Public Desktop
- 7 个 managed folders
- Frame items

快捷方式会索引 DisplayName、文件名、无扩展名文件名、目标文件名、参数和 URI。Canonical dedup 只合并相同启动目标的可见结果，不丢弃桌面别名。当前真实验收已确认 `Steam`、`Codex`、`论文`、`PDF` 和 `Chrome` 均可搜索；其中 Steam 来自 `C:\Users\Public\Desktop\Steam.lnk`。

SpotSearch 关闭时不建立后台索引；打开时刷新并搜索上述有限范围。

## 4. Desktop Group Zones 使用方式

桌面分组以 Header 和边界显示。进入“编辑桌面布局”后可查看所有 Zone 的范围、容量和 Resize handle。

- 拖动 Header 可按桌面图标网格移动整个 Group。
- 组内成员跟随移动，并且不会被当作本组障碍物。
- 拖动右下 Resize handle 可调整 Zone；预览按网格吸附。
- 小于当前成员容量的尺寸会被拒绝。
- 其他 Group 或未管理的稳定图标仍作为障碍物。

## 5. Manual Classification

把项目拖入目标 Zone 并确认，或使用现有人工整理 UI，可写入 `ManualLayoutGroupId`。分类优先级为：

```text
ManualLayoutGroupId
>
ReferenceGroupingClassifier high-confidence result
>
UNASSIGNED
```

Manual classification 会在 Refresh 和重启后保持；它只更新分组元数据，不物理移动、删除、重命名或复制用户文件。

## 6. Identity / Classification 安全机制

Native Desktop item 处理分为两个独立阶段：

1. Identity Resolution 只解析 Explorer label、label ordinal 和候选路径。
2. Classification 对每个已解析真实候选独立应用 manual override 或 classifier。

同名 Explorer label 不共享分类。候选按 ItemType rank 和 normalized OriginalPath 确定性排序；只有 Explorer label count 与 candidate count 相等时才做 ordinal pairing。数量不一致、Shell namespace 或无法可靠解析的场景 fail closed，不自动移动。

真实验收案例：

```text
The Seance of Blake Manor folder -> UNASSIGNED
The Seance of Blake Manor.url    -> GAMES
```

## 7. 已知限制

1. 当前 exe 内部仍叫 `Desktop Frames.exe`。
2. 当前未提供正式 installer。
3. 部分内部 namespace/config identity 保留上游名称。
4. UNKNOWN folder 默认进入“待整理”（UNASSIGNED）。
5. 图标拖入 Zone 后的分类确认不依赖后台高频监听；在明确交互、启动、手动 Refresh 或布局操作时处理。
6. Shell namespace、candidate count mismatch 等无法可靠解析的 Explorer identity 场景 fail closed，因此不会自动移动。
7. 上游 warning baseline 尚未专项清理；Core V1 冻结要求是 NewWarnings=0。

这些限制不是 Core V1 blocker。

## 8. 当前部署方式

项目根目录：

```text
C:\Users\71966\Desktop\DesktopHub\
```

Release 启动目录与程序：

```text
C:\Users\71966\Desktop\DesktopHub\Code\Desktop Frames\bin\Release\net8.0-windows7.0\
Desktop Frames.exe
```

当前部署是直接运行 Release 可执行文件；本轮冻结不改 assembly 或 exe 名称。

## 9. Recovery / Restore

- 如果原生桌面布局异常，使用已有 Snapshot / Restore 恢复已保存的图标坐标。
- 如果不想保留某个 Group 的人工位置，使用“恢复自动位置”。
- 如果不想保留某个 Group 的人工尺寸，使用“恢复自动大小”。
- 如果人工分类错误，使用现有 UI 重新选择分组，或清除对应项目的 `ManualLayoutGroupId`。
- Restore 按稳定路径处理；缺失、ambiguous 和 unmanaged 项目会安全跳过。

Core V1 稳定基线由 annotated tag `desktop-hub-core-v1` 标识。后续功能开发应从该标签对应提交继续，不在冻结基线上顺手扩展功能。
