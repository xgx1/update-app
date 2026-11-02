# 升级 nodejs-lts / python / uv（scoop）。
#
# 这三样都被 DSH 自身占用：dsh-web 跑在 nodejs-lts 上，opencode-zen-bridge 与
# openviking-server 是 python 进程，uv/uvx 是 unity MCP 的启动器。所以必须先停服务、
# 再升级、然后由调用方决定何时把 dsh-web 拉起来（本脚本不启动 dsh-web，避免在
# 升级结果未知时把页面拉成坏状态）。
#
# 用法：作为一次性计划任务执行，日志写 %USERPROFILE%\.dsh\upgrade-rest.log。

$ErrorActionPreference = 'Continue'
$log = Join-Path $env:USERPROFILE '.dsh\upgrade-rest.log'

function Write-Log([string]$Message) {
    Add-Content -Path $log -Value ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message)
}

Write-Log '=== upgrade nodejs-lts / python / uv ==='

foreach ($task in 'dsh-web', 'opencode-zen-bridge', 'openviking-server') {
    $t = Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
    if ($t -and $t.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $task
        Write-Log "stopped task: $task"
    }
}
Start-Sleep -Seconds 4

# 计划任务停的是根进程，子进程（node/python/uv）常会留下来占着 exe，scoop 会拒绝覆盖。
$appPaths = @('\scoop\apps\nodejs-lts\', '\scoop\apps\python\', '\scoop\apps\uv\', '\scoop\persist\nodejs-lts\', '\scoop\shims\')
foreach ($name in 'node', 'python', 'uv', 'uvx') {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        $path = $_.Path
        if (-not $path) { return }
        if (-not ($appPaths | Where-Object { $path -like "*$_*" })) { return }
        Write-Log "kill $($_.ProcessName) pid=$($_.Id) $path"
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }
}
Start-Sleep -Seconds 3

foreach ($app in 'nodejs-lts', 'python', 'uv') {
    Write-Log "scoop update $app"
    (scoop update $app 2>&1) | ForEach-Object { Write-Log "  $_" }
}

$nodeExe = Join-Path $env:USERPROFILE 'scoop\apps\nodejs-lts\current\node.exe'
if (Test-Path $nodeExe) { Write-Log ('node --version: ' + (& $nodeExe --version 2>&1)) }
$pyExe = Join-Path $env:USERPROFILE 'scoop\apps\python\current\python.exe'
if (Test-Path $pyExe) { Write-Log ('python --version: ' + (& $pyExe --version 2>&1)) }
$uvExe = Join-Path $env:USERPROFILE 'scoop\apps\uv\current\uv.exe'
if (Test-Path $uvExe) { Write-Log ('uv --version: ' + (& $uvExe --version 2>&1)) }

foreach ($task in 'opencode-zen-bridge', 'openviking-server') {
    try {
        Start-ScheduledTask -TaskName $task
        Write-Log "started task: $task"
    } catch {
        Write-Log "failed to start task ${task}: $($_.Exception.Message)"
    }
}

Write-Log 'done. dsh-web is stopped — start it with: Start-ScheduledTask -TaskName dsh-web'
