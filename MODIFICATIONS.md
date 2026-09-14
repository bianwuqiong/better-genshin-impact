# 自动化工作流分支变更 / Automation Workflow Fork Changes

本文件用于显著记录相对 BetterGI 上游的修改。修改期间为 **2026-09-12 至 2026-09-14**。

This file prominently records changes from upstream BetterGI. The changes were made from **2026-09-12 through 2026-09-14**.

## 来源 / Origin

- 上游项目 / Upstream: <https://github.com/babalae/better-genshin-impact>
- 上游基线 / Upstream base: `9424d1f3c344f8ba99363e0b0d3756f76bce836d`
- 工作流项目 / Workflow: <https://github.com/bianwuqiong/BetterGI-Automation-Workflow>
- 维护者 / Fork maintainer: `bianwuqiong`

## 修改内容 / Changes

- 验证 HoYoPlay 启动后的游戏前台窗口，并改进首次登录焦点激活。
- 为合成浓缩树脂增加前后库存证据与明确的机器可读结果。
- 为自动秘境增加有界的传送、战斗、古树搜索、接近和领奖阶段；减少重复 OCR，并记录树脂与奖励证据。
- 增加一条龙运行标识、明确结束事件及命令行配置选择，防止不同运行之间混用日志。
- 增加实验性的同用户子会话启动、进程归属和 IPC 校验，用于后续桌面隔离验证。
- 增加相关单元测试和诊断事件。

- Verify the foreground game window after HoYoPlay launch and improve first-login focus activation.
- Add before/after inventory evidence and machine-readable outcomes for condensed-resin crafting.
- Bound teleport, combat, tree-search, approach, and reward stages in automatic domains; reduce repeated OCR and record resin/reward evidence.
- Add run identity, explicit completion events, and command-line configuration selection so logs from separate runs are not mixed.
- Add experimental same-user child-session launch, process ownership, and IPC validation for later desktop-isolation testing.
- Add related unit tests and diagnostic events.

## 验证状态 / Validation status

前台模式中的合成树脂、自动秘境和每日奖励节点已分别完成实机验证。完整连续工作流和子会话模式仍处于验证阶段；本分支不应被描述为 BetterGI 官方稳定版本。

Crafting, automatic-domain, and daily-reward checkpoints were individually validated in foreground mode. The continuous full workflow and child-session mode remain under validation; this branch must not be represented as an official stable BetterGI release.

## 许可证 / License

BetterGI 及本修改分支按仓库中的 [GNU GPL v3](./LICENSE) 发布。上游版权与许可证声明保持不变。若分发该分支的二进制文件，必须同时按 GPL v3 提供对应源码和构建脚本或等价的源码获取方式。

BetterGI and this modified branch are distributed under the repository's [GNU GPL v3](./LICENSE). Upstream copyright and license notices remain intact. Anyone distributing binaries from this branch must also provide the corresponding source and build scripts, or equivalent source access, as required by GPL v3.
