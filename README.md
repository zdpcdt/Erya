# 尔雅 Erya

**简体中文** · [English](README.en.md)

![License](https://img.shields.io/badge/license-MIT-blue) ![UI](https://img.shields.io/badge/UI-Avalonia%2012-red) ![.NET](https://img.shields.io/badge/.NET-10-purple) ![Platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-green)

**中州韵输入法引擎（RIME）的可视化配置工具** —— 不写 YAML，也能把 RIME 调教得服服帖帖。

> 尔，近也；雅，雅言也。《尔雅》为辞书之祖，取「引向正音」之意。

一套代码覆盖三大桌面平台，自动对接本机的 RIME 前端：Windows 小狼毫（Weasel）、macOS 鼠须管（Squirrel）、Linux fcitx5-rime / ibus-rime。界面内置简体中文 / 繁體中文 / English 三语。

| 概览 | 方案管理 | 输入设置 | 候选窗排版 | 应用行为 |
|---|---|---|---|---|
| ![概览](docs/screenshots/overview.png) | ![方案管理](docs/screenshots/schemas.png) | ![输入设置](docs/screenshots/input.png) | ![排版](docs/screenshots/layout.png) | ![应用](docs/screenshots/apps.png) |

![皮肤库：亮/暗候选窗并排预览 + 皮肤列表与选中编辑（真实发行版配置）](docs/screenshots/skin.png)

> 截图由仓库里的无头渲染测试对着本机真实安装的 RIME 前端生成（`DocsScreenshotGenerator`，截图机即真实运行时：Windows 上对小狼毫、macOS 上对鼠须管——本组截图取自鼠须管），所见即真实数据。

## 它解决什么问题

RIME 是高度可定制的中文输入法引擎，但它的全部力量都藏在 YAML 里：补丁语义、键路径、各前端互不相同的键名——普通用户面对的是劝退级别的配置门槛，改错一个键轻则无效、重则候选框消失。

工具生态同样如此：Windows 官方自带的图形界面只覆盖最基础的开关，进阶设置全靠手改 YAML；第三方工具各自优秀，却各守一端、互不跨平台——Qt、WPF、macOS 原生各一套（Qt 版 rimetool 的移植对照见下文）；Linux 更是连图形配置工具都没有——官方的配置管理器东风破（plum）是命令行脚本，桌面面板的设置只管输入法框架本身，RIME 的方案与补丁仍然要手改 YAML。

| 官方自带（小狼毫 · Windows） | RimeControl（WPF · Windows） | SCT（macOS 原生） |
|---|---|---|
| <img src="docs/screenshots/ecosystem-weasel.png" width="280"> | <img src="docs/screenshots/ecosystem-rimecontrol.png" width="280"> | <img src="docs/screenshots/ecosystem-sct.png" width="280"> |

尔雅把这些知识写进程序：

- **所见即生效值**：每个设置显示 base + patch 合并后的真实生效值；保存只写你动过的键，从补丁删除即恢复发行版默认——永不触碰发行版文件。
- **不会写坏**：整键覆盖会丢未建模字段的坑（如 `preset_color_schemes` 整段重写）由叶子键补丁规避；未知键原样带回；写坏自动回滚。
- **每个键都真实**：暴露的每个配置键都对照前端源码逐一核验（小狼毫 0.17 / 鼠须管 / librime 1.16），并有白名单测试防止「能渲染但引擎根本不读」的 placebo 键混入。

## 功能

**方案管理**

- 勾选启用/停用方案、拖动调整切换顺序，保存即写 `default.custom.yaml` 并自动重新部署
- 在线方案安装：一键安装雾凇拼音 / 白霜拼音 / 薄荷拼音（GitHub tarball 直下，无需 git）；安装前自动备份、装完自动部署；已安装的包按文件清单精确删除，只动该包文件
- 方案列表文本级编辑保留你的注释，写入校验失败自动回滚

**输入设置**

- 候选词个数、方案选单、中英切换按键矩阵、功能快捷键（含以词定字）、数字标点——全部三前端通用

**外观**

- **皮肤库**：`preset_color_schemes` 编辑器，BGR 颜色拾取 + 亮/暗候选窗并排实时预览；内置皮肤只读、可「以此新增」派生、一键复制整皮肤 YAML
- **候选窗排版**：字体 / 间距 / 圆角 / 阴影 / 横竖排 / 宽高限制全量键位，顶部实时预览，未保存的编辑立即反映；同一份预览同时认小狼毫 `style/layout/*` 与鼠须管 `style/*` 两套键路径
- **应用行为**：按应用设定默认英文、Vim 模式；macOS 枚举正在运行的应用一键添加，Windows 取有窗口进程

**高级与安全网**

- 高级设置：base + patch 合并配置树、行内改值即写补丁、每键撤销、路径/值搜索；源码模式直接编辑 `.custom.yaml`（保存前 YAML 校验并留 `.bak`）
- 备份恢复：一键 zip 打包用户目录；恢复前自动生成安全备份，恢复后自动重新部署
- 部署：Ctrl+R / Cmd+R 全局重新部署；Linux 双前端并存时可选择尔雅管理的那个；缺部署器时按发行版给出 librime 工具包安装命令

## 下载

到 [Releases](https://github.com/zdpcdt/Erya/releases) 取自包含发布包（无需安装 .NET 运行时，由 CI 构建）：

| 平台 | 文件 | 用法 |
|---|---|---|
| Windows x64 | `Erya_0.1.0_win_x64.zip`（另有 `_aot` 变体，更小更快） | 解压即用 |
| macOS arm64 | `Erya_0.1.0_osx_arm64.zip` | 未经签名/公证，首次运行先 `xattr -cr` 解除隔离 |
| Linux x64 | `Erya_0.1.0_linux_x64.zip` | 解压后 `./Erya`；或用下面的三件套 |

Linux 另有三件套发行包：`tar.gz`（用户级 `install.sh` 安装）/ `AppImage`（免安装）/ `deb`（`sudo apt install`）。

检测与部署链路已在 macOS（arm64 · 鼠须管）与 Ubuntu 26.04（librime 1.16 · fcitx5-rime/ibus-rime）真机验证。

## 从源码构建

| 组件 | 选型 |
|---|---|
| UI 框架 | Avalonia 12 / .NET 10 |
| 主题 | [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) + [Ursa](https://github.com/irihitech/Ursa.Avalonia) |
| MVVM | CommunityToolkit.Mvvm |
| YAML | YamlDotNet |

```bash
dotnet build Erya.slnx
dotnet run --project src/Erya
```

打包：`pwsh packaging/publish.ps1 -Rid win-x64 [-Aot]` 产出 `dist/Erya_<版本>_<平台>[_aot].zip`（Native AOT 不可交叉编译，各 RID 需在原生系统构建）；Linux 三件套由 `packaging/linux/dist.sh` 一次产出。GitHub Actions 在 push/PR 跑三平台全量测试，打包在 Actions 页手动 Run workflow，`release_tag` 填版本号即自动创建 Release 并挂全部产物。

## 工作原理：和补丁语义做朋友

RIME 的用户修改全部落在 `<文件>.custom.yaml` 的 `patch:` 里，且**补丁键是整体替换、不是合并**。尔雅的读写模型由此而来：

1. **显示合并值，只写脏键**——每页读 base + patch 合并后的生效值，保存只写动过的键，未知键原样带回。
2. **叶子键补丁**——编辑内置皮肤走 `preset_color_schemes/<id>/<字段>` 合并进官方定义，未建模字段不丢。
3. **schema 驱动 UI**——设置页由 `Erya.Core/Settings/settings.*.json` 描述（键路径 → 控件类型 → 范围），RIME 出新配置项改 JSON 即可，不用动代码。

全仓 213 条测试三平台均可无头运行：配置读写 round-trip、schema 白名单校验、部署等待逻辑（注入时钟）、真实页面渲染截图——README 里这张画廊本身就是测试产物，兼作视觉验收门。

## 设计参考与移植背景

尔雅是 [Avalonia Port Challenge 2026](https://avaloniaui.net/blog/avalonia-port-challenge) 的参赛作品，功能设计对标三款前辈工具，**代码与资源全部全新编写，未复用任何既有代码**：

- [rimetool](https://github.com/yanhuacuo/rimetool)（Windows · Qt 6/QML）——功能面的主要参考：配色、排版、杂项、热键、标点、方案、备份
- [RimeControl](https://github.com/qzly/RimeControl)（Windows · WPF）——备份恢复的交互思路
- [SCT](https://github.com/neolee/sct)（macOS）——双层配置模型（base + patch）与 schema 驱动设置页的架构参考

完整的移植笔记（英文）见 [docs/porting-notes.md](docs/porting-notes.md)。

| 功能 | rimetool（Qt · 移植前） | 尔雅（Avalonia · 移植后） |
|---|---|---|
| 主题配色 + 预览 | <img src="docs/screenshots/rimetool-qt-color.png" width="360"> | <img src="docs/screenshots/skin.png" width="360"> |
| 排版风格 | <img src="docs/screenshots/rimetool-qt-layout.png" width="360"> | <img src="docs/screenshots/layout.png" width="360"> |
| 深色主题 | <img src="docs/screenshots/rimetool-qt-misc.png" width="360"> | <img src="docs/screenshots/skin-dark.png" width="360"> |

> 「移植前」截图为 Qt 版 rimetool，仅作功能对照。

## 致谢

- [RIME · 中州韵](https://rime.im)（佛振）与各前端：鼠须管 Squirrel、小狼毫 Weasel、fcitx5-rime / ibus-rime
- [rimetool](https://github.com/yanhuacuo/rimetool)（Qt 版配置工具，功能设计的主要参考）、[SCT](https://github.com/neolee/sct)（双层配置模型参考）、[RimeControl](https://github.com/qzly/RimeControl)（备份恢复思路）
- UI 基建：[Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) / [Ursa](https://github.com/irihitech/Ursa.Avalonia) / Avalonia 社区

## License

MIT（见 [LICENSE](LICENSE)）
