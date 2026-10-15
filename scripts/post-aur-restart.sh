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
  [ -n "${1:-}" ] && [ -n "${2:-}" ] && [ -e "$2" ] && [ "$2" -nt "/proc/$1" ]
}

restart_unit() { # restart_unit <unit 名，不带 .service>
  u=$1
  systemctl --user is-active -q "$u.service" 2>/dev/null || { echo "[aur-restart] $u 未运行，跳过"; return 0; }
  pid=$(systemctl --user show -p MainPID --value "$u.service" 2>/dev/null)
  exe=$(readlink -f "/proc/${pid:-0}/exe" 2>/dev/null || true)
  if stale "$pid" "$exe"; then
    echo "[aur-restart] $u 跑的是旧二进制，重启"
    systemctl --user restart "$u.service" || fail=1
  else
    echo "[aur-restart] $u 已是最新二进制"
  fi
}

# 下载器：rayburst 为现名；motrix-next 是未迁移时的旧单元名（兼容保留，存在即处理）
restart_unit rayburst
restart_unit motrix-next

# ---------- Clash Verge Rev ----------
cv_pid=$(pgrep -x clash-verge | head -1 || true)
cv_exe=$(readlink -f "/proc/${cv_pid:-0}/exe" 2>/dev/null || true)
if [ -z "$cv_pid" ]; then
  echo "[aur-restart] clash-verge 未运行，跳过"
elif stale "$cv_pid" "$cv_exe"; then
  echo "[aur-restart] clash-verge 跑的是旧二进制，重启 GUI"
  # 复用原进程环境（DISPLAY/WAYLAND_DISPLAY/DBUS…），否则拉起的实例接不上会话总线
  tr '\0' '\n' < "/proc/$cv_pid/environ" 2>/dev/null |
    grep -E '^(DISPLAY|WAYLAND_DISPLAY|XDG_RUNTIME_DIR|DBUS_SESSION_BUS_ADDRESS|XDG_SESSION_TYPE|XDG_CURRENT_DESKTOP|XAUTHORITY)=' \
      > /tmp/aur-restart-clash-verge.env || true
  kill "$cv_pid" 2>/dev/null || true
  i=0
  while [ "$i" -lt 10 ] && kill -0 "$cv_pid" 2>/dev/null; do sleep 0.5; i=$((i + 1)); done
  if kill -0 "$cv_pid" 2>/dev/null; then
    echo "[aur-restart] clash-verge 未退出，SIGKILL"
    kill -9 "$cv_pid" 2>/dev/null || true
    sleep 1
  fi
  # shellcheck disable=SC1091
  ( set -a; . /tmp/aur-restart-clash-verge.env 2>/dev/null; set +a; setsid -f "$cv_exe" >/dev/null 2>&1 ) || true
  sleep 3
  if pgrep -x clash-verge >/dev/null; then
    echo "[aur-restart] clash-verge 已以新二进制拉起"
  else
    echo "[aur-restart] clash-verge 没能拉起，下次登录会由自启项恢复"
    fail=1
  fi
else
  echo "[aur-restart] clash-verge 已是最新二进制"
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
