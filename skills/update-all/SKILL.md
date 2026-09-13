---
name: update-all
description: 更新所有软件（源码项目 / Arch 系统与 AUR 软件包 / 特殊配置与 DSH 插件源码）：调 update-app CLI（读取 applist.toml），读报错日志自动处理已知问题，需要用户拍板时停下询问并继续处理其他项，把新问题解法固化成 fixes.rules（下次 CLI 自愈，无需 AI）。触发词：更新所有软件、全部更新、跑一下 update all、更新一下所有东西、检查更新。
---

# update all —— 更新所有软件（源码项目 + Arch 系统/AUR 软件包 + 特殊配置/DSH 插件）· Arch Linux 版

## 何时使用

用户说「更新所有软件 / 全部更新 / 跑一下 update all / 更新一下所有东西 / 检查更新」等时使用。

## 一句话架构

**update-app** CLI（.NET 10，本机位于 `/home/sx/projects/update-app`）读取 `applist.toml`，
执行三类更新并把逐项结果写成结构化 JSON；本技能负责：跑 CLI → 读日志 → 分类处理 →
把新问题的解法固化成规则（下次 CLI 自己就能处理，不再需要 AI）。

> 本文件是 **Arch Linux 版**（原版为 Windows/Scoop/PM2，2026-08-28 适配迁移）。
> Windows 关键差异速记见文末。

## 更新逻辑（applist.toml 三类配置）

| 类别 | 配置 | 默认动作 |
|---|---|---|
| 源码型项目 | `[source.projects]`：本地源码目录 | `git -C <path> pull --ff-only`（可自定义 command / post_commands） |
| 系统/AUR 软件包 | Arch 没有 scoop；`[scoop]` 段保持 `update_all = false, apps = []`（无任务 no-op）。系统更新用 pacman、AUR 用 yay，推荐建模成 `[source.projects]` 自定义命令项（见「Arch 更新项模板」），或手动在终端执行 | `sudo pacman -Syu` / `yay -Sua`（模板见下） |
| 特殊配置 | `[special.items]`：如 DSH 插件源码（paths 单仓库或含多仓库的目录） | 逐路径 `git pull` + 可选后置命令（如同步技能到 `~/.dsh/skills`） |

## 前置检查

1. **配置文件**：`/home/sx/projects/update-app/applist.toml`（修改后请向用户确认再动）。
   该文件**已全部改为 Arch 路径**（2026-09-13 复核：表内 8 条路径全部存在），不再是 Windows 迁移残留。
2. **CLI 自举**：`/home/sx/projects/update-app/bin/current.json` 里的 `exe` 字段。
   - Arch 上 `exe` 指向的是 `update-app.dll`，运行方式为 `dotnet "<exe>" <命令>`（不是直接执行）。
   - 若 current.json 还是旧的 Windows `.exe` 路径（迁移残留），取 `bin/` 下最新时间戳目录里的 `update-app.dll`。
   - current.json 不存在或 dll 跑不动 → 按下方「自更新」重新发布一次。
3. **环境要点（本机实测）**：
   - **两份 dotnet 都带 SDK**：`~/.dotnet/dotnet`（**10.0.400**，dotnet-install 装的）与 `/usr/bin/dotnet`（10.0.112，pacman 的 `dotnet-sdk`）。
     `DotnetResolver` 在「PATH + `~/.dotnet/dotnet` + `/usr/bin/dotnet` + `/usr/share/dotnet/dotnet`」里挑 **SDK 版本最高** 的，
     所以落在 `~/.dotnet/dotnet`（`validate` 会显示 `[ok] dotnet: /home/sx/.dotnet/dotnet`）。
     手动 `dotnet build/publish` 也用 `~/.dotnet/dotnet`，免得与 CLI 自己选中的那份不一致。
     ⚠ 2026-09-13 更正：本节原写「`/usr/bin/dotnet` 只有 runtime 没有 SDK」——pacman 装上 `dotnet-sdk` 后该判断已不成立。
   - update-app 用 `/bin/sh -lc` 执行所有配置命令（非交互、非常规 bash）：自定义命令**不要依赖别名/函数/交互提示**；
     `yay` 交互式确认在无 TTY 下会挂住直到超时 → 走 CLI 必须 `--noconfirm`，否则在终端手动跑。
   - 代理：clash-verge（mihomo）默认监听 `127.0.0.1:7897`；测连通用 `curl -x http://127.0.0.1:7897 -sI https://api.nuget.org` 之类。

## Arch 更新项模板（applist.toml，按需给用户确认后启用）

```toml
# scoop 段保持关闭：Arch 无 scoop，开着会报「scoop 不可用」
[scoop]
update_all = false
apps = []

# 系统更新（pacman）与 AUR 更新（yay）：锚点路径必须是真实存在的目录（CLI 会校验）
[[source.projects]]
name = "pacman 系统更新"
path = "/var/cache/pacman/pkg"
command = "sudo pacman -Syu --noconfirm"

[[source.projects]]
name = "yay AUR 更新"
path = "/home/sx/.cache/yay"
command = "yay -Sua --noconfirm"
```

- 把 pacman/yay 放进 applist.toml 后，一次 `run` 就能覆盖「系统 + AUR + 源码 + 特殊配置」，
  且失败仍走同一套 fixes.rules 自愈闭环；代价是 `--noconfirm` 不可见确认交互 → 建议启用前让用户拍板。
- 不想自动动系统包 → 保持模板不加，手动 `sudo pacman -Syu` / `yay -Sua` 即可（技能只负责源码与特殊配置）。

## 标准流程

### 1. 运行

```bash
BIN=/home/sx/projects/update-app/bin
DLL=$(jq -r .exe $BIN/current.json 2>/dev/null | grep '\.dll$')
[ -n "$DLL" ] || DLL=$(ls -1dt $BIN/2026*/ | head -1)update-app.dll
dotnet "$DLL" run --config /home/sx/projects/update-app/applist.toml   # 可后台运行
```

常用子命令：`run`（执行）、`list`（看配置）、`validate`（校验路径/依赖）、`self`（自更新发布）；
选项：`--dry-run`（只预览）、`--only <关键字>`（只跑匹配项，如 `--only dsh` 只更新 DSH 插件源码）。

### 2. 读日志分类

结果在 `/home/sx/projects/update-app/logs/`：
- `latest.json` / `run-<时间戳>.json` —— 结构化摘要（`items[].status`）
- `latest.log` / `run-<时间戳>.log` —— 人类可读日志

逐项 status 语义：

| status | 含义 | 你的动作 |
|---|---|---|
| `ok` / `fixed` | 成功 / 规则自动修复成功 | 无需处理 |
| `skipped` | 已知问题按规则跳过 | 无需处理（可在汇总时提一句） |
| `needs_input` | 命中规则但需要用户拍板 | 提问，见第 3 步 |
| `failed` | 未命中规则，或修复后仍失败 | AI 处理，见第 4 步 |

### 3. 处理 needs_input

- 用 `ask_user_question` 一次问清（每题给选项，如「stash 后重试 / 跳过本次 / 手动处理」），
  **同时继续**处理其他 failed 项和整理剩余报告——不需要干等。
- 用户答复后按答复执行，再跑 `--only <name>` 验证该项变 ok/fixed。

### 4. 处理 failed（自动处理逻辑）

对每个 failed 项读它 Output：

1. **判断根因**：网络？路径？pacman/yay 包损坏？数据库锁？构建失败？git 冲突？WIP？
2. **可自动修 → 直接修**（例：网络设代理 `http://127.0.0.1:7897` 重试、清 pacman 锁 `sudo rm /var/lib/pacman/db.lck`、
   补依赖、清理锁文件），修完跑 `run --only <name>` 验证。验证通过后：
3. **固化规则（核心闭环）**：把解法写进 applist.toml 新增一条 `[[fixes.rules]]`：
   - `match` = 报错关键词正则（尽量精确到稳定子串）
   - `command` = 修复命令模板（可选，支持 `{name}` `{path}`；不写则只重试一次）
   - `retry = true`；需要拍板的语义用 `skip = true` 或 `ask_user = true` + `hint`
   - 再跑一次 CLI 确认该规则能把问题自动处理掉（fixed/skipped/needs_input），
     从此**这个软件的问题不再需要 AI 介入**。
4. **需要用户判断的** → 归入 needs_input 一起问。

### 5. update-app 自身出问题

- 现象：直接调 current.json 的 dll 报错/起不来；或 publish 失败。
- 处理：更新它本身 —— `git -C /home/sx/projects/update-app pull`（有 remote 时），
  修代码 bug，然后 `dotnet "$DLL" self --repo /home/sx/projects/update-app -c /home/sx/projects/update-app/applist.toml`
  重新发布（会写入新的 current.json：Arch 上 `exe` 字段为 `update-app.dll` 路径），再重跑全流程。
- `self` 内部用 DotnetResolver 按「SDK 版本最高」自动选 dotnet（→ `~/.dotnet/dotnet` 10.0.400）；两份都没了才需重建，
  重建走 `sudo pacman -S dotnet-sdk`（extra 源）或用 dotnet-install 装回 `~/.dotnet`（后者版本更新，会被优先选中）。
- update-app 的 bug 修复记得 commit（有 remote 则 push）。

### 6. 汇总报告

按类别给出：成功/自动修复/skipped/待确认/失败 清单 + 下一步动作（哪些等用户回复、哪些建议调整配置）。

### 7. 收尾（标准动作）：重启 DSH 相关服务 —— 每次更新完成后都必须做

**重启是每次完整 run 的标准收尾，不是可选**：哪怕本次没有改 DSH 插件/MCP 配置，
源码仓库可能拉到了新代码（如 dsh-extensions）、pacman/yay 可能升级了服务的运行时依赖
（nodejs 等），重启服务才让这些更新真正生效。只有所有项的 needs_input/failed 都已按用户
拍板处理完、整体确认 OK 之后，才做这一步。**本流程只重启 DSH 相关 systemd 用户服务，
不要求重启系统**（内核等系统级更新后是否重启系统，由用户另行择机决定，与本次收尾无关）。

本机 DSH（dsh-web / headroom 代理）由 **systemd 用户服务**托管（不是 PM2），
服务在 `~/.config/systemd/user/`：`dsh-web.service`、`headroom-deepseek.service`、
`headroom-scnet.service`、`headroom-siliconflow.service`。

顺序（重要：**dsh-web 最后重启**，它一重启当前对话就断线，属于正常现象）：

1. **先重启除 dsh-web 外的所有服务**并确认 active：
   ```bash
   systemctl --user restart headroom-deepseek.service headroom-scnet.service headroom-siliconflow.service
   systemctl --user is-active headroom-deepseek.service headroom-scnet.service headroom-siliconflow.service
   ```
2. **最后重启 dsh-web**：`systemctl --user restart dsh-web.service`
   - dsh-web 就是 DeepSeek harness 本体：重启瞬间当前对话会断线，页面随后自动恢复；
     新代码/新配置（更新后的插件、卸载的 MCP 等）这时才真正生效。
   - 重启前必须先完成：第 6 步汇总报告完整发出 + 用户确认同意重启（断线后 AI 无法再做任何事，
     一切后续动作都必须在重启前完成）。
3. 恢复后验证：页面能正常打开、`systemctl --user is-active dsh-web.service` 为 active；
   若发现插件功能缺失或启动报错，重点检查 `~/.dsh/profiles/web/package.json` 的 `link:` 依赖
   路径是否有效（见「已知问题速查」里 `link:` 依赖那一行）。

- 若 `systemctl --user` 报 `Failed to connect to bus`，先 `export XDG_RUNTIME_DIR=/run/user/$(id -u)`。

## DSH harness 大版本升级（0.1.x → 0.1.y；0.1.1-rc.2 → 0.1.5-rc.2 实证）

源码型项目里的 `deepseek-harness`（分支 `master`）在 applist.toml 中默认禁用；它**就是生产实例（3080）的代码**，
大版本跨越不能只 `git pull`（dev worktree/3081 已于 2026-09-13 下线），按「源码升级 → 隔离冒烟验证 → 重建 → 用户确认后重启」走：

1. **升级源码**（旧进程在内存里继续跑，改文件本身不断线）：
   ```sh
   git -C /home/sx/projects/MyAI/deepseek-harness fetch origin
   git -C /home/sx/projects/MyAI/deepseek-harness merge origin/master
   # 冲突多来自本地定制提交 vs 上游重构（README / agent 预设 / pnpm-lock）；逐个人工定，别整边取
   ```
   - **大版本跳变后必须先 `pnpm run clean` 再 build**：旧 `lib/` 混新源码会报
     `[MISSING_EXPORT] "X" is not exported by ...`，那是陈旧产物不是代码 bug（见第 3 步重建）。
   - 旧会话日志（`SESSION_FORMAT_VERSION` 0 → 3）由 `packages/session/session-format-{v0-to-v1,v1-to-v2,v2-to-v3}`
     + `session-format/chain.ts` 流式迁移，不丢数据（本机最大会话 8.5MB，迁移无感）。
   - 改名/移动过该 worktree 的话，第 4 步重启前必须先跑「vendor 悬空链接重指」——否则生产直接进崩溃循环
     （2026-09-13 `master/`→`deepseek-harness/` 的实例）。

2. **隔离冒烟验证（关键，别跳过）**：拿新代码的构建产物 + 生产 profile 的**副本** + 备用端口起临时实例，
   在不碰 3080 的前提下把所有 profile 兼容问题暴露出来：
   ```sh
   rm -rf /tmp/dsh-verify && mkdir -p /tmp/dsh-verify/profiles/web
   cd /tmp/dsh-verify/profiles/web
   cp ~/.dsh/profiles/web/{package.json,cordis.yml,pnpm-workspace.yaml,cordis.patch.yml} .
   ln -s ~/.dsh/profiles/web/node_modules ./node_modules
   ln -s ~/.dsh/profiles/node_modules /tmp/dsh-verify/profiles/node_modules
   cp ~/.dsh/settings.yaml /tmp/dsh-verify/settings.yaml
   cd /home/sx/projects/MyAI/deepseek-harness && source ~/.dsh/dsh-env.sh \
     && DSH_HOME=/tmp/dsh-verify node apps/cli/lib/bin.js web --port 3083 --no-open
   ```
   - 日志出现 `plugin tree failed to load: dsh: N entries did not activate` = 组合不兼容（下面第一条坑）。
   - 再用 Playwright 打开 `http://127.0.0.1:3083/?token=...` 看控制台：客户端插件报
     `missed the module table` 说明它仍引用旧平台包（下面第二条坑）。
   - **2026-09-12 这次升级就是靠它救的**：两次都提前拦下了会打挂生产的组合问题。

3. **重建**（重建不影响已加载的运行进程，只有重启才换代码；正式部署流程另见
   `deepseek-harness/.agents/skills/dsh-deploy-master`）：
   ```sh
   pnpm -C /home/sx/projects/MyAI/deepseek-harness install --frozen-lockfile
   pnpm -C /home/sx/projects/MyAI/deepseek-harness run clean && pnpm -C /home/sx/projects/MyAI/deepseek-harness run build
   ```
4. **重启**：按第 7 步顺序（headroom-* → dsh-web），重启前先拿用户确认。

### 0.1.5 起的硬性坑（均已实证）

- **profile patch 里的 `sandbox-policy` 不能 `disabled: true`**：0.1.5 的 web-app bundle 新增
  `dsh-api-workspace-files` 与 `dsh-client-ui-deliverables`，二者硬 `inject: ['sandboxPolicy']`；
  禁用该行 → 启动即崩并进 systemd 重启循环（实测 NRestarts 24）。
  `sandbox-policy` 只承载「部署默认档位 + 工作区根」，本身不执行管控；执行层
  （`fs-sandbox` / `bash-sandbox`）仍可关闭。`tool-fs` / `tool-bash` 仅在
  `ctx.fs.sandboxMode !== undefined` 时才查策略，所以挂 `fs-local`（无围栏）时开 policy 不引入限制。
- **客户端插件平台模块表变了**：`@deepseek-ai/dsh-client-runtime/client` 已不存在；现行 seed 为
  `cordis` / `dsh-client-store` / `dsh-client-ui-slots` / `dsh-client-ui-primitives` /
  `dsh-client-ui-dockkit`（react 系列照旧）。自建客户端插件要改 import 来源 + 同步 tsdown 的
  `external` 清单，否则整页 `Failed to load plugins`。`PropsStore` 取代了旧 `hooks` 的 store 写法。
- **非 git 源码交付的插件升级法已作废（2026-09-13）**：原先针对 `_dsh_plugins_src/dsh-agent-teams`、
  `MemOS/apps/memos-local-plugin`、`dsh-web-ui/packages/dsh-remote-web-ui` 的 `npm pack` + `tar` 覆盖 + 
  `relink-plugin-deps.mjs` 补链流程，其对象与工具均已不存在——前两者改为 npm 交付（MemOS 更已彻底移除），
  dsh-web-ui 与 relink 脚本均已删除。现在 web profile 只剩 `link:` 源码插件（第三方克隆在
  `dsh-extensions/vendor/`，自研在 `dsh-extensions/plugins/`）与 npm 包两条路，按各自方式升级即可。

## 本机已知问题速查（Arch）

| 日志特征（match） | 处理 |
|---|---|
| `Could not resolve host` / `Failed to connect` / `unable to access` / `open git-upload-pack` | 网络问题，规则已跳过；可设代理 `http://127.0.0.1:7897` 后重试（clash-verge 默认端口） |
| `git push` 报 `Failed to connect` / `Connection was reset` | 先查代理：`ss -tlnp \| grep 7897`（或 `curl -x http://127.0.0.1:7897 -sI https://github.com`）；端口在听仍失败 = 代理上游问题，本地 commit 不受影响，等网络恢复后重试 push |
| `fatal: not a git repository` | 路径不是 git 仓库：确认路径 / `git init`，规则已 ask_user |
| `CONFLICT` / `Automatic merge failed` | 上游冲突：人工解决（保留双方改动），规则已 ask_user |
| `cannot pull with rebase: You have unstaged changes` / `Your local changes would be overwritten` / `Please commit your changes or stash them` | 仓库有 WIP：先 `git -C <path> status` 看改动，向用户确认 stash/commit/跳过，规则已 ask_user（git 的 rebase/merge 两种文案都已覆盖） |
| `failed to init transaction (unable to lock database)` / `database is locked` | pacman 锁残留：确认无 pacman 进程后 `sudo rm /var/lib/pacman/db.lck` 重试（可固化规则） |
| `invalid or corrupted package (PGP signature)` / `failed to commit transaction` | keyring 过期：`sudo pacman-key --refresh-keys` 或 `sudo pacman -Sy archlinux-keyring` 后重试 |
| `target not found` / `failed to synchronize all databases (unable to lock database)` / 镜像问题 | 换镜像 `sudo pacman-mirrors -c China`（或编辑 /etc/pacman.d/mirrorlist），重试；AUR 包不存在则确认包名 |
| `yay` 交互卡死超时 | 非 TTY 环境跑 `yay` 必须带 `--noconfirm`（与 ansible 无关的确认项会挂住）；要人工确认的在终端手动跑 |
| 构建报 net10 不支持 / 「找不到带 SDK 的 dotnet」 | 用了个没 SDK 的 dotnet 入口。本机两份都带 SDK（`~/.dotnet/dotnet` 10.0.400 优先于 `/usr/bin/dotnet` 10.0.112）；`validate` 应显示 `[ok] dotnet: /home/sx/.dotnet/dotnet` |
| `[缺] scoop 不可用`（validate 警告） | 预期行为：Arch 无 scoop；保持 `[scoop] update_all=false, apps=[]`，警告不影响退出码 |
| 配置里路径报「不存在」（C:/...） | applist.toml 还是 Windows 迁移残留：按「Arch 更新项模板」改为 `/home/sx/...` 路径（需用户确认后改） |
| ⚠️ 清理工作区前必须先查 `~/.dsh/profiles/*/package.json` 的 `link:` 依赖 | 当前 6 条 `link:` 目标是：`dsh-extensions/vendor/{dsh-genui,dsh-toolkit,dsh-drop-to-path}`（第三方克隆，2026-09-13 由 `_dsh_plugins_src/` 迁入）、`dsh-extensions/plugins/{dsh-sidebar-taskbar,dsh-task-manager,web-dsh-web-extension}`（自研）、`deepseek-harness/packages/client/ui-primitives`（DSH 自身）。它们是 web profile 的**运行中插件源码**（bundle 依赖），不是研究残留——误删会导致下次 dsh-web 重启加载失败。**已退役、不必再保留的旧路径**：`_dsh_plugins_src/MemOS`、`_dsh_plugins_src/dsh-agent-teams`（改 npm 交付）、`_dsh_plugins_src/` 与 `rider-skills/` 两个一级目录（已并入 `dsh-extensions/vendor/`）、`dsh-web-ui`、`dsh-extensions-dev`。误删恢复：按 `npm view <pkg> repository.url` 或 GitHub 搜索克隆回上表原路径，再 `pnpm install` |
| `plugin tree failed to load: dsh: N entries did not activate`（含 `pending (waiting for service: sandboxPolicy)`） | profile patch 把 `sandbox-policy` 关掉了：0.1.5 起必须挂载（见「DSH harness 大版本升级」节）。改 `~/.dsh/profiles/web/cordis.patch.yml` 注释掉该 disabled 行后重启 dsh-web |
| 页面 `Failed to load plugins` + 控制台 `require("@deepseek-ai/dsh-client-…") missed the module table` | 自建客户端插件还在引用旧平台包：改 import 到现行 seed（`dsh-client-store` 等）并同步 tsdown `external`，`pnpm build` 后刷新页面 |
| dsh-web 反复重启（`systemctl --user show dsh-web -p NRestarts` 很大） | 先看 `journalctl --user -u dsh-web -n 60`：多半是插件树 pending / patch 语法错误。**别让它在崩溃循环里放着**——每 3s 重试一次；修好 patch 再 `systemctl --user restart dsh-web` |
| dsh-web 崩溃重启循环 + `Cannot find package '@deepseek-ai/…' imported from …/dsh-extensions/vendor/…`（`plugin tree failed to load`） | **自开发检出改名/移动过**：`dsh-extensions/vendor/*/node_modules` 里指向旧 worktree 的绝对符号链接悬空（2026-09-13 `master/`→`deepseek-harness/` 断 23 条）。修复：跑下方「vendor 悬空链接重指」→ `systemctl --user restart dsh-web`；applist.toml 已固化成 fixes.rule，下次命中自动修。预防：改名/移动后先重指、确认 `find /home/sx/projects/MyAI/dsh-extensions -xtype l` 为空再重启 |

### vendor 悬空链接重指（自开发检出改名/移动后的固定动作）

把 `dsh-extensions` 下曾指向旧 worktree 的悬空绝对符号链接，按「剩余路径 + 目标存在」重指到
当前 `deepseek-harness` checkout（幂等，可反复跑）：

```sh
find /home/sx/projects/MyAI/dsh-extensions -xtype l -print0 |
while IFS= read -r -d "" l; do
  t=$(readlink "$l") || continue
  case "$t" in
    /home/sx/projects/MyAI/*/*)
      rest=${t#/home/sx/projects/MyAI/}; rest=${rest#*/}
      [ -e "/home/sx/projects/MyAI/deepseek-harness/$rest" ] \
        && ln -sfn "/home/sx/projects/MyAI/deepseek-harness/$rest" "$l" && echo "relinked: $l";;
  esac
done
```

⚠️ **别拿 `dsh --profile web --dump-config` 当唯一验收**（2026-09-13 实测踩到）：它只组合配置树、
**不 import 插件模块**，悬空链接照过不误——当时输出 54 个条目、0 报错，看着完全正常，而故障
只在**真正加载插件**那一刻（即 `dsh-web` 启动）才暴露。路径变更类的验收顺序固定为：
① `find /home/sx/projects/MyAI/dsh-extensions -xtype l` 输出为空 → ② `dump-config` 组合成功 →
③ 才 `systemctl --user restart dsh-web`。

## 固化原则（硬性要求）

- 每次 AI 解决一个**新的** failed 问题并验证通过后，**必须**把可复现的解法固化进
  `applist.toml [fixes.rules]`（或必要时改 update-app 代码/文档），保证下次不再需要 AI——
  这正是需求第三点的闭环。
- 修复命令严禁破坏用户的 WIP/本地改动；需要取舍时一律 ask_user。
- 涉及「自动跑 `sudo pacman -Syu` / `yay -Sua`」这类影响全系统的更新项，启用前必须先让用户拍板。

## 交接给用户的修改点

修改过 `applist.toml`（新增/调整规则或更新项）时，在汇总里明确列出，并说明每条新规则的作用。

## Windows 旧版差异速记（迁移参考）

| 项 | Windows（旧） | Arch（本版） |
|---|---|---|
| 软件包管理 | scoop | pacman + yay（AUR） |
| 路径 | `C:\Users\Admin\Project\Other\update-app` | `/home/sx/projects/update-app` |
| 运行 CLI | 直接执行 `update-app.exe` | `dotnet update-app.dll`（current.json 的 exe 是 dll 路径） |
| 带 SDK 的 dotnet | scoop 的 dotnet-sdk | `~/.dotnet/dotnet`（10.0.400） |
| 命令执行 shell | cmd.exe /d /c | /bin/sh -lc（ProcessRunner 已按平台分支，2026-08-28 适配） |
| git 探测环境变量坑 | MSBuildSDKsPath / Version | 无（update-app 仍会剥离，无副作用） |
| DSH 托管 | PM2 | systemd 用户服务（dsh-web / headroom-*） |
| 代理 | 127.0.0.1:7897 | 127.0.0.1:7897（clash-verge/mihomo） |