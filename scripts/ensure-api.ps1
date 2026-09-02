# 克隆并准备自托管的网易云音乐 API 服务(Asplla fork,本地回环使用)
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDir = Join-Path $repoRoot "mica-sound-api"

# 解析 git 代理:本地开发经 127.0.0.1:7897,CI 无本地代理则直连。
# 可用 NCM_GIT_PROXY 覆盖,设为 'none' 强制直连。
function Resolve-GitProxy {
    if ($env:NCM_GIT_PROXY) {
        if ($env:NCM_GIT_PROXY -eq 'none') { return $null }
        return $env:NCM_GIT_PROXY
    }
    try {
        $ok = Test-NetConnection 127.0.0.1 -Port 7897 -InformationLevel Quiet -WarningAction SilentlyContinue
        if ($ok) { return "http://127.0.0.1:7897" }
    } catch { }
    return $null
}

if (Test-Path (Join-Path $apiDir "app.js")) {
    Write-Host "[✓] API 已存在: $apiDir" -ForegroundColor Green
}
else {
    Write-Host "[i] 克隆 Asplla/NeteaseCloudMusicApi..."
    $proxy = Resolve-GitProxy
    $cloneArgs = @("clone", "--depth", "1")
    if ($proxy) {
        Write-Host "    经代理 $proxy"
        $cloneArgs += @("-c", "http.proxy=$proxy", "-c", "https.proxy=$proxy")
    }
    else {
        Write-Host "    直连(无代理)"
        $cloneArgs += @("-c", "http.proxy=", "-c", "https.proxy=")
    }
    $cloneArgs += @("https://github.com/Asplla/NeteaseCloudMusicApi.git", $apiDir)
    & git @cloneArgs 2>&1 | Write-Host
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