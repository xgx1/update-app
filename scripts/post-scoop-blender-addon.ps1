# post-scoop-blender-addon.ps1 — 把仓库里的 Blender addon 主副本同步到便携版 Blender。
#
# 为什么需要：scoop 版 Blender 的用户脚本目录在 `apps\blender\<版本>\portable\scripts\`
# （不是 %APPDATA%\Blender Foundation），而 `apps\blender\current` 是指向版本目录的 junction。
# `scoop update blender` 换版本后 junction 指向新目录，旧目录里的 addon 不会被带过去，
# DSH 的 blender 插件就报 blender-unreachable。
#
# 幂等：内容相同就跳过；Blender 未安装则直接成功退出（不算失败项）。
param()
$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'blender-addon\blender_mcp.py'
$current = Join-Path $env:USERPROFILE 'scoop\apps\blender\current'
if (-not (Test-Path $current)) {
    Write-Output 'blender 未安装，跳过 addon 同步'
    exit 0
}
if (-not (Test-Path $source)) {
    Write-Output "addon 主副本缺失: $source"
    exit 1
}

$target = Join-Path $current 'portable\scripts\addons'
New-Item -ItemType Directory -Force -Path $target | Out-Null
$dest = Join-Path $target 'blender_mcp.py'

if ((Test-Path $dest) -and ((Get-FileHash $dest).Hash -eq (Get-FileHash $source).Hash)) {
    Write-Output "addon 已是最新: $dest"
    exit 0
}

Copy-Item $source $dest -Force
Write-Output "addon 已同步: $dest"
Write-Output 'Blender 在跑的话需重开（addon 在启动时加载）'
