# UI Phase 5 grouping apply plan

```text
ReferenceAutoGroupingEnabled=false
ApplyPerformed=false
Reason=Per-icon Desktop visual ownership is unavailable; applying references would create duplicate visuals.
FilesystemActions=NONE
```

## Proposed high-confidence assignments

| Proposed frame | Items | Action |
|---|---|---|
| AI · 开发 | 查看WSL状态; 电池模式-关闭wsl; AI工具学习; Claude - Pro2; Claude Profile Switcher; Claude-Code-Desktop-Switcher-GUI; Codex++; Codex++ 管理工具; Kimi_K3_9页视觉小样素材包_V1; Kimi_K3_PPT研究资料交接包_V1; Visual Studio Code; ZCode | ASSIGN_REFERENCE_BLOCKED_VISUAL_OWNERSHIP |
| 科研 · 学习 | 科研模式-启动wsl; 科研相关Skills-KimiWork-20260821; 论文 - 快捷方式; 论文下载; Graduate_Research - 快捷方式; HUNNU-Research-Harness; HUNNU-Research-Harness-Output | ASSIGN_REFERENCE_BLOCKED_VISUAL_OWNERSHIP |
| 工作工具 | Language Reactor; StataMP 18 (64-bit); zhong - Chrome | ASSIGN_REFERENCE_BLOCKED_VISUAL_OWNERSHIP |
| 游戏 | 九日; 双点博物馆; 死亡空间; 太吾绘卷：天幕心帷; 勇气默示录 FLYING FAIRY HD Remaster; Firestone; Firestone2Green 启动 Firestone; Hades II; Hearthstone Deck Tracker; SteamTools; The Seance of Blake Manor (`.url`) | ASSIGN_REFERENCE_BLOCKED_VISUAL_OWNERSHIP |
| 日常工具 | 百度网盘; 夸克网盘; 西部世界VPN; MSI Afterburner | ASSIGN_REFERENCE_BLOCKED_VISUAL_OWNERSHIP |

## Conservative skips

| Confidence | Items | Action |
|---|---|---|
| MEDIUM | 炉石MOD; BepInEx - 快捷方式 | SKIP_LOW_CONFIDENCE |
| UNASSIGNED | .ssh - 快捷方式; 0731bdd8-dc69-458b-b01b-da03e90d9617; 16_90页正式版V1全量质检; 17_老师旧PPT_vs_90页V1信息密度对比; 687c8c5c-06d5-4a44-88c5-36fd278e8c96; 安装包; 健身环大冒险; 日常; 数据-AI漂洗; 数据表格; 太吾公式; 图片; 文档; 压缩包; Cheat Engine; DesktopHub; Logs - 快捷方式; NINE SOLS ARTBOOK_Digital; PDF; ppt; Run-ClashVerge-TunRescue; Temp; THE MERMAID MASK; The Seance of Blake Manor folder | SKIP_UNASSIGNED |

Manual overrides, when present, have higher priority than every classifier result and use `SKIP_MANUAL` during automatic preview/application.
