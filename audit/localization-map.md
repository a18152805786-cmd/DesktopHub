# Desktop Hub Localization Map

审计日期：2026-08-26
本文件记录现状和推荐方案，不执行汉化、不修改生产 UI。

## 1. 当前机制

当前不是 XAML `ResourceDictionary` 本地化，而是 `.resx` + `ResourceManager`：

- `Localization/Strings.cs` 创建 `ResourceManager("Desktop_Frames.Localization.Strings", ...)`。
- `Strings.Get(key)` 先查可选的 `<exe>\\Languages\\<culture>.resx`，再查编译进程序集的资源，最后回退到 key。
- `Strings.SetLanguage(...)` 设置 `OverrideCulture`；空值跟随 `CultureInfo.CurrentUICulture`。
- `OptionsFormManager` 已有语言选择、Languages 文件夹和导入 `.resx` 语言包逻辑。
- 语言变更当前通过 `OfferRestartAfterLanguageChange()` 提示重启；因为大部分控件在窗口创建时直接取字符串，当前重启策略是合理的保守实现。

现有资源文件：

```text
Strings.resx
Strings.de.resx
Strings.es.resx
Strings.fr.resx
Strings.it.resx
Strings.pl.resx
Strings.pt.resx
Strings.ru.resx
Strings.zh-Hans.resx
```

已核对资源数量：

```text
Base Strings.resx entries=591
Strings.zh-Hans.resx entries=596
Base keys present in zh-Hans=591
zh-Hans-only keys=5
```

这说明中文资源基础已经相当完整；主要问题是部分关键 UI 文字直接写在 C# 中，或者产品名仍然嵌入英文文案。

## 2. 数量估计

统计方法是 XML `.resx` entry 计数，加上 C# 中直接赋给 `Text/Content/Header/Title/ToolTip` 的明显字符串。它不是语义去重后的完整 UI 文案数据库，因此标记为近似值。

```text
TotalVisibleStringsApprox≈665
XamlHardcoded=0 detected in the single App.xaml
CSharpHardcoded≈74 direct assignment matches (undercount)
AlreadyResourceBased=591 base resource entries / 529 typed accessor properties
```

`XamlHardcoded=0` 不表示 UI 没有硬编码，而是当前 XAML 几乎为空；硬编码主要在 C# 动态构建的窗口里。

## 3. 现有用户可见字符串分布

| 来源 | 当前情况 | 代表文件/例子 |
|---|---|---|
| `.resx` / `Strings.*` | 菜单、选项、很多通知和帮助文案已经资源化 | `Localization/Strings.cs`、`Strings.resx`、`Strings.zh-Hans.resx` |
| C# 动态控件 | 仍有明显硬编码 | `FrameManager.cs`、`SearchFormManager.cs`、`OptionsFormManager.cs` |
| default Frame | `New Frame - Drop your shortcuts here` | `FrameManager.cs` |
| Startup Tips | `Desktop Frames + Startup Tips` 及正文 | `FrameManager.cs` |
| Search popup | `_appTitle.Text = "Desktop Frames +"` | `SearchFormManager.cs` |
| Frame controls | lock/unlock tooltip、绘制提示、部分输入对话框 | `FrameManager.cs`、`MessageBoxesManager.cs` |
| Tray/About/通知 | 资源与产品名硬编码混用 | `TrayManager.cs`、`AboutFormManager.cs`、`NotificationFormManager.cs` |

## 4. 要求词条的当前状态

```text
New Frame                 = 部分硬编码/部分资源路径，需统一
Drop your shortcuts here  = FrameManager.cs 硬编码
Desktop Frames + Startup Tips = FrameManager.cs 硬编码
Search                    = 资源已有；搜索 popup 标题仍有硬编码
Options                   = 资源已有
Customize                 = 资源已有/部分上下文仍需核对
Context Menu              = 大部分资源化
Profile                   = 资源已有
Auto Organize             = 资源已有
Frame                     = 资源与代码混用
Portal                    = 资源与代码混用
Startup                   = 设置项已有；产品名/提示仍混用
Update                    = 资源/RemoteInfo 文案混用
Backup / Restore          = 资源已有
Rename / Delete / Cut / Paste / Close / Hide / Show = 多数已有资源，个别控件/tooltip仍可能硬编码
```

## 5. 推荐机制

```text
RecommendedLocalizationMechanism=.resx + existing Strings.cs ResourceManager
Why=项目已经有成熟的资源访问、语言包目录、语言选择和中文 zh-Hans 资源；并非 XAML-heavy UI
MigrationComplexity=LOW to MEDIUM
FilesAffectedApprox=现阶段先处理 Strings.cs/资源文件 + 逐步迁移硬编码的 6-12 个 UI 核心文件
RuntimeLanguageSwitchPossible=技术上可行，但当前控件需重建；V1 保持重启生效
RestartRequired=当前实现 YES；不建议第一阶段改成全局动态刷新
```

不建议新建平行的 `Resources/Strings.zh-CN.xaml`。它会与现有 `.resx`、语言包目录和 `Strings.Get()` 形成第二套本地化系统。推荐的 V1 路线是：

1. 保留 `Strings.cs` 作为唯一访问门面。
2. 把硬编码可见文本迁移为 key。
3. 继续保留 `Strings.resx` 作为英文基础回退。
4. 将 `zh-Hans` 作为当前已有翻译源；未来对外显示 `zh-CN` 时，优先在语言规范化层将 `zh-CN` 映射到 `zh-Hans`，或者提供同键的 `Strings.zh-CN.resx` 语言包，不要两套 key。
5. 语言切换第一版继续要求重启，避免遗漏已经创建的动态窗口。

## 6. 迁移顺序

优先迁移最影响产品感知且冲突范围较小的文案：

1. Search popup 标题/placeholder/result labels。
2. Startup Tips 标题、正文、关闭/不再显示。
3. 新建 Frame 默认标题和拖入提示。
4. Frame lock/filter tooltip 与绘制提示。
5. Options/Tray 中仍然存在的直接字符串。
6. About、更新和品牌文案最后单独处理，避免与产品 rename 混在一个 commit。
