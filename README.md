# 尔雅 Erya

**中州韵输入法引擎（RIME）的可视化配置工具** —— 让每个用户都能"近雅"：不写 YAML，也能把 RIME 调教得服服帖帖。

> 尔，近也；雅，雅言也。《尔雅》为辞书之祖，取"引向正音"之意。

基于 **Avalonia** 构建，一套代码覆盖 Windows（小狼毫 Weasel）、Linux（fcitx5-rime / ibus-rime）、macOS（鼠鬚管 Squirrel）。

## 当前状态

🚧 项目启动于 2026-10（Avalonia Port Challenge 2026 参赛作品，移植自 Qt 版 [rimetool](https://github.com/yanhuacuo/rimetool) 的功能设计，并借鉴 WPF 版 [RimeControl](https://github.com/qzly/RimeControl) 的备份恢复思路）。

- [x] 发行版抽象层（`IRimeDistribution`）与小狼毫检测
- [x] 主界面外壳（Semi.Avalonia + Ursa）
- [x] 方案管理（勾选启用/停用输入方案，写入 `default.custom.yaml` 补丁并自动部署；文本级编辑保留用户注释，写入后校验失败自动回滚）
- [x] 备份恢复（zip 打包用户目录；恢复前自动在备份旁生成安全备份，恢复后自动触发重新部署）
- [ ] 一键安装配置方案（Day 5）
- [ ] Linux 验证（Day 6）

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

## License

MIT（见 LICENSE）
