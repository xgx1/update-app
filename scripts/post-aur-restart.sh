#!/bin/sh
# AUR 升级后的收尾：把仍在跑「旧二进制」的应用换成新二进制。
#
# 存在的问题：pacman/yay 只换磁盘文件，运行中的进程仍旧代码。GUI 自启应用与常驻服务
# 要等到下次登录才换，于是「包已升级、界面还显示旧版本」——用户会以为根本没更新。
#
# 判据：二进制 mtime 晚于进程启动时间（/proc/<pid> 的 mtime），说明该进程是升级前起的。
# ponytail: mtime 判据在「降级 / 同内容重装」时会误判成陈旧，代价只是多重启一次，可接受。
#
# 退出码：0=全部就绪（含「无需重启」）；1=有应用重启失败（会被 update-app 标为该失败项）。
set -u
fail=0

stale() { # stale <pid> <exe-path>
  # 2026-11-01 修：readlink -f 对「已被替换的二进制」会返回带 " (deleted)" 后缀的路径，
  # -e 判存在直接失败 → 升级后的进程全被误判成「已是最新」（rayburst 实测漏重启）。
  # 去掉后缀再比 mtime：进程还在跑被换掉的旧 inode，本来就是陈旧。
  [ -n "${1:-}" ] && [ -n "${2:-}" ] || return 1
  exe=${2% (deleted)}
  [ -e "$exe" ] && [ "$exe" -nt "/proc/$1" ]
}

restart_unit() { # restart_unit <systemd --user 单元全名>
  u=$1
  systemctl --user is-active -q "$u" 2>/dev/null || { echo "[aur-restart] $u 未运行，跳过"; return 0; }
  pid=$(systemctl --user show -p MainPID --value "$u" 2>/dev/null)
  exe=$(readlink -f "/proc/${pid:-0}/exe" 2>/dev/null || true)
  if stale "$pid" "$exe"; then
    echo "[aur-restart] $u 跑的是旧二进制，重启"
    systemctl --user restart "$u" || fail=1
  else
    echo "[aur-restart] $u 已是最新二进制"
  fi
}

# 下载器：rayburst 为现名；motrix-next 是未迁移时的旧单元名（兼容保留，存在即处理）
# 2026-11-05 修：这两个 GUI 由 XDG autostart 生成器托管，单元名是 app-<Name>@autostart.service，
# 写 rayburst.service 会永远命中 is-active=false → 一律「未运行，跳过」（实测漏重启）。
restart_unit app-Rayburst@autostart.service
restart_unit motrix-next.service

# ---------- Clash Verge Rev ----------
# 2026-11-05 修：GUI 也由 XDG autostart 生成器托管（单元名 app-Clash\x20Verge@autostart.service）。
# 旧写法 pgrep -x clash-verge 有两个坑：① 升级瞬间进程已退出 → 判「未运行，跳过」，代理就这么断着；
# ② 重启命令里再拼 .service 会得到 "app-…@autostart.service.service"（见 restart_unit 注释）。
# 更关键的是重启 ≠ 代理可用：2.5.10 升级后 GUI 报 NeedsReinstall 直接不启核心，7897 全断，
# 连 yay 自己末尾的 AUR RPC 都被带崩（exit 1，包其实已装好）。所以这里以 7897 是否监听做验收。
CV='app-Clash\x20Verge@autostart.service'
cv_pid=$(systemctl --user show -p MainPID --value "$CV" 2>/dev/null || true)
cv_exe=$(readlink -f "/proc/${cv_pid:-0}/exe" 2>/dev/null || true)
if [ -z "${cv_pid:-}" ] || [ "$cv_pid" = 0 ]; then
  echo "[aur-restart] clash-verge 未运行，拉起 GUI"
  systemctl --user restart "$CV" || fail=1
elif stale "$cv_pid" "$cv_exe"; then
  echo "[aur-restart] clash-verge 跑的是旧二进制，重启 GUI"
  systemctl --user restart "$CV" || fail=1
else
  echo "[aur-restart] clash-verge 已是最新二进制"
fi
sleep 8

# 代理可用性验收：7897 必须监听。GUI 与特权服务二进制版本不一致（包升级换了 /usr/bin 那份，
# /var/lib 下副本仍是旧的）时 GUI 拒绝启核心 → 全部走代理的流量断掉。重装服务即对齐版本。
if ! ss -tln 2>/dev/null | grep -q '127\.0\.0\.1:7897'; then
  echo "[aur-restart] 7897 未监听：重装 clash-verge 特权服务并重启 GUI"
  sudo -n /usr/bin/clash-verge-service-install >/dev/null 2>&1 || true
  sudo -n systemctl restart clash-verge-service 2>/dev/null || { echo "[aur-restart] 特权服务重启失败"; fail=1; }
  systemctl --user restart "$CV" || fail=1
  sleep 10
  if ss -tln 2>/dev/null | grep -q '127\.0\.0\.1:7897'; then
    echo "[aur-restart] 代理已恢复 (127.0.0.1:7897)"
  else
    echo "[aur-restart] 代理未恢复，需人工处理"
    fail=1
  fi
fi

# 特权服务：pacman 安装脚本也建议升级后重启它；exe 路径写死（root 进程的 /proc/<pid>/exe 读不到）
svc_exe=/usr/bin/clash-verge-service
svc_pid=$(pgrep -x clash-verge-ser | head -1 || true)
if [ -n "$svc_pid" ] && stale "$svc_pid" "$svc_exe"; then
  if sudo -n systemctl restart clash-verge-service 2>/dev/null; then
    echo "[aur-restart] clash-verge-service 已重启"
  else
    echo "[aur-restart] clash-verge-service 需人工重启（sudo 非免密）"
  fi
fi

exit $fail
