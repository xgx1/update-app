# AGENTS.md — update-app

.NET 10 命令行工具：读 [`applist.toml`](applist.toml)，批量更新本机软件；配一个技能让 agent 会用这个 CLI。
**改代码前先读 [`README.md`](README.md)**（仓库构成、三类更新项、`[fixes.rules]` 语义、日志格式都在那里，本文不重复）。

## 这个仓库的规矩

- **两类内容同仓**：CLI 在仓库根（`Program.cs` / `Runner.cs` / `AppConfig.cs` / `ProcessRunner.cs` / `Logger.cs`），
  技能在 [`skills/update-all/SKILL.md`](skills/update-all/SKILL.md)。改 CLI 行为要同步技能里的判据，反之亦然。
- **技能靠软链，不复制**：`~/projects/MyAI/dsh-extensions/install-skill.sh` 把 `skills/update-all` 软链到
  `~/.dsh/skills/update-all`。往 `~/.dsh/skills/` 放副本必与仓库漂移（以前就漂过一次）。
- **新增/修改更新项只改 `applist.toml`**，不改代码：`[source]`（源码仓，默认 `git pull --ff-only`）、
  `[scoop]`（**Arch 上恒为 no-op**，系统更新走 `[source]` 里的 pacman/yay 自定义命令）、
  `[special]`（DSH 插件这类，逐路径 pull + 后置命令）。
- **修好的问题要固化**：任何「AI 现场处理掉的失败」，都要在 `[fixes.rules]` 里加一条正则规则
  （`command` 自愈并重试一次 / `skip` / `ask_user`）。只修不固化＝下次还要人陪跑。
- **自更新项指向本仓**：`applist.toml` 里 `name = "update-app"` 那条的路径必须与仓库当前位置一致
  （2026-09-13 从 `MyAI/update-app` 迁到 `~/projects/update-app` 时同步改过一次）。

## 跑与验

```bash
dotnet run -- --help                 # 看当前支持的参数（别照抄文档里的旧参数）
dotnet run                           # 全量更新（本机含 pacman/yay，单项超时 1800s）
cat logs/latest.json                 # 结果汇总：fixed / skipped / needs_input / failed
```

- 结果四态里只有 `needs_input` 需要停下来问人，`failed` 要自己看日志处理并把解法固化进 `[fixes.rules]`。
- 有单元测试就 `dotnet test`；本仓库目前**没有测试工程**，改动靠 `--help` 与一次小范围实跑来验
  （例如临时只留一个 `[source]` 项）。
