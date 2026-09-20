---
name: update-all
description: 更新所有软件（源码项目 / Arch 系统与 AUR 软件包 / 特殊配置与 DSH 插件源码）：调 update-app CLI（读取 applist.toml），读报错日志自动处理已知问题，需要用户拍板时停下询问并继续处理其他项，把新问题解法固化成 fixes.rules（下次 CLI 自愈，无需 AI）。触发词：更新所有软件、全部更新、跑一下 update all、更新一下所有东西、检查更新。
---

# update all —— 更新所有软件（源码项目 + Arch 系统/AUR 软件包 + 特殊配置/DSH 插件）· Arch Linux 版

## 何时使用

用户说「更新所有软件 / 全部更新 / 跑一下 update all / 更新一下所有东西 / 检查更新」等时使用。

## 一句话架构

**update-app** CLI（.NET 10，本机位于 `~/projects/update-app`）读取 `applist.toml`，
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

1. **配置文件**：`~/projects/update-app/applist.toml`（修改后请向用户确认再动）。
   该文件**已全部改为 Arch 路径**（2026-09-13 复核：表内 8 条路径全部存在），不再是 Windows 迁移残留。
2. **CLI 自举**：`~/projects/update-app/bin/current.json` 里的 `exe` 字段。
   - Arch 上 `exe` 指向的是 `update-app.dll`，运行方式为 `dotnet "<exe>" <命令>`（不是直接执行）。
   - 若 current.json 还是旧的 Windows `.exe` 路径（迁移残留），取 `bin/` 下最新时间戳目录里的 `update-app.dll`。
   - current.json 不存在或 dll 跑不动 → 按下方「自更新」重新发布一次。
3. **环境要点（本机实测）**：
   - **两份 dotnet 都带 SDK**：`~/.dotnet/dotnet`（**10.0.400**，dotnet-install 装的）与 `/usr/bin/dotnet`（10.0.112，pacman 的 `dotnet-sdk`）。
     `DotnetResolver` 在「PATH + `~/.dotnet/dotnet` + `/usr/bin/dotnet` + `/usr/share/dotnet/dotnet`」里挑 **SDK 版本最高** 的，
     所以落在 `~/.dotnet/dotnet`（`validate` 会显示 `[ok] dotnet: ~/.dotnet/dotnet`）。
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
path = "/var/cache/yay"
command = "yay -Sua --noconfirm"
```

- 把 pacman/yay 放进 applist.toml 后，一次 `run` 就能覆盖「系统 + AUR + 源码 + 特殊配置」，
  且失败仍走同一套 fixes.rules 自愈闭环；代价是 `--noconfirm` 不可见确认交互 → 建议启用前让用户拍板。
- 不想自动动系统包 → 保持模板不加，手动 `sudo pacman -Syu` / `yay -Sua` 即可（技能只负责源码与特殊配置）。

## 标准流程

### 1. 运行

```bash
BIN=~/projects/update-app/bin
DLL=$(jq -r .exe $BIN/current.json 2>/dev/null | grep '\.dll$')
[ -n "$DLL" ] || DLL=$(ls -1dt $BIN/2026*/ | head -1)update-app.dll
dotnet "$DLL" run --config ~/projects/update-app/applist.toml   # 可后台运行
```

常用子命令：`run`（执行）、`list`（看配置）、`validate`（校验路径/依赖）、`self`（自更新发布）；
选项：`--dry-run`（只预览）、`--only <关键字>`（只跑匹配项，如 `--only dsh` 只更新 DSH 插件源码）。

### 2. 读日志分类

结果在 `~/projects/update-app/logs/`：
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
- 处理：更新它本身 —— `git -C ~/projects/update-app pull`（有 remote 时），
  修代码 bug，然后 `dotnet "$DLL" self --repo ~/projects/update-app -c ~/projects/update-app/applist.toml`
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
服务在 `~/.config/systemd/user/`：`dsh-web.service`、`headroom-deepseek.service`（就这两个）。
2026-09-18 核实：`headroom-scnet.service`（:8789）与 `headroom-siliconflow.service`、
:8790 的 modelscope 实例**都已不存在**——SCNet 套餐额度耗尽（HTTP 429 `Token Plan quota has
been exceeded`）后连同代理一起清掉了。语音输入（vinput）现在直接走 `headroom-deepseek`
的 :8787，链路与排障见技能 `fcitx-voice-input`。

顺序固定为「先体检 → 先撤走不受影响的 → 再发报告 → **最后才排程延时重启**」：

0. **先做一次缺库体检**（pacman 常出现「新依赖先装上、依赖它的包还没重建」，服务会在下次
   启动时炸在 `error while loading shared libraries`；详见下方「更新后缺库体检」）：
   ```bash
   systemctl --user --failed --no-pager          # 先看有没有服务在崩
   for f in /usr/bin/vinput-daemon /usr/bin/headroom ~/.local/bin/headroom; do
     [ -e "$f" ] && { ldd "$f" 2>/dev/null | grep 'not found' && echo "^^ 缺库: $f"; }
   done
   ```
   有缺库就按「更新后缺库体检」节处置（只装重建版，**不要 `pacman -Sy`**）。
1. **再重启除 dsh-web 外的所有服务**并确认 active（这一步不影响当前对话）：
   ```bash
   systemctl --user restart headroom-deepseek.service
   systemctl --user is-active headroom-deepseek.service
   ```
2. **再输出第 6 步汇总报告**——报告里必须写明：重启**已排程**、延迟多少秒、怎么取消、页面会
   自动恢复。
3. **最后排程 dsh-web 延时重启**（`<N>` 见下方选值）：
   ```bash
   systemd-run --user --collect --on-active=<N> --unit=dsh-restart-once \
     systemctl --user restart dsh-web.service
   ```

**为什么必须延时而不是同步重启**：`systemctl --user restart dsh-web.service` 是同步阻塞的，
执行的那一刻就把当前会话（含正在跑这条命令的 agent）一起杀掉——报告还没发出去就断线了。
`--on-active=<N>` 把重启交给一个**独立于本进程**的定时单元，延迟窗口内本进程照常工作。

**顺序是硬要求**：排程返回后，本流程**只剩一句话可说了**（即本轮最终回复）。重启后排程它的
agent 已经不存在，无法再补发任何内容。所以「报告完整发出 → 排程 → 结束本轮」是唯一安全顺序，
**排程后不要再发新一轮工具调用**（每个调用都在消耗延迟窗口）。

**`<N>` 怎么选**：给「排程返回 → 最终回复说完」留出余量。常规汇总报 **10** 秒足够；只有极短
的确认语时 5 秒也可以。别给 3 秒——长报告可能说到一半就被切断。

**排程后仍可取消**（实测可行，`--collect` 不影响取消）：
```bash
systemctl --user stop dsh-restart-once.timer
```
这是本设计在"自动"与"可控"之间的平衡点：默认自动重启，用户在延迟窗口内仍能反悔一次。

4. **恢复后验证**（需要用户/下一个会话执行，agent 已随重启消失）：页面刷新后能正常打开、
   `systemctl --user is-active dsh-web.service` 为 active；若发现插件功能缺失或启动报错，
   重点检查 `~/.dsh/profiles/web/package.json` 的 `link:` 依赖路径是否有效
   （见「已知问题速查」里 `link:` 依赖那一行）。

- **本机不支持 `systemd-run` 时**：放弃延时，改为报告完整发出后同步执行
  `systemctl --user restart dsh-web.service`，并告知用户会立即断线（这属于预期，不需要用户确认）。
- **页面没自动恢复 / token 已换（2026-09-20 实测）**：`?token=` **每次启动都是新发的、且一次性**——
  token 换成功后写入 cookie，再拿同一个 `?token=` URL 重复请求会 401（不是服务坏）。
  重启后取新地址：`journalctl --user -u dsh-web -n 20 --no-pager | grep -oE 'token=[^ ]+' | tail -1`。
  判活要带 cookie jar，否则永远 401：
  `curl -sL -c /tmp/j -b /tmp/j -o /tmp/p.html -w "%{http_code}" "http://127.0.0.1:3080/?token=<新token>"` → 期望 `200` 且 HTML 里出现 `data-plugin="..."`（插件树注入成功的标志）。
  若 `200` 但仍打不开，检查 `systemctl --user is-active dsh-web.service`。
- **万一没拦住**（`stop` 晚了一步，服务已被重启）：页面恢复后立刻 `systemctl --user stop
  dsh-restart-once.timer`，并 `systemctl --user reset-failed dsh-restart-once` 清理单元残留。
- 若 `systemctl --user` 报 `Failed to connect to bus`，先 `export XDG_RUNTIME_DIR=/run/user/$(id -u)`。

## DSH harness 大版本升级（0.1.x → 0.1.y；0.1.1-rc.2 → 0.1.5-rc.2 实证）

源码型项目里的 `deepseek-harness`（分支 `master`）在 applist.toml 中默认禁用；它**就是生产实例（3080）的代码**，
大版本跨越不能只 `git pull`（dev worktree/3081 已于 2026-09-13 下线），按「源码升级 → 隔离冒烟验证 → 重建 → 报告后延时重启」走：

1. **升级源码**（旧进程在内存里继续跑，改文件本身不断线）：
   ```sh
   git -C ~/projects/MyAI/deepseek-harness fetch origin
   git -C ~/projects/MyAI/deepseek-harness merge origin/master
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
   cd ~/projects/MyAI/deepseek-harness && source ~/.dsh/dsh-env.sh \
     && DSH_HOME=/tmp/dsh-verify node apps/cli/lib/bin.js web --port 3083 --no-open
   ```
   - 日志出现 `plugin tree failed to load: dsh: N entries did not activate` = 组合不兼容（下面第一条坑）。
   - 再用 Playwright 打开 `http://127.0.0.1:3083/?token=...` 看控制台：客户端插件报
     `missed the module table` 说明它仍引用旧平台包（下面第二条坑）。
   - **2026-09-12 这次升级就是靠它救的**：两次都提前拦下了会打挂生产的组合问题。

3. **重建**（重建不影响已加载的运行进程，只有重启才换代码；正式部署流程另见
   `deepseek-harness/.agents/skills/dsh-deploy-master`）：
   ```sh
   pnpm -C ~/projects/MyAI/deepseek-harness install --frozen-lockfile
   pnpm -C ~/projects/MyAI/deepseek-harness run clean && pnpm -C ~/projects/MyAI/deepseek-harness run build
   ```
4. **重启**：按第 7 步顺序（headroom-* → 报告 → 排程延时重启 dsh-web），用「报告 → 排程」而非
   同步重启，让升级报告完整留在会话里。

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

## link: 插件源码更新后的重建（pull ≠ 生效）

`[special.items]` 里那些 `link:` 插件的 `git pull` **只改源码**——运行时加载的是各仓库自己的
`lib/`（或 `dist/`）构建产物，不重建就还是旧版（2026-09-20 实证：dsh-genui 从 0.10 拉到
0.11.1-preview.2、上游 ~60 个文件，而 `lib/client.js` 仍是 9月12日 的旧产物，体积 258KB → 重建后 560KB）。

判据：`ls -la <插件>/lib` 的时间戳是否早于本次 `git pull`；或 `package.json` 版本已变而产物没变。

重建（用插件自己的脚本，别手写 tsc/tsdown）：

```sh
cd ~/projects/MyAI/dsh-extensions/vendor/<插件>
pnpm install --frozen-lockfile   # 上游 lock 变了就必须先装
pnpm run build                   # 各仓脚本不同：genui=rm -rf lib && tsc && tsdown；evolve-modes=tsdown+归一化产物
```

- **先核对 peer/engines 与 harness 版本兼容**再重建：`jq -r .peerDependencies package.json` 与
  `dsh --version`（2026-09-20：genui 允许 `^0.1.6-alpha.1`，本机 harness `0.1.6-alpha.2` → 兼容）。
- 成群重建时按仓库顺序来（`pnpm install` 会动各自 `node_modules`），别并行。
- 重建**不影响已加载的运行进程**，只有 dsh-web 重启后才生效 → 归入第 7 步的延时重启一起做，
  并在报告里说明「重建了什么、重启后生效」。
- `dsh-evolve-modes` 这类自研/自有 fork 若是**本地提交领先上游**（`git status -sb` 显示 `[领先 N]`），
  `git pull` 不动它，`fetch` 检测会报「上游无新提交」——指针与推送按 AGENTS.md 的 submodule 规矩单独处理。

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
| 构建报 net10 不支持 / 「找不到带 SDK 的 dotnet」 | 用了个没 SDK 的 dotnet 入口。本机两份都带 SDK（`~/.dotnet/dotnet` 10.0.400 优先于 `/usr/bin/dotnet` 10.0.112）；`validate` 应显示 `[ok] dotnet: ~/.dotnet/dotnet` |
| `[缺] scoop 不可用`（validate 警告） | 预期行为：Arch 无 scoop；保持 `[scoop] update_all=false, apps=[]`，警告不影响退出码 |
| 配置里路径报「不存在」（C:/...） | applist.toml 还是 Windows 迁移残留：按「Arch 更新项模板」改为 `~/...` 路径（需用户确认后改） |
| 配置里路径报「不存在」（`~/...` 路径，2026-09-20 起） | **配置漂移**：该插件/仓库已被删除或改名。已固化成 fixes.rule → `needs_input`，把漂移事实摆给用户拍板后改 applist.toml（权威是 `~/.dsh/profiles/*/package.json` 的 `link:` 目标）。实例：`dsh-drop-to-path` 被移除、`vendor/dsh-evolve-modes` 新挂 |
| 服务启动即崩：journal 里 `error while loading shared libraries: libXXX.so.N` + `status=127` | 系统升级把某个依赖的 **SONAME 换代**了（2026-09-18 实例：protobuf 36.0→36.1，`libprotobuf-lite.so.36.0.0` 消失，`vinput-daemon` 连崩三次；真正缺库的是**传递依赖** `libonnxruntime.so.1`，归属包 `onnxruntime-cpu` 当时还没重建）→ 按下方「更新后缺库体检」处置 |
| ⚠️ 清理工作区前必须先查 `~/.dsh/profiles/*/package.json` 的 `link:` 依赖 | 当前 6 条 `link:` 目标是（2026-09-20 核对）：`dsh-extensions/vendor/{dsh-genui,dsh-toolkit,dsh-evolve-modes}`（第三方克隆，2026-09-13 由 `_dsh_plugins_src/` 迁入；`dsh-drop-to-path` 同日已删）、`dsh-extensions/plugins/{dsh-sidebar-taskbar,dsh-task-manager,web-dsh-web-extension}`（自研）。它们是 web profile 的**运行中插件源码**（bundle 依赖），不是研究残留——误删会导致下次 dsh-web 重启加载失败。**⚠️ pull 只是刷新源码**：这类插件加载的是各仓库自己的 `lib/`/`dist/` 构建产物，`git pull` 后不 `pnpm build` 则运行行为仍是旧版（2026-09-20 dsh-genui 0.10→0.11.1-preview.2 实测：lib/client.js 还是 9月12日 的旧产物）。**已退役、不必再保留的旧路径**：`_dsh_plugins_src/MemOS`、`_dsh_plugins_src/dsh-agent-teams`（改 npm 交付）、`_dsh_plugins_src/` 与 `rider-skills/` 两个一级目录（已并入 `dsh-extensions/vendor/`）、`dsh-web-ui`、`dsh-extensions-dev`、`vendor/dsh-drop-to-path`。误删恢复：按 `npm view <pkg> repository.url` 或 GitHub 搜索克隆回上表原路径，再 `pnpm install` |
| `plugin tree failed to load: dsh: N entries did not activate`（含 `pending (waiting for service: sandboxPolicy)`） | profile patch 把 `sandbox-policy` 关掉了：0.1.5 起必须挂载（见「DSH harness 大版本升级」节）。改 `~/.dsh/profiles/web/cordis.patch.yml` 注释掉该 disabled 行后重启 dsh-web |
| 页面 `Failed to load plugins` + 控制台 `require("@deepseek-ai/dsh-client-…") missed the module table` | 自建客户端插件还在引用旧平台包：改 import 到现行 seed（`dsh-client-store` 等）并同步 tsdown `external`，`pnpm build` 后刷新页面 |
| dsh-web 反复重启（`systemctl --user show dsh-web -p NRestarts` 很大） | 先看 `journalctl --user -u dsh-web -n 60`：多半是插件树 pending / patch 语法错误。**别让它在崩溃循环里放着**——每 3s 重试一次；修好 patch 后按第 7 步排程延时重启（先说明修了什么，再让重启发生） |
| dsh-web 崩溃重启循环 + `Cannot find package '@deepseek-ai/…' imported from …/dsh-extensions/vendor/…`（`plugin tree failed to load`） | **自开发检出改名/移动过**：`dsh-extensions/vendor/*/node_modules` 里指向旧 worktree 的绝对符号链接悬空（2026-09-13 `master/`→`deepseek-harness/` 断 23 条）。修复：跑下方「vendor 悬空链接重指」→ 再按第 7 步排程延时重启 dsh-web；applist.toml 已固化成 fixes.rule，下次命中自动修。预防：改名/移动后先重指、确认 `find ~/projects/MyAI/dsh-extensions -xtype l` 为空再重启 |

### 更新后缺库体检（SONAME 换代类故障）

**症状**：某个 systemd 服务/程序在升级后启动即崩，journal 里是
`error while loading shared libraries: libXXX.so.N: cannot open shared object file`
（`status=127`）。原因不是包损坏，而是 **Arch 升级了某个库并换了 SONAME**，而依赖它的包
（常是 AUR / archlinuxcn 的包，或当时仓库还没重建的包）还链着旧名字。**这类故障不会出现在
update-app 的失败清单里**——升级本身是成功的，炸的是下次启动。

**定位三步**（以 2026-09-18 的 vinput 为例，实测有效）：

```bash
# 1) 谁缺库（注意：缺的常常不是直接被依赖的那层，而是传递依赖）
ldd /usr/bin/vinput-daemon | grep 'not found'          # → libprotobuf-lite.so.36.0.0 => not found
# 2) 在一度依赖里找「谁还在要旧 SONAME」
ldd /usr/bin/vinput-daemon | awk '/=>/ {print $3}' | while read -r p; do
  [ -f "$p" ] && readelf -d "$p" 2>/dev/null | grep -q 'libprotobuf-lite.so.36.0.0' && echo "要旧 SONAME: $p"
done                                                     # → /usr/lib/libonnxruntime.so.1
# 3) 找归属包 & 本地版本
pacman -Qo /usr/lib/libonnxruntime.so.1                  # → onnxruntime-cpu 1.29.0-2
```

**修复**：装该包的**重建版**，且**只装这一个包**（`pacman -U` 单包升级，绝不用 `pacman -Sy`
——那会造成部分升级，把问题扩大）：

```bash
# ① 读仓库最新索引（写到 /tmp，不碰本地 pacman 数据库）
curl -sL -o /tmp/extra.db.tar.gz https://geo.mirror.pkgbuild.com/extra/os/x86_64/extra.db.tar.gz
mkdir -p /tmp/extra && tar -xzf /tmp/extra.db.tar.gz -C /tmp/extra
grep -A1 -E '^%(VERSION|BUILDDATE|FILENAME)%' /tmp/extra/onnxruntime-cpu-*/desc   # 看有没有 pkgrel+1 的重建版
# ② 下载 → 验 SHA256 → 单包升级
curl -sL -o /tmp/pkg.tar.zst https://geo.mirror.pkgbuild.com/extra/os/x86_64/onnxruntime-cpu-1.29.0-3-x86_64.pkg.tar.zst
grep -A1 '^%SHA256SUM%' /tmp/extra/onnxruntime-cpu-1.29.0-3/desc | tail -1          # 与 sha256sum 结果比对
sudo pacman -U --noconfirm /tmp/pkg.tar.zst
# ③ 复验：缺库消失 + 服务能起
ldd /usr/bin/vinput-daemon | grep 'not found' || echo "OK"
systemctl --user restart vinput-daemon && systemctl --user is-active vinput-daemon
```

archlinuxcn 的包同理，把索引换成 `https://repo.archlinuxcn.org/x86_64/archlinuxcn.db.tar.gz`、
镜像目录换成 `https://repo.archlinuxcn.org/x86_64/` 即可（`fcitx5-vinput`、`sherpa-onnx` 都在这里）。
**判断重建版够不够新**：比较它的 `%BUILDDATE%` 是否晚于肇事库的升级时间（`grep <库名> /var/log/pacman.log | tail`）。

**全盘扫描（可选，注意噪音）**：

```bash
for f in /usr/bin/* /usr/lib/*.so /usr/lib/*.so.*; do
  [ -f "$f" ] || continue
  ldd "$f" 2>/dev/null | grep -q 'not found' && echo "缺库: $f → $(ldd "$f" 2>/dev/null | grep 'not found' | head -2 | tr '\n' ' ')"
done
```

本机基线里本来就有约 20 条噪音（`libQt5*.so.5`、`libclang*.so.22.1`、`libgd.so.3` 之类——
**可选依赖从未安装**，不是故障）。判断标准是「本次升级后**新出现**的条目」，或直接盯自己要用的
那几个二进制（`/usr/bin/vinput-daemon`、`~/.local/bin/headroom`、`/usr/bin/fcitx5`…）。

### vendor 悬空链接重指（自开发检出改名/移动后的固定动作）

把 `dsh-extensions` 下曾指向旧 worktree 的悬空绝对符号链接，按「剩余路径 + 目标存在」重指到
当前 `deepseek-harness` checkout（幂等，可反复跑）：

```sh
find ~/projects/MyAI/dsh-extensions -xtype l -print0 |
while IFS= read -r -d "" l; do
  t=$(readlink "$l") || continue
  case "$t" in
    ~/projects/MyAI/*/*)
      rest=${t#~/projects/MyAI/}; rest=${rest#*/}
      [ -e "~/projects/MyAI/deepseek-harness/$rest" ] \
        && ln -sfn "~/projects/MyAI/deepseek-harness/$rest" "$l" && echo "relinked: $l";;
  esac
done
```

⚠️ **别拿 `dsh --profile web --dump-config` 当唯一验收**（2026-09-13 实测踩到）：它只组合配置树、
**不 import 插件模块**，悬空链接照过不误——当时输出 54 个条目、0 报错，看着完全正常，而故障
只在**真正加载插件**那一刻（即 `dsh-web` 启动）才暴露。路径变更类的验收顺序固定为：
① `find ~/projects/MyAI/dsh-extensions -xtype l` 输出为空 → ② `dump-config` 组合成功 →
③ 才按第 7 步排程 dsh-web 延时重启（报告先发，避免修复说明被同步重启一起杀掉）。

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
| 路径 | `~\Project\Other\update-app` | `~/projects/update-app` |
| 运行 CLI | 直接执行 `update-app.exe` | `dotnet update-app.dll`（current.json 的 exe 是 dll 路径） |
| 带 SDK 的 dotnet | scoop 的 dotnet-sdk | `~/.dotnet/dotnet`（10.0.400） |
| 命令执行 shell | cmd.exe /d /c | /bin/sh -lc（ProcessRunner 已按平台分支，2026-08-28 适配） |
| git 探测环境变量坑 | MSBuildSDKsPath / Version | 无（update-app 仍会剥离，无副作用） |
| DSH 托管 | PM2 | systemd 用户服务（dsh-web / headroom-*） |
| 代理 | 127.0.0.1:7897 | 127.0.0.1:7897（clash-verge/mihomo） |
