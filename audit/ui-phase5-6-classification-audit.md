# UI Phase 5.6 Desktop Classification Audit

Read from the live Desktop. Only HIGH-confidence, evidence-backed corrections were applied;
everything else is recorded and left for the user to decide through 整理待分类项目.

```text
DesktopItemsAudited=85
ClassificationFixesApplied=8
ReviewedAndDeliberatelyUnchanged=14
ReclassifiedByRuleChange=7
ReclassifiedByUser=1
WholeDesktopReclassified=false
NetworkAiClassifierUsed=false
Classifier=ReferenceGroupingClassifier + ManualLayoutGroupId override
```

## 1. Corrections applied

| Name | Was | Now | Confidence | Evidence |
|---|---|---|---|---|
| Clash Verge | AI · 开发 | 日常工具 | HIGH | Target is C:\Program Files\Clash Verge\clash-verge.exe, a proxy/VPN client. It was only in AI_DEV because "clash verge" had been listed as a developer term. |
| Run-ClashVerge-TunRescue.cmd - 快捷方式 | AI · 开发 | 日常工具 | HIGH | Same Clash Verge family; a network rescue script, not a development tool. |
| 不心动才有鬼 | 待整理 | 游戏 | HIGH | Target D:\CAGAMEGames\BXDCYGV1.2\DontBlush.exe lives in a game library directory. |
| THE MERMAID MASK | 待整理 | 游戏 | HIGH | Target E:\Games\The Mermaid Mask\The Mermaid Mask.exe lives in a game library directory. |
| Free Download Manager | 待整理 | 日常工具 | HIGH | Target Softdeluxe\Free Download Manager\fdm.exe; a general download utility, the same class as the cloud-drive and browser entries already in 日常工具. |
| Creative App | 待整理 | 日常工具 | HIGH | Target Creative\Creative App\Creative.App.exe; audio-hardware software, the same class as NVIDIA App and Logitech G HUB, which were already in 日常工具. |
| .ssh - 快捷方式 | 待整理 | AI · 开发 | HIGH | Target C:\Users\71966\.ssh; an SSH key directory is a developer artefact. |
| 太吾公式 | 待整理 | 游戏 | MANUAL | Assigned by the user through 整理待分类项目 and stored as ManualLayoutGroupId. |

### Rule changes behind them

1. `clash verge` / `run-clash` moved out of the AI_DEV strong terms and into DAILY_TOOLS, together with
   `clash`. A proxy client is a network tool.
2. `free download manager` and `creative app` added to the DAILY_TOOLS strong terms, matching the
   named-utility style the rule set already used for NVIDIA App, Logitech G HUB and Revo Uninstaller.
3. `.ssh` added to the AI_DEV strong terms.
4. New games evidence pass: if a **shortcut's target path** contains a directory segment ending in
   `games`, or the segment `steamapps`, the item is GAMES with HIGH confidence. Whole path segments are
   matched, not a bare substring, and only the parent directories are examined - never the file name.
   Folders have no target and are never matched this way.

## 2. Reviewed and deliberately left alone

| Name | Current group | Evidence | Confidence | Would arguably be | Why it was not changed |
|---|---|---|---|---|---|
| The Seance of Blake Manor (.url) | 待整理 | `steam://rungameid/1395520` | MEDIUM | 游戏 | The .url really is a Steam game, but the Desktop also holds a FOLDER with the identical display name. Explorer reports both icons with the same label, so neither can be safely identified and neither is ever moved. Fixing this needs per-item shell identity, which is out of scope for this phase. |
| The Seance of Blake Manor (folder) | 待整理 | `(folder, no target)` | LOW | 待整理 | Same display-name collision. The folder holds localisation-patch working files; its name alone is not evidence of a group. |
| Kimi_K3_9页视觉小样素材包_V1 | AI · 开发 | `(folder, no target)` | MEDIUM | 办公 · 文档 or 科研 · 学习 | Matched AI_DEV on the bare vendor word "kimi". It is a PPT asset pack, not a development tool, but the correct destination is a judgement call, so it was left for the user to set through 整理待分类项目. Removing folder matching for AI_DEV would misfile Claude-Code-Desktop-Switcher-GUI, so the rule was kept. |
| Cheat Engine | 待整理 | `C:\Program Files\Cheat Engine\Cheat Engine.exe` | MEDIUM | 游戏 (arguable) | A memory editor used both for game trainers and for general reverse engineering. Not obvious enough to move. |
| 迅游加速器 | 待整理 | `E:\xunyou\xunyou.exe` | MEDIUM | 游戏 or 日常工具 | A game network accelerator; sits on the boundary between the two groups. |
| 飞智空间站 4.0 | 待整理 | `E:\Flydigi Space Station\Flydigi Space Station.exe` | MEDIUM | 日常工具 (arguable) | Gamepad configuration software: a device utility used for games. |
| NINE SOLS ARTBOOK_Digital | 待整理 | `(folder, no target)` | MEDIUM | 游戏 (arguable) | An artbook for the game 九日. Folder-name evidence only. |
| 健身环大冒险 [版本1.2(1).0] | 待整理 | `(folder, no target)` | MEDIUM | 游戏 (arguable) | A console game title. Folder-name evidence only. |
| 炉石MOD | 游戏 | `(folder, no target)` | MEDIUM | 游戏 | Already correct, matched on the broad keyword "mod". Left as is. |
| BepInEx - 快捷方式 | 游戏 | `E:\Hearthstone\BepInEx` | MEDIUM | 游戏 | Already correct. A mod loader inside the Hearthstone install. |
| Firestone / Hearthstone Deck Tracker | 游戏 | `overwolf / HearthstoneDeckTracker` | HIGH | 游戏 | Already correct on the display name. No change. |
| Kimi_K3_PPT研究资料交接包_V1 | 科研 · 学习 | `(folder, no target)` | HIGH | 科研 · 学习 | Already correct: the research rules are evaluated before the AI-tool rules, so 研究资料 wins over "kimi". |
| DesktopHub | 待整理 | `(folder, no target)` | MEDIUM | AI · 开发 (arguable) | The user's own source repository. Folder-name evidence only. |
| 西部世界VPN | 日常工具 | `WestWorldVPN.exe` | HIGH | 日常工具 | Already correct on the "vpn" term. No change. |

## 3. Full classification of the live Desktop

| Name | Group | Type | TargetPath | Arguments | Uri | Confidence | Reason |
|---|---|---|---|---|---|---|---|
| .ssh - 快捷方式 | AI · 开发 | Shortcut | C:\Users\71966\.ssh | - | - | HIGH | DisplayName matched '.ssh'. |
| Claude - Pro2 | AI · 开发 | Shortcut | C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe | -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "C:\Users\71966\Desktop\Claude-Code-Desktop-Switcher-GUI\ClaudeSwitcher.ps1" -Launch "Pro2" | - | HIGH | DisplayName matched 'claude'. |
| Claude Profile Switcher | AI · 开发 | Shortcut | C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe | -NoProfile -ExecutionPolicy Bypass -File "C:\Users\71966\Desktop\Claude-Code-Desktop-Switcher-GUI\ClaudeSwitcher.ps1" | - | HIGH | DisplayName matched 'claude'. |
| Claude-Code-Desktop-Switcher-GUI | AI · 开发 | Folder | - | - | - | HIGH | FolderName matched 'claude'. |
| Codex++ | AI · 开发 | Shortcut | C:\Users\71966\AppData\Local\Programs\Codex++\codex-plus-plus.exe | - | - | HIGH | DisplayName matched 'codex'. |
| Codex++ 管理工具 | AI · 开发 | Shortcut | C:\Users\71966\AppData\Local\Programs\Codex++\codex-plus-plus-manager.exe | - | - | HIGH | DisplayName matched 'codex'. |
| Kimi_K3_9页视觉小样素材包_V1 | AI · 开发 | Folder | - | - | - | HIGH | FolderName matched 'kimi'. |
| Visual Studio Code | AI · 开发 | Shortcut | C:\Users\71966\AppData\Local\Programs\Microsoft VS Code\Code.exe | - | - | HIGH | DisplayName matched 'visual studio code'. |
| WorkBuddy | AI · 开发 | Shortcut | D:\ai helper\WorkBuddy\WorkBuddy.exe | - | - | HIGH | DisplayName matched 'workbuddy'. |
| ZCode | AI · 开发 | Shortcut | E:\z code\ZCode\ZCode.exe | - | - | HIGH | DisplayName matched 'zcode'. |
| 查看WSL状态 | AI · 开发 | Shortcut | C:\Windows\System32\cmd.exe | /k "wsl -l -v" | - | HIGH | DisplayName matched 'wsl'. |
| 电池模式-关闭wsl | AI · 开发 | Shortcut | C:\Windows\System32\wsl.exe | --shutdown | - | HIGH | DisplayName matched 'wsl'. |
| Clash Verge | 日常工具 | Shortcut | C:\Program Files\Clash Verge\clash-verge.exe | - | - | HIGH | DisplayName matched 'clash verge'. |
| Creative App | 日常工具 | Shortcut | C:\Program Files (x86)\Creative\Creative App\Creative.App.exe | - | - | HIGH | DisplayName matched 'creative app'. |
| Free Download Manager | 日常工具 | Shortcut | C:\Program Files\Softdeluxe\Free Download Manager\fdm.exe | - | - | HIGH | DisplayName matched 'free download manager'. |
| Google Chrome | 日常工具 | Shortcut | C:\Program Files\Google\Chrome\Application\chrome.exe | - | - | HIGH | DisplayName matched 'google chrome'. |
| Language Reactor | 日常工具 | Shortcut | C:\Program Files\Google\Chrome\Application\chrome_proxy.exe |  --profile-directory="Profile 1" --app-id=lipamhmjfhnakccinjcimdpjobfkbmkj | - | HIGH | TargetPath matched 'chrome'. |
| Logitech G HUB | 日常工具 | Shortcut | C:\Program Files\LGHUB\system_tray\lghub_system_tray.exe | - | - | HIGH | DisplayName matched 'logitech g hub'. |
| MSI Afterburner | 日常工具 | Shortcut | E:\MSI Afterburner\MSIAfterburner.exe | - | - | HIGH | DisplayName matched 'msi afterburner'. |
| Microsoft Edge | 日常工具 | Shortcut | C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe | - | - | HIGH | DisplayName matched 'microsoft edge'. |
| NVIDIA App | 日常工具 | Shortcut | C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA App.exe | - | - | HIGH | DisplayName matched 'nvidia app'. |
| Pixel Repair | 日常工具 | Shortcut | C:\Program Files\Rizonesoft\Pixel Repair\PixRepair.exe | - | - | HIGH | DisplayName matched 'pixel repair'. |
| Revo Uninstaller Pro | 日常工具 | Shortcut | E:\Revo Uninstaller Pro\RevoUninPro.exe | - | - | HIGH | DisplayName matched 'revo uninstaller'. |
| Run-ClashVerge-TunRescue.cmd - 快捷方式 | 日常工具 | Shortcut | C:\Users\71966\Documents\Codex\2026-07-08\documents-plugin-documents-openai-primary-runtime\outputs\Run-ClashVerge-TunRescue.cmd | - | - | HIGH | DisplayName matched 'clash'. |
| zhong - Chrome | 日常工具 | Shortcut | C:\Program Files\Google\Chrome\Application\chrome.exe | --profile-directory="Default" | - | HIGH | DisplayName matched 'chrome'. |
| 夸克网盘 | 日常工具 | Shortcut | C:\Users\71966\AppData\Local\Programs\QuarkCloudDrive\quark_cloud_drive.exe | --launch-from=desktopicon --brand-clouddrive | - | HIGH | DisplayName matched '夸克网盘'. |
| 微信 | 日常工具 | Shortcut | C:\Program Files\Tencent\Weixin\Weixin.exe | --scene=desktop | - | HIGH | DisplayName matched '微信'. |
| 百度网盘 | 日常工具 | Shortcut | E:\BaiduNetdisk\BaiduNetdisk.exe | - | - | HIGH | DisplayName matched '百度网盘'. |
| 西部世界VPN | 日常工具 | Shortcut | C:\Users\71966\Downloads\WestWorldVPN-For-Windows-1.07\WestWorldVPN.exe | - | - | HIGH | DisplayName matched 'vpn'. |
| Battle.net | 游戏 | Shortcut | E:\Battle.net\Battle.net Launcher.exe | - | - | HIGH | DisplayName matched 'battle.net'. |
| BepInEx - 快捷方式 | 游戏 | Shortcut | E:\Hearthstone\BepInEx | - | - | MEDIUM | Only a broad keyword matched 'bepinex'. |
| Firestone | 游戏 | Shortcut | E:\overwolf\OverwolfLauncher.exe | -launchapp lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob -from-desktop | - | HIGH | DisplayName matched 'firestone'. |
| Firestone2Green 启动 Firestone | 游戏 | Shortcut | C:\WINDOWS\system32\wscript.exe | "C:\Users\71966\AppData\Local\Firestone2Green\LaunchFirestone2Green.vbs" | - | HIGH | DisplayName matched 'firestone'. |
| Hades II | 游戏 | Url | D:\steam\steam\games\e8cd1233d374c647e8e3d7bbaa98a5d399e3f40b.ico | - | steam://rungameid/1145350 | HIGH | DisplayName matched 'hades'. |
| Hearthstone Deck Tracker | 游戏 | Shortcut | C:\Users\71966\AppData\Local\HearthstoneDeckTracker\HearthstoneDeckTracker.exe | - | - | HIGH | DisplayName matched 'hearthstone'. |
| Steam | 游戏 | Shortcut | D:\steam\Steam.exe | - | - | HIGH | DisplayName matched 'steam'. |
| SteamTools | 游戏 | Shortcut | E:\SteamTools\SteamTools.exe | - | - | HIGH | DisplayName matched 'steam'. |
| THE MERMAID MASK | 游戏 | Shortcut | E:\Games\The Mermaid Mask\The Mermaid Mask.exe | - | - | HIGH | TargetPath is inside a game library directory 'Games'. |
| 不心动才有鬼 | 游戏 | Shortcut | D:\CAGAMEGames\BXDCYGV1.2\DontBlush.exe | - | - | HIGH | TargetPath is inside a game library directory 'CAGAMEGames'. |
| 九日 | 游戏 | Url | D:\steam\steam\games\880883c0b681c02a9236ef29b7fac6bc2a5b91f7.ico | - | steam://rungameid/1809540 | HIGH | Uri matched 'steam://'. |
| 勇气默示录 FLYING FAIRY HD Remaster | 游戏 | Url | D:\steam\steam\games\e170a71efbc4276692a6eebcc91f208661b00c67.ico | - | steam://rungameid/2833580 | HIGH | Uri matched 'steam://'. |
| 双点博物馆 | 游戏 | Url | D:\steam\steam\games\a03d10b6ab59b5fae8e7f7f5330a25cdd5115af0.ico | - | steam://rungameid/2185060 | HIGH | Uri matched 'steam://'. |
| 太吾公式 | 游戏 | StablePath | - | - | - | HIGH | ManualLayoutGroupId is authoritative; automatic classification is skipped. |
| 太吾绘卷：天幕心帷 | 游戏 | Url | D:\steam\steam\games\6624ddb5d12a0d09e1087ba885e038c3e96eed0a.ico | - | steam://rungameid/838350 | HIGH | Uri matched 'steam://'. |
| 死亡空间 | 游戏 | Url | D:\steam\steam\games\b1c9ddb943b17683fb3f647c9cea5f8b97267ffc.ico | - | steam://rungameid/1693980 | HIGH | Uri matched 'steam://'. |
| 炉石MOD | 游戏 | Folder | - | - | - | MEDIUM | Only a broad keyword matched 'mod'. |
| 爐石戰記 | 游戏 | Shortcut | E:\Hearthstone\Hearthstone Beta Launcher.exe | - | - | HIGH | TargetPath matched 'hearthstone'. |
| LibreOffice 26.2 | 办公 · 文档 | Shortcut | C:\Program Files\LibreOffice\program\soffice.exe | - | - | HIGH | DisplayName matched 'libreoffice'. |
| PDF | 办公 · 文档 | Folder | - | - | - | HIGH | FolderName matched 'pdf'. |
| ppt | 办公 · 文档 | Folder | - | - | - | HIGH | FolderName matched 'ppt'. |
| 压缩包 | 办公 · 文档 | Folder | - | - | - | HIGH | FolderName matched '压缩包'. |
| 图片 | 办公 · 文档 | Folder | - | - | - | HIGH | FolderName matched '图片'. |
| 安装包 | 办公 · 文档 | Folder | - | - | - | HIGH | FolderName matched '安装包'. |
| 数据表格 | 办公 · 文档 | Folder | - | - | - | HIGH | FolderName matched '数据表格'. |
| 文档 | 办公 · 文档 | Folder | - | - | - | HIGH | FolderName matched '文档'. |
| AI工具学习 | 科研 · 学习 | Folder | - | - | - | HIGH | FolderName matched 'ai工具学习'. |
| Graduate_Research - 快捷方式 | 科研 · 学习 | Shortcut | C:\Users\71966\Documents\Graduate_Research | - | - | HIGH | DisplayName matched 'research'. |
| HUNNU-Research-Harness | 科研 · 学习 | Folder | - | - | - | HIGH | FolderName matched 'hunnu'. |
| HUNNU-Research-Harness-Output | 科研 · 学习 | Folder | - | - | - | HIGH | FolderName matched 'hunnu'. |
| Kimi_K3_PPT研究资料交接包_V1 | 科研 · 学习 | Folder | - | - | - | HIGH | FolderName matched '研究资料'. |
| Obsidian | 科研 · 学习 | Shortcut | C:\Program Files\Obsidian\Obsidian.exe | - | - | HIGH | DisplayName matched 'obsidian'. |
| StataMP 18 (64-bit) | 科研 · 学习 | Shortcut | E:\Stata\StataMP-64.exe | /UseRegistryStartin | - | HIGH | DisplayName matched 'stata'. |
| Zotero | 科研 · 学习 | Shortcut | C:\Program Files\Zotero\zotero.exe | - | - | HIGH | DisplayName matched 'zotero'. |
| 科研模式-启动wsl | 科研 · 学习 | Shortcut | C:\Windows\System32\wsl.exe | -d Ubuntu-26.04 | - | HIGH | DisplayName matched '科研'. |
| 科研相关Skills-KimiWork-20260821 | 科研 · 学习 | Folder | - | - | - | HIGH | FolderName matched '科研'. |
| 论文 - 快捷方式 | 科研 · 学习 | Shortcut | E:\AI helper\论文 | - | - | HIGH | DisplayName matched '论文'. |
| 论文下载 | 科研 · 学习 | Folder | - | - | - | HIGH | FolderName matched '论文'. |
| Logs - 快捷方式 | 系统 · 临时 | Shortcut | E:\Hearthstone\Logs | - | - | HIGH | DisplayName matched 'logs'. |
| Temp | 系统 · 临时 | Folder | - | - | - | HIGH | FolderName matched 'temp'. |
| 0731bdd8-dc69-458b-b01b-da03e90d9617 | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| 16_90页正式版V1全量质检 | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| 17_老师旧PPT_vs_90页V1信息密度对比 | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| 687c8c5c-06d5-4a44-88c5-36fd278e8c96 | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| Cheat Engine | 待整理 | Shortcut | C:\Program Files\Cheat Engine\Cheat Engine.exe | - | - | UNASSIGNED | No high-confidence rule matched. |
| DesktopHub | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| NINE SOLS ARTBOOK_Digital | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| The Seance of Blake Manor | 待整理 | AmbiguousDisplayName | - | - | - | AmbiguousDisplayName | Non-stable identity is an occupied obstacle and is never moved. |
| The Seance of Blake Manor | 待整理 | AmbiguousDisplayName | - | - | - | AmbiguousDisplayName | Non-stable identity is an occupied obstacle and is never moved. |
| 健身环大冒险 [版本1.2(1).0] | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| 回收站 | 待整理 | UnmanagedShellItem | - | - | - | UnmanagedShellItem | Non-stable identity is an occupied obstacle and is never moved. |
| 数据-AI漂洗 | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| 日常 | 待整理 | Folder | - | - | - | UNASSIGNED | No high-confidence rule matched. |
| 此电脑 | 待整理 | UnmanagedShellItem | - | - | - | UnmanagedShellItem | Non-stable identity is an occupied obstacle and is never moved. |
| 迅游加速器 | 待整理 | Shortcut | E:\xunyou\xunyou.exe | -e=1 | - | UNASSIGNED | No high-confidence rule matched. |
| 飞智空间站 4.0 | 待整理 | Shortcut | E:\Flydigi Space Station\Flydigi Space Station.exe | - | - | UNASSIGNED | No high-confidence rule matched. |
