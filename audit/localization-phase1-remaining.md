# Phase 1 Remaining Localization Audit

审计日期：2026-08-26
范围：Phase 1 修改后的源码、`.resx` 资源和高可见 UI 调用点。

本清单不是要求本阶段把英文归零，而是记录剩余工作边界。低频文案和产品身份文案保留在后续独立 commit 处理。

## 区域状态

```text
Frame=高频新增硬编码已迁移；低频自定义菜单/帮助和产品身份仍可能保留英文
Search=标题和结果位置 tooltip 已迁移；当前不存在独立 No results/Searching 可见控件，因此没有虚构资源 key
StartupTips=新初始化标题和正文已迁移；既有 frames.json 中的历史 NoteContent 不自动改写
Tray=NotifyIcon tooltip、新建 Profile 对话框和刷新标题已迁移；About/更新身份文字仍保留
Settings=既有资源体系已覆盖大部分设置；部分低频动态控件和历史术语留待后续
AutoOrganize=新规则默认名已迁移；规则执行链和安全逻辑未修改
Portal=既有资源已统一部分“文件夹视图”术语；未改变 Portal 功能
Other=AboutFormManager.cs、MessageBoxesManager.cs 中的 Desktop Frames + 属于保留的产品身份；日志英文不属于用户可见 UI
```

## 仍可见或需人工确认的直接字符串

当前扫描中需要保留/后续单独判断的代表性内容：

- `AboutFormManager.cs` 和 `MessageBoxesManager.cs` 中的 `Desktop Frames +`：本阶段不做产品 rename。
- `MessageBoxesManager.CreateWaitWindow` 的默认 title：作为 legacy 产品身份 fallback 保留。
- 少量低频帮助、About、更新和开发者诊断文案：不属于本阶段的高频迁移范围。
- `Tag`、JSON 值、日志、内部枚举和快捷键代码中的英文：不是用户可见文案，不应机械迁移。

## 资源完整性

```text
EveryMigratedKeyExistsInEnglish=true
EveryMigratedKeyExistsInZhHans=true
MissingKeys=0
PlaceholderMismatches=0
```

新增资源 key 共 16 个：`AppDisplayName`、`FrameNewTitle`、`FrameNamePrompt`、`FrameCreateTitle`、`FrameLockedTooltip`、`FrameUnlockedTooltip`、`FrameDrawInstruction`、`FrameRefreshingConfiguration`、`FrameRefreshingFrames`、`StartupTipsTitle`、`StartupTipsContent`、`ProfileNamePrompt`、`ProfileCreateTitle`、`TrayIconTitle`、`AutoOrgNewRuleName`、`SearchResultTooltip`。

## 下一次扫描建议

后续迁移应按独立区域提交：Startup Tips 生命周期、Frame 视觉控件、SpotSearch 结果 UI、Settings 低频文案，以及最后单独的产品 rename。不要把内部 configuration key 或 Auto Organize 执行逻辑作为“英文清理”对象。
