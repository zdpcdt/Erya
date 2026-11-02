# AGENTS.md — 尔雅 AI 协作约定

给在本仓库工作的 AI 编码代理（以及人类协作者）看的单一事实源。规则按踩坑代价排序，**改代码前先读完**。架构细节与数据流在 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)，不要在这里重复。

## 这是什么项目

**尔雅（Erya）** 是 RIME 输入法引擎的跨平台可视化配置工具：Avalonia 12 / .NET 10，一套代码对接三个 RIME 前端——Windows 小狼毫（Weasel）、macOS 鼠须管（Squirrel）、Linux fcitx5-rime / ibus-rime。界面三语（简中 / 繁中 / English）。

核心难点不在 UI，而在 RIME 的补丁语义：**补丁键是整体替换、不是合并**。整个配置层（显示合并值、只写脏键、叶子键补丁、永不触碰发行版文件）都由此推导而来。动配置层之前先读 ARCHITECTURE.md 的「核心数据流」。

## 常用命令

需要 .NET 10 SDK（仓库没有 `global.json`，不会自动钉版本）。解决方案文件是 `Erya.slnx`（XML 新格式），仓库里没有 `.sln`。

| 要做的事 | 命令 |
|---|---|
| 构建全部 | `dotnet build Erya.slnx` |
| 运行主程序 | `dotnet run --project src/Erya` |
| 运行控件 demo | `dotnet run --project demo/Erya.Controls.Demo` |
| Core 单元测试 | `dotnet test tests/Erya.Core.Tests -c Release` |
| UI 测试（**本地请带 filter**，原因见下） | `dotnet test tests/Erya.UiTests -c Release --filter "FullyQualifiedName!~DocsScreenshotGenerator"` |
| 打 zip 发布包 | `pwsh packaging/publish.ps1 -Rid osx-arm64 [-Aot \| -SingleFile]`（RID：`win-x64` `linux-x64` `linux-arm64` `osx-x64` `osx-arm64`） |
| Linux 三件套（tar.gz / AppImage / deb） | `bash packaging/linux/dist.sh linux-x64` |
| macOS DMG | `bash packaging/macos/dmg.sh artifacts/publish/osx_arm64`（需先 publish） |

- Native AOT **不能交叉编译**，每个 RID 必须在对应系统上构建；`-Aot` 与 `-SingleFile` 互斥。
- Linux/macOS 上打包依赖 `zip` CLI（PowerShell 的 `Compress-Archive` 会丢可执行位）。
- 应用版本号唯一来源：`src/Erya/Erya.csproj` 的 `<Version>`，三个打包脚本都从这里读。

## 项目地图

| 项目 | 职责 | 依赖 |
|---|---|---|
| `src/Erya.Core` | RIME 的一切磁盘事务：前端探测、YAML base+patch 模型、补丁写入、部署触发、备份恢复、在线装方案、设置 schema。**零 Avalonia 依赖**，自带轻量 i18n（`CoreLoc`） | YamlDotNet |
| `src/Erya.Controls` | Semi 风格可复用控件：`Cell` / `CellGroup` / `ReorderableItemsControl` / `DragHandle` / `ValidationErrorIcon` 及其主题 | Avalonia、Irihi.Avalonia.Shared |
| `src/Erya` | 主应用：MVVM 外壳、schema 驱动的设置页、皮肤编辑 + 候选窗实时预览、应用服务、Lingua 三语 | Core + Controls |
| `demo/Erya.Controls.Demo` | Controls 库的可视化演示 / 回归台，假数据，不碰 RIME | Controls |
| `tests/Erya.Core.Tests` | 纯单元测试（**xunit v2**） | Core（`InternalsVisibleTo`） |
| `tests/Erya.UiTests` | 无头 UI 测试（**xunit.v3** + Avalonia.Headless，挂载真实 `App`、真 Skia 渲染） | Erya（`InternalsVisibleTo`） |

Core 与 Controls 互不依赖，`src/Erya` 是唯一的组合点（组合根在 `src/Erya/App.axaml.cs`）。

## 硬性规则

每一条背后都有一次真实事故或一道 CI 防线。

### Git 与发布

- **未经明确要求，不要 commit，不要 push。** 改完告诉用户改了什么，让用户决定何时提交。
- 提交信息用 Conventional Commits（`feat` / `fix` / `docs` / `chore` / `refactor` 等），一行祈使句说清改了什么、为什么；一次提交只做一件事。
- `git reset --hard`、`git push --force`、`rm -rf`、删分支等破坏性命令，执行前必须先向用户确认。
- 发布产物（zip / DMG / Release）必须等用户在真机验证并点头后才能推送；视觉类 bug 先复述你对问题的理解再动手。
- 改了共享文件（尤其 `docs/screenshots/`、README）后主动提醒用户提交——未提交的修改曾两次被 `git pull` 冲掉。

### 依赖与构建

- **Central Package Management**：所有 NuGet 版本只写在根目录 `Directory.Packages.props`，csproj 的 `<PackageReference>` 只写 `Include`。给 csproj 加 `Version` 会直接报 NU1008。
- 没有 `Directory.Build.props` / `.editorconfig` / `.runsettings`，不要臆想它们存在。
- 代码风格跟随现有文件：`Nullable` + `ImplicitUsings` 开启、文件级命名空间、优先 `sealed`、4 空格缩进、注释以中文为主。

### 测试

- 所有测试必须在 **ubuntu / windows / macos 三平台全绿**（`.github/workflows/test.yml` 矩阵，每次 push/PR 都跑）。不要写只在本机 OS 上能过的测试。
- 平台相关行为通过 **可注入的构造参数 + 平台默认值** 做成可测的（时钟、路径、环境变量覆盖），不要用 `#if` 或在测试里真跑外部二进制。四个前端的探测都支持 env 覆盖：`ERYA_WEASEL_DIR` / `ERYA_SQUIRREL_APP` / `ERYA_FCITX5_RIME_HOME` / `ERYA_IBUS_RIME_HOME`。
- Core.Tests 用 xunit v2，UiTests 用 xunit.v3 + `Avalonia.Headless.XUnit`，**两边不要随手统一**。没有 Moq / NSubstitute / FluentAssertions，用手写 fake、沙盒临时目录（`IDisposable` 清理）。
- **`DocsScreenshotGenerator` 会写仓库**：`tests/Erya.UiTests/DocsScreenshotGenerator.cs` 检测到本机真实安装的 RIME 前端时，会把 10 张截图直接覆盖写入 `docs/screenshots/`（这些 PNG 是提交进仓库的 README 画廊）。本地跑 UiTests 请加上面表格里的 `--filter`；CI 机器没装 RIME，它自动跳过。
- UI 测试驱动 Flyout 要走真实指针路径（`window.MouseMove/MouseDown/MouseUp`），`Button.Flyout` 不响应路由 `Click`；每步之后 `Dispatcher.UIThread.RunJobs()`。详见 ARCHITECTURE.md「测试架构」。

### RIME 配置层（最容易写坏用户环境）

- **永远不写发行版的 base 文件**（`default.yaml` / `squirrel.yaml` / `weasel.yaml` / `*.schema.yaml`），一切改动只落在用户目录的 `<name>.custom.yaml`。这是 `RimeConfigStore` 的契约，新代码不得绕过它直接写文件。
- **只写叶子键**：补丁键整体替换，整段写 `preset_color_schemes` 会丢掉所有未建模字段。编辑内置皮肤必须写 `preset_color_schemes/<id>/<字段>`。
- `CustomYamlPatcher` 的既有安全策略——先写 `.bak`、行级编辑保留注释、结构重写只作兜底、写后重新解析校验失败即回滚——新的写入路径必须复用它，不要另起炉灶。
- **Schema 键白名单**：设置页由 `src/Erya.Core/Settings/settings.{default,squirrel,weasel}.json` 驱动。给 schema 加任何新 `keyPath` 之前，先到上游源码确认引擎 / 前端真的读这个键：librime 1.16 源码（`default.yaml`）、Squirrel 的 `SquirrelTheme.swift`（`squirrel.yaml`）、Weasel 0.17 的 `RimeWithWeasel.cpp`（`weasel.yaml`）。`tests/Erya.Core.Tests/SettingsSchemaTests.cs` 里有逐键对照上游的硬编码白名单，没对上的键会让 CI 红——这是防止「能渲染但引擎根本不读」的 placebo 控件混入的最后一道防线，**不要为了让测试通过去改白名单**，除非你附上上游源码出处。
- 两个前端的 `appOptions` 可选项不同（鼠须管 `ascii_mode / vim_mode / inline / no_inline`，小狼毫 `ascii_mode / vim_mode / inline_preedit`），不要混用。

### i18n（三语必须同步）

- **应用层**（Irihi.Lingua 源生成器）：`src/Erya/Resources/Strings.json`（基础 = 简中）+ `Strings.zh-Hant.json` + `Strings.en.json`。两层嵌套 `section.key` 生成为 `LanguageManager.Keys.section_key`。**新 key 必须三份文件都写**——变体缺键时该字符串会静默停留在旧值，没有任何回退或警告。XAML 用 `{Translate {x:Static loc:LanguageManager+Keys.xxx}}`，ViewModel 用 `Loc.Get(...)` / `Loc.Format(...)`。
- **Core 层**（`CoreLoc`）：`src/Erya.Core/Localization/CoreStrings*.json`、设置 schema 的语言覆盖 `settings.<name>.<lang>.json`、在线方案目录的 `packages.catalog*.json`，都是平铺点号 key。**覆盖文件只改文案，不动结构**（section / field id、keyPath、默认值在所有语言里必须一致，`SettingsSchemaOverlayTests.cs` 会校验）。
- Core 的嵌入资源必须保持 `WithCulture="false"`（见 `src/Erya.Core/Erya.Core.csproj` 注释）：否则文件名里的 `.en` / `.zh-Hant` 后缀会被 MSBuild 当作文化拆进卫星程序集，运行时什么都读不到。
- 品牌名（RIME、雾凇拼音等）不翻译。

### Native AOT / 裁剪

- Windows 发布含 Native AOT 变体。`System.Text.Json` 一律走 `JsonSerializerContext` 源生成，不要写反射式 `JsonSerializer.Deserialize<T>()`。
- 设置 schema JSON 里的 `icon` 是按字符串反射实例化的 Iconica 图标类型。**新增图标必须同步 `src/Erya/aot-roots.xml`**，否则裁剪后图标静默消失，不报错。

### 平台差异的落点

- 前端探测与部署的全部差异都封装在 `src/Erya.Core/Distributions/` 的 `IRimeDistribution` 实现里（Weasel / Squirrel / Fcitx5Rime / IbusRime + 各自 Locator）。UI 与 ViewModel 层不要出现 `OperatingSystem.IsXxx()` 分支，组合根 `App.axaml.cs` 除外。
- Linux 前端没有 UI 配置文件（`UiConfigFile == null`），因此没有外观三页（皮肤 / 排版 / 应用行为）；这是有意为之，不是缺失。

## 深入阅读

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — 依赖图、Core 各类职责、设置改动从 ViewModel 到 YAML 到部署的完整链路、i18n 双层机制、测试架构、打包。
- [docs/porting-notes.md](docs/porting-notes.md) — 从 Qt rimetool 移植时的设计决策与 Avalonia 12 踩坑（英文）。
- [README.md](README.md) / [README.en.md](README.en.md) — 面向用户的功能说明与下载。
