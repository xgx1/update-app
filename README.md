# update-app（update APP）

.NET 10 命令行应用：读取 `applist.toml`，批量更新本机软件。

## 更新三类内容

| 类别 | 说明 | 默认动作 |
|---|---|---|
| `[source]` 源码型项目 | 记录本地源码目录 | `git -C <path> pull --ff-only`（可选自定义命令 / 后置命令） |
| `[scoop]` Scoop 应用 | `update_all = true` 则 `scoop update *`；false 则只更新 `apps` 列表 | `scoop update …` |
| `[special]` 特殊配置 | 如 DSH 插件（均为源码安装），paths 可为单仓库或含多仓库的目录 | 逐路径 `git pull --ff-only` + 可选后置命令（如同步技能到 `~/.dsh/skills`） |

## 已知问题自愈（对应需求：问题修复后不再需要 AI）

`[fixes.rules]` 定义正则规则：失败输出命中后自动处理：

- `command` → 执行修复命令（支持 `{name}` `{path}` 占位符）后**重试一次**，成功标记 `fixed`
- `skip = true` → 标记 `skipped`（已知问题，跳过）
- `ask_user = true` → 标记 `needs_input`（停下等用户回复）
- 未命中任何规则 → `failed`（需要 AI 处理；AI 修好后应把解法固化进 `[fixes.rules]`）

## 结果与日志

每次运行写入配置文件同目录的 `logs/`：

- `latest.log` / `run-<时间戳>.log` —— 人类可读日志
- `latest.json` / `run-<时间戳>.json` —— 结构化摘要（逐项 status: ok/fixed/skipped/needs_input/failed + 输出），供 AI 技能读取分析

## 用法

```text
update-app run            # 执行全部更新；--dry-run 只预览；--only <关键字> 只跑匹配项
update-app list           # 列出配置中的更新项
update-app validate       # 校验配置与依赖（路径/git/scoop/dotnet）
update-app self           # 自更新：git pull 自身源码 + dotnet publish → <config>/bin/current.json
```

- 默认配置文件 `./applist.toml`，可用 `-c <path>` 指定（日志/发布产物均相对于配置文件所在目录）。
- `self` 解析「带 SDK 的 dotnet」：优先 PATH，其次 scoop 的 `dotnet-sdk`（.NET 10）/ `dotnet9-sdk`，最后 `C:\Program Files\dotnet`。
- 退出码：`0` 全部成功/已修复；`1` 存在失败或需确认项；`2` 参数/配置错误。

## 构建

```powershell
& C:\Users\Admin\scoop\apps\dotnet-sdk\current\dotnet.exe build -c Release
```

依赖：Tomlyn（TOML 解析）。