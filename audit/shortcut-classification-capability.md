# Shortcut Classification Capability Audit

审计日期：2026-08-26
目标：判断未来是否能把快捷方式和文件夹进行“视觉 Frame 分组”而不物理移动对象。

## 1. 当前解析能力

| 能力 | 结论 | 实现 |
|---|---|---|
| `ShortcutParsingExisting` | true | `IWshRuntimeLibrary.WshShell/IWshShortcut`；并有二进制 fallback |
| `ShortcutTargetAvailable` | true/partial | `Utility.GetShortcutTarget` 与 `FilePathUtilities.GetShortcutTargetUnicodeSafe`；死链、网络路径或异常 shortcut 可能失败 |
| `ShortcutArgumentsAvailable` | true/partial | `Utility.GetShortcutArguments` 通过 COM 读取；当前没有统一持久化成分类索引字段 |
| `ShortcutIconAvailable` | true | `Utility.GetShellIcon`、`IconManager`、shortcut icon location/Shell icon |
| `SteamShortcutRecognized` | partial | 代码识别 `steam://` 等路径/URL 并使用 Steam icon；没有通用 Steam AppID/游戏库分类模型 |
| `StoreAppRecognized` | partial | `Utility.IsStoreAppShortcut` 检查 `.lnk` 的 `APPS` 签名，并在 launch 侧做 Store/AppFolder 处理 |
| `URLShortcutRecognized` | true/partial | `.url` 的 `URL=`、`IconFile`/`IconIndex` 可解析；没有丰富网页 metadata 模型 |
| `AppUserModelID` | not found | 源码未发现 AppUserModelID 解析/存储逻辑 |
| `DisplayName` | true | Frame item/shortcut display name 已被读取并用于显示 |
| `TargetPath` classifier field | 可提取，但未用于当前 SpotSearch/Frame 自动分类 | 解析能力存在，策略层不存在 |
| `Arguments` classifier field | 可提取，但未用于当前 SpotSearch/Frame 自动分类 | 同上 |

## 2. 当前文件导入行为

现有外部拖入 Frame 路径不是纯“记录原路径”：

```text
Drop .lnk/.url
  -> copy shortcut into profile Shortcuts (usually)
  -> save copied filename in frame Items

Drop ordinary file/folder
  -> may create profile .lnk pointing to original
  -> save profile shortcut metadata in frame Items

Optional setting DeleteOriginalShortcutsOnDrop
  -> may delete original Desktop shortcut when enabled
```

所以：

```text
CanVisuallyAssignShortcutWithoutMove=当前没有可直接复用的纯 reference-only assignment path
```

当前实现“不物理移动原始目标文件”不等于“不产生任何文件系统副作用”；复制 Profile shortcut 和可选删除原 `.lnk` 必须被视为不同的导入模式。

## 3. 推荐的未来 reference 模型（只设计，不实现）

为视觉分组新增独立 item 类型，例如：

```json
{
  "ItemKind": "Reference",
  "FrameId": "AI_DEV",
  "DisplayName": "Codex",
  "OriginalPath": "C:\\Users\\...\\Desktop\\Codex.lnk",
  "Uri": null,
  "TargetPathSnapshot": "C:\\...\\Codex.exe",
  "ArgumentsSnapshot": "",
  "IsFolder": false,
  "IsLink": true,
  "DisplayOrder": 0
}
```

安全约束：

- assign/unassign 只修改 `frames.json` membership，不调用 `File.Move`、`File.Delete`、shortcut copy 或 recycle。
- 删除 Frame membership 只删除 JSON 项，不删除原 `.lnk`、原文件或目录。
- target/arguments/icon 是可刷新快照，不是把快捷方式复制到 Profile 的替代品。
- 原有“导入/复制 shortcut”保持兼容，明确与“引用到 Frame”分为两个用户动作。
- 一条引用初始只属于一个 Frame；以后如需多 Frame 视图再单独定义，不隐式复制文件。

## 4. 分类器可用输入

未来视觉自动分组可以使用：

```text
DisplayName
Shortcut extension (.lnk/.url)
TargetPath
Arguments
Steam URI / URL
Store Apps signature
Icon / AppUserModelID (待补能力)
```

第一版应使用明确白名单/用户确认的规则，不按模糊关键词自动搬动或重写任何 Desktop 对象。对不能可靠判断的快捷方式保持 unframed/原位。
