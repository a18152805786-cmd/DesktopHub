# DesktopHub BUG 审查 — 2026-08-30

审查基准：`3d450fb`。审查开始时工作区无未提交业务修改。

发现 **12 项有明确触发路径的问题：3 项 P1、9 项 P2**。P1 表示优先修复的数据/配置风险；P2 表示特定操作或环境下的功能、稳定性问题。不意味着这些问题已在用户当前配置中全部发生。

本轮覆盖原生桌面布局、分组拖动/缩放、自动整理、配置切换、快捷方式复制和导入、Portal 文件夹窗口、搜索热键和图标资源。没有修改业务代码、切换真实配置、移动桌面图标或搬运用户文件，也没有停止正在运行的程序。

## 验证范围与结果

- 使用 Visual Studio 完整 MSBuild 独立构建成功；输出在 `audit/tmp_bugreview_20260830/bin/`，未覆盖运行中的 Release。构建日志：`audit/tmp_bugreview_20260830_build.log`。已有编译警告未当作 BUG 数量。
- 对图标句柄释放和托盘文本长度执行了隔离运行验证。
- 对布局使用仓库已有布局样例和本次构建 DLL，通过反射调用实际规划逻辑；没有调用真实桌面坐标读写方法。验证源码归档于 `audit/tmp_bugreview_20260830/layout-validation.cs`。
- 对导入重名、路径解析和同目录重命名执行了纯内存/路径验证。
- 其余问题以明确源码调用链确认，未声称完成 GUI 端到端复现。尤其没有修改系统 DPI、制造网络断线或在真实桌面运行重命名循环。

## 1. [P1] 配置切换后自动整理使用旧规则，并把旧规则写入新配置

定位：[ProfileManager.cs:448](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/ProfileManager.cs:448>)；相关 [AutoOrganizeManager.cs:45](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/AutoOrganizeManager.cs:45>)、[AutoOrganizeManager.cs:315](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/AutoOrganizeManager.cs:315>)。

**触发条件：** 配置 A、B 都启用自动整理、配置的目标目录不同。从 A 切换至 B，再向桌面加入匹配文件。

`SwitchToProfile` 更新当前配置、工作目录、FrameData 和 Settings，却没有停止自动整理任务、加载 B 的 `Rules` 或重新建立 watcher。`LoadRules` 仅在程序启动的初始化链中调用。因此整理仍按 A 的规则执行，文件会进入 A 的目标目录。成功后的 `SaveRules()` 使用动态计算的当前配置路径，又将 A 的内存规则写入 B 的 `auto_organize.json`。

**影响：** 文件进入错误目录，新配置的整理规则被覆盖。已经排队的任务也没有配置归属隔离。

**修复方向：** 切换前停止并取消旧任务；切换后先清空并加载新规则，再按新配置决定是否启动。无规则文件时也必须重置内存规则，并防止旧任务完成后写入新配置。

**证据：** 已核对启动、切换、队列、成功保存路径的完整调用链；未对真实配置执行搬运。

## 2. [P1] 自动整理目标为桌面本身时，会反复重命名同一个文件

定位：[AutoOrganizeManager.cs:286](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/AutoOrganizeManager.cs:286>)；相关 [AutoOrganizeManager.cs:128](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/AutoOrganizeManager.cs:128>)。

**触发条件：** 规则目标选择正在监听的桌面根目录，冲突策略选择 `Rename`，使用扩展名匹配规则。

`destPath` 与 `sourcePath` 相同，却被当作目标重名。程序把 `report.txt` 改为 `report (1).txt`；自身产生的 Renamed 事件将新路径再次入队，再变为 `report (1) (1).txt`。队列按完整路径去重，无法阻止不断变化的新路径。

**影响：** 真实文件不断改名，破坏外部路径引用，直到停用功能或名称过长等条件中断。使用 `Skip` 策略时不会触发这条循环。

**修复方向：** 规范化比较源目录和目标目录，同目录直接跳过；保存规则时阻止该无效目的地。

**证据：** 事件链确认及纯内存名称演算；未实际启动循环。

## 3. [P1] 文件夹暂时不可访问，会永久删除对应 Portal 窗口配置

定位：[FrameManager.cs:3443](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/FrameManager.cs:3443>)、[FrameManager.cs:3456](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/FrameManager.cs:3456>)；同类错误处理在 [FrameManager.cs:6243](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/FrameManager.cs:6243>)。

**触发条件：** Portal 指向移动硬盘、尚未连接的网络共享等，程序启动/刷新时该目录不可访问。

启动检查把 `Directory.Exists == false` 直接认定为必须删除，将该 Frame 从列表移除并保存。Portal 初始化时，任何异常也会进入删除并保存的分支。代码没有区分暂时离线、权限问题和用户明确删除文件夹。

**影响：** 目录恢复后窗口不会回来，原有位置、样式等配置需要从备份恢复或重新创建；这里丢失的是窗口配置，不是目录中的原始文件。

**修复方向：** 保留配置并显示不可用状态，允许重试；仅在用户明确删除窗口时移除持久化数据。

**证据：** 两条删除及保存路径均已从源码确认；没有断开用户磁盘或修改配置进行演示。

## 4. [P2] 分组缩放先保存无效尺寸，导致之后刷新持续失败

定位：[DesktopGroupZoneService.cs:74](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/NativeDesktop/DesktopGroupZoneService.cs:74>)；相关 [DesktopGroupHeaderManager.cs:221](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/NativeDesktop/DesktopGroupHeaderManager.cs:221>)。

Resize 只验证本组容量、当前位置加尺寸是否越过屏幕边界，没有验证同一侧所有组能否同时排下，却先执行 `SaveSize`。随后重规划失败，仅弹出错误，未回滚手工尺寸和预览边框。

**隔离复现：** 仓库布局样例 `SafeRows=14`，把 SYSTEM_TEMP 从 `2×1` 改为 `2×13`，通过 Resize 当前检查；实际规划器返回：

```text
Resize_2x13_passes_current_checks=True
Resize_next_refresh=DUAL_RAIL_TOO_TALL Rail=Left; SafeRows=14; Required=22
```

**影响：** 无效尺寸持久化后，每次刷新继续失败，直到恢复自动尺寸或修正配置。

**修复方向：** 对完整候选布局预验证后再保存；应用失败回滚元数据和窗口几何。

## 5. [P2] 高 DPI 下，分组标题、边框与原生图标错位

定位：[DesktopGroupHeaderWindow.cs:80](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/NativeDesktop/DesktopGroupHeaderWindow.cs:80>)；同类代码 [DesktopGroupZoneWindow.cs:194](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/NativeDesktop/DesktopGroupZoneWindow.cs:194>)。

**触发条件：** Windows 显示缩放不是 100%，例如 150%。

Explorer 返回的原生像素间距被规划器用于计算几何，之后原样赋给 WPF 的 `Left/Top/Width/Height`（DIP）。例如 150% 下，原生 `x=1500px` 被解释为 `1500 DIP`，预期会绘制到 `2250px`。鼠标位置却在读取时转换成 DIP，再除以未转换的像素间距，拖动格数也不一致。

**影响：** 标题和边框不再围住对应图标，位置、尺寸和拖动距离错误。

**修复方向：** 明确原生像素与 WPF DIP 的边界，统一转换位置、尺寸、鼠标增量；启动恢复旧布局时验证 DPI、网格和显示器参数。

**证据：** 已核对整条几何数据链及启动恢复路径，未发现原生组窗口的 DPI 补偿。上述数值为坐标换算分析，非修改本机 DPI 后的截图实测。

## 6. [P2] 复制粘贴把快捷方式写入全局目录，却按配置目录读取

定位：[CopyPasteManager.cs:247](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/CopyPasteManager.cs:247>)、[CopyPasteManager.cs:282](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/CopyPasteManager.cs:282>)。

实际复制目的地是 `exeDir\Shortcuts`，持久化的却是相对 `Shortcuts\文件名`。应用已把工作目录设为 `Profiles\当前配置`，所以显示、启动和备份解析出的路径与实际写入位置不同。

**影响：** 同配置粘贴可能误用原有同名链接，跨配置粘贴可能失效，备份/导出无法收录真实副本。

**修复方向：** 统一使用 `ProfileManager.GetProfileFilePath("Shortcuts")`，确保物理文件和持久化引用指向相同位置。

**证据：** 纯路径比较确认写入与解析位置不同，并已核对启动及配置切换时的工作目录设置。

## 7. [P2] 导入快捷方式时，重名处理会让不同项目指向同一个目标

定位：[BackupManager.cs:363](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/BackupManager.cs:363>)。

**触发条件：** 导出包含 `report.lnk` 和 `report (1).lnk`，目标目录已有 `report.lnk`，且前者先被枚举导入。

第一个文件被改名为 `report (1).lnk`，对应引用随即改写。导入包内原来的 `report (1).lnk` 时，它又被改名为 `report (1) (1).lnk`。引用更新扫描已被前一轮改写的数据，于是两个项目最终都指向后者。

**影响：** 导入看似成功，但点击第一个项目也会启动第二个目标，原映射丢失。主列表和 Tab 列表均受影响。

**修复方向：** 先建立原始文件名到最终文件名的映射，再基于未修改的原始引用统一更新。

**证据：** 纯内存复现得到两个 `Filename` 都是 `Shortcuts\report (1) (1).lnk`。

## 8. [P2] 图标句柄释放方式错误，长期常驻会持续泄漏资源

定位：[Utility.cs:582](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/Utility.cs:582>)。

`SHGetFileInfo` 返回的 `hIcon` 调用了 `DeleteObject`，该调用不能释放图标句柄，必须使用 `DestroyIcon`。搜索和其他窗口反复提取图标都会走到这里；搜索关闭清理托管缓存也不会释放这些已丢失的原生句柄。

**隔离复现：** 用相同参数读取系统 Notepad 图标 100 次，`DeleteObject` 成功 0/100；进程 USER 资源 3→103，GDI 0→340。随后 `DestroyIcon` 成功 100/100，USER 回到 3，GDI 回到 40（剩余 Shell 缓存）。

**影响：** 长时间使用会持续消耗进程图形资源，最终可能出现图标缺失、窗口创建失败等问题。本轮没有故意运行到资源耗尽。

**修复方向：** 用 `DestroyIcon` 并放入 `finally`，同时覆盖图像转换抛异常的路径。

## 9. [P2] 设置界面允许的搜索组合热键无法触发

定位：[GlobalHotkeyManager.cs:266](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/GlobalHotkeyManager.cs:266>)；相关 [OptionsFormManager.cs:1190](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/OptionsFormManager.cs:1190>)。

**触发条件：** 用户正常设置 `Ctrl+Alt+F1`、`Win+F1`，或取消所有修饰键。

编辑器支持 Ctrl/Alt/Shift/Win 多选，组合保存为 `Control, Alt` 等字符串；搜索钩子却仅接受完整字符串等于 `control`、`alt`、`shift`、`none`。组合、Win 和编辑器生成的空字符串都无法进入成功分支。

**影响：** 搜索快捷键永久不响应，重启不能解决。默认的单 Ctrl 组合不受这项问题影响。

**修复方向：** 复用项目已有的统一组合键解析逻辑，并统一无修饰键的表示。

**证据：** 已核对编辑器、保存和钩子解析的分支；未注入真实按键。

## 10. [P2] 解锁分组后，拖动和缩放仍被拒绝

定位：[DesktopGroupHeaderManager.cs:298](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/NativeDesktop/DesktopGroupHeaderManager.cs:298>)。

**触发条件：** 分组锁定后刷新或重启，再通过右键菜单解锁。

菜单同步了持久化数据、HeaderWindow 和 ZoneWindow，却未更新 `_plan.Groups[].Locked`。UI 已允许操作，但拖动和缩放服务仍读取旧的 `true`，返回 `GROUP_LOCKED`。

**影响：** 必须额外刷新一次，解锁才真正生效。

**修复方向：** 切换锁定状态时同步当前 plan，或确保所有消费者读取同一个状态来源。

**证据：** 已核对菜单写入与两个执行服务的读取位置。

## 11. [P2] Portal 导航进入子文件夹后，拖入文件仍复制到原始根目录

定位：[FrameManager.cs:6584](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/FrameManager.cs:6584>)；相关 [FrameManager.cs:537](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/FrameManager.cs:537>)。

**触发条件：** Portal 原始目录为 A，用户在窗口中导航到 A\B，然后从外部拖入文件。

导航保存到 `_portalNavigationStates`，实际列表的 watcher 也切换到 A\B；Drop 分支却只读取 `frame.Path`，即原始的 A，从未读取当前导航目录。

**影响：** 用户以为复制进当前显示的 B，实际文件落在 A，且当前列表可能看不到刚复制的文件。

**修复方向：** Drop 使用 Portal manager 的当前目录或一致的导航状态；在执行前重新确认该目录有效。

**证据：** 已核对导航状态、列表 watcher 与 Drop 目的地的完整数据流；未执行真实复制。

## 12. [P2] 超长配置名会导致切换异常，并可能令下次启动直接退出

定位：[TrayManager.cs:239](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/TrayManager.cs:239>)、[TrayManager.cs:793](</C:/Users/71966/Desktop/DesktopHub/Code/Desktop Frames/TrayManager.cs:793>)。

**触发条件：** 创建合法但很长的配置名并切换，格式化后的托盘提示超过 127 字符。

创建配置只检查文件名非法字符，没有检查托盘提示长度。完整名称直接写入 `NotifyIcon.Text`；活动配置在托盘更新前已被保存。下次启动再次构建相同提示，异常进入 `App.OnStartup` 的外层捕获并调用 `Shutdown()`。

**影响：** 低频但会造成持续启动失败，需要外部修正活动配置或修复代码。

**修复方向：** 截断托盘显示文本，不必截断真实配置名；让托盘提示错误不能中断整个程序启动。

**证据：** 隔离验证 `NotifyIcon.Text` 127 字符可接受、128 字符抛 `ArgumentOutOfRangeException`；持久化顺序与启动退出路径由源码确认。

## 暂不计入确认数量的风险

`NativeDesktopLayoutApplicationService.BuildStagingPositions` 对仓库布局样例产生 83 个暂存点，其中 39 个超出项目自身安全网格，第一个为 `(2550,1400)`。但本轮未实际验证 Explorer 如何处理这些坐标，不能据此断言它必然挤走其他图标或破坏桌面，故仅保留为后续隔离 GUI 验证项。

## 建议修复顺序

1. 先处理 1–3，避免错误搬运、连续改名和配置丢失。
2. 处理 4–8，使布局操作可恢复、适配高 DPI，并保证快捷方式和原生资源管理正确。
3. 处理 9–12，修复热键、解锁、导航拖放和低频启动失败。

本报告是本次覆盖范围内的发现，不能证明未审到的路径或 GUI 行为不存在其他 BUG。
