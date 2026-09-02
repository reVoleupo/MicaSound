# 克隆并准备自托管的网易云音乐 API 服务(Asplla fork,本地回环使用)
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot "mica-sound-api"

if (Test-Path (Join-Path $apiDir "app.js")) {
    Write-Host "[✓] API 已存在: $apiDir" -ForegroundColor Green
}
else {
    Write-Host "[i] 克隆 Asplla/NeteaseCloudMusicApi(经 7897 代理)..."
    $proxy = "http://127.0.0.1:7897"
    git -c http.proxy=$proxy -c https.proxy=$proxy clone --depth 1 https://github.com/Asplla/NeteaseCloudMusicApi.git $apiDir
    if ($LASTEXITCODE -ne 0) { throw "git clone 失败" }
}

Push-Location $apiDir
try {
    Write-Host "[i] 安装依赖(跳过 husky 钩子)..."
    npm install --omit=dev --ignore-scripts --no-audit --no-fund 2>&1 | Select-Object -Last 5
    if ($LASTEXITCODE -ne 0) { throw "npm install 失败" }
    Write-Host "[✓] API 服务依赖就绪" -ForegroundColor Green

    # 覆盖启动入口:使用微声的 boot.js 绕过可能卡死的 generateConfig
    Copy-Item (Join-Path $PSScriptRoot "api-boot.js") (Join-Path $apiDir "boot.js") -Force
    Write-Host "[✓] 已注入微声启动器 boot.js" -ForegroundColor Green
}
finally {
    Pop-Location
}