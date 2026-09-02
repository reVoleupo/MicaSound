// 微声 MicaSound · 自托管 API 启动器
// 原 app.js 启动时会调用 generateConfig() 联网注册 anonymous_token,
// 网络不可用或缓慢时会卡死进程。此启动器在超时后照常拉起服务,保证可用性。
const fs = require('fs')
const path = require('path')
const os = require('os')

async function main() {
  const tokenPath = path.resolve(os.tmpdir(), 'anonymous_token')
  if (!fs.existsSync(tokenPath)) fs.writeFileSync(tokenPath, '', 'utf-8')

  try {
    await Promise.race([
      require('./generateConfig')(),
      new Promise((resolve) =>
        setTimeout(() => {
          console.log('[boot] generateConfig 超时,跳过匿名 token 注册')
          resolve()
        }, 5000),
      ),
    ])
  } catch (e) {
    console.log('[boot] generateConfig 跳过:', e && e.message ? e.message : e)
  }

  require('./server').serveNcmApi({ checkVersion: true })
}

main()