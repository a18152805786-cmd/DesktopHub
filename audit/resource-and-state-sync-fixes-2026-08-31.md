# DesktopHub 资源与状态同步修复审计 — 2026-08-31

修复基准：`3d450fb`（`desktop-hub-v1.2.1`）。工作分支：`fix/resource-and-state-sync`。

本轮只修复审查报告指定的四项：Shell 图标句柄泄漏、分组解锁状态不同步、自动整理同目录目标、分组缩放无效状态持久化。没有处理报告附录中的其他问题，也没有更改内部程序集名称、注册表路径、安装程序、搜索范围、身份分类规则或 Explorer 交互方案。

## 提交拆分

| 提交 | 修复项 |
| --- | --- |
| `fc55323` | 用 `DestroyIcon` 在 `finally` 中释放 `SHGetFileInfo` 返回的 `HICON` |
| `c050dda` | 运行时跳过、规则编辑器拒绝“目标目录等于桌面源目录” |
| `9996673` | 拖动和缩放统一从 `DesktopGroupZoneStore` 读取锁定状态 |
| `5940164` | 缩放前验证完整候选布局，并在应用失败时回滚元数据和窗口/原生坐标 |

## 1. Shell 图标句柄

`Utility.GetShellIcon` 现在把 `SHFILEINFO.hIcon` 当作 USER 图标句柄处理，并在 `finally` 中调用 `DestroyIcon`。因此位图转换或 `Freeze` 抛出异常时也会释放句柄。没有调整图标缓存策略。

隔离程序通过反射调用实际 Release DLL 中的 `GetShellIcon`，使用同一路径重复提取图标，并读取当前进程的 GDI/USER 对象计数：

| 构建 | 调用 | USER | GDI |
| --- | ---: | ---: | ---: |
| `3d450fb` 基线 | 100 次 | `+100` | `+300` |
| 修复版（预热 100 次后） | 继续 1,000 次 | `3 → 3` | `40 → 40` |
| 修复版（累计 2,000 次） | 继续检查 | `3 → 3` | `40 → 40` |

返回的 32×32 位图已验证为冻结且可读取。另对空路径和不存在路径各调用 500 次，句柄数没有增长。全项目扫描没有发现其他把 `HICON` 交给 `DeleteObject` 的位置；保留的 `DeleteObject` 调用对应 `HBITMAP` 或 `HRGN`。

## 2. 分组锁定状态

`DesktopGroupDragService` 和 `DesktopGroupZoneService` 不再读取可能过期的 `_plan.Groups[].Locked`，而是读取 UI 切换锁定时立即更新的 `DesktopGroupZoneStore`。这使持久化状态、标题/边框窗口状态和拖动/缩放判定共享同一个锁定来源。

隔离宿主使用最终 Release DLL 验证了两种故意制造的不一致状态：

- plan 为锁定、store 为解锁：拖动不再返回 `GROUP_LOCKED`；缩放也进入正常验证路径。
- plan 为解锁、store 为锁定：拖动和缩放都返回 `GROUP_LOCKED`。

锁定、缩放及回滚联合宿主共 47 项断言全部通过，没有访问或移动真实桌面图标。

## 3. 自动整理同目录守卫

运行时在任何目录创建、文件稳定性检查或移动操作之前，先规范化源目录和目标目录。比较覆盖绝对化、相对路径、大小写差异和尾部分隔符；同目录直接记录警告并返回。规则保存界面也拒绝此目标，并显示明确的本地化提示。

临时目录隔离验证对 9 种等价路径形式、3 轮共 27 次调用实际 `ExecuteMoveAsync`：

- 文件名、内容、哈希、修改时间和目录内容均未变化；
- 没有生成重命名文件，规则 `LastRun` 没有更新；
- 27 次均记录同目录跳过日志；
- watcher 未启动，没有访问真实用户桌面或配置目录。

该宿主总计 158/158 项断言通过。`DeleteNever`、`OverwriteNever`、冲突 `Skip`、临时文件过滤、稳定性与锁定检查、debounce 和 kill switch 的原有分支未放宽。

## 4. 分组缩放预验证与回滚

缩放服务现在先把候选尺寸放入完整分组集合，再调用现有 Dual-Rail 规划逻辑和既有 dry-run 安全门。只有候选布局有效时才保存尺寸。保存后若刷新或应用失败，则恢复尺寸锚点文件的原始字节/不存在状态、计划文件、预览窗口几何；只有原生坐标确实发生变化时才恢复坐标。错误提示包含失败原因，并建议缩小尺寸或恢复自动大小。

报告中的 `SafeRows=14` 场景已用最终 Release DLL 重现：把 `SYSTEM_TEMP` 从 `2×1` 改为 `2×13` 时，完整规划返回 `DUAL_RAIL_TOO_TALL Rail=Left; SafeRows=14; Required=22`。修复版在 `SaveSize` 前拒绝，尺寸锚点和计划字节均保持不变；随后以原尺寸刷新成功。

联合宿主还验证了：

- 合法缩放仍可提交；
- 五个既有 dry-run 安全门仍被执行；
- 应用失败后可逐字节恢复带 BOM/未知字段的锚点文件；
- 原先不存在的锚点文件回滚后仍不存在；
- `SaveSize` 自身失败时不会错误调用布局恢复；
- 窗口/原生坐标比较可容忍列表重排，但能识别单点变化和同标签坐标互换。

结果为 47 PASS、0 FAIL。验证没有调用 Explorer 应用或恢复接口。

## 构建与回归

基线和修复版均使用 VS2022 BuildTools MSBuild、Release/Any CPU、`-restore` 构建，没有覆盖输出路径：

```text
msbuild "Code\Desktop Frames.sln" -p:Configuration=Release -p:Platform="Any CPU" -restore
```

| 指标 | 基线 | 修复版 |
| --- | ---: | ---: |
| 构建退出码 | 0 | 0 |
| 警告 | 2124 | 2124 |
| 错误 | 0 | 0 |
| 新依赖 | — | 0 |

修复版主 DLL SHA-256：`7CB4D4C2EFEB71CCDF8774A9E6A24DD39EC5581B84ED8733BC06A74903D4FF84`。

项目自带的三组安全回归均使用隔离目录中的最终 Release 产物，退出码全部为 0：

- Dual-Rail 只读预览：`StableIconCount=86`、`CentralClearZoneViolations=0`、`DuplicateTargetCoordinates=0`、`VisualQualityGate=PASS`。
- SpotSearch：`UserDesktopIndexed=true`、`PublicDesktopIndexed=true`、`BackgroundScanWhenClosed=false`；Steam、Codex、论文、PDF、Chrome 均命中。Steam 命中公共桌面的 `Steam.lnk`。
- 身份分类：`DeterministicHarness=PASS`、`SteamUrlClassifiedAsGames=true`、`SameNameFolderDoesNotInheritSteamClassification=true`、`ManualOverrideStillHighestPriority=true`、`CountMismatchFailsClosed=true`、`TestArtifactsDeleted=true`。
- 实际预览分类保持：`The Seance of Blake Manor` 文件夹为 `UNASSIGNED`，同名 Steam `.url` 为 `GAMES`。

没有运行 `--native-layout-dualrail-apply`、游戏分组拖动或任何 restore 命令。

## 部署与文件系统检查

部署前停止了原 `Desktop Frames` 进程，只覆盖 21 个 Release 构建产物，然后从实际 Release 目录重新启动。部署 DLL 哈希与已验证产物完全一致。`ProfileOptions.json`、`Profiles\`、`Desktop_Frames.log` 和 `audit\` 的部署前后哈希/清单差异为 0。部署后只读预览退出码为 0，`StableIconCount` 仍为 86。

Explorer 在本轮前后保持同一 PID 和启动时间，进程可响应；没有观察到崩溃、重启或桌面图标丢失。停止进程、复制 21 个产物并重启程序这一部署操作的即时前后快照差异为 0。

会话最初快照到收尾检查之间另观察到三项并发差异：`HUNNU-Research-Harness.zip` 被移除，`HarnessOutputOwnershipAudit_20260830` 目录被创建，已有 `HUNNU-Research-Harness` 目录的修改时间不再等于初始值，并且在收尾复核期间仍由外部活动继续变化。本轮命令、测试和部署均未引用这些路径，也没有尝试恢复、删除或改写它们。由于无法替用户判定这些外部变化是否预期，本轮整体文件系统验收保守标记为待确认；本轮修复和部署操作自身的意外移动、删除、重命名和复制均为 0。
