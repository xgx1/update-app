# 更新 DSH 技能子模块（dsh-extensions/skills/ 是 submodule，父仓 git pull 不会动它们）
# 用法: pwsh -NoProfile -File update-skills.ps1 [-Repo C:/Users/Admin/Project/MyAI/dsh-extensions]
# 退出码: 0 = 成功/无需更新；1 = 真失败（网络、提交失败等）
param([string]$Repo = 'C:/Users/Admin/Project/MyAI/dsh-extensions')

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Repo)) { "路径不存在: $Repo"; exit 1 }
Set-Location $Repo

# 1. 技能子模块快进到各自远端 HEAD（只碰 skills/，vendor/ 保持"只检测不更新"）
#    不加 --recursive：rea 等仓库的三级子模块含 Windows 非法文件名（unblob 测试样本），
#    递归会让整次更新 fatal 退出，而这些嵌套模块对技能内容无用。
git submodule update --init --remote -- skills
if ($LASTEXITCODE -ne 0) { "submodule update 失败"; exit 1 }

# 2. 指针有变化就在父仓本地提交（不 push）：否则下一轮父仓 pull --ff-only 会被脏指针挡住
git add -- skills
if (git diff --cached --name-only -- skills) {
    git commit -q -m 'chore(skills): bump submodule pointers (update-all)'
    if ($LASTEXITCODE -ne 0) { "指针提交失败"; exit 1 }
    'submodule pointers committed'
} else {
    'submodule pointers unchanged'
}

# 3. 新技能补软链（install-skill.sh 需要 Git Bash；WSL 的 bash 是桩，不能用）
#    MSYS=winsymlinks:nativestrict 必须设：否则 MSYS 的 ln 会静默复制目录而不是建符号链接。
$bash = @('C:\Program Files\Git\bin\bash.exe', 'C:\Program Files (x86)\Git\bin\bash.exe') |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if ($bash) {
    # 转成 Git Bash 的 /c/... 形式，保留大小写：install-skill.sh 按字符串比对目标，
    # 小写化的路径会被判成"漂移"每次重指。
    $posix = $Repo -replace '^([A-Za-z]):', '/$1' -replace '\\', '/'
    $posix = '/' + $posix[1].ToString().ToLower() + $posix.Substring(2)
    $env:MSYS = 'winsymlinks:nativestrict'
    $out = & $bash -lc "cd '$posix' && ./install-skill.sh 2>&1"
    $out | Select-Object -Last 4
    if ($LASTEXITCODE -ne 0) { "install-skill.sh 失败"; exit 1 }
} else {
    '跳过技能软链重建：未找到 Git Bash'
}
exit 0
