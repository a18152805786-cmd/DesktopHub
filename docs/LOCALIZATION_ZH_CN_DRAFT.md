# Desktop Hub 中文文案草案（zh-CN）

状态：设计草案，不是已应用的翻译资源。
原则：自然、简洁、像桌面工具；不做逐词机械直译。当前代码仍可能从 `.resx` 或 C# 硬编码读取英文。

## 1. 核心词汇建议

| English/current term | 建议中文 | 备注 |
|---|---|---|
| Desktop Hub | Desktop Hub | 产品名先保留英文，不在本轮正式 rename |
| Frame | 分组 | 对用户比“框架/画框”更直观；代码内部仍保留 Frame |
| New Frame | 新建分组 | 动作导向 |
| Drop your shortcuts here | 将快捷方式拖到这里 | 避免“放置”式生硬翻译 |
| Startup Tips | 使用提示 | 不让提示看起来像常驻工具 |
| Desktop Frames + Startup Tips | Desktop Hub 使用提示 | 仅作为未来文案映射，不改当前生产 UI |
| Search | 搜索 | 统一名词 |
| Search desktop, files, apps… | 搜索桌面、文件和应用… | 搜索框 placeholder |
| Options | 设置 | Windows 用户熟悉 |
| Customize | 个性化 | Frame/外观语境 |
| Profile | 配置 | 若指 Profile 切换，可用“配置方案” |
| Auto Organize | 自动整理 | 与当前产品功能一致 |
| Portal | 文件夹视图 | 避免用户误解为网络 portal |
| Startup | 开机启动 | 设置项使用；普通 start 动作则用“启动” |
| Update | 更新 | 统一 |
| Backup | 备份 | 统一 |
| Restore | 恢复 | 统一 |
| Rename | 重命名 | 统一 |
| Delete | 删除 | 危险动作保持明确 |
| Cut | 剪切 | 统一 |
| Paste | 粘贴 | 统一 |
| Close | 关闭 | 统一 |
| Hide | 隐藏 | Frame/窗口语境 |
| Show | 显示 | Frame/窗口语境 |
| Locked | 已锁定 | 状态文本 |
| Unlocked | 未锁定 | 状态文本 |
| Settings | 设置 | 与 Options 合并为一个用户概念 |
| Tray | 系统托盘 | 说明性文案可用；菜单中可省略 |
| Folder | 文件夹 | 不使用“目录”作为普通 UI 名词 |
| Shortcut | 快捷方式 | 不译为“捷径” |
| Unknown | 未分类 | 仅显示状态，不创建自动搬运规则 |

## 2. Frame 状态文案

```text
New Frame - Drop your shortcuts here
→ 新建分组 · 将快捷方式拖到这里

Frame is locked (click to unlock)
→ 分组已锁定（点击解锁）

Frame is unlocked (click to lock)
→ 分组未锁定（点击锁定）

Draw a box to create a Frame (Esc to cancel)
→ 拖出区域以创建分组（按 Esc 取消）
```

标题栏推荐显示短标题；帮助信息和操作说明放到 tooltip 或首次提示中，避免常驻桌面占空间。

## 3. Startup Tips 文案草案

```text
Desktop Hub 使用提示

分组可以帮助你整理桌面图标，而不会改变文件原来的位置。
普通文件可由“自动整理”按规则归类；快捷方式和文件夹建议使用分组进行视觉整理。

[知道了] [不再显示]
```

“不再显示”必须对应一个持久化设置/状态；在该状态落地前，不要把按钮做成只有视觉效果的假选项。

## 4. 搜索文案

```text
Search                         → 搜索
Search desktop, files, apps…  → 搜索桌面、文件和应用…
No results                    → 没有找到匹配项
Open                           → 打开
Location                      → 位置
In frame                      → 所在分组
Folder                        → 文件夹
File                          → 文件
Application                   → 应用
Shortcut                      → 快捷方式
```

## 5. 术语边界

- “自动整理”只指已验证的 Auto Organize 文件规则；不能被翻译成会暗示移动快捷方式/文件夹的“智能桌面”。
- “分组”表示 Frame 的视觉容器；不等于 filesystem directory。
- “文件夹视图”表示 Portal 的浏览方式；不暗示 Portal 会复制或移动目录。
- “配置”要区分 Profile（整套 Frame/settings）和单个 setting；必要时使用“配置方案”。
- 危险操作保持直白，不用“清理”替代“删除”。

## 6. 实施备注

当前最小稳定路线是继续使用现有 `.resx`/`Strings.cs`。未来可将 `zh-CN` 规范化为现有 `zh-Hans`，或新增同键 `Strings.zh-CN.resx`；不要另建一套 WPF XAML 字符串系统。英文 `Strings.resx` 保持 fallback，语言切换第一版继续重启生效。
