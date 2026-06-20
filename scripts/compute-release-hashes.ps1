# 计算 release 产物的 SHA-256, 供粘贴到 GitHub release 发布说明 (零成本完整性校验, 问题#5)。
# 用法:
#   pwsh scripts/compute-release-hashes.ps1                      # 默认扫 ./dist 下的 exe/zip
#   pwsh scripts/compute-release-hashes.ps1 -Path .\publish      # 指定目录
#   pwsh scripts/compute-release-hashes.ps1 -Path .\CleanScope.exe
#
# 用户侧校验 (README 已写): Get-FileHash .\CleanScope.exe -Algorithm SHA256
param(
    [string]$Path = "dist"
)

$ErrorActionPreference = "Stop"

if (Test-Path $Path -PathType Leaf) {
    $files = @(Get-Item $Path)
} elseif (Test-Path $Path -PathType Container) {
    $files = Get-ChildItem -Path $Path -Recurse -Include *.exe, *.zip -File
} else {
    Write-Error "路径不存在: $Path"
    exit 1
}

if (-not $files -or $files.Count -eq 0) {
    Write-Warning "未找到 .exe / .zip 产物 (路径: $Path)。"
    exit 0
}

Write-Host "SHA-256 (粘贴到 release 发布说明):`n" -ForegroundColor Cyan
foreach ($f in $files) {
    $hash = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash.ToLower()
    "{0}  {1}" -f $hash, $f.Name
}
