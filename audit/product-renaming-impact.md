# Product Rename Impact: Desktop Frames + -> Desktop Hub

审计日期：2026-08-26
结论：产品 rename 不是一次字符串替换；需要兼容旧配置、旧注册表、旧互斥体和 upstream 更新路径。本轮不执行 rename。

## 1. 影响面

| 领域 | 当前值/证据 | 风险 |
|---|---|---|
| Project/assembly | 项目文件名为 `Desktop Frames.csproj`；未发现显式 `<AssemblyName>`，因此输出为 `Desktop Frames.exe` | 改名会影响快捷方式、运行路径和用户预期 |
| Namespace | `RootNamespace=Desktop_Frames` | 大规模 namespace rename 高冲突，且会影响 resource base name |
| Window/tray text | 多处 `Desktop Frames +` 硬编码与 `.resx` 文案 | 可分阶段迁移，但不能与 UI 大改混 commit |
| Mutex | `Global\\DesktopFramesPlus_Mutex_UniqueId_v2` | 改动可能导致旧进程与新进程并存；需兼容策略 |
| HKCU registry base | `SOFTWARE\\Desktop_Frames_Plus\\...` | 改名会影响 settings、messaging、instance trigger、program management |
| Context menu | `HKCU\\Software\\Classes\\DesktopBackground\\Shell\\DesktopFrames` | 需要迁移旧 key，并防止重复菜单项 |
| Startup Run | `HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run` value `Desktop Frames +` | 需兼容旧值；不得在 rename 时意外开启启动 |
| Profile/config paths | exe 旁 `Profiles`、`Languages`、`MasterOptions.json`、`frames.json`、`options.json`、`Shortcuts` 等 | 路径迁移可能丢失 profile 或生成双份配置 |
| Update manifest | `RemoteInfoManager.MANIFEST_URL` 指向 upstream `raw.githubusercontent.com/.../DesktopFramesPlus/.../getversion.json` | rename 不应误改 upstream 更新源；需单独决定更新策略 |
| Release links | About/RemoteInfo 链接到 `github.com/limbo666/DesktopFramesPlus` | 项目品牌与上游归属需区分，不能凭名称替换 URL |
| Developer override | exe 旁 `developer_repo.txt` 可改变 RemoteInfo target | rename/发布需保留或明确禁用该调试路径 |
| Resource base name | `ResourceManager("Desktop_Frames.Localization.Strings", ...)` | 改 namespace/assembly 后必须保持旧资源读取兼容 |

## 2. 已存在的迁移提示

`RegistryHelper` 已包含从 `Desktop_Fences_Plus` 到 `Desktop_Frames_Plus` 的历史 rename migration，以及 startup/context-menu 清理和重建逻辑。这说明产品 rename 过去已经是一个带副作用的迁移问题，不能再添加第二个未经设计的 rename 分支。

## 3. 推荐策略（未来单独阶段）

```text
ProductRenameDifficulty=HIGH
```

必须单独设计：

1. 保留旧 assembly/resource base 或提供兼容读取。
2. 一次性确定旧/new mutex 互斥策略，避免双实例。
3. 对旧 HKCU keys、旧 Run value、旧 context-menu key 做显式只迁移一次。
4. 对 `Profiles`、`Languages`、`MasterOptions.json` 做原位兼容读取，避免盲目搬目录。
5. 明确 update manifest、GitHub URL、release channel 是否仍跟随 upstream。
6. 先导出/备份配置，再在测试账户验证；不与 Frame UI、搜索、汉化合并。

V1 UI 阶段建议只通过资源文案和窗口显示名称做“用户可见品牌草案”，不改变 exe、namespace、registry、mutex、配置目录或 updater。
