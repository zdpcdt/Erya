# 尔雅 Erya

**中州韵输入法引擎（RIME）的可视化配置工具** —— 让每个用户都能"近雅"：不写 YAML，也能把 RIME 调教得服服帖帖。

> 尔，近也；雅，雅言也。《尔雅》为辞书之祖，取"引向正音"之意。

基于 **Avalonia** 构建，一套代码覆盖 Windows（小狼毫 Weasel）、Linux（fcitx5-rime / ibus-rime）、macOS（鼠鬚管 Squirrel）。

## 当前状态

🚧 项目启动于 2026-10（Avalonia Port Challenge 2026 参赛作品，移植自 Qt 版 [rimetool](https://github.com/yanhuacuo/rimetool) 的功能设计，并借鉴 WPF 版 [RimeControl](https://github.com/qzly/RimeControl) 的备份恢复思路；设置页架构参考 macOS 版 [SCT](https://github.com/neolee/sct) 的双层配置模型与 schema 驱动 UI）。

- [x] 发行版抽象层（`IRimeDistribution`）；Windows 小狼毫、Linux fcitx5-rime / ibus-rime、macOS 鼠须管的检测与部署（Ctrl+R / Cmd+R 全局重新部署）；概览页版本号在 Linux 上取自包管理器（dpkg-query / rpm / pacman）
- [x] 前端切换器（fcitx5-rime 与 ibus-rime 并存时选择尔雅管理的那个；选择持久化到 `~/.config/Erya/preferences.json`）
- [x] 部署器缺失引导（Linux 读取 `/etc/os-release`，按发行版给出 librime 工具包安装命令：Debian/Ubuntu `librime-bin`、Arch/Fedora `librime`、openSUSE `rime`）
- [x] 主界面外壳（Semi.Avalonia + Ursa）
- [x] 方案管理（勾选启用/停用、启用顺序调整即切换顺序；写入 `default.custom.yaml` 补丁并自动部署；文本级编辑保留用户注释，写入后校验失败自动回滚）
- [x] 输入设置（schema 驱动：候选词个数、方案选单、中英切换按键矩阵、功能快捷键、数字标点——全部前端通用，写入 `default.custom.yaml`）
- [x] 外观设置（schema 驱动：候选面板样式/键盘体验/按应用行为，macOS 写 `squirrel.custom.yaml`、Windows 写 `weasel.custom.yaml`；Linux 前端显示指引——面板外观归 fcitx5/ibus 自身设置）
- [x] 皮肤库（`preset_color_schemes` 编辑器：BGR 颜色 + 候选条实时预览，整库写入补丁）
- [x] 高级设置（base+patch 合并配置树、行内改值即写补丁、每键撤销、路径/值搜索、仅显示已修改；源码模式直接编辑 `.custom.yaml`，保存前 YAML 校验并留 `.bak`）
- [x] 备份恢复（zip 打包用户目录；恢复前自动在备份旁生成安全备份，恢复后自动触发重新部署）
- [x] Linux 打包三件套（`packaging/linux/dist.sh` 一次构建产出 tar.gz / AppImage / deb，均自包含）
- [x] 在线方案安装（方案管理页一键安装雾凇拼音/白霜拼音/薄荷输入法：GitHub tarball 直下无需 git、自动过滤文档与 CI 杂物、安装前自动备份用户目录、装完自动重新部署）
- [ ] macOS 真机验证（定位器与部署已实现，可用 `ERYA_SQUIRREL_APP` 环境变量在 CI 指向测试目录）。Linux 检测与部署链路已在 Ubuntu 26.04（librime 1.16）真机验证。

### 设置页如何工作（schema 驱动）

设置页由 `Erya.Core/Settings/settings.*.json` 描述（字段 → `keyPath` → 控件类型 → 范围/选项 → 目标文件），RIME 出新配置项时改 JSON 即可，不用动代码。读写遵循 RIME 补丁规则：显示 base + patch 合并后的生效值，修改只写入对应 `.custom.yaml` 的 `patch:`，永不触碰发行版默认文件；每键可撤销（从补丁中删除即恢复默认）。

## 技术栈

| 组件 | 选型 |
|---|---|
| UI 框架 | Avalonia 12 / .NET 10 |
| 主题 | [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) + [Ursa](https://github.com/irihitech/Ursa.Avalonia) |
| MVVM | CommunityToolkit.Mvvm |
| YAML | YamlDotNet |

## 构建运行

```bash
dotnet build Erya.slnx
dotnet run --project src/Erya
```

## Linux 发行包

```bash
packaging/linux/dist.sh        # 一次构建，产出三种格式（自包含，无需 .NET 运行时）
```

| 产物 | 安装方式 |
|---|---|
| `dist/erya-<版本>-linux-x64.tar.gz` | 解压后 `./install.sh`：用户级装到 `~/.local/share/erya` + 应用菜单入口与图标（无需 root；`uninstall.sh` 卸载） |
| `dist/Erya_<版本>_x86_64.AppImage` | `chmod +x` 后双击运行，免安装 |
| `dist/erya_<版本>-1_amd64.deb` | `sudo apt install ./erya_*.deb`（程序装到 `/usr/lib/erya`，入口 `/usr/bin/erya`） |

`dist.sh [RID]` 可指定运行时（如 `linux-arm64`，需在对应架构主机或 qemu-user 下构建）；AppImage 构建首次会自动下载官方 `appimagetool`（缓存于 `dist/cache/`）。

## License

MIT（见 LICENSE）
