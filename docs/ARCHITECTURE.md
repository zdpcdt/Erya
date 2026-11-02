# 尔雅架构地图

面向协作者与 AI 编码代理的架构参考。协作规则（Git 纪律、测试防线、i18n 三语同步等）在根目录 [AGENTS.md](../AGENTS.md)，本文只讲「代码长什么样、数据怎么流」。

## 总览与依赖图

```
Erya.Core            （net10.0 类库；唯一依赖 YamlDotNet —— 零 Avalonia 依赖，自带 CoreLoc i18n）
Erya.Controls        （net10.0 类库；Avalonia + Irihi.Avalonia.Shared —— 不依赖 Core）
      ▲   ▲
      │   └────────────── demo/Erya.Controls.Demo   （WinExe 演示台，假数据）
      │
  src/Erya           （WinExe 主应用 = 唯一组合点）
      │                 Avalonia.Desktop / AvaloniaEdit / ColorPicker、CommunityToolkit.Mvvm、
      │                 Semi.Avalonia + Ursa(+Themes.Semi)、Irihi.Lingua、Iconica、Irihi.Branding
      ▼
tests/Erya.Core.Tests ──► Erya.Core    （xunit v2；Core 经 InternalsVisibleTo 开放内部类型）
tests/Erya.UiTests    ──► Erya         （xunit.v3 + Avalonia.Headless；挂载真实 App）
```

设计意图：Core 与 Controls 互不认识，各自可以独立演进 / 复用；`src/Erya` 负责把它们组合起来。Core 保持 Avalonia-free 是为了测试轻、可移植，也因此它有自己的一套轻量本地化（`CoreLoc`），与应用层的 Lingua 并行（见「i18n 双层机制」）。

所有 JSON 反序列化（Core 与应用层）都走 `System.Text.Json` 源生成 context（Native AOT 兼容）。

---

## Erya.Core —— RIME 的一切磁盘事务

### `Configuration/`：base + patch 模型

RIME 的铁律：发行版 base 文件（`default.yaml` / `squirrel.yaml` / `weasel.yaml`）永不被写；用户的一切修改落在用户目录的 `<name>.custom.yaml` 的 `patch:` 里，且**补丁键整体替换、不是合并**。这一层的全部职责就是把这条铁律变成安全的读写 API。

| 类 | 职责 |
|---|---|
| `RimeConfigStore` | 对一个用户目录（+ 共享目录 fallback）上一个 base 文件族的公开门面。关键 API：`BasePath` / `CustomPath`、`LoadBase`、`LoadMerged`（base + patch 合并，解析 `__include:`）、`ReadMergedValue(baseFile, keyPath)`、`ReadPatch` / `ReadPatchValues`、`ListMappingKeys`、`WritePatch(baseFile, dict)`、`RemovePatchKeys`、`ReadCustomText` / `WriteCustomText`（高级源码模式，写前校验、留 `.bak`） |
| `CustomYamlPatcher`（internal） | 任意键的 `*.custom.yaml` 读写器。**读**：兼容 RIME 世界里所有三种 patch 形态——嵌套（`patch:→a:→b:`）、patch 内平铺（`patch:→a/b:`）、顶层平铺（`patch/a/b:`），外加 `key/+` 追加键。**写**：统一迁移为第二种形态。策略：优先行级手术（保留注释与文件原貌），失败才结构化重写（YamlDotNet）；写前 `.bak`，写后重新解析校验，不一致即回滚 |
| `DefaultCustomYamlEditor`（internal） | 只负责 `default.custom.yaml` 的 `patch/schema_list` 一件事（同样的保字节策略），被 `RimeSchemaCatalog.WriteSelectedSchemas` 使用 |
| `MergedConfig` | 把平铺补丁按 librime 语义应用到 `YamlMappingNode` 上：普通键整体替换、`key/+` 追加到序列、中间层映射自动创建；`TryGetByPath` 解析 `a/b/c` 路径 |
| `PatchYaml` | CLR 值 ⇄ YAML 文本的双向转换：`ToClrValue(YamlNode)`（带引号的标量保持 string，否则推断 bool/long/double/string/list/有序字典）与手写发射器（`RenderEntry` / `RenderBlock`，含引号规则） |
| `RimeYaml`（internal） | 只读 helper：`LoadRoot`（缺失 / 解析错误返回 null）、`TryGetChild`、`ReadSchemaList`、`MergeAppend` |
| `YamlLines`（internal） | 行级原语（`IsKeyLine` / `KeyTextOf` / `HasInlineValue` / `IndentOf` / 换行保持的 `SplitLines`/`Join`），被两个写入器共用。存在理由：YamlDotNet round-trip 会丢注释 |
| `RimeSchemaCatalog` | 跨用户目录 + 共享目录发现 `*.schema.yaml`（用户目录遮蔽发行版自带的同名方案），读取生效 `schema_list`（补丁覆盖 base，三种形态都认），产出 `RimeSchemaSnapshot`；写回走 `DefaultCustomYamlEditor` |

### `Distributions/`：一套代码，三个前端

`IRimeDistribution` 契约（成员）：`Id` / `DisplayName` / `Version` / `InstallDir` / `UserDir` / `UiConfigFile` / `CanDeploy` / `GetConfigStore()` / `DeployAsync()` / `GetSchemasAsync()` / `SetSelectedSchemasAsync()`。

`RimeDistributionBase` 提供共享实现：`SchemaSearchDirs`（发行版自带数据目录）、`GetConfigStore() => new RimeConfigStore(UserDir, SchemaSearchDirs)`、方案目录读写委托 `RimeSchemaCatalog`。`DistributionManager` 持有序 Locator，`LocateAll()` / `Locate()`（首个命中者胜出——Linux 双前端并存时用户可在设置里选择）。`ExternalTool` 负责 PATH 查找与非零退出码抛错。

| 前端 | 安装位置 | UserDir | UiConfigFile | 部署方式 | env 覆盖（测试用） |
|---|---|---|---|---|---|
| Weasel（Windows） | `Program Files\Rime\weasel-<ver>` | `%APPDATA%\Rime` | `weasel.yaml` | `WeaselDeployer.exe /deploy`（互斥体冲突退出码 1 时重试一次） | `ERYA_WEASEL_DIR` |
| Squirrel（macOS） | `/Library/Input Methods/Squirrel.app`（或 `~/Library/...`） | `~/Library/Rime` | `squirrel.yaml` | `Squirrel --reload` 只发通知，随后**轮询 `UserDir/build` 目录 mtime 最多 20 秒**确认真的重新部署了 | `ERYA_SQUIRREL_APP` |
| fcitx5-rime（Linux） | 包管理器 | `$XDG_DATA_HOME/fcitx5/rime` | `null` | `rime_deployer --build <userDir> <sharedDir>` | `ERYA_FCITX5_RIME_HOME` |
| ibus-rime（Linux） | 包管理器 | `$XDG_CONFIG_HOME/ibus/rime` | `null` | `ibus restart`，失败回退 `rime_deployer` | `ERYA_IBUS_RIME_HOME` |

`UiConfigFile == null`（Linux）→ 应用不显示外观三页（皮肤 / 排版 / 应用行为），这是有意设计。`LinuxPackageProbe` 经 dpkg/rpm/pacman 探测版本并 memoize。

`Distributions/Weasel/WeaselSetup.cs`（`IWeaselSetup`）：空态引导用的一键装前端——问 GitHub latest release API 要 `*-installer.exe` 资产，流式下载到临时目录（进度回调 + 512 MB 安全上限 + 安装包文件名消毒）；启动安装器与提权交给 OS shell，此类只负责取字节。`HttpMessageHandler` 与下载目录均可注入，测试走假 handler 零网络。

### `Settings/`：schema 驱动的设置页

- 三个嵌入式基础 schema：`settings.default.json`（`default.yaml`，三前端通用的引擎键）、`settings.squirrel.json`、`settings.weasel.json`；每个都有 `.en` / `.zh-Hans` / `.zh-Hant` 语言覆盖（只改文案）。
- 结构：`{ targetFile, sections[] { id, title, description?, group?, fields[] { id, label, type, keyPath, min/max/step?, choices?, choicesRef?, default?, keys?, itemLabels?, icon? } } }`。`group` ∈ `skin|layout|apps` 把 UI schema 的 section 分配到外观三子页（`SettingsEditorViewModel.SectionsFor(group)`）；`type` 必须属于 `SettingsSchema.KnownFieldTypes`（`toggle / stepper / slider / segmented / enum / text / font / hotkey / hotkeyList / hotkeyPairList / keyMapping / checkList / appOptions`）。
- `SettingsSchemas.LoadDefault()` / `LoadForUiConfig(uiConfigFile)` 反序列化 → `Validate` 快速失败 → `ApplyLanguageOverlay` 合并语言覆盖。
- **schema 即白名单**：每个 `keyPath` 都被 `tests/Erya.Core.Tests/SettingsSchemaTests.cs` 里逐键对照上游源码（librime / SquirrelTheme.swift / RimeWithWeasel.cpp）的硬编码集合校验——加键流程与红线见 AGENTS.md。

### `Packages/`：在线方案安装

`SchemaPackageCatalog`（嵌入式 `packages.catalog*.json` + 语言覆盖）列出可装方案（雾凇拼音等，GitHub codeload tarball 直下、无需 git）；`SchemaInstaller.InstallAsync` 走 HTTPS 下载 → 暂存 → 装前备份 → 拷贝入 UserDir，`UninstallAsync` 按安装清单精确删除该包文件。

### `Backup/`

`RimeBackup.Create(userDir, zip)` / `Restore(zip, userDir)`：排除 `build/`、跳过被锁文件、恢复前先做 `pre-restore-*.zip` 安全快照、拒绝 zip-slip 路径，产出 `RimeBackupReport`。

---

## 核心数据流：一个设置项从改控件到生效

### 启动组合

1. `src/Erya/App.axaml.cs`（组合根，仓库里唯一合法的 `OperatingSystem.IsXxx()` 出现地）按 OS 装配 Locator：Windows → `WeaselLocator`；Linux → `Fcitx5RimeLocator` + `IbusRimeLocator`；macOS → `SquirrelLocator`；然后 `new ShellViewModel(new DistributionManager(locators), AppPreferences.Load(), showAppearance: !IsLinux, supportsInAppSetup: IsWindows)`。
2. `ShellViewModel`（`src/Erya/ViewModels/ShellViewModel.cs`）构造 `MainViewModel`（`LocateAll()` 并按偏好选定 `Distribution`），`BuildPages()`（`ShellViewModel.cs:134`）装配页面 VM：方案管理、`SettingsEditorViewModel(dist, "default")`、共享的一个 `SettingsEditorViewModel(dist, "ui")`（被皮肤 / 排版 / 应用行为三页按 `group` 切片复用）、高级设置、备份恢复、关于。页面首次导航才加载；`DistributionChanged` → `RebuildPages()`。
3. `MainWindow.axaml` 用 `ContentControl.DataTemplates` 按 VM 类型路由到页面视图，导航是 Ursa `NavMenu`。

### 空态引导（首启，本机没有 RIME 时）

`MainViewModel.IsMissing` 时总览页给安装引导卡，三个动作：一键装小狼毫（`supportsInAppSetup`，组合根只在 Windows 开）、打开官方下载页（`AppShell.OpenUrl`）、重新检测。一键流程 = `WeaselSetup` 查最新版 → 带进度下载 → 交 OS shell 启动安装器（UAC 由安装器自理）→ 轮询 `DistributionManager.Locate()`（40 × 3s 窗口，安装向导慢或被取消则超时给提示）直到新装的 Weasel 出现。检测统一走 `ApplyAutoDetection()`：重跑全部 Locator，同 id 只刷新信息字段，异 id 才换 `Distribution`（整页重建、丢未保存编辑），且不写偏好——`RedetectCommand` 与安装后的自动点亮共用它，换前端无需重启。

### 读（merged 生效值）

4. `SettingsEditorViewModel.LoadAsync`（`src/Erya/ViewModels/Settings/SettingsEditorViewModel.cs:136`）：`_store = _distribution.GetConfigStore()`；加载 schema（`default` 用 `LoadDefault()`，`ui` 用 `LoadForUiConfig(dist.UiConfigFile)`）；每个 `SettingsField` 生成对应类型的字段 VM（`SettingsFields.cs`：`Toggle/Number/Text/Font/Hotkey/Choice/List/KeyMapping/CheckList/AppOptions`），`fieldVm.Load(_store.ReadMergedValue(targetFile, keyPath))`。
5. 读取链：`ReadMergedValue` → `LoadMerged` → `RimeYaml.LoadRoot(BasePath)` + `CustomYamlPatcher.ReadFlatPatch(CustomPath)` → 解析 `__include:` → `MergedConfig.Apply` → `TryGetByPath` → `PatchYaml.ToClrValue`。
6. `ui` 目标额外构建 `ColorSchemeLibraryViewModel`（`preset_color_schemes` 库）和两个 `CandidatePreviewViewModel`（亮 / 暗候选窗实时预览）。

### 改（脏值跟踪）

7. 字段 VM 的属性变更回调置 `HasPendingEdits = value != _loaded`；编辑器 `IsDirty`（`SettingsEditorViewModel.cs:120`）聚合全部字段 + 皮肤库。

### 存（只写脏键）与部署

8. `SaveAndDeployAsync`（`SettingsEditorViewModel.cs:220`）：收集 `writes[keyPath] = field.PendingValue()` 与 `removals`（改回默认 / 清空的键要从补丁里删掉）；皮肤库贡献逐叶子键（内置皮肤 `preset_color_schemes/<id>/<字段>`，用户新建的才写整个 `preset_color_schemes/<id>`）。
9. `_store.WritePatch(targetFile, writes)` → `CustomYamlPatcher.Write(CustomPath, ...)`：文件不存在则生成带本地化头的新补丁；存在则 `.bak` → 行级编辑（删除该键所有既有形态的行，在 `patch:` 下 indent 2 插入 `key/path: value`）→ 结构化重写兜底 → 重新解析校验、失败回滚。`RemovePatchKeys` 同理。**被触碰的文件只有 `<UserDir>/<target>.custom.yaml`（+ `.bak`）。**
10. `dist.CanDeploy` 时 `await DeployAsync()`（见前表各前端差异）；成功 toast、失败告警（此时补丁已在盘上，仅部署未完成）；`!CanDeploy` 给出对应发行版的部署器安装提示。最后 `LoadAsync()` 重新读合并值。

### 并行路径（殊途同归到 custom.yaml + DeployAsync）

- **高级源码页**：`AdvancedSettingsViewModel`（`ViewModels/Advanced/AdvancedSettingsViewModels.cs`）经 `ReadCustomText` → AvaloniaEdit 文档 → `WriteCustomText`（YAML 校验 + `.bak`）→ 部署。目标：`default.yaml` 恒有，`UiConfigFile` 存在时加一个。
- **方案管理**：`SchemaManagerViewModel` 拖动排序（`ReorderableItemsControl`）→ `SetSelectedSchemasAsync(ids)` → `RimeSchemaCatalog.WriteSelectedSchemas` → `default.custom.yaml` 的 `patch/schema_list` → 部署；在线安装 = `SchemaInstaller.InstallAsync` → 选中方 → 部署。
- **备份恢复**：`BackupRestoreViewModel` → 后台任务跑 `RimeBackup.Create/Restore` → 恢复后自动部署。
- **全局重新部署**：Ctrl+R / Cmd+R → `ShellViewModel.DeployCommand` → `MainViewModel.DeployAsync`。

---

## i18n 双层机制

两层独立、由 `ShellViewModel.ApplyLanguage`（`ShellViewModel.cs:203`）保持同步：

- **应用层（Irihi.Lingua 源生成器）**：`src/Erya/Localization/LanguageManager.cs` 上的 `[LinguaManager("./Resources/Strings.json")]` 让源生成器读 `Resources/Strings.json`（基础 = 简中，回退不变量）+ `Strings.zh-Hant.json` / `Strings.en.json`（经 csproj `AdditionalFiles` 接入），为每个平铺 key（`section.key` → `section_key`）生成一个 `IObservable<string?>` 属性。XAML：`{Translate {x:Static loc:LanguageManager+Keys.xxx}}`；代码 / VM：`Loc.Get(key)` / `Loc.Format(key, args)`（`Loc` 是把 observable 适配成同步读的小包装）。语言选择器是 `MainWindow.axaml` 里的 `lingua:CulturePicker`。
- **Core 层（`CoreLoc`）**：静态 `Language`（`""`=简中 / `zh-Hant` / `en`），从嵌入式 `CoreStrings[.lang].json`（平铺 key → 文案，缺失回退基础再回退 key）取串；同一套 `TryLoadMap` 也服务于设置 schema 与方案目录的语言覆盖。
- **切换流程**：`ApplyLanguage` → `LanguageManager.Instance.UpdateCulture(...)` + `CoreLoc.Language = ...` + `AppPreferences.Save()` → `RebuildNav()` + `RebuildPages()`（页面 VM 在构造期取串，schema 也要带新语言重载）+ `MainViewModel.RefreshLocalizedTexts()`；启动时先应用持久化语言再建页面。
- 嵌入资源的 `WithCulture="false"` 红线与「三份语言文件必须写全」的原因见 AGENTS.md 的 i18n 一节。

主题：`App.axaml` 挂 `SemiTheme` + `UrsaSemiTheme` + AvaloniaEdit / ColorPicker 的 Semi 变体，合并 `Erya.Controls` 的 `CellTheme.axaml` / `ReorderTheme.axaml` / `CellStyles.axaml` 与 Lingua 的 `Generic.axaml`；`MainWindow` 是 `UrsaWindow`，**必须显式安装 `WindowToastManager` / `WindowNotificationManager`**（Ursa 不会自动挂），否则 `AppNotifications` 全部静默失效。

---

## Erya.Controls 与 demo

公开控件（`src/Erya.Controls/`）：

- `Cell : Button` —— 设置中心风格的一行：`Icon` / `Content`（标题）/ `Trailing`（如「已修改」徽标）/ `Description` / `Note`（当前值）/ `RightContent`（编辑控件）/ `Detail`（可展开区，`:expanded` 伪类）/ `ShowChevron` / `Command`；只有可展开 / 可导航的行才有 hover / 按压反馈。
- `CellGroup : HeaderedItemsControl` —— Semi 风格圆角卡片，自动分隔线，圆角经 `CornerRadiusMixerConverter` 传给首末行。
- `ReorderableItemsControl : ItemsControl` —— 拖拽排序，attached `IsDragHandle` 指定把手，实时移动绑定的 `ObservableCollection`，Esc 取消。
- `DragHandle` / `ReorderableItem`（容器，`:dragging` 伪类）/ `ValidationErrorIcon`（配合 `Classes="silent-errors"` 的行内校验红叹号）。

消费方只有主应用（设置页 `SettingsSectionsView` 把 section 渲染成 `CellGroup` + 按类型 DataTemplate 的 `Cell`，61 处 `Cell`；方案管理用 `ReorderableItemsControl`）与 UiTests。`demo/Erya.Controls.Demo` 是脱离 RIME 的可视化演示 / 回归台（拖拽排序 + CellGroup 假数据）。

---

## 测试架构

两套测试都在任何 OS 上无头运行（CI 三平台矩阵），没有显示器要求：

- **`tests/Erya.Core.Tests`（xunit v2）**：配置读写 round-trip（三种 patch 形态、注释保留、写入迁移）、schema 白名单与 overlay 校验、发行版探测（env 覆盖沙盒化）、部署等待（注入时钟，二进制从不真跑）、备份、在线安装与小狼毫一键安装（手写 `HttpMessageHandler`，假 tar.gz / 假 release JSON，无网络；切 `CoreLoc.Language` 的三类测试共享串行 Collection）。
- **`tests/Erya.UiTests`（xunit.v3 + Avalonia.Headless）**：
  - 引导：`[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`（`ShellNavTests.cs:18`），`TestAppBuilder` 用 `AppBuilder.Configure<App>()` 挂载**真实 App**（Semi 主题、图标、Cell 主题全部生效）+ `UseHeadlessDrawing = false` + `UseSkia` —— 真 CPU Skia 渲染，测试里才能 `RenderTargetBitmap` 导出截图。
  - 输入模拟约定：Flyout 必须走真实指针路径（`window.MouseMove/MouseDown/MouseUp` 到控件中心，路由 `Click` 事件打不开 Flyout）；Popup 内容经 `Flyout.Content` 取，不在视觉树里找；每步之后 `Dispatcher.UIThread.RunJobs()`。
  - `DocsScreenshotGenerator.cs`：一条 `[AvaloniaFact]`，对真实 RIME 前端逐页截图写入 `docs/screenshots/`（README 画廊 = 测试产物 = 视觉验收门）。本机没装 RIME 时静默跳过（`MainViewModel { Distribution: not null }` 不满足即 return），CI 永远绿。本地跑 UiTests 请排除（filter 见 AGENTS.md）。
- 两个被测项目都开了 `InternalsVisibleTo` 给对应测试程序集。

---

## 打包与发布

- `packaging/publish.ps1`：一次一个「平台 × AOT/非 AOT」组合，产出 `dist/Erya_<版本>_<平台>[_aot].zip`；版本默认读 `src/Erya/Erya.csproj` 的 `<Version>`；Linux/macOS 用 `zip` CLI 保可执行位；AOT 分支剔除调试符号（`.pdb` / `.dSYM` / Linux `.dbg`）。
- `packaging/linux/dist.sh`：Linux 三件套（tar.gz + AppImage + deb）一次产出。
- `packaging/macos/dmg.sh`：对既有 publish 产物组装 DMG（icns、Applications 别名、背景图、预生成 DS_Store 摆位）。
- CI：`test.yml`（push/PR，三平台 `dotnet test` 两个套件）、`package.yml`（手动触发，按平台 × 模式矩阵打包上传）、`release.yml`（手动填 `release_tag`，跑全部产物并创建 GitHub Release）。
- Native AOT 不可交叉编译；裁剪注意 `src/Erya/aot-roots.xml`（见 AGENTS.md 的 AOT 一节）。
