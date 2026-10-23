# 尔雅发布打包：一次构建一个「平台 × AOT/非 AOT」组合，产出 dist/Erya_<版本>_<平台>[_aot].zip
#
# 用法（仓库根目录）：
#   pwsh packaging/publish.ps1 -Rid win-x64            # 自包含便携版（非 AOT）
#   pwsh packaging/publish.ps1 -Rid win-x64 -Aot       # Native AOT 版
#   # Windows 本机没有 pwsh 时（Windows PowerShell 5.1 同样可跑）：
#   powershell -ExecutionPolicy Bypass -File packaging\publish.ps1 -Rid win-x64 -Aot
#
# RID：win-x64 / linux-x64 / linux-arm64 / osx-x64 / osx-arm64
# 注意：Native AOT 不能交叉编译——目标平台必须在对应系统的本机上构建
#       （win-x64 在 Windows、linux-x64 在 Linux、osx-arm64 在 macOS；.github/workflows/ci.yml 矩阵已对齐）。
#       非 AOT 的自包含发布可以交叉构建（如 Windows 上打 linux-x64/osx-arm64 包）。
# Linux/macOS 主机上用 zip CLI 打包以保留可执行位（Compress-Archive 会丢 +x，解压后无法直接运行）。

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Rid,

    [switch]$Aot,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    # 默认读 src/Erya/Erya.csproj 的 <Version>
    [string]$Version,

    [string]$DistDir
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot 'src/Erya/Erya.csproj'

if (-not $Version) {
    $csproj = Get-Content -LiteralPath $Project -Raw
    if ($csproj -match '<Version>\s*([^<]+?)\s*</Version>') { $Version = $Matches[1] }
    if (-not $Version) { throw '无法从 src/Erya/Erya.csproj 读取 <Version>，请用 -Version 显式指定。' }
}
if (-not $DistDir) { $DistDir = Join-Path $RepoRoot 'dist' }

$label = $Rid.Replace('-', '_')
$mode = 'self-contained'
$extra = @()
if ($Aot) {
    $label += '_aot'
    $mode = 'Native AOT'
    $extra += '/p:PublishAot=true'
}

$publishDir = Join-Path $RepoRoot ('artifacts/publish/' + $label)
$zipPath = Join-Path $DistDir ('Erya_' + $Version + '_' + $label + '.zip')

Write-Host ('==> dotnet publish（{0}，{1}）' -f $Rid, $mode)
dotnet publish $Project -c $Configuration -r $Rid --self-contained true -o $publishDir @extra
if ($LASTEXITCODE -ne 0) { throw ('dotnet publish 失败（{0}，{1}）' -f $Rid, $mode) }

# 调试符号不进发行包
Get-ChildItem -LiteralPath $publishDir -Recurse -Filter *.pdb | Remove-Item -Force

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

# $env:OS 只在 Windows 上是 Windows_NT（pwsh 与 Windows PowerShell 5.1 皆然）
if ($env:OS -eq 'Windows_NT') {
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force
} else {
    $zipTool = Get-Command zip -ErrorAction SilentlyContinue
    if (-not $zipTool) { throw '未找到 zip：Linux/macOS 主机打包需要 zip CLI 保留可执行位（apt install zip / brew install zip）。' }
    Push-Location $publishDir
    try {
        & $zipTool.Source -r -q -X $zipPath .
        if ($LASTEXITCODE -ne 0) { throw ('zip 失败（{0}，{1}）' -f $Rid, $mode) }
    } finally { Pop-Location }
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
Write-Host ('==> {0}（{1} MB）' -f $zipPath, $sizeMb)
